using System.Diagnostics;

namespace Bifrost.Core;

public static class Migrator
{
    public static event Action<int, int>? OnProgress; // (completed, total)

    public static int Run(MigrationConfig config, bool bulk = false, bool dryRun = false)
    {
        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost — Direct Migration{(bulk ? " (bulk)" : "")}{(dryRun ? " [DRY RUN]" : "")}");
        Logger.Log("============================================================");
        Logger.Log("");

        var sourceConn = config.Source;
        var targetConn = config.Target;

        var sw = Stopwatch.StartNew();
        int totalOk = 0;
        int totalFail = 0;
        long totalRows = 0;

        // Pre-resolve all tables for progress tracking
        var allTables = new List<(DbEntry Entry, TableRef Table)>();
        foreach (var entry in config.Databases)
        {
            try
            {
                using var srcConn = Database.Open(sourceConn, entry.SourceDatabase);
                foreach (var t in TableResolver.Resolve(srcConn, entry))
                    if (!t.Ignore) allTables.Add((entry, t));
            }
            catch (Exception ex)
            {
                Logger.Log($"  [FAIL] Could not resolve tables for '{entry.SourceDatabase}': {ex.Message}");
            }
        }

        int completed = 0;
        OnProgress?.Invoke(0, allTables.Count);

        foreach (var entry in config.Databases)
        {
            Logger.Log($"\n  [DB] {entry.SourceDatabase} -> {entry.TargetDatabase}");

            try
            {
                if (!dryRun) Database.EnsureDatabase(targetConn, entry.TargetDatabase);
                Logger.Log($"    [{DateTime.Now:HH:mm:ss}] [DB] {entry.TargetDatabase} ready");

                using var srcConn = Database.Open(sourceConn, entry.SourceDatabase);
                using var dstConn = dryRun ? null : Database.Open(targetConn, entry.TargetDatabase);

                var tables = TableResolver.Resolve(srcConn, entry);
                Logger.Log($"    Tables found: {tables.Count}");

                foreach (var t in tables)
                {
                    if (t.Ignore)
                    {
                        Logger.Log($"    [{DateTime.Now:HH:mm:ss}] -> Skipping [{t.Schema}].[{t.Name}] (ignored)");
                        continue;
                    }

                    var rename = t.TargetName != null ? $" → [{t.EffectiveTargetSchema}].[{t.EffectiveTargetName}]" : "";
                    var msg = $"    [{DateTime.Now:HH:mm:ss}] -> {(dryRun ? "[DRY] " : "")}Migrating [{t.Schema}].[{t.Name}]{rename}";
                    if (t.Where != null) msg += " (filtered)";
                    if (t.Query != null) msg += " (custom query)";
                    Logger.Log(msg + "...");

                    try
                    {
                        long rows = 0;
                        if (dryRun)
                        {
                            rows = CountRows(srcConn, t);
                            Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [DRY] Would migrate {rows:N0} rows");
                        }
                        else
                        {
                            rows = RetryHelper.Run(
                                () => MigrateTable(srcConn, dstConn!, t, bulk, entry.DropAndCreate, entry.AppendOnly),
                                label: $"{t.Schema}.{t.Name}");
                        }

                        totalRows += rows;
                        totalOk++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [FAIL] {t.Schema}.{t.Name}{(t.TargetName != null ? $" → {t.EffectiveTargetSchema}.{t.EffectiveTargetName}" : "")}: {ex.Message}");
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
        }

        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost direct migration{(dryRun ? " [DRY RUN]" : "")} complete");
        Logger.Log($"  [OK]   Success : {totalOk} tables");
        if (totalFail > 0) Logger.Log($"  [FAIL] Failed  : {totalFail} tables");
        Logger.Log($"  [OK]   Rows    : {totalRows:N0}");
        Logger.Log($"  [TIME] Total   : {sw.Elapsed:hh\\:mm\\:ss}");
        Logger.Log("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static bool TableExistsOnTarget(Microsoft.Data.SqlClient.SqlConnection conn, string schema, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = @schema AND t.name = @table";
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
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

    private static long MigrateTable(
        Microsoft.Data.SqlClient.SqlConnection srcConn,
        Microsoft.Data.SqlClient.SqlConnection dstConn,
        TableRef t, bool bulk, bool dropAndCreate = false, bool appendOnly = false)
    {
        var srcFullName = $"[{t.Schema}].[{t.Name}]";
        var tgtFullName = $"[{t.EffectiveTargetSchema}].[{t.EffectiveTargetName}]";
        var columns = Database.GetColumns(srcConn, t.Schema, t.Name);
        if (columns.Count == 0) throw new Exception("No columns found — table may not exist");

        var hasIdentity = columns.Any(c => c.IsIdentity);
        var colNames = string.Join(", ", columns.Select(c => $"[{c.ColName}]"));

        var tableExists = TableExistsOnTarget(dstConn, t.EffectiveTargetSchema, t.EffectiveTargetName);

        if (dropAndCreate)
        {
            var createSql = SqlBuilder.BuildCreateTable(t.EffectiveTargetSchema, t.EffectiveTargetName, columns, dropAndCreate: true);
            Database.ExecuteBatch(dstConn, createSql.Replace("\r\nGO", "").Replace("\nGO", "").Trim());
        }
        else if (!tableExists)
        {
            var createSql = SqlBuilder.BuildCreateTable(t.EffectiveTargetSchema, t.EffectiveTargetName, columns, dropAndCreate: false);
            Database.ExecuteBatch(dstConn, createSql.Replace("\r\nGO", "").Replace("\nGO", "").Trim());
        }
        else if (!appendOnly)
        {
            Database.ExecuteBatch(dstConn, $"DELETE FROM {tgtFullName}");
        }

        long rows = bulk
            ? MigrateTableBulk(srcConn, dstConn, t, srcFullName, tgtFullName, colNames, hasIdentity)
            : MigrateTableInsert(srcConn, dstConn, t, srcFullName, tgtFullName, columns, colNames, hasIdentity);

        var targetLabel = t.TargetName != null ? $"{t.Schema}.{t.Name} → {t.EffectiveTargetSchema}.{t.EffectiveTargetName}" : $"{t.Schema}.{t.Name}";
        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [OK] {targetLabel} ({rows:N0} rows)");
        return rows;
    }

    private static long MigrateTableBulk(
        Microsoft.Data.SqlClient.SqlConnection srcConn,
        Microsoft.Data.SqlClient.SqlConnection dstConn,
        TableRef t, string srcFullName, string tgtFullName, string colNames,
        bool hasIdentity)
    {
        using var cmd = srcConn.CreateCommand();
        cmd.CommandTimeout = 300;
        cmd.CommandText = t.Query
            ?? (t.Where != null
                ? $"SELECT {colNames} FROM {srcFullName} WHERE {t.Where}"
                : $"SELECT {colNames} FROM {srcFullName}");

        using var reader = cmd.ExecuteReader();

        var bulkOptions = hasIdentity
            ? Microsoft.Data.SqlClient.SqlBulkCopyOptions.KeepIdentity
            : Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default;

        using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(dstConn, bulkOptions, null)
        {
            DestinationTableName = tgtFullName,
            BatchSize = 1000,
            BulkCopyTimeout = 300,
            NotifyAfter = 1000,
        };

        bulkCopy.SqlRowsCopied += (_, e) =>
            Logger.Log($"       [{DateTime.Now:HH:mm:ss}]     {e.RowsCopied:N0} rows copied...");

        foreach (var col in reader.GetColumnSchema())
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        bulkCopy.WriteToServer(reader);

        // Always use COUNT for the final row count — SqlRowsCopied is unreliable
        // for small tables (never fires) and misses partial final batches.
        // reader and bulkCopy are done at this point; dstConn is free.
        using var countCmd = dstConn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM {tgtFullName}";
        return Convert.ToInt64(countCmd.ExecuteScalar());
    }

    private static long MigrateTableInsert(
        Microsoft.Data.SqlClient.SqlConnection srcConn,
        Microsoft.Data.SqlClient.SqlConnection dstConn,
        TableRef t, string srcFullName, string tgtFullName, List<ColumnInfo> columns,
        string colNames, bool hasIdentity)
    {
        if (hasIdentity)
            Database.ExecuteBatch(dstConn,
                $"IF OBJECTPROPERTY(OBJECT_ID('{t.EffectiveTargetSchema}.{t.EffectiveTargetName}'), 'TableHasIdentity') = 1 " +
                $"SET IDENTITY_INSERT {tgtFullName} ON");

        const int BatchSize = 100;
        long rowCount = 0;
        var insertBuffer = new List<string>(BatchSize);

        void FlushBuffer()
        {
            if (insertBuffer.Count == 0) return;
            Database.ExecuteBatch(dstConn, string.Join("\n", insertBuffer));
            insertBuffer.Clear();
        }

        void OnRow(Microsoft.Data.SqlClient.SqlDataReader reader)
        {
            insertBuffer.Add(SqlBuilder.BuildInsert(t.EffectiveTargetSchema, t.EffectiveTargetName, columns, reader));
            rowCount++;
            if (insertBuffer.Count >= BatchSize) FlushBuffer();
        }

        if (t.Query != null) Database.StreamCustomQuery(srcConn, t.Query, OnRow);
        else Database.StreamRows(srcConn, t.Schema, t.Name, colNames, t.Where, OnRow);
        FlushBuffer();

        if (hasIdentity)
            Database.ExecuteBatch(dstConn,
                $"IF OBJECTPROPERTY(OBJECT_ID('{t.EffectiveTargetSchema}.{t.EffectiveTargetName}'), 'TableHasIdentity') = 1 " +
                $"SET IDENTITY_INSERT {tgtFullName} OFF");

        return rowCount;
    }
}