using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bifrost.Core;

public class RunRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("configName")] public string ConfigName { get; set; } = "";
    [JsonPropertyName("operation")] public string Operation { get; set; } = "";
    [JsonPropertyName("startedAt")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("duration")] public string Duration { get; set; } = "";
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("log")] public List<string> Log { get; set; } = [];
}

public static class RunHistory
{
    private static readonly string HistoryPath = Path.Combine(
        AppContext.BaseDirectory, "bifrost-history.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// Returns true if a log line should be saved to history.
    /// Skips SqlBulkCopy progress lines and blank lines.
    /// </summary>
    public static bool ShouldSaveLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.Contains("rows copied...")) return false;
        return true;
    }

    public static List<RunRecord> Load()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return [];
            return JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(HistoryPath), JsonOpts) ?? [];
        }
        catch { return []; }
    }

    public static void Save(List<RunRecord> records)
    {
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(records, JsonOpts));
    }

    public static void Append(RunRecord record)
    {
        var history = Load();
        history.Insert(0, record);
        if (history.Count > 100) history = history.Take(100).ToList();
        Save(history);
    }

    public static void Delete(IEnumerable<string> ids)
    {
        var history = Load();
        var idSet = ids.ToHashSet();
        Save(history.Where(r => !idSet.Contains(r.Id)).ToList());
    }
}