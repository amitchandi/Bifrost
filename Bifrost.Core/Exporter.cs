using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Bifrost.Core;

public static class Exporter
{
    public static event Action<int, int>? OnProgress;

    public static int Run(MigrationConfig config, string outputDir, bool dryRun = false)
    {
        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost — Export{(dryRun ? " [DRY RUN]" : "")}");
        Logger.Log("============================================================");
        Logger.Log("");

        var conn = config.Source;
        if (!dryRun) Directory.CreateDirectory(outputDir);
        var resolvedOutput = Path.GetFullPath(outputDir);

        var manifest = new Manifest
        {
            ExportedAt = DateTime.UtcNow.ToString("O"),
            Server     = conn.Server,
        };

        var sw         = Stopwatch.StartNew();
        int totalOk    = 0;
        int totalFail  = 0;
        long totalRows = 0;

        // Pre-count for progress
        var allTables = new List<(DbEntry Entry, TableRef Table)>();
        foreach (var entry in config.Databases)
        {
            try
            {
                using var sqlConn = Database.Open(conn, entry.SourceDatabase);
                foreach (var t in TableResolver.Resolve(sqlConn, entry))
                    if (!t.Ignore) allTables.Add((entry, t));
            }
            catch { }
        }

        int completed = 0;
        OnProgress?.Invoke(0, allTables.Count);

        foreach (var entry in config.Databases)
        {
            Logger.Log($"\n  [DB] {entry.SourceDatabase} -> {entry.TargetDatabase}");

            var dbOutDir = Path.Combine(resolvedOutput, entry.SourceDatabase);
            if (!dryRun) Directory.CreateDirectory(dbOutDir);

            var manifestDb = new ManifestDb
            {
                SourceDatabase = entry.SourceDatabase,
                TargetDatabase = entry.TargetDatabase,
            };

            try
            {
                using var sqlConn = Database.Open(conn, entry.SourceDatabase);
                var tables = TableResolver.Resolve(sqlConn, entry);
                Logger.Log($"    Tables found: {tables.Count}");

                foreach (var t in tables)
                {
                    if (t.Ignore)
                    {
                        Logger.Log($"    [{DateTime.Now:HH:mm:ss}] -> Skipping [{t.Schema}].[{t.Name}] (ignored)");
                        continue;
                    }

                    try
                    {
                        long rows;
                        if (dryRun)
                        {
                            rows = CountRows(sqlConn, t);
                            Logger.Log($"    [{DateTime.Now:HH:mm:ss}] [DRY] [{t.Schema}].[{t.Name}] — would export {rows:N0} rows");
                            manifestDb.Files.Add($"{t.Schema}.{t.Name}.sql");
                        }
                        else
                        {
                            (var file, rows) = RetryHelper.Run(
                                () => ExportTable(sqlConn, t, dbOutDir),
                                label: $"{t.Schema}.{t.Name}");
                            manifestDb.Files.Add(file);
                        }

                        totalRows += rows;
                        totalOk++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [FAIL] {t.Schema}.{t.Name}: {ex.Message}");
                        totalFail++;
                    }

                    completed++;
                    OnProgress?.Invoke(completed, allTables.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"  [FAIL] Could not process '{entry.SourceDatabase}': {ex.Message}");
                totalFail++;
            }

            manifest.Databases.Add(manifestDb);
        }

        if (!dryRun)
            File.WriteAllText(
                Path.Combine(resolvedOutput, "_manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost export{(dryRun ? " [DRY RUN]" : "")} complete");
        Logger.Log($"  [OK]   Success : {totalOk} tables");
        if (totalFail > 0) Logger.Log($"  [FAIL] Failed  : {totalFail} tables");
        Logger.Log($"  [OK]   Rows    : {totalRows:N0}");
        if (!dryRun) Logger.Log($"  [DIR]  Output  : {resolvedOutput}");
        Logger.Log($"  [TIME] Total   : {sw.Elapsed:hh\\:mm\\:ss}");
        Logger.Log("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static long CountRows(Microsoft.Data.SqlClient.SqlConnection conn, TableRef t)
    {
        using var cmd = conn.CreateCommand();
        var fullName = $"[{t.Schema}].[{t.Name}]";
        cmd.CommandText = t.Query != null
            ? $"SELECT COUNT(*) FROM ({t.Query}) AS _q"
            : t.Where != null
                ? $"SELECT COUNT(*) FROM {fullName} WHERE {t.Where}"
                : $"SELECT COUNT(*) FROM {fullName}";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static (string FileName, long RowCount) ExportTable(
        Microsoft.Data.SqlClient.SqlConnection conn, TableRef t, string dbOutDir)
    {
        var fullName = $"[{t.Schema}].[{t.Name}]";
        var msg      = $"    [{DateTime.Now:HH:mm:ss}] -> Exporting {fullName}";
        if (t.Where != null) msg += " (filtered)";
        if (t.Query != null) msg += " (custom query)";
        Logger.Log(msg + "...");

        var columns     = Database.GetColumns(conn, t.Schema, t.Name);
        if (columns.Count == 0) throw new Exception("No columns found — table may not exist");

        var hasIdentity = columns.Any(c => c.IsIdentity);
        var colNames    = string.Join(", ", columns.Select(c => $"[{c.ColName}]"));
        var fileName    = $"{t.Schema}.{t.Name}.sql";
        var filePath    = Path.Combine(dbOutDir, fileName);
        long rowCount   = 0;
        const int BatchSize = 100;

        using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
        {
            writer.WriteLine($"-- ============================================================");
            writer.WriteLine($"-- Table   : {fullName}");
            writer.WriteLine($"-- Exported: {DateTime.UtcNow:O}");
            writer.WriteLine($"-- ============================================================");
            writer.WriteLine();
            writer.WriteLine("-- Schema");
            writer.Write(SqlBuilder.BuildCreateTable(t.Schema, t.Name, columns));
            writer.WriteLine();
            writer.WriteLine("-- Clear existing data");
            writer.WriteLine($"DELETE FROM [{t.Schema}].[{t.Name}];");
            writer.WriteLine("GO");
            writer.WriteLine();

            if (hasIdentity)
            {
                writer.WriteLine($"IF OBJECTPROPERTY(OBJECT_ID('{t.Schema}.{t.Name}'), 'TableHasIdentity') = 1");
                writer.WriteLine($"    SET IDENTITY_INSERT {fullName} ON;");
                writer.WriteLine("GO");
                writer.WriteLine();
            }

            writer.WriteLine("-- Data");

            void WriteInsert(Microsoft.Data.SqlClient.SqlDataReader reader)
            {
                writer.WriteLine(SqlBuilder.BuildInsert(t.Schema, t.Name, columns, reader));
                rowCount++;
                if (rowCount % BatchSize == 0) { writer.WriteLine("GO"); writer.WriteLine(); }
            }

            if (t.Query != null) Database.StreamCustomQuery(conn, t.Query, WriteInsert);
            else                  Database.StreamRows(conn, t.Schema, t.Name, colNames, t.Where, WriteInsert);

            writer.WriteLine("GO");
            writer.WriteLine();

            if (hasIdentity)
            {
                writer.WriteLine($"IF OBJECTPROPERTY(OBJECT_ID('{t.Schema}.{t.Name}'), 'TableHasIdentity') = 1");
                writer.WriteLine($"    SET IDENTITY_INSERT {fullName} OFF;");
                writer.WriteLine("GO");
                writer.WriteLine();
            }
        }

        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [OK] {fileName} ({rowCount:N0} rows)");
        return (fileName, rowCount);
    }
}
