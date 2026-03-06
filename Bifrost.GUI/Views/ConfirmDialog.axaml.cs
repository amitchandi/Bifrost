using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Bifrost.Core;

namespace Bifrost.GUI.Views;

public class ConfirmDialog : Window
{
    public ConfirmDialog(string action, string description, MigrationConfig config)
    {
        Title  = "Confirm";
        Width  = 460;
        Height = 320;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#1e1e2e"));

        var dbList = string.Join("\n", config.Databases.Select(d => $"  • {d.SourceDatabase}  →  {d.TargetDatabase}"));

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };

        panel.Children.Add(new TextBlock
        {
            Text       = action,
            FontSize   = 18,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#89b4fa")),
        });

        panel.Children.Add(new TextBlock
        {
            Text       = $"This will {description}.",
            FontSize   = 13,
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            TextWrapping = TextWrapping.Wrap,
        });

        var infoBox = new Border
        {
            Background    = new SolidColorBrush(Color.Parse("#313244")),
            CornerRadius  = new CornerRadius(6),
            Padding       = new Thickness(14),
        };
        var infoStack = new StackPanel { Spacing = 6 };
        infoStack.Children.Add(MakeInfoRow("Source",    config.Source.Server));
        infoStack.Children.Add(MakeInfoRow("Target",    config.Target.Server));
        infoStack.Children.Add(MakeInfoRow("Databases", dbList));
        infoBox.Child = infoStack;
        panel.Children.Add(infoBox);

        var buttons = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing             = 10,
        };

        var cancelBtn = new Button
        {
            Content    = "Cancel",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            Padding    = new Thickness(16, 8),
        };
        cancelBtn.Click += (_, _) => Close(false);

        var confirmBtn = new Button
        {
            Content    = "Confirm",
            Background = new SolidColorBrush(Color.Parse("#89b4fa")),
            Foreground = new SolidColorBrush(Color.Parse("#1e1e2e")),
            Padding    = new Thickness(16, 8),
            FontWeight = FontWeight.SemiBold,
        };
        confirmBtn.Click += (_, _) => Close(true);

        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(confirmBtn);
        panel.Children.Add(buttons);

        Content = panel;
    }

    private static StackPanel MakeInfoRow(string label, string value) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing     = 8,
        Children    =
        {
            new TextBlock
            {
                Text       = label + ":",
                FontSize   = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#6c7086")),
                Width      = 70,
            },
            new TextBlock
            {
                Text       = value,
                FontSize   = 12,
                Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            },
        }
    };
}
