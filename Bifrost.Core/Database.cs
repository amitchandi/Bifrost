using Microsoft.Data.SqlClient;

namespace Bifrost.Core;

public static class Database
{
    public static SqlConnection Open(ConnectionConfig conn, string database)
    {
        var sqlConn = new SqlConnection(conn.BuildConnectionString(database));
        sqlConn.Open();
        return sqlConn;
    }

    public static List<TableRef> GetTables(SqlConnection conn)
    {
        var tables = new List<TableRef>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add(new TableRef { Schema = reader.GetString(0), Name = reader.GetString(1) });
        return tables;
    }

    public static List<TableRef> GetTenantTables(SqlConnection conn, string tenantId)
    {
        var tables = new List<TableRef>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
              AND TABLE_NAME LIKE '%{tenantId}'
            ORDER BY TABLE_SCHEMA, TABLE_NAME
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add(new TableRef { Schema = reader.GetString(0), Name = reader.GetString(1) });
        return tables;
    }

    public static List<ColumnInfo> GetColumns(SqlConnection conn, string schema, string table)
    {
        var columns = new List<ColumnInfo>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                c.IS_NULLABLE,
                c.COLUMN_DEFAULT,
                CASE WHEN ic.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_identity,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS is_pk
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN sys.identity_columns ic
                ON ic.object_id = OBJECT_ID('{schema}.{table}')
                AND ic.name = c.COLUMN_NAME
            LEFT JOIN (
                SELECT kcu.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                    ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
                    AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA
                    AND kcu.TABLE_NAME = tc.TABLE_NAME
                WHERE tc.TABLE_SCHEMA = '{schema}'
                  AND tc.TABLE_NAME = '{table}'
                  AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON pk.COLUMN_NAME = c.COLUMN_NAME
            WHERE c.TABLE_SCHEMA = '{schema}'
              AND c.TABLE_NAME = '{table}'
            ORDER BY c.ORDINAL_POSITION
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(new ColumnInfo
            {
                ColName = reader.GetString(0),
                DataType = reader.GetString(1),
                MaxLength = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Precision = reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)),
                Scale = reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                IsNullable = reader.GetString(5) == "YES",
                DefaultValue = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsIdentity = reader.GetInt32(7) == 1,
                IsPrimaryKey = reader.GetInt32(8) == 1,
            });
        }
        return columns;
    }

    public static void StreamRows(
        SqlConnection conn, string schema, string table,
        string columns, string? whereClause, Action<SqlDataReader> onRow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 300;
        cmd.CommandText = string.IsNullOrEmpty(whereClause)
            ? $"SELECT {columns} FROM [{schema}].[{table}] ORDER BY (SELECT NULL)"
            : $"SELECT {columns} FROM [{schema}].[{table}] WHERE {whereClause}";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) onRow(reader);
    }

    public static void StreamCustomQuery(SqlConnection conn, string query, Action<SqlDataReader> onRow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 300;
        cmd.CommandText = query;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) onRow(reader);
    }

    public static void ExecuteBatch(SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 300;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static void ExecuteBatchInTx(SqlConnection conn, SqlTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandTimeout = 300;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static void EnsureDatabase(ConnectionConfig conn, string database)
    {
        using var master = Open(conn, "master");
        ExecuteBatch(master,
            $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{database}') " +
            $"CREATE DATABASE [{database}]");
    }
}