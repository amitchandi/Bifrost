using System.Diagnostics;

namespace Bifrost.Core;

public static class Migrator
{
    public static int Run(MigrationConfig config, bool bulk = false)
    {
        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost — Direct Migration{(bulk ? " (bulk)" : "")}");
        Logger.Log("============================================================");
        Logger.Log("");

        var sourceConn = config.Source;
        var targetConn = config.Target;

        var sw = Stopwatch.StartNew();
        int totalOk = 0;
        int totalFail = 0;

        foreach (var entry in config.Databases)
        {
            Logger.Log($"\n  [DB] {entry.SourceDatabase} -> {entry.TargetDatabase}");

            try
            {
                Database.EnsureDatabase(targetConn, entry.TargetDatabase);
                Logger.Log($"    [{DateTime.Now:HH:mm:ss}] [DB] {entry.TargetDatabase} ready");

                using var srcConn = Database.Open(sourceConn, entry.SourceDatabase);
                using var dstConn = Database.Open(targetConn, entry.TargetDatabase);

                var tables = TableResolver.Resolve(srcConn, entry);
                Logger.Log($"    Tables found: {tables.Count}");

                foreach (var t in tables)
                {
                    if (t.Ignore)
                    {
                        Logger.Log($"    [{DateTime.Now:HH:mm:ss}] -> Skipping [{t.Schema}].[{t.Name}] (ignored)");
                        continue;
                    }

                    var msg = $"    [{DateTime.Now:HH:mm:ss}] -> Migrating [{t.Schema}].[{t.Name}]";
                    if (t.Where != null) msg += " (filtered)";
                    if (t.Query != null) msg += " (custom query)";
                    Logger.Log(msg + "...");

                    try
                    {
                        MigrateTable(srcConn, dstConn, t, bulk);
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
        }

        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost direct migration complete");
        Logger.Log($"  [OK]   Success : {totalOk} tables");
        if (totalFail > 0) Logger.Log($"  [FAIL] Failed  : {totalFail} tables");
        Logger.Log($"  [TIME] Total   : {sw.Elapsed:hh\\:mm\\:ss}");
        Logger.Log("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static void MigrateTable(
        Microsoft.Data.SqlClient.SqlConnection srcConn,
        Microsoft.Data.SqlClient.SqlConnection dstConn,
        TableRef t, bool bulk)
    {
        var fullName = $"[{t.Schema}].[{t.Name}]";
        var columns = Database.GetColumns(srcConn, t.Schema, t.Name);
        if (columns.Count == 0) throw new Exception("No columns found — table may not exist");

        var hasIdentity = columns.Any(c => c.IsIdentity);
        var colNames = string.Join(", ", columns.Select(c => $"[{c.ColName}]"));

        var createSql = SqlBuilder.BuildCreateTable(t.Schema, t.Name, columns);
        Database.ExecuteBatch(dstConn, createSql.Replace("\r\nGO", "").Replace("\nGO", "").Trim());
        Database.ExecuteBatch(dstConn, $"DELETE FROM {fullName}");

        if (bulk)
            MigrateTableBulk(srcConn, dstConn, t, fullName, colNames, columns, hasIdentity);
        else
            MigrateTableInsert(srcConn, dstConn, t, fullName, columns, colNames, hasIdentity);

        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [OK] {t.Schema}.{t.Name}");
    }

    private static void MigrateTableBulk(
        Microsoft.Data.SqlClient.SqlConnection srcConn,
        Microsoft.Data.SqlClient.SqlConnection dstConn,
        TableRef t, string fullName, string colNames,
        List<ColumnInfo> columns, bool hasIdentity)
    {
        using var cmd = srcConn.CreateCommand();
        cmd.CommandTimeout = 300;
        cmd.CommandText = t.Query
            ?? (t.Where != null
                ? $"SELECT {colNames} FROM {fullName} WHERE {t.Where}"
                : $"SELECT {colNames} FROM {fullName}");

        using var reader = cmd.ExecuteReader();

        var bulkOptions = hasIdentity
            ? Microsoft.Data.SqlClient.SqlBulkCopyOptions.KeepIdentity
            : Microsoft.Data.SqlClient.SqlBulkCopyOptions.Default;

        using var bulkCopy = new Microsoft.Data.SqlClient.SqlBulkCopy(dstConn, bulkOptions, null)
        {
            DestinationTableName = fullName,
            BatchSize = 1000,
            BulkCopyTimeout = 300,
            NotifyAfter = 1000,
        };

        bulkCopy.SqlRowsCopied += (_, e) =>
            Logger.Log($"       [{DateTime.Now:HH:mm:ss}]     {e.RowsCopied} rows copied...");

        foreach (var col in reader.GetColumnSchema())
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        bulkCopy.WriteToServer(reader);
    }

    private static void MigrateTableInsert(
        Microsoft.Data.SqlClient.SqlConnection srcConn,
        Microsoft.Data.SqlClient.SqlConnection dstConn,
        TableRef t, string fullName, List<ColumnInfo> columns,
        string colNames, bool hasIdentity)
    {
        if (hasIdentity)
            Database.ExecuteBatch(dstConn,
                $"IF OBJECTPROPERTY(OBJECT_ID('{t.Schema}.{t.Name}'), 'TableHasIdentity') = 1 " +
                $"SET IDENTITY_INSERT {fullName} ON");

        const int BatchSize = 100;
        int rowCount = 0;
        var insertBuffer = new List<string>(BatchSize);

        void FlushBuffer()
        {
            if (insertBuffer.Count == 0) return;
            Database.ExecuteBatch(dstConn, string.Join("\n", insertBuffer));
            insertBuffer.Clear();
        }

        void OnRow(Microsoft.Data.SqlClient.SqlDataReader reader)
        {
            insertBuffer.Add(SqlBuilder.BuildInsert(t.Schema, t.Name, columns, reader));
            rowCount++;
            if (insertBuffer.Count >= BatchSize) FlushBuffer();
        }

        if (t.Query != null) Database.StreamCustomQuery(srcConn, t.Query, OnRow);
        else Database.StreamRows(srcConn, t.Schema, t.Name, colNames, t.Where, OnRow);
        FlushBuffer();

        if (hasIdentity)
            Database.ExecuteBatch(dstConn,
                $"IF OBJECTPROPERTY(OBJECT_ID('{t.Schema}.{t.Name}'), 'TableHasIdentity') = 1 " +
                $"SET IDENTITY_INSERT {fullName} OFF");
    }
}