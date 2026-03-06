using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Bifrost.Core;

public static class Exporter
{
    public static int Run(MigrationConfig config, string outputDir)
    {
        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log("  Bifrost — Export");
        Logger.Log("============================================================");
        Logger.Log("");

        var conn = config.Source;
        Directory.CreateDirectory(outputDir);
        var resolvedOutput = Path.GetFullPath(outputDir);

        var manifest = new Manifest
        {
            ExportedAt = DateTime.UtcNow.ToString("O"),
            Server = conn.Server,
        };

        var sw = Stopwatch.StartNew();
        int totalOk = 0;
        int totalFail = 0;

        foreach (var entry in config.Databases)
        {
            Logger.Log($"\n  [DB] {entry.SourceDatabase} -> {entry.TargetDatabase}");

            var dbOutDir = Path.Combine(resolvedOutput, entry.SourceDatabase);
            Directory.CreateDirectory(dbOutDir);

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
                        var file = ExportTable(sqlConn, t, dbOutDir);
                        manifestDb.Files.Add(file);
                        totalOk++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [FAIL] {t.Schema}.{t.Name}: {ex.Message}");
                        totalFail++;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"  [FAIL] Could not process '{entry.SourceDatabase}': {ex.Message}");
                totalFail++;
            }

            manifest.Databases.Add(manifestDb);
        }

        File.WriteAllText(
            Path.Combine(resolvedOutput, "_manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost export complete");
        Logger.Log($"  [OK]   Success : {totalOk} tables");
        if (totalFail > 0) Logger.Log($"  [FAIL] Failed  : {totalFail} tables");
        Logger.Log($"  [DIR]  Output  : {resolvedOutput}");
        Logger.Log($"  [TIME] Total   : {sw.Elapsed:hh\\:mm\\:ss}");
        Logger.Log("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static string ExportTable(Microsoft.Data.SqlClient.SqlConnection conn, TableRef t, string dbOutDir)
    {
        var fullName = $"[{t.Schema}].[{t.Name}]";
        var msg = $"    [{DateTime.Now:HH:mm:ss}] -> Exporting {fullName}";
        if (t.Where != null) msg += " (filtered)";
        if (t.Query != null) msg += " (custom query)";
        Logger.Log(msg + "...");

        var columns = Database.GetColumns(conn, t.Schema, t.Name);
        if (columns.Count == 0) throw new Exception("No columns found — table may not exist");

        var hasIdentity = columns.Any(c => c.IsIdentity);
        var colNames = string.Join(", ", columns.Select(c => $"[{c.ColName}]"));
        var fileName = $"{t.Schema}.{t.Name}.sql";
        var filePath = Path.Combine(dbOutDir, fileName);
        int rowCount = 0;
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
            else Database.StreamRows(conn, t.Schema, t.Name, colNames, t.Where, WriteInsert);

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

        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [OK] {fileName} ({rowCount} rows)");
        return fileName;
    }
}