using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bifrost.Core;

public class RunRecord
{
    [JsonPropertyName("id")]         public string Id          { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("configName")] public string ConfigName  { get; set; } = "";
    [JsonPropertyName("operation")]  public string Operation   { get; set; } = "";
    [JsonPropertyName("startedAt")]  public string StartedAt   { get; set; } = "";
    [JsonPropertyName("duration")]   public string Duration    { get; set; } = "";
    [JsonPropertyName("success")]    public bool   Success     { get; set; }
    [JsonPropertyName("okCount")]    public int    OkCount     { get; set; }
    [JsonPropertyName("failCount")]  public int    FailCount   { get; set; }
    [JsonPropertyName("rowCount")]   public long   RowCount    { get; set; }
}

public static class RunHistory
{
    private static readonly string HistoryPath = Path.Combine(
        AppContext.BaseDirectory, "bifrost-history.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static List<RunRecord> Load()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return [];
            return JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(HistoryPath), JsonOpts) ?? [];
        }
        catch { return []; }
    }

    public static void Append(RunRecord record)
    {
        var history = Load();
        history.Insert(0, record);
        // Keep last 100 runs
        if (history.Count > 100) history = history.Take(100).ToList();
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(history, JsonOpts));
    }
}
