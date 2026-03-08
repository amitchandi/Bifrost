using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Bifrost.Core;

public static class Importer
{
    public static event Action<int, int>? OnProgress;

    public static int Run(MigrationConfig config, string outputDir, bool dryRun = false)
    {
        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost — Import{(dryRun ? " [DRY RUN]" : "")}");
        Logger.Log("============================================================");
        Logger.Log("");

        var manifestPath = Path.Combine(outputDir, "_manifest.json");
        if (!File.Exists(manifestPath)) { Logger.Log($"[FAIL] Manifest not found: {manifestPath}"); return 1; }

        var conn     = config.Target;
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
            ?? throw new Exception("Failed to parse manifest");

        var sw         = Stopwatch.StartNew();
        int totalOk    = 0;
        int totalFail  = 0;
        int totalFiles = manifest.Databases.Sum(d => d.Files.Count);
        int completed  = 0;

        OnProgress?.Invoke(0, totalFiles);

        foreach (var dbEntry in manifest.Databases)
        {
            Logger.Log($"\n  [DB] {dbEntry.SourceDatabase} -> {dbEntry.TargetDatabase}");

            try
            {
                if (!dryRun) Database.EnsureDatabase(conn, dbEntry.TargetDatabase);
                Logger.Log($"    [{DateTime.Now:HH:mm:ss}] [DB] {dbEntry.TargetDatabase} ready");

                foreach (var file in dbEntry.Files)
                {
                    var filePath = Path.Combine(outputDir, dbEntry.SourceDatabase, file);

                    if (!File.Exists(filePath))
                    {
                        Logger.Log($"    [{DateTime.Now:HH:mm:ss}] [SKIP] File not found: {file}");
                        completed++;
                        OnProgress?.Invoke(completed, totalFiles);
                        continue;
                    }

                    Logger.Log($"    [{DateTime.Now:HH:mm:ss}] -> {(dryRun ? "[DRY] " : "")}Importing {file}...");

                    if (dryRun)
                    {
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [DRY] Would import {file}");
                        totalOk++;
                    }
                    else
                    {
                        try
                        {
                            RetryHelper.Run(() =>
                            {
                                using var sqlConn = Database.Open(conn, dbEntry.TargetDatabase);
                                ExecuteFileInTransaction(sqlConn, filePath);
                            }, label: file);

                            Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [OK]");
                            totalOk++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [FAIL] {ex.Message}");
                            totalFail++;
                        }
                    }

                    completed++;
                    OnProgress?.Invoke(completed, totalFiles);
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
        Logger.Log($"  Bifrost import{(dryRun ? " [DRY RUN]" : "")} complete");
        Logger.Log($"  [OK]   Success : {totalOk} files");
        if (totalFail > 0) Logger.Log($"  [FAIL] Failed  : {totalFail} files");
        Logger.Log($"  [TIME] Total   : {sw.Elapsed:hh\\:mm\\:ss}");
        Logger.Log("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static void ExecuteFileInTransaction(SqlConnection sqlConn, string filePath)
    {
        var batches = SplitIntoBatches(filePath);

        using var transaction = sqlConn.BeginTransaction();
        try
        {
            foreach (var sql in batches)
            {
                using var cmd = sqlConn.CreateCommand();
                cmd.Transaction   = transaction;
                cmd.CommandText   = sql;
                cmd.CommandTimeout = 300;
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static List<string> SplitIntoBatches(string filePath)
    {
        var batches = new List<string>();
        var batch   = new StringBuilder();

        foreach (var line in File.ReadLines(filePath))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                var sql = batch.ToString().Trim();
                batch.Clear();
                if (sql.Length == 0) continue;

                bool hasNonComment = sql.Split('\n')
                    .Select(l => l.Trim())
                    .Any(l => l.Length > 0 && !l.StartsWith("--"));

                if (hasNonComment) batches.Add(sql);
            }
            else
            {
                batch.AppendLine(line);
            }
        }

        var remaining = batch.ToString().Trim();
        if (remaining.Length > 0) batches.Add(remaining);

        return batches;
    }
}
