using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

/// <summary>
/// Runs the Bifrost CLI as a subprocess. Compatible with .NET Framework 4.6.1+.
/// </summary>
public class BifrostRunner
{
    private readonly string _exePath;

    /// <param name="exePath">Full path to bifrost.exe</param>
    public BifrostRunner(string exePath)
    {
        _exePath = exePath;
    }

    /// <summary>Result of a Bifrost run.</summary>
    public class BifrostResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
    }

    // ── High-level helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Migrate a single table directly from source to target.
    /// Set targetTable to rename it on arrival.
    /// </summary>
    public BifrostResult DirectTable(
        string sourceServer, string sourceDatabase, string sourceTable,
        string targetDatabase, string targetTable = null,
        string sourceUser = null, string sourcePass = null,
        string targetServer = null,
        string targetUser = null, string targetPass = null,
        string where = null,
        string query = null,
        bool bulk = true,
        bool dropAndCreate = false,
        bool appendOnly = false,
        bool noHistory = false,
        bool dryRun = false)
    {
        var args = new ArgBuilder("direct")
            .Add("--source-server", sourceServer)
            .Add("--source-database", sourceDatabase)
            .Add("--target-database", targetDatabase)
            .Add("--source-table", sourceTable)
            .AddIf("--target-table", targetTable)
            .AddIf("--where", where)
            .AddIf("--query", query)
            .AddIf("--target-server", targetServer)
            .AddIf("--source-user", sourceUser)
            .AddIf("--source-pass", sourcePass)
            .AddIf("--target-user", targetUser)
            .AddIf("--target-pass", targetPass)
            .Flag("--bulk", bulk)
            .Flag("--drop-and-create", dropAndCreate)
            .Flag("--append-only", appendOnly)
            .Flag("--no-history", noHistory)
            .Flag("--dry-run", dryRun)
            .Build();

        return Run(args);
    }

    /// <summary>
    /// Migrate all tables in a database directly from source to target.
    /// </summary>
    public BifrostResult DirectDatabase(
        string sourceServer, string sourceDatabase,
        string targetDatabase, string targetServer = null,
        string sourceUser = null, string sourcePass = null,
        string targetUser = null, string targetPass = null,
        bool bulk = true,
        bool dropAndCreate = false,
        bool appendOnly = false,
        bool noHistory = false,
        bool dryRun = false)
    {
        var args = new ArgBuilder("direct")
            .Add("--source-server", sourceServer)
            .Add("--source-database", sourceDatabase)
            .Add("--target-database", targetDatabase)
            .AddIf("--target-server", targetServer)
            .AddIf("--source-user", sourceUser)
            .AddIf("--source-pass", sourcePass)
            .AddIf("--target-user", targetUser)
            .AddIf("--target-pass", targetPass)
            .Add("--table-filter", "all")
            .Flag("--bulk", bulk)
            .Flag("--drop-and-create", dropAndCreate)
            .Flag("--append-only", appendOnly)
            .Flag("--no-history", noHistory)
            .Flag("--dry-run", dryRun)
            .Build();

        return Run(args);
    }

    /// <summary>
    /// Run using a config file. Use for multi-database, tenant, or restructure operations.
    /// </summary>
    public BifrostResult RunWithConfig(string command, string configPath, bool bulk = false, bool dropAndCreate = false, bool dryRun = false)
    {
        var args = new ArgBuilder(command)
            .Add("--config", configPath)
            .Flag("--bulk", bulk)
            .Flag("--drop-and-create", dropAndCreate)
            .Flag("--append-only", appendOnly)
            .Flag("--dry-run", dryRun)
            .Build();

        return Run(args);
    }

    // ── Core runner ───────────────────────────────────────────────────────────

    /// <summary>
    /// Run bifrost.exe with the given arguments. Blocks until complete.
    /// Automatically answers the confirmation prompt with "y".
    /// </summary>
    public BifrostResult Run(string arguments)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (var process = new Process { StartInfo = psi })
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Answer the confirmation prompt
            process.StandardInput.WriteLine("y");
            process.StandardInput.Close();

            process.WaitForExit();

            return new BifrostResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Output = output.ToString(),
                Error = error.ToString(),
            };
        }
    }

    // ── Arg builder ───────────────────────────────────────────────────────────

    private class ArgBuilder
    {
        private readonly List<string> _parts = new List<string>();

        public ArgBuilder(string command)
        {
            _parts.Add(command);
        }

        public ArgBuilder Add(string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _parts.Add(key);
                _parts.Add(Quote(value));
            }
            return this;
        }

        public ArgBuilder AddIf(string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
                Add(key, value);
            return this;
        }

        public ArgBuilder Flag(string key, bool value)
        {
            if (value) _parts.Add(key);
            return this;
        }

        public string Build()
        {
            return string.Join(" ", _parts);
        }

        private static string Quote(string value)
        {
            // Wrap in quotes if the value contains spaces
            return value.Contains(" ") ? "\"" + value + "\"" : value;
        }
    }
}