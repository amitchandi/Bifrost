using System.Diagnostics;
using System.Text.Json;

namespace Bifrost;

public static class Importer
{
    public static int Run(string connectionFile, string outputDir)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("  Bifrost — Import");
        Console.WriteLine("============================================================");
        Console.WriteLine();

        var manifestPath = Path.Combine(outputDir, "_manifest.json");

        if (!File.Exists(connectionFile)) { Console.Error.WriteLine($"[FAIL] Connection not found: {connectionFile}"); return 1; }
        if (!File.Exists(manifestPath)) { Console.Error.WriteLine($"[FAIL] Manifest not found: {manifestPath}"); return 1; }

        var conn = JsonSerializer.Deserialize<ConnectionConfig>(File.ReadAllText(connectionFile))
            ?? throw new Exception("Failed to parse connection file");
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
            ?? throw new Exception("Failed to parse manifest");

        if (manifest.Server.Equals(conn.Server, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("[WARN] Target server matches source server!");
            Console.Error.WriteLine("       Sleeping 10 seconds — press Ctrl+C to abort.");
            Thread.Sleep(10000);
        }

        var sw = Stopwatch.StartNew();
        int totalOk = 0;
        int totalFail = 0;

        foreach (var dbEntry in manifest.Databases)
        {
            Console.WriteLine($"\n  [DB] {dbEntry.SourceDatabase} -> {dbEntry.TargetDatabase}");

            try
            {
                using (var masterConn = Database.Open(conn, "master"))
                {
                    Database.ExecuteBatch(masterConn,
                        $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{dbEntry.TargetDatabase}') " +
                        $"CREATE DATABASE [{dbEntry.TargetDatabase}]");
                    Console.WriteLine($"    [{DateTime.Now:HH:mm:ss}] [DB] {dbEntry.TargetDatabase} ready");
                }

                foreach (var file in dbEntry.Files)
                {
                    var filePath = Path.Combine(outputDir, dbEntry.SourceDatabase, file);

                    if (!File.Exists(filePath))
                    {
                        Console.Error.WriteLine($"       [SKIP] File not found: {filePath}");
                        continue;
                    }

                    Console.Write($"    [{DateTime.Now:HH:mm:ss}] -> Importing {file}... ");

                    try
                    {
                        using var sqlConn = Database.Open(conn, dbEntry.TargetDatabase);
                        ExecuteFile(sqlConn, filePath);
                        Console.WriteLine("[OK]");
                        totalOk++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[FAIL] {ex.Message}");
                        totalFail++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [FAIL] Could not process '{dbEntry.TargetDatabase}': {ex.Message}");
                totalFail++;
            }
        }

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine($"  Bifrost import complete");
        Console.WriteLine($"  [OK]   Success : {totalOk} files");
        if (totalFail > 0)
            Console.WriteLine($"  [FAIL] Failed  : {totalFail} files");
        Console.WriteLine($"  [TIME]  Total    : {sw.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static void ExecuteFile(Microsoft.Data.SqlClient.SqlConnection sqlConn, string filePath)
    {
        var batch = new System.Text.StringBuilder();

        foreach (var line in File.ReadLines(filePath))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                var sql = batch.ToString().Trim();
                batch.Clear();

                if (sql.Length == 0) continue;

                bool hasNonComment = false;
                foreach (var l in sql.Split('\n'))
                {
                    var t = l.Trim();
                    if (t.Length > 0 && !t.StartsWith("--"))
                    {
                        hasNonComment = true;
                        break;
                    }
                }
                if (!hasNonComment) continue;

                Database.ExecuteBatch(sqlConn, sql);
            }
            else
            {
                batch.AppendLine(line);
            }
        }

        var remaining = batch.ToString().Trim();
        if (remaining.Length > 0)
            Database.ExecuteBatch(sqlConn, remaining);
    }
}