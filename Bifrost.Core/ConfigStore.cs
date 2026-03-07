using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bifrost.Core;

public class NamedConfig
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("config")] public MigrationConfig Config { get; set; } = new();
}

public static class ConfigStore
{
    private static readonly string StorePath = Path.Combine(
        AppContext.BaseDirectory, "bifrost-configs.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static List<NamedConfig> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            return JsonSerializer.Deserialize<List<NamedConfig>>(File.ReadAllText(StorePath), JsonOpts) ?? [];
        }
        catch { return []; }
    }

    public static void Save(List<NamedConfig> configs)
    {
        File.WriteAllText(StorePath, JsonSerializer.Serialize(configs, JsonOpts));
    }
}