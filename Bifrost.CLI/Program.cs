using System.CommandLine;
using System.Text.Json;
using Bifrost.Core;

Logger.UseConsole();

var rootCmd = new RootCommand("Bifrost — SQL Server migration tool");

// ── Shared options ────────────────────────────────────────────────────────────

var configOpt = new Option<string?>("--config", () => null, "Config file path (if omitted, use inline connection args)");
var outputOpt = new Option<string>("--output", () => "output", "Output directory");
var dryRunOpt = new Option<bool>("--dry-run", () => false, "Preview without making changes");
var bulkOpt = new Option<bool>("--bulk", () => false, "Use SqlBulkCopy");
var dropCreateOpt = new Option<bool>("--drop-and-create", () => false, "Drop and recreate target tables");
var noHistoryOpt = new Option<bool>("--no-history", () => false, "Do not record this run in history");
var appendOnlyOpt = new Option<bool>("--append-only", () => false, "Insert into target without deleting existing rows");

// ── Inline connection options ─────────────────────────────────────────────────

var srcServerOpt = new Option<string?>("--source-server", () => null, "Source server");
var srcPortOpt = new Option<int>("--source-port", () => 1433, "Source port");
var srcUserOpt = new Option<string?>("--source-user", () => null, "Source username");
var srcPassOpt = new Option<string?>("--source-pass", () => null, "Source password");
var srcDbOpt = new Option<string?>("--source-database", () => null, "Source database");
var tgtServerOpt = new Option<string?>("--target-server", () => null, "Target server (defaults to source server for same-instance transfers)");
var tgtPortOpt = new Option<int>("--target-port", () => 1433, "Target port");
var tgtUserOpt = new Option<string?>("--target-user", () => null, "Target username (defaults to source username)");
var tgtPassOpt = new Option<string?>("--target-pass", () => null, "Target password (defaults to source password)");
var tgtDbOpt = new Option<string?>("--target-database", () => null, "Target database");
var tableFilterOpt = new Option<string>("--table-filter", () => "all", "Table filter: all, tenant, explicit");
var tenantIdOpt = new Option<string?>("--tenant-id", () => null, "Tenant ID (required when --table-filter tenant)");
var srcTableOpt = new Option<string?>("--source-table", () => null, "Single source table (e.g. dbo.Orders) — implies explicit filter");
var tgtTableOpt = new Option<string?>("--target-table", () => null, "Target table name override (e.g. dbo.OrdersArchive)");
var whereOpt = new Option<string?>("--where", () => null, "WHERE clause to filter rows from the source table");
var queryOpt = new Option<string?>("--query", () => null, "Custom SELECT query to read from the source table");

// ── Config builder ────────────────────────────────────────────────────────────

static MigrationConfig LoadConfig(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException($"Config not found: {path}");
    return JsonSerializer.Deserialize<MigrationConfig>(File.ReadAllText(path))
        ?? throw new Exception("Failed to parse config");
}

static MigrationConfig BuildInlineConfig(
    string? srcServer, int srcPort, string? srcUser, string? srcPass, string? srcDb,
    string? tgtServer, int tgtPort, string? tgtUser, string? tgtPass, string? tgtDb,
    string tableFilter, string? tenantId,
    string? srcTable, string? tgtTable,
    string? where, string? query,
    bool dropAndCreate,
    bool appendOnly = false)
{
    if (srcServer is null) throw new Exception("--source-server is required when not using --config");
    if (srcDb is null) throw new Exception("--source-database is required when not using --config");
    if (tgtDb is null) throw new Exception("--target-database is required when not using --config");

    // Same-instance transfer: target defaults to source connection
    var src = new ConnectionConfig
    {
        Server = srcServer,
        Port = srcPort,
        Username = srcUser ?? "",
        Password = srcPass ?? "",
        Encrypt = false,
        TrustServerCertificate = true,
    };

    var tgt = new ConnectionConfig
    {
        Server = tgtServer ?? srcServer,
        Port = tgtPort,
        Username = tgtUser ?? srcUser ?? "",
        Password = tgtPass ?? srcPass ?? "",
        Encrypt = false,
        TrustServerCertificate = true,
    };

    List<JsonTable>? tables = null;
    if (srcTable is not null)
    {
        tableFilter = "explicit";
        tables = [new JsonTable { Name = srcTable, TargetName = tgtTable, Where = where, Query = query }];
    }

    var db = new DbEntry
    {
        SourceDatabase = srcDb,
        TargetDatabase = tgtDb,
        TableFilter = tableFilter,
        TenantId = tenantId,
        Tables = tables,
        DropAndCreate = dropAndCreate,
        AppendOnly = appendOnly,
    };

    return new MigrationConfig { Source = src, Target = tgt, Databases = [db] };
}

static (MigrationConfig cfg, string label) Resolve(
    string? configPath,
    string? srcServer, int srcPort, string? srcUser, string? srcPass, string? srcDb,
    string? tgtServer, int tgtPort, string? tgtUser, string? tgtPass, string? tgtDb,
    string tableFilter, string? tenantId,
    string? srcTable, string? tgtTable,
    string? where, string? query,
    bool dropAndCreate,
    bool appendOnly = false)
{
    if (configPath is not null)
        return (LoadConfig(configPath), Path.GetFileNameWithoutExtension(configPath));

    var cfg = BuildInlineConfig(
        srcServer, srcPort, srcUser, srcPass, srcDb,
        tgtServer, tgtPort, tgtUser, tgtPass, tgtDb,
        tableFilter, tenantId, srcTable, tgtTable, dropAndCreate);

    return (cfg, "inline");
}

// ── export ────────────────────────────────────────────────────────────────────

var exportCmd = new Command("export", "Export tables to .sql files");
exportCmd.AddOption(configOpt); exportCmd.AddOption(outputOpt); exportCmd.AddOption(dryRunOpt);
exportCmd.AddOption(dropCreateOpt); exportCmd.AddOption(appendOnlyOpt); exportCmd.AddOption(srcServerOpt); exportCmd.AddOption(srcPortOpt);
exportCmd.AddOption(srcUserOpt); exportCmd.AddOption(srcPassOpt); exportCmd.AddOption(srcDbOpt);
exportCmd.AddOption(tgtServerOpt); exportCmd.AddOption(tgtPortOpt); exportCmd.AddOption(tgtUserOpt);
exportCmd.AddOption(tgtPassOpt); exportCmd.AddOption(tgtDbOpt); exportCmd.AddOption(tableFilterOpt);
exportCmd.AddOption(tenantIdOpt); exportCmd.AddOption(srcTableOpt); exportCmd.AddOption(tgtTableOpt);
exportCmd.AddOption(whereOpt); exportCmd.AddOption(queryOpt);
exportCmd.AddOption(noHistoryOpt);

exportCmd.SetHandler((ctx) =>
{
    var (cfg, label) = Resolve(
        ctx.ParseResult.GetValueForOption(configOpt),
        ctx.ParseResult.GetValueForOption(srcServerOpt), ctx.ParseResult.GetValueForOption(srcPortOpt),
        ctx.ParseResult.GetValueForOption(srcUserOpt), ctx.ParseResult.GetValueForOption(srcPassOpt),
        ctx.ParseResult.GetValueForOption(srcDbOpt),
        ctx.ParseResult.GetValueForOption(tgtServerOpt), ctx.ParseResult.GetValueForOption(tgtPortOpt),
        ctx.ParseResult.GetValueForOption(tgtUserOpt), ctx.ParseResult.GetValueForOption(tgtPassOpt),
        ctx.ParseResult.GetValueForOption(tgtDbOpt),
        ctx.ParseResult.GetValueForOption(tableFilterOpt), ctx.ParseResult.GetValueForOption(tenantIdOpt),
        ctx.ParseResult.GetValueForOption(srcTableOpt), ctx.ParseResult.GetValueForOption(tgtTableOpt),
        ctx.ParseResult.GetValueForOption(whereOpt), ctx.ParseResult.GetValueForOption(queryOpt),
        ctx.ParseResult.GetValueForOption(dropCreateOpt),
        ctx.ParseResult.GetValueForOption(appendOnlyOpt));

    var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
    var output = ctx.ParseResult.GetValueForOption(outputOpt);
    if (!Confirm(cfg, dryRun ? "dry-run export" : "export")) { Console.WriteLine("  Aborted."); return; }
    var noHistory = ctx.ParseResult.GetValueForOption(noHistoryOpt);
    var result = Exporter.Run(cfg, output, dryRun);
    if (!dryRun && !noHistory) AppendHistory(cfg, label, "export", result);
    Environment.Exit(result);
});

// ── import ────────────────────────────────────────────────────────────────────

var importCmd = new Command("import", "Import .sql files into target database");
importCmd.AddOption(configOpt); importCmd.AddOption(outputOpt); importCmd.AddOption(dryRunOpt);
importCmd.AddOption(dropCreateOpt); importCmd.AddOption(appendOnlyOpt); importCmd.AddOption(srcServerOpt); importCmd.AddOption(srcPortOpt);
importCmd.AddOption(srcUserOpt); importCmd.AddOption(srcPassOpt); importCmd.AddOption(srcDbOpt);
importCmd.AddOption(tgtServerOpt); importCmd.AddOption(tgtPortOpt); importCmd.AddOption(tgtUserOpt);
importCmd.AddOption(tgtPassOpt); importCmd.AddOption(tgtDbOpt); importCmd.AddOption(tableFilterOpt);
importCmd.AddOption(tenantIdOpt); importCmd.AddOption(srcTableOpt); importCmd.AddOption(tgtTableOpt);
importCmd.AddOption(whereOpt); importCmd.AddOption(queryOpt);
importCmd.AddOption(noHistoryOpt);

importCmd.SetHandler((ctx) =>
{
    var (cfg, label) = Resolve(
        ctx.ParseResult.GetValueForOption(configOpt),
        ctx.ParseResult.GetValueForOption(srcServerOpt), ctx.ParseResult.GetValueForOption(srcPortOpt),
        ctx.ParseResult.GetValueForOption(srcUserOpt), ctx.ParseResult.GetValueForOption(srcPassOpt),
        ctx.ParseResult.GetValueForOption(srcDbOpt),
        ctx.ParseResult.GetValueForOption(tgtServerOpt), ctx.ParseResult.GetValueForOption(tgtPortOpt),
        ctx.ParseResult.GetValueForOption(tgtUserOpt), ctx.ParseResult.GetValueForOption(tgtPassOpt),
        ctx.ParseResult.GetValueForOption(tgtDbOpt),
        ctx.ParseResult.GetValueForOption(tableFilterOpt), ctx.ParseResult.GetValueForOption(tenantIdOpt),
        ctx.ParseResult.GetValueForOption(srcTableOpt), ctx.ParseResult.GetValueForOption(tgtTableOpt),
        ctx.ParseResult.GetValueForOption(whereOpt), ctx.ParseResult.GetValueForOption(queryOpt),
        ctx.ParseResult.GetValueForOption(dropCreateOpt),
        ctx.ParseResult.GetValueForOption(appendOnlyOpt));

    var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
    var output = ctx.ParseResult.GetValueForOption(outputOpt);
    if (!Confirm(cfg, dryRun ? "dry-run import" : "import")) { Console.WriteLine("  Aborted."); return; }
    var noHistory = ctx.ParseResult.GetValueForOption(noHistoryOpt);
    var result = Importer.Run(cfg, output, dryRun);
    if (!dryRun && !noHistory) AppendHistory(cfg, label, "import", result);
    Environment.Exit(result);
});

// ── direct ────────────────────────────────────────────────────────────────────

var directCmd = new Command("direct", "Migrate directly from source to target");
directCmd.AddOption(configOpt); directCmd.AddOption(bulkOpt); directCmd.AddOption(dryRunOpt);
directCmd.AddOption(dropCreateOpt); directCmd.AddOption(appendOnlyOpt); directCmd.AddOption(srcServerOpt); directCmd.AddOption(srcPortOpt);
directCmd.AddOption(srcUserOpt); directCmd.AddOption(srcPassOpt); directCmd.AddOption(srcDbOpt);
directCmd.AddOption(tgtServerOpt); directCmd.AddOption(tgtPortOpt); directCmd.AddOption(tgtUserOpt);
directCmd.AddOption(tgtPassOpt); directCmd.AddOption(tgtDbOpt); directCmd.AddOption(tableFilterOpt);
directCmd.AddOption(tenantIdOpt); directCmd.AddOption(srcTableOpt); directCmd.AddOption(tgtTableOpt);
directCmd.AddOption(whereOpt); directCmd.AddOption(queryOpt);
directCmd.AddOption(noHistoryOpt);

directCmd.SetHandler((ctx) =>
{
    var (cfg, label) = Resolve(
        ctx.ParseResult.GetValueForOption(configOpt),
        ctx.ParseResult.GetValueForOption(srcServerOpt), ctx.ParseResult.GetValueForOption(srcPortOpt),
        ctx.ParseResult.GetValueForOption(srcUserOpt), ctx.ParseResult.GetValueForOption(srcPassOpt),
        ctx.ParseResult.GetValueForOption(srcDbOpt),
        ctx.ParseResult.GetValueForOption(tgtServerOpt), ctx.ParseResult.GetValueForOption(tgtPortOpt),
        ctx.ParseResult.GetValueForOption(tgtUserOpt), ctx.ParseResult.GetValueForOption(tgtPassOpt),
        ctx.ParseResult.GetValueForOption(tgtDbOpt),
        ctx.ParseResult.GetValueForOption(tableFilterOpt), ctx.ParseResult.GetValueForOption(tenantIdOpt),
        ctx.ParseResult.GetValueForOption(srcTableOpt), ctx.ParseResult.GetValueForOption(tgtTableOpt),
        ctx.ParseResult.GetValueForOption(whereOpt), ctx.ParseResult.GetValueForOption(queryOpt),
        ctx.ParseResult.GetValueForOption(dropCreateOpt),
        ctx.ParseResult.GetValueForOption(appendOnlyOpt));

    var bulk = ctx.ParseResult.GetValueForOption(bulkOpt);
    var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
    var name = (dryRun ? "dry-run " : "") + (bulk ? "direct (bulk)" : "direct");
    if (!Confirm(cfg, name)) { Console.WriteLine("  Aborted."); return; }
    var noHistory = ctx.ParseResult.GetValueForOption(noHistoryOpt);
    var result = Migrator.Run(cfg, bulk, dryRun);
    if (!dryRun && !noHistory) AppendHistory(cfg, label, bulk ? "direct-bulk" : "direct", result);
    Environment.Exit(result);
});

// ── restructure ───────────────────────────────────────────────────────────────

var restructureCmd = new Command("restructure", "Move tenant tables into named schemas");
restructureCmd.AddOption(configOpt);
restructureCmd.SetHandler((config) =>
{
    if (config is null) throw new Exception("--config is required for restructure");
    var cfg = LoadConfig(config);
    if (!Confirm(cfg, "restructure")) { Console.WriteLine("  Aborted."); return; }
    var result = Restructurer.Run(cfg);
    AppendHistory(cfg, Path.GetFileNameWithoutExtension(config), "restructure", result);
    Environment.Exit(result);
}, configOpt);

// ── status ────────────────────────────────────────────────────────────────────

var statusCmd = new Command("status", "Show table counts and row differences between source and target");
statusCmd.AddOption(configOpt);
statusCmd.SetHandler((config) =>
{
    if (config is null) throw new Exception("--config is required for status");
    RunStatus(LoadConfig(config));
}, configOpt);

rootCmd.AddCommand(exportCmd);
rootCmd.AddCommand(importCmd);
rootCmd.AddCommand(directCmd);
rootCmd.AddCommand(restructureCmd);
rootCmd.AddCommand(statusCmd);

return await rootCmd.InvokeAsync(args);

// ── helpers ───────────────────────────────────────────────────────────────────

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

static void AppendHistory(MigrationConfig cfg, string label, string operation, int result)
{
    try
    {
        RunHistory.Append(new RunRecord
        {
            ConfigName = label,
            Operation = operation,
            StartedAt = DateTime.Now.ToString("O"),
            Success = result == 0,
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