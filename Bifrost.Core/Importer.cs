using System.Diagnostics;
using System.Text.Json;

namespace Bifrost.Core;

public static class Importer
{
    public static int Run(MigrationConfig config, string outputDir)
    {
        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log("  Bifrost — Import");
        Logger.Log("============================================================");
        Logger.Log("");

        var manifestPath = Path.Combine(outputDir, "_manifest.json");
        if (!File.Exists(manifestPath)) { Logger.Log($"[FAIL] Manifest not found: {manifestPath}"); return 1; }

        var conn = config.Target;
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
            ?? throw new Exception("Failed to parse manifest");

        var sw = Stopwatch.StartNew();
        int totalOk = 0;
        int totalFail = 0;

        foreach (var dbEntry in manifest.Databases)
        {
            Logger.Log($"\n  [DB] {dbEntry.SourceDatabase} -> {dbEntry.TargetDatabase}");

            try
            {
                Database.EnsureDatabase(conn, dbEntry.TargetDatabase);
                Logger.Log($"    [{DateTime.Now:HH:mm:ss}] [DB] {dbEntry.TargetDatabase} ready");

                foreach (var file in dbEntry.Files)
                {
                    var filePath = Path.Combine(outputDir, dbEntry.SourceDatabase, file);

                    if (!File.Exists(filePath))
                    {
                        Logger.Log($"       [SKIP] File not found: {filePath}");
                        continue;
                    }

                    Logger.Log($"    [{DateTime.Now:HH:mm:ss}] -> Importing {file}...");

                    try
                    {
                        using var sqlConn = Database.Open(conn, dbEntry.TargetDatabase);
                        ExecuteFile(sqlConn, filePath);
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [OK]");
                        totalOk++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [FAIL] {ex.Message}");
                        totalFail++;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"  [FAIL] Could not process '{dbEntry.TargetDatabase}': {ex.Message}");
                totalFail++;
            }
        }

        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost import complete");
        Logger.Log($"  [OK]   Success : {totalOk} files");
        if (totalFail > 0) Logger.Log($"  [FAIL] Failed  : {totalFail} files");
        Logger.Log($"  [TIME] Total   : {sw.Elapsed:hh\\:mm\\:ss}");
        Logger.Log("============================================================");

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
                    if (t.Length > 0 && !t.StartsWith("--")) { hasNonComment = true; break; }
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
        if (remaining.Length > 0) Database.ExecuteBatch(sqlConn, remaining);
    }
}