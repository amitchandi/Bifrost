using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Bifrost.Core;

public static class Restructurer
{
    public static int Run(MigrationConfig config)
    {
        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log("  Bifrost — Restructure");
        Logger.Log("============================================================");
        Logger.Log("");

        if (config.Tenants.Count == 0)
        {
            Logger.Log("[FAIL] No tenants defined in config.");
            return 1;
        }

        var sw        = Stopwatch.StartNew();
        int totalOk   = 0;
        int totalFail = 0;

        foreach (var tenant in config.Tenants)
        {
            Logger.Log($"\n  [TENANT] {tenant.TenantId} -> schema [{tenant.Schema}] in {tenant.Database}");

            try
            {
                using var conn = Database.Open(config.Target, tenant.Database);

                // Ensure target schema exists
                Database.ExecuteBatch(conn,
                    $"IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{tenant.Schema}') " +
                    $"EXEC('CREATE SCHEMA [{tenant.Schema}]')");
                Logger.Log($"    [{DateTime.Now:HH:mm:ss}] Schema [{tenant.Schema}] ready");

                var effectiveSource = string.IsNullOrWhiteSpace(tenant.SourceSchema) ? null : tenant.SourceSchema;
                Logger.Log($"    [{DateTime.Now:HH:mm:ss}] Mode: {(effectiveSource != null ? $"schema move from [{effectiveSource}]" : $"tenant suffix match [{tenant.TenantId}]")}");

                var tables = effectiveSource != null
                    ? GetSchemaTable(conn, effectiveSource)
                    : GetTenantTables(conn, tenant.TenantId);
                Logger.Log($"    Tables found: {tables.Count}");

                // ── Conflict detection ────────────────────────────────────────
                var conflicts = new List<string>();
                foreach (var (schema, oldName) in tables)
                {
                    var newName = effectiveSource != null
                        ? oldName
                        : StripSuffix(oldName, tenant.TenantId);

                    if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase)
                        && TableExists(conn, tenant.Schema, newName))
                        conflicts.Add($"[{tenant.Schema}].[{newName}] (from [{schema}].[{oldName}])");
                }

                if (conflicts.Count > 0)
                {
                    Logger.Log($"    [FAIL] Conflict detected — the following target tables already exist:");
                    foreach (var c in conflicts) Logger.Log($"       {c}");
                    Logger.Log($"    Aborting tenant {tenant.TenantId} to prevent data loss.");
                    totalFail += tables.Count;
                    continue;
                }

                // ── Execute with rollback log ─────────────────────────────────
                var rollbackLog = new List<(string OldSchema, string OldName, string NewSchema, string NewName)>();

                foreach (var (schema, oldName) in tables)
                {
                    var newName = effectiveSource != null
                        ? oldName
                        : StripSuffix(oldName, tenant.TenantId);

                    Logger.Log($"    [{DateTime.Now:HH:mm:ss}] -> [{schema}].[{oldName}] => [{tenant.Schema}].[{newName}]...");

                    try
                    {
                        RestructureTable(conn, schema, oldName, tenant.Schema, newName,
                            tenant.CreateCompatibilityViews);
                        rollbackLog.Add((schema, oldName, tenant.Schema, newName));
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [OK]");
                        totalOk++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [FAIL] {ex.Message}");
                        Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [WARN] Attempting rollback of {rollbackLog.Count} completed tables...");

                        // Roll back completed tables in reverse order
                        foreach (var (rs, rOld, rNew, rNewName) in Enumerable.Reverse(rollbackLog))
                        {
                            try
                            {
                                // Reverse: move back and rename back
                                if (!rOld.Equals(rNewName, StringComparison.OrdinalIgnoreCase))
                                    Database.ExecuteBatch(conn, $"EXEC sp_rename N'{rNew}.{rNewName}', N'{rOld}', N'OBJECT'");
                                Database.ExecuteBatch(conn, $"ALTER SCHEMA [{rs}] TRANSFER [{rNew}].[{rOld}]");
                                Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [WARN] Rolled back [{rNew}].[{rNewName}] -> [{rs}].[{rOld}]");
                            }
                            catch (Exception rex)
                            {
                                Logger.Log($"       [{DateTime.Now:HH:mm:ss}] [FAIL] Rollback failed for [{rNew}].[{rNewName}]: {rex.Message}");
                            }
                        }

                        totalFail++;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"  [FAIL] Tenant {tenant.TenantId}: {ex.Message}");
                totalFail++;
            }
        }

        Logger.Log("");
        Logger.Log("============================================================");
        Logger.Log($"  Bifrost restructure complete");
        Logger.Log($"  [OK]   Success : {totalOk} tables");
        if (totalFail > 0) Logger.Log($"  [FAIL] Failed  : {totalFail} tables");
        Logger.Log($"  [TIME] Total   : {sw.Elapsed:hh\\:mm\\:ss}");
        Logger.Log("============================================================");

        return totalFail > 0 ? 1 : 0;
    }

    private static void RestructureTable(
        SqlConnection conn,
        string oldSchema, string oldName,
        string newSchema, string newName,
        bool createCompatView)
    {
        Database.ExecuteBatch(conn,
            $"ALTER SCHEMA [{newSchema}] TRANSFER [{oldSchema}].[{oldName}]");

        if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
            Database.ExecuteBatch(conn,
                $"EXEC sp_rename N'{newSchema}.{oldName}', N'{newName}', N'OBJECT'");

        if (createCompatView)
            Database.ExecuteBatch(conn,
                $"IF OBJECT_ID(N'[{oldSchema}].[{oldName}]', N'V') IS NOT NULL " +
                $"DROP VIEW [{oldSchema}].[{oldName}]; " +
                $"EXEC('CREATE VIEW [{oldSchema}].[{oldName}] AS SELECT * FROM [{newSchema}].[{newName}]')");
    }

    private static bool TableExists(SqlConnection conn, string schema, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES " +
                          $"WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{name}' AND TABLE_TYPE = 'BASE TABLE'";
        return (int)cmd.ExecuteScalar()! > 0;
    }

    private static List<(string Schema, string Name)> GetSchemaTable(SqlConnection conn, string schema)
    {
        var tables = new List<(string, string)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND TABLE_SCHEMA = '{schema}'
            ORDER BY TABLE_NAME
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add((reader.GetString(0), reader.GetString(1)));
        return tables;
    }

    private static List<(string Schema, string Name)> GetTenantTables(SqlConnection conn, string tenantId)
    {
        var tables = new List<(string, string)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND (TABLE_NAME LIKE '%_' + '{tenantId}' OR TABLE_NAME LIKE '%{tenantId}')
              AND TABLE_NAME != '{tenantId}'
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add((reader.GetString(0), reader.GetString(1)));
        return tables;
    }

    private static string StripSuffix(string tableName, string tenantId)
    {
        var withUnderscore = $"_{tenantId}";
        if (tableName.EndsWith(withUnderscore, StringComparison.OrdinalIgnoreCase))
            return tableName[..^withUnderscore.Length];
        if (tableName.EndsWith(tenantId, StringComparison.OrdinalIgnoreCase))
            return tableName[..^tenantId.Length];
        return tableName;
    }
}
