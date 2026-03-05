using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Bifrost;

public static class Exporter
{
    public static int Run(string configFile, string connectionFile, string outputDir)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("  Bifrost — Export");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        if (!File.Exists(configFile)) { Console.Error.WriteLine($"[FAIL] Config not found: {configFile}"); return 1; }
        if (!File.Exists(connectionFile)) { Console.Error.WriteLine($"[FAIL] Connection not found: {connectionFile}"); return 1; }

        var config = JsonSerializer.Deserialize<MigrationConfig>(File.ReadAllText(configFile))
            ?? throw new Exception("Failed to parse config.json");
        var conn = JsonSerializer.Deserialize<ConnectionConfig>(File.ReadAllText(connectionFile))
            ?? throw new Exception("Failed to parse connection file");

        Directory.CreateDirectory(outputDir);

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
            Console.WriteLine($"\n  [DB] {entry.SourceDatabase} -> {entry.TargetDatabase}");

            var dbOutDir = Path.Combine(outputDir, entry.SourceDatabase);
            Directory.CreateDirectory(dbOutDir);

            var manifestDb = new ManifestDb
            {
                SourceDatabase = entry.SourceDatabase,
                TargetDatabase = entry.TargetDatabase,
            };

            try
            {
                using var sqlConn = Database.Open(conn, entry.SourceDatabase);

                var tables = ResolveTables(sqlConn, entry);
                Console.WriteLine($"    Tables found: {tables.Count}");

                foreach (var t in tables)
                {
                    if (t.Ignore)
                    {
                        Console.WriteLine($"    -> Skipping [{t.Schema}].[{t.Name}] (ignored)");
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
                        Console.Error.WriteLine($"       [FAIL] {t.Schema}.{t.Name}: {ex.Message}");
                        totalFail++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [FAIL] Could not process '{entry.SourceDatabase}': {ex.Message}");
                totalFail++;
            }

            manifest.Databases.Add(manifestDb);
        }

        File.WriteAllText(
            Path.Combine(outputDir, "_manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine($"  Bifrost export complete");
        Console.WriteLine($"  [OK]   Success : {totalOk} tables");
        if (totalFail > 0)
            Console.WriteLine($"  [FAIL] Failed  : {totalFail} tables");
        Console.WriteLine($"  [DIR]  Output  : {Path.GetFullPath(outputDir)}");
        Console.WriteLine($"  [TIME]  Total    : {sw.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static List<TableRef> ResolveTables(Microsoft.Data.SqlClient.SqlConnection conn, DbEntry entry)
    {
        List<TableRef> tables = entry.TableFilter switch
        {
            "tenant" => entry.TenantId is null
                ? throw new Exception("tableFilter 'tenant' requires tenantId")
                : Database.GetTenantTables(conn, entry.TenantId),
            "explicit" => entry.Tables is null || entry.Tables.Count == 0
                ? throw new Exception("tableFilter 'explicit' requires a tables array")
                : entry.Tables.Select(t =>
                {
                    var parts = t.Name.Split('.');
                    return new TableRef
                    {
                        Schema = parts.Length == 2 ? parts[0] : "dbo",
                        Name = parts.Length == 2 ? parts[1] : parts[0],
                        Ignore = t.Ignore ?? false,
                        Where = t.Where,
                        Query = t.Query,
                    };
                }).ToList(),
            "all" => Database.GetTables(conn),
            _ => throw new Exception($"Unknown tableFilter '{entry.TableFilter}'"),
        };

        // Apply overrides for tenant/all
        if (entry.Overrides is { Count: > 0 })
        {
            tables = tables.Select(t =>
            {
                var full = $"{t.Schema}.{t.Name}".ToLower();
                var ov = entry.Overrides.FirstOrDefault(o => o.Name.ToLower() == full);
                return ov is null ? t : new TableRef
                {
                    Schema = t.Schema,
                    Name = t.Name,
                    Ignore = ov.Ignore ?? false,
                    Where = ov.Where,
                    Query = ov.Query,
                };
            }).ToList();
        }

        return tables;
    }

    private static string ExportTable(
        Microsoft.Data.SqlClient.SqlConnection conn,
        TableRef t,
        string dbOutDir)
    {
        var fullName = $"[{t.Schema}].[{t.Name}]";
        Console.Write($"    [{DateTime.Now:HH:mm:ss}] -> Exporting {fullName}");
        if (t.Where != null) Console.Write(" (filtered)");
        if (t.Query != null) Console.Write(" (custom query)");
        Console.WriteLine("...");

        var columns = Database.GetColumns(conn, t.Schema, t.Name);

        if (columns.Count == 0)
            throw new Exception("No columns found — table may not exist");

        var hasIdentity = columns.Any(c => c.IsIdentity);
        var colNames = string.Join(", ", columns.Select(c => $"[{c.ColName}]"));

        var fileName = $"{t.Schema}.{t.Name}.sql";
        var filePath = Path.Combine(dbOutDir, fileName);
        int rowCount = 0;

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
            writer.WriteLine($"-- Clear existing data");
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

            const int BatchSize = 100;

            void WriteInsert(Microsoft.Data.SqlClient.SqlDataReader reader)
            {
                writer.WriteLine(SqlBuilder.BuildInsert(t.Schema, t.Name, columns, reader));
                rowCount++;
                if (rowCount % BatchSize == 0)
                {
                    writer.WriteLine("GO");
                    writer.WriteLine();
                }
            }

            if (t.Query != null)
                Database.StreamCustomQuery(conn, t.Query, WriteInsert);
            else
                Database.StreamRows(conn, t.Schema, t.Name, colNames, t.Where, WriteInsert);

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

        Console.WriteLine($"       [{DateTime.Now:HH:mm:ss}] [OK] {fileName} ({rowCount} rows)");
        return fileName;
    }
}