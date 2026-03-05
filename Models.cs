using System.Text.Json.Serialization;

namespace Bifrost;

public class ConnectionConfig
{
    [JsonPropertyName("server")]       public string Server                { get; set; } = "";
    [JsonPropertyName("port")]         public int    Port                  { get; set; } = 1433;
    [JsonPropertyName("username")]     public string Username              { get; set; } = "";
    [JsonPropertyName("password")]     public string Password              { get; set; } = "";
    [JsonPropertyName("encrypt")]      public bool   Encrypt               { get; set; } = true;
    [JsonPropertyName("trustServerCertificate")] public bool TrustServerCertificate { get; set; } = false;

    public string BuildConnectionString(string database) =>
        $"Server={Server},{Port};Database={database};User Id={Username};Password={Password};" +
        $"Encrypt={Encrypt};TrustServerCertificate={TrustServerCertificate};" +
        $"Connection Timeout=30;Command Timeout=300;";
}

public class TableOverride
{
    [JsonPropertyName("name")]   public string  Name   { get; set; } = "";
    [JsonPropertyName("ignore")] public bool?   Ignore { get; set; }
    [JsonPropertyName("where")]  public string? Where  { get; set; }
    [JsonPropertyName("query")]  public string? Query  { get; set; }
}

public class DbEntry
{
    [JsonPropertyName("comment")]        public string?         Comment        { get; set; }
    [JsonPropertyName("sourceDatabase")] public string          SourceDatabase { get; set; } = "";
    [JsonPropertyName("targetDatabase")] public string          TargetDatabase { get; set; } = "";
    [JsonPropertyName("tableFilter")]    public string          TableFilter    { get; set; } = "all";
    [JsonPropertyName("tenantId")]       public string?         TenantId       { get; set; }
    [JsonPropertyName("tables")]         public List<JsonTable>? Tables        { get; set; }
    [JsonPropertyName("overrides")]      public List<TableOverride>? Overrides { get; set; }
}

// Tables in "explicit" mode can be a string or an object
[JsonConverter(typeof(JsonTableConverter))]
public class JsonTable
{
    public string  Name   { get; set; } = "";
    public bool?   Ignore { get; set; }
    public string? Where  { get; set; }
    public string? Query  { get; set; }
}

public class MigrationConfig
{
    [JsonPropertyName("databases")] public List<DbEntry> Databases { get; set; } = [];
}

public class TableRef
{
    public string  Schema { get; set; } = "dbo";
    public string  Name   { get; set; } = "";
    public bool    Ignore { get; set; }
    public string? Where  { get; set; }
    public string? Query  { get; set; }
}

public class ColumnInfo
{
    public string  ColName      { get; set; } = "";
    public string  DataType     { get; set; } = "";
    public int?    MaxLength    { get; set; }
    public int?    Precision    { get; set; }
    public int?    Scale        { get; set; }
    public bool    IsNullable   { get; set; }
    public bool    IsIdentity   { get; set; }
    public bool    IsPrimaryKey { get; set; }
    public string? DefaultValue { get; set; }
}

public class ManifestDb
{
    public string       SourceDatabase { get; set; } = "";
    public string       TargetDatabase { get; set; } = "";
    public List<string> Files          { get; set; } = [];
}

public class Manifest
{
    public string          ExportedAt { get; set; } = "";
    public string          Server     { get; set; } = "";
    public List<ManifestDb> Databases { get; set; } = [];
}
