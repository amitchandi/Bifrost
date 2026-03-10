using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bifrost.Core;
using Bifrost.GUI.ViewModels;
using Avalonia.Controls;

namespace Bifrost.GUI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.LogLines.CollectionChanged += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var scroller = this.FindControl<ScrollViewer>("LogScroller");
                scroller?.ScrollToEnd();
            }, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    // ── Title bar ─────────────────────────────────────────────────────────────

    private void OnTitleBarDrag(object? sender, PointerPressedEventArgs e)
        => BeginMoveDrag(e);

    private void OnMinimize(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseToTray(object? sender, RoutedEventArgs e)
        => Hide();

    // ── Config import / export ───────────────────────────────────────────────

    private async void OnImportConfig(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Config",
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0) return;

        try
        {
            var json   = await File.ReadAllTextAsync(files[0].Path.LocalPath);
            var config = JsonSerializer.Deserialize<MigrationConfig>(json)
                ?? throw new Exception("Invalid config file");
            var name   = Path.GetFileNameWithoutExtension(files[0].Path.LocalPath);
            _vm.AddOrUpdateConfig(new NamedConfig { Name = name, Config = config });
        }
        catch (Exception ex)
        {
            await new ConfirmDialog("Import Failed", ex.Message, new MigrationConfig())
                .ShowDialog<bool>(this);
        }
    }

    private async void OnExportConfig(object? sender, RoutedEventArgs e)
    {
        var btn = (Button)sender!;
        var named = btn.DataContext as NamedConfig;
        if (named == null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title           = "Export Config",
            SuggestedFileName = $"{named.Name}.json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (file == null) return;

        var json = JsonSerializer.Serialize(named.Config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(file.Path.LocalPath, json);
    }

    // ── Config management ─────────────────────────────────────────────────────

    private async void OnNewConfig(object? sender, RoutedEventArgs e)
    {
        var result = await new ConfigEditorDialog().ShowDialog<NamedConfig?>(this);
        if (result != null) _vm.AddOrUpdateConfig(result);
    }

    private async void OnEditConfig(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedConfig == null) return;
        var result = await new ConfigEditorDialog(_vm.SelectedConfig).ShowDialog<NamedConfig?>(this);
        if (result != null) _vm.AddOrUpdateConfig(result, _vm.SelectedConfig);
    }

    private async void OnDeleteConfig(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedConfig == null) return;
        var confirmed = await new ConfirmDialog(
            "Delete Config",
            $"permanently delete \"{_vm.SelectedConfig.Name}\"",
            _vm.SelectedConfig.Config).ShowDialog<bool>(this);
        if (confirmed) _vm.DeleteConfig(_vm.SelectedConfig);
    }

    // ── Output directory ──────────────────────────────────────────────────────

    private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Directory",
        });
        if (folder.Count > 0) _vm.OutputDir = folder[0].Path.LocalPath;
    }

    // ── History ───────────────────────────────────────────────────────────────

    private void OnViewAllHistory(object? sender, RoutedEventArgs e)
        => new RunHistoryWindow(() => _vm.RefreshHistory()).Show();

    private void OnViewRunLog(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is RunRecord record)
            new RunLogWindow(record).Show();
    }

    // ── Log ───────────────────────────────────────────────────────────────────

    private void OnClearLog(object? sender, RoutedEventArgs e) => _vm.ClearLog();


    // ── Actions ───────────────────────────────────────────────────────────────

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmRun("Export", "read from SOURCE and write .sql files")) return;
        await _vm.RunExport();
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmRun("Import", "write to TARGET database")) return;
        await _vm.RunImport();
    }

    private async void OnDirect(object? sender, RoutedEventArgs e)
    {
        var mode = _vm.UseBulk ? "Direct Migration (SqlBulkCopy)" : "Direct Migration";
        if (!await ConfirmRun(mode, "read from SOURCE and write directly to TARGET")) return;
        if (_vm.UseBulk) await _vm.RunBulk();
        else             await _vm.RunDirect();
    }

    private async void OnRestructure(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmRun("Restructure", "move tenant tables into named schemas on TARGET")) return;
        await _vm.RunRestructure();
    }

    private async Task<bool> ConfirmRun(string action, string description)
    {
        if (_vm.SelectedConfig == null) return false;
        return await new ConfirmDialog(action, description, _vm.SelectedConfig.Config)
            .ShowDialog<bool>(this);
    }
}
