using System.CommandLine;
using Bifrost;

var rootCmd = new RootCommand("SQL Server migration tool");

// ── export ───────────────────────────────────────────────────────────────────
var exportCmd = new Command("export", "Export tables from source database to .sql files");
var exportConfig = new Option<string>("--config", () => "config.json", "Migration config file");
var exportConnection = new Option<string>("--connection", () => "source-connection.json", "Source connection file");
var exportOutput = new Option<string>("--output", () => "output", "Output directory");
exportCmd.AddOption(exportConfig);
exportCmd.AddOption(exportConnection);
exportCmd.AddOption(exportOutput);
exportCmd.SetHandler((config, connection, output) =>
    Environment.Exit(Exporter.Run(config, connection, output)),
    exportConfig, exportConnection, exportOutput);

// ── import ───────────────────────────────────────────────────────────────────
var importCmd = new Command("import", "Import .sql files into target database");
var importConnection = new Option<string>("--connection", () => "target-connection.json", "Target connection file");
var importOutput = new Option<string>("--output", () => "output", "Output directory containing _manifest.json");
importCmd.AddOption(importConnection);
importCmd.AddOption(importOutput);
importCmd.SetHandler((connection, output) =>
    Environment.Exit(Importer.Run(connection, output)),
    importConnection, importOutput);

// ── direct ───────────────────────────────────────────────────────────────────
var directCmd = new Command("direct", "Migrate directly from source to target (both must be reachable)");
var directConfig = new Option<string>("--config", () => "config.json", "Migration config file");
var directSource = new Option<string>("--source", () => "source-connection.json", "Source connection file");
var directTarget = new Option<string>("--target", () => "target-connection.json", "Target connection file");
var directBulk = new Option<bool>("--bulk", () => false, "Use SqlBulkCopy for faster transfers");
directCmd.AddOption(directConfig);
directCmd.AddOption(directSource);
directCmd.AddOption(directTarget);
directCmd.AddOption(directBulk);
directCmd.SetHandler((config, source, target, bulk) =>
    Environment.Exit(Migrator.Run(config, source, target, bulk)),
    directConfig, directSource, directTarget, directBulk);

// ── root ─────────────────────────────────────────────────────────────────────
rootCmd.AddCommand(exportCmd);
rootCmd.AddCommand(importCmd);
rootCmd.AddCommand(directCmd);

return await rootCmd.InvokeAsync(args);