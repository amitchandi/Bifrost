using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Bifrost.Core;

namespace Bifrost.GUI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // ── Configs ───────────────────────────────────────────────────────────────

    public ObservableCollection<NamedConfig> Configs { get; } = [];

    private NamedConfig? _selectedConfig;
    public NamedConfig? SelectedConfig
    {
        get => _selectedConfig;
        set { _selectedConfig = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasConfig)); OnPropertyChanged(nameof(ConfigSummary)); }
    }

    public bool HasConfig => _selectedConfig != null;

    public string ConfigSummary => _selectedConfig == null
        ? "No config selected"
        : $"Source : {_selectedConfig.Config.Source.Server}\n" +
          $"Target : {_selectedConfig.Config.Target.Server}\n" +
          $"DBs    : {string.Join(", ", _selectedConfig.Config.Databases.Select(d => d.SourceDatabase))}";

    public void AddOrUpdateConfig(NamedConfig config, NamedConfig? replacing = null)
    {
        if (replacing != null)
        {
            var idx = Configs.IndexOf(replacing);
            if (idx >= 0) Configs[idx] = config;
        }
        else Configs.Add(config);

        SelectedConfig = config;
        ConfigStore.Save(Configs.ToList());
    }

    public void DeleteConfig(NamedConfig config)
    {
        Configs.Remove(config);
        if (SelectedConfig == config) SelectedConfig = Configs.FirstOrDefault();
        ConfigStore.Save(Configs.ToList());
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    private string _outputDir = "output";
    public string OutputDir
    {
        get => _outputDir;
        set { _outputDir = value; OnPropertyChanged(); }
    }

    private bool _useBulk;
    public bool UseBulk
    {
        get => _useBulk;
        set { _useBulk = value; OnPropertyChanged(); }
    }

    private bool _dryRun;
    public bool DryRun
    {
        get => _dryRun;
        set { _dryRun = value; OnPropertyChanged(); }
    }

    // ── Busy / Progress ───────────────────────────────────────────────────────

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
    }
    public bool IsNotBusy => !_isBusy;

    private int _progressValue;
    public int ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    private int _progressMax = 1;
    public int ProgressMax
    {
        get => _progressMax;
        set { _progressMax = value; OnPropertyChanged(); }
    }

    private bool _progressVisible;
    public bool ProgressVisible
    {
        get => _progressVisible;
        set { _progressVisible = value; OnPropertyChanged(); }
    }

    // ── Status / Log ──────────────────────────────────────────────────────────

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> LogLines { get; } = [];

    public void AppendLog(string line) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LogLines.Add(line));

    public void ClearLog() => LogLines.Clear();

    // ── Run history ───────────────────────────────────────────────────────────

    public ObservableCollection<RunRecord> History { get; } = [];

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var r in RunHistory.Load().Take(20))
            History.Add(r);
    }

    // ── Operations ────────────────────────────────────────────────────────────

    public async Task RunExport()      => await RunOperation("export",      () => Exporter.Run(_selectedConfig!.Config, _outputDir, _dryRun));
    public async Task RunImport()      => await RunOperation("import",      () => Importer.Run(_selectedConfig!.Config, _outputDir, _dryRun));
    public async Task RunDirect()      => await RunOperation("direct",      () => Migrator.Run(_selectedConfig!.Config, false, _dryRun));
    public async Task RunBulk()        => await RunOperation("direct-bulk", () => Migrator.Run(_selectedConfig!.Config, true, _dryRun));
    public async Task RunRestructure() => await RunOperation("restructure", () => Restructurer.Run(_selectedConfig!.Config));

    private async Task RunOperation(string operation, Func<int> action)
    {
        if (_selectedConfig == null) return;

        ClearLog();
        IsBusy        = true;
        ProgressValue = 0;
        ProgressMax   = 1;
        ProgressVisible = true;
        StatusText    = _dryRun ? $"Dry run: {operation}..." : $"Running {operation}...";

        // Wire up progress
        Action<int, int> onProgress = (done, total) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ProgressMax   = Math.Max(total, 1);
                ProgressValue = done;
            });

        Exporter.OnProgress  += onProgress;
        Importer.OnProgress  += onProgress;
        Migrator.OnProgress  += onProgress;
        Logger.SetHandler(AppendLog);

        var sw     = Stopwatch.StartNew();
        int result = 0;

        try
        {
            result = await Task.Run(action);
        }
        finally
        {
            Exporter.OnProgress  -= onProgress;
            Importer.OnProgress  -= onProgress;
            Migrator.OnProgress  -= onProgress;
        }

        if (!_dryRun)
        {
            RunHistory.Append(new RunRecord
            {
                ConfigName = _selectedConfig.Name,
                Operation  = operation,
                StartedAt  = DateTime.Now.ToString("O"),
                Duration   = sw.Elapsed.ToString(@"hh\:mm\:ss"),
                Success    = result == 0,
            });
            RefreshHistory();
        }

        IsBusy          = false;
        ProgressVisible = false;
        StatusText      = result == 0
            ? (_dryRun ? "Dry run complete" : "Completed successfully")
            : "Completed with errors";

        AppendLog("");
        AppendLog("");
        AppendLog("");
        AppendLog("");

        PlayDone(result == 0);
    }

    // ── Sound ─────────────────────────────────────────────────────────────────

    private static void PlayDone(bool success)
    {
        Task.Run(() =>
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (success) { Console.Beep(440, 120); Console.Beep(550, 180); }
                    else         { Console.Beep(300, 250); Console.Beep(220, 350); }
                }
                else if (OperatingSystem.IsLinux())
                {
                    var sound = success
                        ? "/usr/share/sounds/freedesktop/stereo/complete.oga"
                        : "/usr/share/sounds/freedesktop/stereo/dialog-error.oga";
                    if (File.Exists(sound))
                        Process.Start("paplay", sound)?.WaitForExit(2000);
                }
            }
            catch { }
        });
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainViewModel()
    {
        foreach (var c in ConfigStore.Load()) Configs.Add(c);
        SelectedConfig = Configs.FirstOrDefault();
        RefreshHistory();
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
