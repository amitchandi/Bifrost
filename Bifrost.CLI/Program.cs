using System.CommandLine;
using System.Text.Json;
using Bifrost.Core;

Logger.UseConsole();

var rootCmd = new RootCommand("Bifrost — SQL Server migration tool");

var configOpt  = new Option<string>("--config",  () => "config.json", "Config file path");
var outputOpt  = new Option<string>("--output",  () => "output",      "Output directory");
var dryRunOpt  = new Option<bool>  ("--dry-run", () => false,         "Preview without making changes");

// ── export ───────────────────────────────────────────────────────────────────
var exportCmd = new Command("export", "Export tables to .sql files");
exportCmd.AddOption(configOpt);
exportCmd.AddOption(outputOpt);
exportCmd.AddOption(dryRunOpt);
exportCmd.SetHandler((config, output, dryRun) =>
{
    var cfg = LoadConfig(config);
    if (!Confirm(cfg, dryRun ? "dry-run export" : "export")) { Console.WriteLine("  Aborted."); return; }
    var result = Exporter.Run(cfg, output, dryRun);
    if (!dryRun) AppendHistory(cfg, config, "export", result);
    Environment.Exit(result);
}, configOpt, outputOpt, dryRunOpt);

// ── import ───────────────────────────────────────────────────────────────────
var importCmd = new Command("import", "Import .sql files into target database");
importCmd.AddOption(configOpt);
importCmd.AddOption(outputOpt);
importCmd.AddOption(dryRunOpt);
importCmd.SetHandler((config, output, dryRun) =>
{
    var cfg = LoadConfig(config);
    if (!Confirm(cfg, dryRun ? "dry-run import" : "import")) { Console.WriteLine("  Aborted."); return; }
    var result = Importer.Run(cfg, output, dryRun);
    if (!dryRun) AppendHistory(cfg, config, "import", result);
    Environment.Exit(result);
}, configOpt, outputOpt, dryRunOpt);

// ── direct ───────────────────────────────────────────────────────────────────
var directCmd = new Command("direct", "Migrate directly from source to target");
var bulkOpt   = new Option<bool>("--bulk", () => false, "Use SqlBulkCopy");
directCmd.AddOption(configOpt);
directCmd.AddOption(bulkOpt);
directCmd.AddOption(dryRunOpt);
directCmd.SetHandler((config, bulk, dryRun) =>
{
    var cfg  = LoadConfig(config);
    var name = (dryRun ? "dry-run " : "") + (bulk ? "direct (bulk)" : "direct");
    if (!Confirm(cfg, name)) { Console.WriteLine("  Aborted."); return; }
    var result = Migrator.Run(cfg, bulk, dryRun);
    if (!dryRun) AppendHistory(cfg, config, bulk ? "direct-bulk" : "direct", result);
    Environment.Exit(result);
}, configOpt, bulkOpt, dryRunOpt);

// ── restructure ───────────────────────────────────────────────────────────────
var restructureCmd = new Command("restructure", "Move tenant tables into named schemas");
restructureCmd.AddOption(configOpt);
restructureCmd.SetHandler((config) =>
{
    var cfg = LoadConfig(config);
    if (!Confirm(cfg, "restructure")) { Console.WriteLine("  Aborted."); return; }
    var result = Restructurer.Run(cfg);
    AppendHistory(cfg, config, "restructure", result);
    Environment.Exit(result);
}, configOpt);

// ── status ────────────────────────────────────────────────────────────────────
var statusCmd = new Command("status", "Show table counts and row differences between source and target");
statusCmd.AddOption(configOpt);
statusCmd.SetHandler((config) =>
{
    var cfg = LoadConfig(config);
    RunStatus(cfg);
}, configOpt);

rootCmd.AddCommand(exportCmd);
rootCmd.AddCommand(importCmd);
rootCmd.AddCommand(directCmd);
rootCmd.AddCommand(restructureCmd);
rootCmd.AddCommand(statusCmd);

return await rootCmd.InvokeAsync(args);

// ── helpers ───────────────────────────────────────────────────────────────────

static MigrationConfig LoadConfig(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException($"Config not found: {path}");
    return JsonSerializer.Deserialize<MigrationConfig>(File.ReadAllText(path))
        ?? throw new Exception("Failed to parse config");
}

static bool Confirm(MigrationConfig cfg, string operation)
{
    Console.WriteLine();
    Console.WriteLine($"  Operation : {operation}");
    Console.WriteLine($"  Source    : {cfg.Source.Server}");
    Console.WriteLine($"  Target    : {cfg.Target.Server}");
    Console.WriteLine($"  Databases : {string.Join(", ", cfg.Databases.Select(d => d.SourceDatabase))}");
    Console.WriteLine();
    Console.Write("  Proceed? [y/N] ");
    return Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ?? false;
}

static void AppendHistory(MigrationConfig cfg, string configPath, string operation, int result)
{
    try
    {
        RunHistory.Append(new RunRecord
        {
            ConfigName = Path.GetFileNameWithoutExtension(configPath),
            Operation  = operation,
            StartedAt  = DateTime.Now.ToString("O"),
            Success    = result == 0,
        });
    }
    catch { }
}

static void RunStatus(MigrationConfig cfg)
{
    Console.WriteLine();
    Console.WriteLine("============================================================");
    Console.WriteLine("  Bifrost — Status");
    Console.WriteLine("============================================================");
    Console.WriteLine();

    foreach (var entry in cfg.Databases)
    {
        Console.WriteLine($"  [DB] {entry.SourceDatabase} -> {entry.TargetDatabase}");

        try
        {
            using var srcConn = Database.Open(cfg.Source, entry.SourceDatabase);
            var srcTables = Database.GetTables(srcConn);
            Console.WriteLine($"    Source tables : {srcTables.Count}");
        }
        catch (Exception ex) { Console.WriteLine($"    [FAIL] Source: {ex.Message}"); }

        try
        {
            using var dstConn = Database.Open(cfg.Target, entry.TargetDatabase);
            var dstTables = Database.GetTables(dstConn);
            Console.WriteLine($"    Target tables : {dstTables.Count}");
        }
        catch (Exception ex) { Console.WriteLine($"    [FAIL] Target: {ex.Message}"); }

        Console.WriteLine();
    }

    Console.WriteLine("  Run history (last 5):");
    var history = RunHistory.Load().Take(5);
    foreach (var r in history)
        Console.WriteLine($"    {r.StartedAt[..19]}  {r.Operation,-15} {r.ConfigName,-20} {(r.Success ? "[OK]" : "[FAIL]")}");

    Console.WriteLine();
    Console.WriteLine("============================================================");
}
