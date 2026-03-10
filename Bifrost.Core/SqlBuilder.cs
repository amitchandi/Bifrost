using System.Text;
using Microsoft.Data.SqlClient;

namespace Bifrost.Core;

public static class SqlBuilder
{
    public static string BuildCreateTable(string schema, string table, List<ColumnInfo> columns, bool dropAndCreate = false)
    {
        var sb = new StringBuilder();

        if (dropAndCreate)
        {
            sb.AppendLine($"IF OBJECT_ID(N'[{schema}].[{table}]', N'U') IS NOT NULL");
            sb.AppendLine($"    DROP TABLE [{schema}].[{table}];");
            sb.AppendLine("GO");
        }
        else
        {
            sb.AppendLine($"IF OBJECT_ID(N'[{schema}].[{table}]', N'U') IS NULL");
        }

        sb.AppendLine($"CREATE TABLE [{schema}].[{table}] (");
        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var comma = i < columns.Count - 1 ? "," : "";
            sb.AppendLine($"    [{col.ColName}] {BuildColumnDef(col)}{comma}");
        }
        sb.AppendLine(");");
        sb.AppendLine("GO");
        return sb.ToString();
    }

    private static string BuildColumnDef(ColumnInfo col)
    {
        var typeDef = col.DataType.ToUpper() switch
        {
            "VARCHAR" or "CHAR" or "BINARY" or "VARBINARY"
                => col.MaxLength == -1 ? $"{col.DataType}(MAX)"
                 : col.MaxLength.HasValue ? $"{col.DataType}({col.MaxLength})" : col.DataType,
            "NVARCHAR" or "NCHAR"
                => col.MaxLength == -1 ? $"{col.DataType}(MAX)"
                 : col.MaxLength.HasValue ? $"{col.DataType}({col.MaxLength})" : col.DataType,
            "DECIMAL" or "NUMERIC"
                => col.Precision.HasValue && col.Scale.HasValue
                    ? $"{col.DataType}({col.Precision},{col.Scale})" : col.DataType,
            "FLOAT" or "REAL"
                => col.Precision.HasValue ? $"{col.DataType}({col.Precision})" : col.DataType,
            _ => col.DataType,
        };

        var identity = col.IsIdentity ? " IDENTITY(1,1)" : "";
        var nullable = col.IsNullable ? " NULL" : " NOT NULL";
        var pk = col.IsPrimaryKey ? " PRIMARY KEY" : "";
        var defVal = !string.IsNullOrEmpty(col.DefaultValue) && !col.IsIdentity
                           ? $" DEFAULT {col.DefaultValue}" : "";

        return $"{typeDef}{identity}{nullable}{pk}{defVal}";
    }

    public static string EscapeValue(object? value, string dataType)
    {
        if (value == null || value == DBNull.Value) return "NULL";
        if (value is bool b) return b ? "1" : "0";
        if (value is byte or short or int or long or float or double or decimal)
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL";
        if (value is byte[] bytes) return "0x" + Convert.ToHexString(bytes);
        if (value is DateTime dt) return $"'{dt:yyyy-MM-ddTHH:mm:ss.fff}'";
        if (value is DateTimeOffset dto) return $"'{dto:yyyy-MM-ddTHH:mm:ss.fffzzz}'";

        var str = value.ToString() ?? "";
        str = str.Replace("'", "''");
        return dataType.ToUpper() is "NVARCHAR" or "NCHAR" or "NTEXT" or "XML"
            ? $"N'{str}'" : $"'{str}'";
    }

    public static string BuildInsert(string schema, string table, List<ColumnInfo> columns, SqlDataReader reader)
    {
        var colNames = string.Join(", ", columns.Select(c => $"[{c.ColName}]"));
        var vals = string.Join(", ", columns.Select((c, i) =>
            EscapeValue(reader.IsDBNull(i) ? null : reader.GetValue(i), c.DataType)));
        return $"INSERT INTO [{schema}].[{table}] ({colNames}) VALUES ({vals});";
    }
}