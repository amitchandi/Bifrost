using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Bifrost.Core;
using Bifrost.GUI;

namespace Bifrost.GUI.Views;

public class RunLogWindow : BifrostWindow
{
    public RunLogWindow(RunRecord record)
    {
        Title  = $"Log — {record.Operation} — {record.ConfigName}";
        Width  = 900;
        Height = 600;
        CanResize = true;
        WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner;

        var converter = new LogLineColorConverter();

        var itemsControl = new ItemsControl
        {
            ItemsSource  = record.Log,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((line, _) =>
            {
                var color = (IBrush?)converter.Convert(
                    line, typeof(IBrush), null,
                    System.Globalization.CultureInfo.CurrentCulture)
                    ?? Brushes.White;

                return new TextBlock
                {
                    Text         = line,
                    Foreground   = color,
                    FontFamily   = new FontFamily("Cascadia Code,Consolas,monospace"),
                    FontSize     = 12,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin       = new Avalonia.Thickness(0, 1),
                };
            })
        };

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Padding  = new Avalonia.Thickness(16, 16, 16, 32),
            Content  = itemsControl,
        };

        SetWindowContent($"Log — {record.Operation} — {record.ConfigName}", scroll);
    }
}
