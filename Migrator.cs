using System.Diagnostics;
using System.Text.Json;

namespace Bifrost;

public static class Migrator
{
    public static int Run(string configFile, string sourceConnectionFile, string targetConnectionFile)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("  Bifrost — Direct Migration");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        if (!File.Exists(configFile)) { Console.Error.WriteLine($"[FAIL] Config not found: {configFile}"); return 1; }
        if (!File.Exists(sourceConnectionFile)) { Console.Error.WriteLine($"[FAIL] Source connection not found: {sourceConnectionFile}"); return 1; }
        if (!File.Exists(targetConnectionFile)) { Console.Error.WriteLine($"[FAIL] Target connection not found: {targetConnectionFile}"); return 1; }

        var config = JsonSerializer.Deserialize<MigrationConfig>(File.ReadAllText(configFile))
            ?? throw new Exception("Failed to parse config.json");
        var sourceConn = JsonSerializer.Deserialize<ConnectionConfig>(File.ReadAllText(sourceConnectionFile))
            ?? throw new Exception("Failed to parse source connection file");
        var targetConn = JsonSerializer.Deserialize<ConnectionConfig>(File.ReadAllText(targetConnectionFile))
            ?? throw new Exception("Failed to parse target connection file");

        if (sourceConn.Server.Equals(targetConn.Server, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("[WARN] Source and target servers are the same!");
            Console.Error.WriteLine("       Sleeping 10 seconds — press Ctrl+C to abort.");
            Thread.Sleep(10000);
        }

        var sw = Stopwatch.StartNew();
        int totalOk = 0;
        int totalFail = 0;

        foreach (var entry in config.Databases)
        {
            Console.WriteLine($"\n  [DB] {entry.SourceDatabase} -> {entry.TargetDatabase}");

            try
            {
                // Ensure target database exists
                using (var masterConn = Database.Open(targetConn, "master"))
                {
                    Database.ExecuteBatch(masterConn,
                        $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{entry.TargetDatabase}') " +
                        $"CREATE DATABASE [{entry.TargetDatabase}]");
                    Console.WriteLine($"    [{DateTime.Now:HH:mm:ss}] [DB] {entry.TargetDatabase} ready");
                }

                using var srcConn = Database.Open(sourceConn, entry.SourceDatabase);
                using var dstConn = Database.Open(targetConn, entry.TargetDatabase);

                var tables = ResolveTables(srcConn, entry);
                Console.WriteLine($"    Tables found: {tables.Count}");

                foreach (var t in tables)
                {
                    if (t.Ignore)
                    {
                        Console.WriteLine($"    [{DateTime.Now:HH:mm:ss}] -> Skipping [{t.Schema}].[{t.Name}] (ignored)");
                        continue;
                    }

                    Console.Write($"    [{DateTime.Now:HH:mm:ss}] -> Migrating [{t.Schema}].[{t.Name}]");
                    if (t.Where != null) Console.Write(" (filtered)");
                    if (t.Query != null) Console.Write(" (custom query)");
                    Console.WriteLine("...");

                    try
                    {
                        MigrateTable(srcConn, dstConn, t);
                        totalOk++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"       [{DateTime.Now:HH:mm:ss}] [FAIL] {t.Schema}.{t.Name}: {ex.Message}");
                        totalFail++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [FAIL] Could not process '{entry.SourceDatabase}': {ex.Message}");
                totalFail++;
            }
        }

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine($"  Bifrost direct migration complete");
        Console.WriteLine($"  [OK]   Success : {totalOk} tables");
        if (totalFail > 0)
            Console.WriteLine($"  [FAIL] Failed  : {totalFail} tables");
        Console.WriteLine($"  [TIME] Total    : {sw.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static void MigrateTable(
        Microsoft.Data.SqlClient.SqlConnection srcConn,
        Microsoft.Data.SqlClient.SqlConnection dstConn,
        TableRef t)
    {
        var fullName = $"[{t.Schema}].[{t.Name}]";
        var columns = Database.GetColumns(srcConn, t.Schema, t.Name);

        if (columns.Count == 0)
            throw new Exception("No columns found — table may not exist");

        var hasIdentity = columns.Any(c => c.IsIdentity);
        var colNames = string.Join(", ", columns.Select(c => $"[{c.ColName}]"));

        // Create table on target if not exists
        var createSql = SqlBuilder.BuildCreateTable(t.Schema, t.Name, columns);
        Database.ExecuteBatch(dstConn, createSql.Replace("\r\nGO", "").Replace("\nGO", "").Trim());

        // Clear existing data
        Database.ExecuteBatch(dstConn, $"DELETE FROM {fullName}");

        if (hasIdentity)
        {
            Database.ExecuteBatch(dstConn,
                $"IF OBJECTPROPERTY(OBJECT_ID('{t.Schema}.{t.Name}'), 'TableHasIdentity') = 1 " +
                $"SET IDENTITY_INSERT {fullName} ON");
        }

        const int BatchSize = 100;
        int rowCount = 0;
        var insertBuffer = new List<string>(BatchSize);

        void FlushBuffer()
        {
            if (insertBuffer.Count == 0) return;
            var batch = string.Join("\n", insertBuffer);
            Database.ExecuteBatch(dstConn, batch);
            insertBuffer.Clear();
        }

        void OnRow(Microsoft.Data.SqlClient.SqlDataReader reader)
        {
            insertBuffer.Add(SqlBuilder.BuildInsert(t.Schema, t.Name, columns, reader));
            rowCount++;
            if (insertBuffer.Count >= BatchSize) FlushBuffer();
        }

        if (t.Query != null)
            Database.StreamCustomQuery(srcConn, t.Query, OnRow);
        else
            Database.StreamRows(srcConn, t.Schema, t.Name, colNames, t.Where, OnRow);

        FlushBuffer();

        if (hasIdentity)
        {
            Database.ExecuteBatch(dstConn,
                $"IF OBJECTPROPERTY(OBJECT_ID('{t.Schema}.{t.Name}'), 'TableHasIdentity') = 1 " +
                $"SET IDENTITY_INSERT {fullName} OFF");
        }

        Console.WriteLine($"       [{DateTime.Now:HH:mm:ss}] [OK] {t.Schema}.{t.Name} ({rowCount} rows)");
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
}