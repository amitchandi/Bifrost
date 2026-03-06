using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bifrost.GUI.Views;

namespace Bifrost.GUI;

public partial class App : Application
{
    public static ICommand ShowWindowCommand { get; } = new RelayCommand(ShowWindow);
    public static ICommand QuitCommand { get; } = new RelayCommand(Quit);

    private static MainWindow? _window;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _window = new MainWindow();
            desktop.MainWindow = _window;

            // Hide to tray instead of closing
            _window.Closing += (_, e) =>
            {
                e.Cancel = true;
                _window.Hide();
            };

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowWindow()
    {
        _window?.Show();
        _window?.Activate();
        if (_window?.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
    }

    private static void Quit()
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}

public class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}