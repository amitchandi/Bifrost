using System.Text.Json;
using Bifrost.Core;
using Xunit;

namespace Bifrost.Tests;

public class ConfigStoreTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static MigrationConfig MakeSampleConfig() => new()
    {
        Source = new ConnectionConfig
        {
            Server   = "localhost",
            Port     = 1433,
            Username = "sa",
            Password = "P@ssword123",
            Encrypt  = false,
            TrustServerCertificate = true,
        },
        Target = new ConnectionConfig
        {
            Server   = "localhost",
            Port     = 1433,
            Username = "sa",
            Password = "P@ssword123",
            Encrypt  = false,
            TrustServerCertificate = true,
        },
        Databases =
        [
            new DbEntry
            {
                SourceDatabase = "MainAppDB_Prod",
                TargetDatabase = "MainAppDB_Staging",
                TableFilter    = "tenant",
                TenantId       = "142",
                Comment        = "Test entry",
            }
        ],
        Tenants =
        [
            new TenantEntry
            {
                TenantId                 = "142",
                Schema                   = "WarehouseCo",
                Database                 = "MainAppDB_Staging",
                CreateCompatibilityViews = true,
            }
        ]
    };

    [Fact]
    public void Serialize_ThenDeserialize_PreservesSourceServer()
    {
        var config  = MakeSampleConfig();
        var json    = JsonSerializer.Serialize(config, JsonOpts);
        var result  = JsonSerializer.Deserialize<MigrationConfig>(json);
        Assert.Equal(config.Source.Server, result!.Source.Server);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesTargetServer()
    {
        var config  = MakeSampleConfig();
        var json    = JsonSerializer.Serialize(config, JsonOpts);
        var result  = JsonSerializer.Deserialize<MigrationConfig>(json);
        Assert.Equal(config.Target.Server, result!.Target.Server);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesDatabaseCount()
    {
        var config  = MakeSampleConfig();
        var json    = JsonSerializer.Serialize(config, JsonOpts);
        var result  = JsonSerializer.Deserialize<MigrationConfig>(json);
        Assert.Equal(config.Databases.Count, result!.Databases.Count);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesSourceDatabase()
    {
        var config  = MakeSampleConfig();
        var json    = JsonSerializer.Serialize(config, JsonOpts);
        var result  = JsonSerializer.Deserialize<MigrationConfig>(json);
        Assert.Equal(config.Databases[0].SourceDatabase, result!.Databases[0].SourceDatabase);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesTenantId()
    {
        var config  = MakeSampleConfig();
        var json    = JsonSerializer.Serialize(config, JsonOpts);
        var result  = JsonSerializer.Deserialize<MigrationConfig>(json);
        Assert.Equal(config.Databases[0].TenantId, result!.Databases[0].TenantId);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesTenantSchema()
    {
        var config  = MakeSampleConfig();
        var json    = JsonSerializer.Serialize(config, JsonOpts);
        var result  = JsonSerializer.Deserialize<MigrationConfig>(json);
        Assert.Equal(config.Tenants[0].Schema, result!.Tenants[0].Schema);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesCompatibilityViews()
    {
        var config  = MakeSampleConfig();
        var json    = JsonSerializer.Serialize(config, JsonOpts);
        var result  = JsonSerializer.Deserialize<MigrationConfig>(json);
        Assert.Equal(config.Tenants[0].CreateCompatibilityViews, result!.Tenants[0].CreateCompatibilityViews);
    }

    [Fact]
    public void NamedConfig_Serialize_ThenDeserialize_PreservesName()
    {
        var named  = new NamedConfig { Name = "Test Config", Config = MakeSampleConfig() };
        var list   = new List<NamedConfig> { named };
        var json   = JsonSerializer.Serialize(list, JsonOpts);
        var result = JsonSerializer.Deserialize<List<NamedConfig>>(json);
        Assert.Equal(named.Name, result![0].Name);
    }

    [Fact]
    public void ConnectionConfig_BuildConnectionString_ContainsServer()
    {
        var conn = new ConnectionConfig { Server = "myserver", Port = 1433, Username = "sa", Password = "pw" };
        var cs   = conn.BuildConnectionString("mydb");
        Assert.Contains("myserver", cs);
    }

    [Fact]
    public void ConnectionConfig_BuildConnectionString_ContainsDatabase()
    {
        var conn = new ConnectionConfig { Server = "myserver", Port = 1433, Username = "sa", Password = "pw" };
        var cs   = conn.BuildConnectionString("mydb");
        Assert.Contains("mydb", cs);
    }
}
