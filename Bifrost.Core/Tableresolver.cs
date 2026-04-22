using Microsoft.Data.SqlClient;

namespace Bifrost.Core;

public static class TableResolver
{
    public static List<TableRef> Resolve(SqlConnection conn, DbEntry entry)
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
                        TargetName = t.TargetName,
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
                    TargetName = ov.TargetName,
                    Ignore = ov.Ignore ?? false,
                    Where = ov.Where,
                    Query = ov.Query,
                };
            }).ToList();
        }

        return tables;
    }
}