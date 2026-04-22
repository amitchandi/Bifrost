using System.Text.Json.Serialization;

namespace Bifrost.Core;

public class ConnectionConfig
{
    [JsonPropertyName("server")] public string Server { get; set; } = "";
    [JsonPropertyName("port")] public int Port { get; set; } = 1433;
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("password")] public string Password { get; set; } = "";
    [JsonPropertyName("encrypt")] public bool Encrypt { get; set; } = true;
    [JsonPropertyName("trustServerCertificate")] public bool TrustServerCertificate { get; set; } = false;

    public string BuildConnectionString(string database) =>
        $"Server={Server},{Port};Database={database};User Id={Username};Password={Password};" +
        $"Encrypt={Encrypt};TrustServerCertificate={TrustServerCertificate};" +
        $"Connection Timeout=30;Command Timeout=300;";

    public string DisplayName => Server;
}

public class TableOverride
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("targetName")] public string? TargetName { get; set; }
    [JsonPropertyName("ignore")] public bool? Ignore { get; set; }
    [JsonPropertyName("where")] public string? Where { get; set; }
    [JsonPropertyName("query")] public string? Query { get; set; }
}

public class DbEntry
{
    [JsonPropertyName("comment")] public string? Comment { get; set; }
    [JsonPropertyName("sourceDatabase")] public string SourceDatabase { get; set; } = "";
    [JsonPropertyName("targetDatabase")] public string TargetDatabase { get; set; } = "";
    [JsonPropertyName("tableFilter")] public string TableFilter { get; set; } = "all";
    [JsonPropertyName("tenantId")] public string? TenantId { get; set; }
    [JsonPropertyName("tables")] public List<JsonTable>? Tables { get; set; }
    [JsonPropertyName("overrides")] public List<TableOverride>? Overrides { get; set; }
    [JsonPropertyName("dropAndCreate")] public bool DropAndCreate { get; set; } = false;
}

[JsonConverter(typeof(JsonTableConverter))]
public class JsonTable
{
    public string Name { get; set; } = "";
    public string? TargetName { get; set; }
    public bool? Ignore { get; set; }
    public string? Where { get; set; }
    public string? Query { get; set; }
}

public class MigrationConfig
{
    [JsonPropertyName("source")] public ConnectionConfig Source { get; set; } = new();
    [JsonPropertyName("target")] public ConnectionConfig Target { get; set; } = new();
    [JsonPropertyName("databases")] public List<DbEntry> Databases { get; set; } = [];
    [JsonPropertyName("tenants")] public List<TenantEntry> Tenants { get; set; } = [];
}

public class TableRef
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = "";
    public string? TargetName { get; set; }  // if null, same as Name
    public bool Ignore { get; set; }
    public string? Where { get; set; }
    public string? Query { get; set; }

    /// <summary>The name to use on the target — falls back to Name if TargetName not set.</summary>
    public string EffectiveTargetName => TargetName ?? Name;
}

public class ColumnInfo
{
    public string ColName { get; set; } = "";
    public string DataType { get; set; } = "";
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsPrimaryKey { get; set; }
    public string? DefaultValue { get; set; }
}

public class ManifestDb
{
    public string SourceDatabase { get; set; } = "";
    public string TargetDatabase { get; set; } = "";
    public List<string> Files { get; set; } = [];
}

public class Manifest
{
    public string ExportedAt { get; set; } = "";
    public string Server { get; set; } = "";
    public List<ManifestDb> Databases { get; set; } = [];
}

public class TenantEntry
{
    [JsonPropertyName("tenantId")] public string TenantId { get; set; } = "";
    [JsonPropertyName("schema")] public string Schema { get; set; } = "";
    [JsonPropertyName("database")] public string Database { get; set; } = "";
    [JsonPropertyName("sourceSchema")] public string? SourceSchema { get; set; }
    [JsonPropertyName("createCompatibilityViews")] public bool CreateCompatibilityViews { get; set; } = false;
}