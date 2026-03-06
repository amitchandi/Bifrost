using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Bifrost.Core;

namespace Bifrost.GUI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // ── Config list ───────────────────────────────────────────────────────────

    public ObservableCollection<NamedConfig> Configs { get; } = [];

    private NamedConfig? _selectedConfig;
    public NamedConfig? SelectedConfig
    {
        get => _selectedConfig;
        set
        {
            _selectedConfig = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConfig));
            OnPropertyChanged(nameof(ConfigSummary));
        }
    }

    public bool HasConfig => _selectedConfig != null;

    public string ConfigSummary
    {
        get
        {
            var cfg = _selectedConfig?.Config;
            if (cfg == null) return "No config selected";
            var dbs = string.Join("\n", cfg.Databases.Select(d => $"  {d.SourceDatabase} -> {d.TargetDatabase}"));
            return $"Source : {cfg.Source.Server}\nTarget : {cfg.Target.Server}\n\n{dbs}";
        }
    }

    // ── Operation state ───────────────────────────────────────────────────────

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
    }
    public bool IsNotBusy => !_isBusy;

    private string _outputDir = "output";
    public string OutputDir
    {
        get => _outputDir;
        set { _outputDir = value; OnPropertyChanged(); }
    }

    private bool _useBulk = true;
    public bool UseBulk
    {
        get => _useBulk;
        set { _useBulk = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> LogLines { get; } = [];

    // ── Init ──────────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        foreach (var c in ConfigStore.Load())
            Configs.Add(c);
        SelectedConfig = Configs.FirstOrDefault();
    }

    // ── Config management ─────────────────────────────────────────────────────

    public void AddOrUpdateConfig(NamedConfig named, NamedConfig? replacing = null)
    {
        if (replacing != null)
        {
            var idx = Configs.IndexOf(replacing);
            if (idx >= 0) Configs[idx] = named;
            else Configs.Add(named);
        }
        else
        {
            Configs.Add(named);
        }
        SelectedConfig = named;
        PersistConfigs();
    }

    public void DeleteConfig(NamedConfig named)
    {
        Configs.Remove(named);
        if (SelectedConfig == named) SelectedConfig = Configs.FirstOrDefault();
        PersistConfigs();
    }

    private void PersistConfigs() => ConfigStore.Save(Configs.ToList());

    // ── Logging ───────────────────────────────────────────────────────────────

    public void AppendLog(string line)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => LogLines.Add(line));

    public void ClearLog() => LogLines.Clear();

    // ── Run operations ────────────────────────────────────────────────────────

    public async Task RunExport() => await RunOperation(() => Exporter.Run(_selectedConfig!.Config, OutputDir));
    public async Task RunImport() => await RunOperation(() => Importer.Run(_selectedConfig!.Config, OutputDir));
    public async Task RunDirect() => await RunOperation(() => Migrator.Run(_selectedConfig!.Config, false));
    public async Task RunBulk() => await RunOperation(() => Migrator.Run(_selectedConfig!.Config, true));

    private async Task RunOperation(Func<int> operation)
    {
        if (_selectedConfig == null || IsBusy) return;
        ClearLog();
        IsBusy = true;
        Logger.SetHandler(AppendLog);
        int result = 0;
        try { result = await Task.Run(operation); }
        catch (Exception ex) { AppendLog($"[FAIL] Unhandled error: {ex.Message}"); result = 1; }
        finally
        {
            IsBusy = false;
            PlayDone(result == 0);
        }
    }

    private static void PlayDone(bool success)
    {
        try
        {
            if (success) { Console.Beep(440, 120); Console.Beep(550, 180); }
            else { Console.Beep(300, 250); Console.Beep(220, 350); }
        }
        catch { }
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}