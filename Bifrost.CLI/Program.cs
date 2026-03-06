using Bifrost.Core;
using System.Text.Json;
using System.CommandLine;

Logger.UseConsole();

MigrationConfig LoadConfig(string path)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"[FAIL] Config not found: {path}"); Environment.Exit(1); }
    return JsonSerializer.Deserialize<MigrationConfig>(File.ReadAllText(path))
        ?? throw new Exception("Failed to parse config file");
}

bool Confirm(MigrationConfig cfg, string mode)
{
    Console.WriteLine();
    Console.WriteLine("  Source  : " + cfg.Source.Server);
    Console.WriteLine("  Target  : " + cfg.Target.Server);
    Console.WriteLine("  Mode    : " + mode);
    Console.WriteLine();
    Console.WriteLine("  Databases:");
    foreach (var db in cfg.Databases)
    {
        var comment = db.Comment != null ? $"  ({db.Comment})" : "";
        Console.WriteLine($"    {db.SourceDatabase} -> {db.TargetDatabase}{comment}");
    }
    Console.WriteLine();
    Console.Write("  Confirm? [y/N] ");
    var input = Console.ReadLine()?.Trim().ToLower();
    Console.WriteLine();
    return input == "y" || input == "yes";
}

var rootCmd = new RootCommand("Bifrost — SQL Server migration tool");

// ── export ───────────────────────────────────────────────────────────────────
var exportCmd = new Command("export", "Export tables from source database to .sql files");
var exportConfig = new Option<string>("--config", () => "config.json", "Config file");
var exportOutput = new Option<string>("--output", () => "output", "Output directory");
exportCmd.AddOption(exportConfig);
exportCmd.AddOption(exportOutput);
exportCmd.SetHandler((config, output) =>
{
    var cfg = LoadConfig(config);
    if (!Confirm(cfg, "export")) { Console.WriteLine("  Aborted."); return; }
    Environment.Exit(Exporter.Run(cfg, output));
}, exportConfig, exportOutput);

// ── import ───────────────────────────────────────────────────────────────────
var importCmd = new Command("import", "Import .sql files into target database");
var importConfig = new Option<string>("--config", () => "config.json", "Config file");
var importOutput = new Option<string>("--output", () => "output", "Output directory");
importCmd.AddOption(importConfig);
importCmd.AddOption(importOutput);
importCmd.SetHandler((config, output) =>
{
    var cfg = LoadConfig(config);
    if (!Confirm(cfg, "import")) { Console.WriteLine("  Aborted."); return; }
    Environment.Exit(Importer.Run(cfg, output));
}, importConfig, importOutput);

// ── direct ───────────────────────────────────────────────────────────────────
var directCmd = new Command("direct", "Migrate directly from source to target");
var directConfig = new Option<string>("--config", () => "config.json", "Config file");
var directBulk = new Option<bool>("--bulk", () => false, "Use SqlBulkCopy for faster transfers");
directCmd.AddOption(directConfig);
directCmd.AddOption(directBulk);
directCmd.SetHandler((config, bulk) =>
{
    var cfg = LoadConfig(config);
    if (!Confirm(cfg, bulk ? "direct (bulk)" : "direct")) { Console.WriteLine("  Aborted."); return; }
    Environment.Exit(Migrator.Run(cfg, bulk));
}, directConfig, directBulk);

rootCmd.AddCommand(exportCmd);
rootCmd.AddCommand(importCmd);
rootCmd.AddCommand(directCmd);

return await rootCmd.InvokeAsync(args);