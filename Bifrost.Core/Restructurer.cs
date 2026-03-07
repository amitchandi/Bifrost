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

        var sw = Stopwatch.StartNew();
        int totalOk = 0;
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
        // 1. Transfer to new schema (keeps old name temporarily)
        Database.ExecuteBatch(conn,
            $"ALTER SCHEMA [{newSchema}] TRANSFER [{oldSchema}].[{oldName}]");

        // 2. Rename to strip tenant suffix
        if (!oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            if (TableExists(conn, newSchema, newName))
                throw new Exception($"Cannot rename: [{newSchema}].[{newName}] already exists");
            Database.ExecuteBatch(conn,
                $"EXEC sp_rename N'{newSchema}.{oldName}', N'{newName}', N'OBJECT'");
        }

        // 3. Optionally create compatibility view in original schema
        if (createCompatView)
        {
            Database.ExecuteBatch(conn,
                $"IF OBJECT_ID(N'[{oldSchema}].[{oldName}]', N'V') IS NOT NULL " +
                $"DROP VIEW [{oldSchema}].[{oldName}]; " +
                $"EXEC('CREATE VIEW [{oldSchema}].[{oldName}] AS SELECT * FROM [{newSchema}].[{newName}]')");
        }
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

    private static bool TableExists(SqlConnection conn, string schema, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{name}' AND TABLE_TYPE = 'BASE TABLE'";
        return (int)cmd.ExecuteScalar()! > 0;
    }

    private static string StripSuffix(string tableName, string tenantId)
    {
        // Try _142 first, then bare 142
        var withUnderscore = $"_{tenantId}";
        if (tableName.EndsWith(withUnderscore, StringComparison.OrdinalIgnoreCase))
            return tableName[..^withUnderscore.Length];
        if (tableName.EndsWith(tenantId, StringComparison.OrdinalIgnoreCase))
            return tableName[..^tenantId.Length];
        return tableName;
    }
}