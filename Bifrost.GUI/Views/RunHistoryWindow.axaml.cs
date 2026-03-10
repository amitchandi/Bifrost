using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Bifrost.Core;
using Bifrost.GUI;

namespace Bifrost.GUI.Views;

public class RunHistoryWindow : BifrostWindow
{
    private readonly StackPanel _listPanel;
    private List<RunRecord> _allRecords = [];
    private string _filter = "all"; // all / success / fail
    private readonly List<(RunRecord Record, CheckBox Check)> _rows = [];
    private readonly Action? _onDeleted;

    public RunHistoryWindow(Action? onDeleted = null)
    {
        Title  = "Run History";
        Width  = 860;
        Height = 620;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ── toolbar ───────────────────────────────────────────────────────────
        var allBtn = MakeFilterBtn("All",     "all");
        var okBtn  = MakeFilterBtn("Success", "success");
        var failBtn = MakeFilterBtn("Failed",  "fail");

        var deleteBtn = new Button
        {
            Content    = "Delete Selected",
            Background = new SolidColorBrush(Color.Parse("#f38ba8")),
            Foreground = new SolidColorBrush(Color.Parse("#1e1e2e")),
            FontWeight = FontWeight.SemiBold,
            Padding    = new Thickness(14, 7),
            FontSize   = 12,
            CornerRadius = new CornerRadius(4),
            Cursor     = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        deleteBtn.Click += OnDeleteSelected;

        var toolbar = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#11111b")),
            Padding         = new Thickness(14, 10),
            BorderBrush     = new SolidColorBrush(Color.Parse("#313244")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child           = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"),
                ColumnSpacing     = 8,
                Children =
                {
                    allBtn.Also(b => Grid.SetColumn(b, 0)),
                    okBtn.Also(b => Grid.SetColumn(b, 1)),
                    failBtn.Also(b => Grid.SetColumn(b, 2)),
                    deleteBtn.Also(b => { Grid.SetColumn(b, 4); b.HorizontalAlignment = HorizontalAlignment.Right; }),
                }
            }
        };

        // ── list ──────────────────────────────────────────────────────────────
        _listPanel = new StackPanel { Spacing = 4, Margin = new Thickness(14) };

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children =
            {
                toolbar.Also(b => Grid.SetRow(b, 0)),
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = _listPanel,
                }.Also(b => Grid.SetRow(b, 1)),
            }
        };

        SetWindowContent("Run History", body);

        _onDeleted = onDeleted;
        LoadRecords();
    }

    private void LoadRecords()
    {
        _allRecords = RunHistory.Load();
        Render();
    }

    private void Render()
    {
        _listPanel.Children.Clear();
        _rows.Clear();

        var filtered = _filter switch
        {
            "success" => _allRecords.Where(r => r.Success).ToList(),
            "fail"    => _allRecords.Where(r => !r.Success).ToList(),
            _         => _allRecords,
        };

        if (filtered.Count == 0)
        {
            _listPanel.Children.Add(new TextBlock
            {
                Text       = "No runs found.",
                FontSize   = 13,
                Foreground = new SolidColorBrush(Color.Parse("#6c7086")),
                Margin     = new Thickness(0, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        foreach (var record in filtered)
        {
            var check = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
            };

            var dot = new Avalonia.Controls.Shapes.Ellipse
            {
                Width  = 8,
                Height = 8,
                Fill   = new SolidColorBrush(record.Success ? Color.Parse("#a6e3a1") : Color.Parse("#f38ba8")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };

            var viewBtn = new Button
            {
                Content      = "View Log",
                Background   = new SolidColorBrush(Color.Parse("#313244")),
                Foreground   = new SolidColorBrush(Color.Parse("#89b4fa")),
                Padding      = new Thickness(10, 4),
                FontSize     = 11,
                CornerRadius = new CornerRadius(4),
                Cursor       = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            viewBtn.Click += (_, _) => new RunLogWindow(record).Show();

            var row = new Border
            {
                Background      = new SolidColorBrush(Color.Parse("#181825")),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(12, 10),
                Child           = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto"),
                    ColumnSpacing     = 10,
                    Children =
                    {
                        check.Also(b => Grid.SetColumn(b, 0)),
                        dot.Also(b => Grid.SetColumn(b, 1)),
                        new TextBlock
                        {
                            Text              = record.Operation,
                            FontSize          = 13,
                            FontWeight        = FontWeight.SemiBold,
                            Foreground        = new SolidColorBrush(Color.Parse("#cdd6f4")),
                            VerticalAlignment = VerticalAlignment.Center,
                            Width             = 120,
                        }.Also(b => Grid.SetColumn(b, 2)),
                        new TextBlock
                        {
                            Text              = record.ConfigName,
                            FontSize          = 12,
                            Foreground        = new SolidColorBrush(Color.Parse("#bac2de")),
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming      = Avalonia.Media.TextTrimming.CharacterEllipsis,
                        }.Also(b => Grid.SetColumn(b, 3)),
                        new TextBlock
                        {
                            Text              = DurationConverter.Format(record.Duration),
                            FontSize          = 11,
                            Foreground        = new SolidColorBrush(Color.Parse("#6c7086")),
                            VerticalAlignment = VerticalAlignment.Center,
                            Width             = 60,
                        }.Also(b => Grid.SetColumn(b, 4)),
                        new TextBlock
                        {
                            Text              = record.StartedAt[..19].Replace("T", " "),
                            FontSize          = 11,
                            Foreground        = new SolidColorBrush(Color.Parse("#6c7086")),
                            VerticalAlignment = VerticalAlignment.Center,
                            Width             = 140,
                        }.Also(b => Grid.SetColumn(b, 5)),
                        viewBtn.Also(b => Grid.SetColumn(b, 6)),
                    }
                }
            };

            _rows.Add((record, check));
            _listPanel.Children.Add(row);
        }
    }

    private void OnDeleteSelected(object? sender, RoutedEventArgs e)
    {
        var toDelete = _rows.Where(r => r.Check.IsChecked == true).Select(r => r.Record.Id).ToList();
        if (toDelete.Count == 0) return;
        RunHistory.Delete(toDelete);
        _onDeleted?.Invoke();
        LoadRecords();
    }

    private Button MakeFilterBtn(string label, string filter)
    {
        var btn = new Button
        {
            Content      = label,
            Background   = new SolidColorBrush(_filter == filter ? Color.Parse("#313244") : Color.Parse("#1e1e2e")),
            Foreground   = new SolidColorBrush(_filter == filter ? Color.Parse("#cdd6f4") : Color.Parse("#6c7086")),
            Padding      = new Thickness(12, 6),
            FontSize     = 12,
            CornerRadius = new CornerRadius(4),
            Cursor       = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        btn.Click += (_, _) =>
        {
            _filter = filter;
            Render();
        };
        return btn;
    }
}

file static class WinExtensions
{
    public static T Also<T>(this T control, Action<T> configure) where T : Avalonia.Controls.Control
    {
        configure(control);
        return control;
    }
}
