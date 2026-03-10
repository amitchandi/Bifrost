using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Bifrost.GUI.Views;

/// <summary>
/// Base class for all Bifrost windows. Provides the shared custom title bar
/// with drag, minimize, maximize, and close buttons.
/// </summary>
public class BifrostWindow : Window
{
    public BifrostWindow()
    {
        Background = new SolidColorBrush(Color.Parse("#1e1e2e"));
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;

        Styles.Add(new Style(x => x.OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, (IBrush)new SolidColorBrush(Color.Parse("#cdd6f4"))) }
        });
    }

    /// <summary>
    /// Wraps the given content with the title bar. Call from the subclass constructor.
    /// </summary>
    protected void SetWindowContent(string title, Control content)
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("32,*") };
        var titleBar = BuildTitleBar(title);
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(content, 1);
        root.Children.Add(titleBar);
        root.Children.Add(content);

        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#45475a")),
            BorderThickness = new Thickness(1),
            Child = root,
        };
    }

    // ── Title bar ─────────────────────────────────────────────────────────────

    private Border BuildTitleBar(string title)
    {
        // Load icon safely; fall back to a unicode glyph if asset unavailable
        Control iconControl;
        try
        {
            var bmp = new Bitmap(AssetLoader.Open(new Uri("avares://bifrost-gui/Assets/bifrost.ico")));
            iconControl = new Image { Source = bmp, Width = 16, Height = 16 };
        }
        catch
        {
            iconControl = new TextBlock
            {
                Text = "⬡",
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#89b4fa")),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0),
            Children = { iconControl, titleText },
        };

        var dragArea = new Panel { Background = Brushes.Transparent };
        dragArea.PointerPressed += (_, e) => BeginMoveDrag(e);

        var minimizeBtn = MakeWinBtn("─");
        minimizeBtn.Click += (_, _) => WindowState = WindowState.Minimized;

        var maximizeBtn = MakeWinBtn("□");
        maximizeBtn.Click += (_, _) => WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        var closeBtn = MakeWinBtn("✕", isClose: true);
        closeBtn.Click += (_, _) => Close();

        var winBtns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { minimizeBtn, maximizeBtn, closeBtn },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(dragArea, 1);
        Grid.SetColumn(winBtns, 2);
        grid.Children.Add(left);
        grid.Children.Add(dragArea);
        grid.Children.Add(winBtns);

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#11111b")),
            Child = grid,
        };
    }

    private static Button MakeWinBtn(string label, bool isClose = false)
    {
        var btn = new Button
        {
            Content = label,
            Width = 40,
            Height = 32,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#6c7086")),
            CornerRadius = new CornerRadius(0),
            FontSize = 12,
            FontWeight = FontWeight.Normal,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        btn.Classes.Add("winbtn");
        if (isClose) btn.Classes.Add("close");

        // Hover: darken background
        btn.Styles.Add(new Style(x => x
            .OfType<Button>().Class(":pointerover")
            .Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty,
                    (IBrush)new SolidColorBrush(Color.Parse("#313244"))),
            }
        });

        // Close button hover: red
        if (isClose)
        {
            btn.Styles.Add(new Style(x => x
                .OfType<Button>().Class(":pointerover")
                .Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
            {
                Setters =
                {
                    new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty,
                        (IBrush)new SolidColorBrush(Color.Parse("#f38ba8"))),
                }
            });
        }

        return btn;
    }
}