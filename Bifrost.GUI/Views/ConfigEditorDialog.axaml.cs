using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Bifrost.Core;

namespace Bifrost.GUI.Views;

public class ConfigEditorDialog : BifrostWindow
{
    private readonly TextBox _nameBox;
    private readonly TextBox _srcServer, _srcPort, _srcUser, _srcPass;
    private readonly CheckBox _srcEncrypt, _srcTrust;
    private readonly TextBox _tgtServer, _tgtPort, _tgtUser, _tgtPass;
    private readonly CheckBox _tgtEncrypt, _tgtTrust;
    private readonly StackPanel _dbPanel;
    private readonly StackPanel _tenantPanel;
    private readonly List<TenantRowControls> _tenantRows = [];
    private readonly List<DbRowControls> _dbRows = [];

    private record DbRowControls(
        TextBox Source, TextBox Target, ComboBox Filter,
        TextBox TenantId, TextBox Comment,
        StackPanel TablesPanel, List<TableRowControls> TableRows,
        StackPanel OverridesPanel, List<TableRowControls> OverrideRows,
        CheckBox DropAndCreate);

    private record TableRowControls(TextBox Name, TextBox TargetName, TextBox Where, TextBox Query, CheckBox Ignore);
    private record TenantRowControls(TextBox TenantId, TextBox Schema, TextBox Database, CheckBox CompatViews, TextBox SourceSchema);

    public ConfigEditorDialog(NamedConfig? existing = null)
    {
        Title = existing == null ? "New Config" : "Edit Config";
        Width = 680;
        Height = 720;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // ── helpers ──────────────────────────────────────────────────────────
        TextBox MakeBox(string watermark, string? val = null) => new()
        {
            Watermark = watermark,
            Text = val ?? "",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#45475a")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            FontSize = 12,
        };

        CheckBox MakeCheck(string label, bool val) => new()
        {
            Content = label,
            IsChecked = val,
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            FontSize = 12,
        };

        TextBlock MakeLabel(string text, string? color = null) => new()
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse(color ?? "#6c7086")),
            Margin = new Thickness(0, 8, 0, 4),
        };

        Border MakeSection(string title, Control content) => new()
        {
            Background = new SolidColorBrush(Color.Parse("#181825")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { MakeLabel(title, "#89b4fa"), content }
            }
        };

        StackPanel MakeConnFields(
            out TextBox server, out TextBox port, out TextBox user,
            out TextBox pass, out CheckBox encrypt, out CheckBox trust,
            ConnectionConfig? c = null)
        {
            server = MakeBox("Server / IP", c?.Server);
            port = MakeBox("Port", c?.Port.ToString() ?? "1433");
            user = MakeBox("Username", c?.Username);
            pass = MakeBox("Password", c?.Password);
            pass.PasswordChar = '●';
            encrypt = MakeCheck("Encrypt", c?.Encrypt ?? false);
            trust = MakeCheck("Trust Server Certificate", c?.TrustServerCertificate ?? true);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            Grid.SetColumn(server, 0); Grid.SetColumn(port, 1);
            port.Width = 80;
            grid.Children.Add(server); grid.Children.Add(port);

            return new StackPanel { Spacing = 6, Children = { grid, user, pass, encrypt, trust } };
        }

        // ── fields ───────────────────────────────────────────────────────────
        _nameBox = MakeBox("e.g. UNFI Staging", existing?.Name);

        var srcPanel = MakeConnFields(out _srcServer, out _srcPort, out _srcUser, out _srcPass,
            out _srcEncrypt, out _srcTrust, existing?.Config.Source);
        var tgtPanel = MakeConnFields(out _tgtServer, out _tgtPort, out _tgtUser, out _tgtPass,
            out _tgtEncrypt, out _tgtTrust, existing?.Config.Target);

        _dbPanel = new StackPanel { Spacing = 6 };
        _tenantPanel = new StackPanel { Spacing = 6 };

        var addTenantBtn = new Button
        {
            Content = "+ Add Tenant",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#cba6f7")),
            FontSize = 12,
            Padding = new Thickness(10, 6),
        };
        addTenantBtn.Click += (_, _) => AddTenantRow();

        if (existing?.Config.Tenants is { Count: > 0 } tenants)
            foreach (var t in tenants) AddTenantRow(t);

        var addDbBtn = new Button
        {
            Content = "+ Add Database",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#89b4fa")),
            FontSize = 12,
            Padding = new Thickness(10, 6),
        };
        addDbBtn.Click += (_, _) => AddDbRow();

        if (existing?.Config.Databases is { Count: > 0 } dbs)
            foreach (var db in dbs) AddDbRow(db);
        else
            AddDbRow();

        // ── buttons ───────────────────────────────────────────────────────────
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            Padding = new Thickness(16, 8),
            FontSize = 12,
        };
        cancelBtn.Click += (_, _) => Close(null);

        var saveBtn = new Button
        {
            Content = "Save",
            Background = new SolidColorBrush(Color.Parse("#89b4fa")),
            Foreground = new SolidColorBrush(Color.Parse("#1e1e2e")),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(16, 8),
            FontSize = 12,
        };
        saveBtn.Click += OnSave;

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelBtn, saveBtn },
        };

        // ── layout ────────────────────────────────────────────────────────────
        var scroll = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 0,
                Children =
                {
                    MakeLabel("CONFIG NAME", "#6c7086"),
                    _nameBox,
                    MakeSection("SOURCE CONNECTION", srcPanel),
                    MakeSection("TARGET CONNECTION", tgtPanel),
                    MakeSection("TENANTS (for restructure)", new StackPanel
                    {
                        Spacing  = 8,
                        Children = { _tenantPanel, addTenantBtn },
                    }),
                    MakeSection("DATABASES", new StackPanel
                    {
                        Spacing  = 8,
                        Children = { _dbPanel, addDbBtn },
                    }),
                    btnRow,
                }
            }
        };

        SetWindowContent(existing == null ? "New Config" : $"Edit Config — {existing.Name}", scroll);
    }

    // ── AddDbRow ──────────────────────────────────────────────────────────────

    private void AddDbRow(DbEntry? db = null)
    {
        TextBox MakeBox(string wm, string? val = null) => new()
        {
            Watermark = wm,
            Text = val ?? "",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#45475a")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5),
            FontSize = 12,
        };

        var srcBox = MakeBox("Source DB", db?.SourceDatabase);
        var tgtBox = MakeBox("Target DB", db?.TargetDatabase);
        var tenantBox = MakeBox("Tenant ID", db?.TenantId);
        var commentBox = MakeBox("Comment (optional)", db?.Comment);
        var dropAndCreateChk = new CheckBox
        {
            Content = "Drop and recreate tables (use when columns have changed)",
            IsChecked = db?.DropAndCreate ?? false,
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            FontSize = 12,
        };

        var filterBox = new ComboBox
        {
            ItemsSource = new[] { "all", "tenant", "explicit" },
            SelectedItem = db?.TableFilter ?? "all",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#45475a")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // Tables panel — only visible when filter = explicit
        var tablesPanel = new StackPanel { Spacing = 4 };
        var tableRows = new List<TableRowControls>();

        // Overrides panel — only visible when filter = tenant or all
        var overridesPanel = new StackPanel { Spacing = 4 };
        var overrideRows = new List<TableRowControls>();

        var addTableBtn = new Button
        {
            Content = "+ Add Table",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#a6e3a1")),
            FontSize = 11,
            Padding = new Thickness(8, 4),
        };

        var explicitSection = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1e1e2e")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 0),
            IsVisible = (db?.TableFilter == "explicit"),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text       = "EXPLICIT TABLES",
                        FontSize   = 10,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#6c7086")),
                    },
                    tablesPanel,
                    addTableBtn,
                }
            }
        };

        // Populate existing explicit tables
        if (db?.Tables != null)
            foreach (var t in db.Tables) AddTableRow(tablesPanel, tableRows, t);

        addTableBtn.Click += (_, _) => AddTableRow(tablesPanel, tableRows);

        var addOverrideBtn = new Button
        {
            Content = "+ Add Override",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#f9e2af")),
            FontSize = 11,
            Padding = new Thickness(8, 4),
        };

        var overridesSection = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1e1e2e")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 0),
            IsVisible = db?.TableFilter is "tenant" or "all",
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text       = "TABLE OVERRIDES",
                        FontSize   = 10,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#6c7086")),
                    },
                    overridesPanel,
                    addOverrideBtn,
                }
            }
        };

        // Populate existing overrides
        if (db?.Overrides != null)
            foreach (var o in db.Overrides) AddTableRow(overridesPanel, overrideRows, new JsonTable
            {
                Name = o.Name,
                Where = o.Where,
                Query = o.Query,
                Ignore = o.Ignore,
            });

        addOverrideBtn.Click += (_, _) => AddTableRow(overridesPanel, overrideRows);

        // Show/hide sections based on filter selection
        filterBox.SelectionChanged += (_, _) =>
        {
            var selected = filterBox.SelectedItem as string;
            explicitSection.IsVisible = selected == "explicit";
            overridesSection.IsVisible = selected is "tenant" or "all";
            tenantBox.IsVisible = selected == "tenant";
        };

        // Set initial visibility of tenantBox
        tenantBox.IsVisible = db?.TableFilter == "tenant";

        var removeBtn = new Button
        {
            Content = "✕",
            Background = new SolidColorBrush(Color.Parse("#f38ba8")),
            Foreground = new SolidColorBrush(Color.Parse("#1e1e2e")),
            Padding = new Thickness(8, 5),
            FontSize = 11,
            Width = 30,
        };

        var row = new DbRowControls(srcBox, tgtBox, filterBox, tenantBox, commentBox, tablesPanel, tableRows, overridesPanel, overrideRows, dropAndCreateChk);
        _dbRows.Add(row);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,Auto"),
            ColumnSpacing = 6,
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 4,
        };

        Grid.SetColumn(srcBox, 0); Grid.SetRow(srcBox, 0);
        Grid.SetColumn(tgtBox, 1); Grid.SetRow(tgtBox, 0);
        Grid.SetColumn(removeBtn, 2); Grid.SetRow(removeBtn, 0);
        Grid.SetColumn(filterBox, 0); Grid.SetRow(filterBox, 1);
        Grid.SetColumn(tenantBox, 1); Grid.SetRow(tenantBox, 1);
        Grid.SetColumn(commentBox, 0); Grid.SetRow(commentBox, 2); Grid.SetColumnSpan(commentBox, 2);
        Grid.SetColumn(explicitSection, 0); Grid.SetRow(explicitSection, 3); Grid.SetColumnSpan(explicitSection, 3);
        Grid.SetColumn(overridesSection, 0); Grid.SetRow(overridesSection, 4); Grid.SetColumnSpan(overridesSection, 3);
        Grid.SetColumn(dropAndCreateChk, 0); Grid.SetRow(dropAndCreateChk, 5); Grid.SetColumnSpan(dropAndCreateChk, 3);

        grid.Children.Add(srcBox);
        grid.Children.Add(tgtBox);
        grid.Children.Add(removeBtn);
        grid.Children.Add(filterBox);
        grid.Children.Add(tenantBox);
        grid.Children.Add(commentBox);
        grid.Children.Add(explicitSection);
        grid.Children.Add(overridesSection);
        grid.Children.Add(dropAndCreateChk);

        var container = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#313244")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = grid,
        };

        removeBtn.Click += (_, _) =>
        {
            _dbRows.Remove(row);
            _dbPanel.Children.Remove(container);
        };

        _dbPanel.Children.Add(container);
    }

    // ── AddTableRow ───────────────────────────────────────────────────────────

    private static void AddTableRow(StackPanel panel, List<TableRowControls> rows, JsonTable? t = null)
    {
        TextBox MakeBox(string wm, string? val = null) => new()
        {
            Watermark = wm,
            Text = val ?? "",
            Background = new SolidColorBrush(Color.Parse("#2a2a3e")),
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#45475a")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4),
            FontSize = 11,
        };

        var nameBox = MakeBox("dbo.TableName", t?.Name);
        var targetNameBox = MakeBox("Target name (optional)", t?.TargetName);
        var whereBox = MakeBox("WHERE clause (optional)", t?.Where);
        var queryBox = MakeBox("Custom query (optional)", t?.Query);
        var ignoreChk = new CheckBox
        {
            Content = "Ignore",
            IsChecked = t?.Ignore ?? false,
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            FontSize = 11,
        };
        var removeBtn = new Button
        {
            Content = "✕",
            Background = new SolidColorBrush(Color.Parse("#f38ba8")),
            Foreground = new SolidColorBrush(Color.Parse("#1e1e2e")),
            Padding = new Thickness(6, 3),
            FontSize = 10,
            Width = 26,
        };

        var row = new TableRowControls(nameBox, targetNameBox, whereBox, queryBox, ignoreChk);
        rows.Add(row);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto"),
            ColumnSpacing = 4,
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 3,
        };

        Grid.SetColumn(nameBox, 0); Grid.SetRow(nameBox, 0);
        Grid.SetColumn(ignoreChk, 1); Grid.SetRow(ignoreChk, 0); Grid.SetColumnSpan(ignoreChk, 1);
        Grid.SetColumn(removeBtn, 3); Grid.SetRow(removeBtn, 0);
        Grid.SetColumn(whereBox, 0); Grid.SetRow(whereBox, 1); Grid.SetColumnSpan(whereBox, 2);
        Grid.SetColumn(queryBox, 0); // will add below conditionally

        // Only show where/query when not ignored
        var detailRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 4,
        };
        Grid.SetColumn(whereBox, 0);
        Grid.SetColumn(queryBox, 1);

        var container = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#313244")),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 2),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto"),
                        ColumnSpacing = 6,
                        Children =
                        {
                            nameBox.Also(b => Grid.SetColumn(b, 0)),
                            targetNameBox.Also(b => Grid.SetColumn(b, 1)),
                            ignoreChk.Also(b => { Grid.SetColumn(b, 2); b.VerticalAlignment = VerticalAlignment.Center; }),
                            removeBtn.Also(b => Grid.SetColumn(b, 3)),
                        }
                    },
                    whereBox,
                    queryBox,
                }
            }
        };

        removeBtn.Click += (_, _) =>
        {
            rows.Remove(row);
            panel.Children.Remove(container);
        };

        panel.Children.Add(container);
    }

    // ── AddTenantRow ──────────────────────────────────────────────────────────

    private void AddTenantRow(TenantEntry? t = null)
    {
        TextBox MakeBox(string wm, string? val = null) => new()
        {
            Watermark = wm,
            Text = val ?? "",
            Background = new SolidColorBrush(Color.Parse("#313244")),
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#45475a")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5),
            FontSize = 12,
        };

        var tenantIdBox = MakeBox("Tenant ID (e.g. 142)", t?.TenantId);
        var schemaBox = MakeBox("New schema name (e.g. WarehouseCo)", t?.Schema);
        var dbBox = MakeBox("Database", t?.Database);
        var sourceSchemaBox = MakeBox("Source schema (optional, skips suffix stripping)", t?.SourceSchema);
        var compatCheck = new CheckBox
        {
            Content = "Create compatibility views",
            IsChecked = t?.CreateCompatibilityViews ?? false,
            Foreground = new SolidColorBrush(Color.Parse("#cdd6f4")),
            FontSize = 12,
        };
        var removeBtn = new Button
        {
            Content = "✕",
            Background = new SolidColorBrush(Color.Parse("#f38ba8")),
            Foreground = new SolidColorBrush(Color.Parse("#1e1e2e")),
            Padding = new Thickness(8, 5),
            FontSize = 11,
            Width = 30,
        };

        var row = new TenantRowControls(tenantIdBox, schemaBox, dbBox, compatCheck, sourceSchemaBox);
        _tenantRows.Add(row);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,Auto"),
            ColumnSpacing = 6,
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 4,
        };

        Grid.SetColumn(tenantIdBox, 0); Grid.SetRow(tenantIdBox, 0);
        Grid.SetColumn(schemaBox, 1); Grid.SetRow(schemaBox, 0);
        Grid.SetColumn(removeBtn, 2); Grid.SetRow(removeBtn, 0);
        Grid.SetColumn(dbBox, 0); Grid.SetRow(dbBox, 1);
        Grid.SetColumn(compatCheck, 1); Grid.SetRow(compatCheck, 1);
        Grid.SetColumn(sourceSchemaBox, 0); Grid.SetRow(sourceSchemaBox, 2); Grid.SetColumnSpan(sourceSchemaBox, 2);

        grid.Children.Add(tenantIdBox);
        grid.Children.Add(schemaBox);
        grid.Children.Add(removeBtn);
        grid.Children.Add(dbBox);
        grid.Children.Add(compatCheck);
        grid.Children.Add(sourceSchemaBox);

        var container = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#313244")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = grid,
        };

        removeBtn.Click += (_, _) =>
        {
            _tenantRows.Remove(row);
            _tenantPanel.Children.Remove(container);
        };

        _tenantPanel.Children.Add(container);
    }

    // ── OnSave ────────────────────────────────────────────────────────────────

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var name = _nameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { _nameBox.Focus(); return; }

        int ParsePort(string? s) => int.TryParse(s, out var p) ? p : 1433;

        var config = new MigrationConfig
        {
            Source = new ConnectionConfig
            {
                Server = _srcServer.Text?.Trim() ?? "",
                Port = ParsePort(_srcPort.Text),
                Username = _srcUser.Text?.Trim() ?? "",
                Password = _srcPass.Text ?? "",
                Encrypt = _srcEncrypt.IsChecked ?? false,
                TrustServerCertificate = _srcTrust.IsChecked ?? true,
            },
            Target = new ConnectionConfig
            {
                Server = _tgtServer.Text?.Trim() ?? "",
                Port = ParsePort(_tgtPort.Text),
                Username = _tgtUser.Text?.Trim() ?? "",
                Password = _tgtPass.Text ?? "",
                Encrypt = _tgtEncrypt.IsChecked ?? false,
                TrustServerCertificate = _tgtTrust.IsChecked ?? true,
            },
            Databases = _dbRows.Select(r =>
            {
                var filter = r.Filter.SelectedItem as string ?? "all";
                return new DbEntry
                {
                    SourceDatabase = r.Source.Text?.Trim() ?? "",
                    TargetDatabase = r.Target.Text?.Trim() ?? "",
                    TableFilter = filter,
                    TenantId = r.TenantId.Text?.Trim() is { Length: > 0 } t ? t : null,
                    Comment = r.Comment.Text?.Trim() is { Length: > 0 } c ? c : null,
                    DropAndCreate = r.DropAndCreate.IsChecked ?? false,
                    Tables = filter == "explicit"
                        ? r.TableRows.Select(tr => new JsonTable
                        {
                            Name = tr.Name.Text?.Trim() ?? "",
                            TargetName = tr.TargetName.Text?.Trim() is { Length: > 0 } tn ? tn : null,
                            Where = tr.Where.Text?.Trim() is { Length: > 0 } w ? w : null,
                            Query = tr.Query.Text?.Trim() is { Length: > 0 } q ? q : null,
                            Ignore = tr.Ignore.IsChecked is true ? true : null,
                        }).Where(t => t.Name.Length > 0).ToList()
                        : null,
                    Overrides = filter is "tenant" or "all"
                        ? r.OverrideRows.Select(tr => new TableOverride
                        {
                            Name = tr.Name.Text?.Trim() ?? "",
                            TargetName = tr.TargetName.Text?.Trim() is { Length: > 0 } tn ? tn : null,
                            Where = tr.Where.Text?.Trim() is { Length: > 0 } w ? w : null,
                            Query = tr.Query.Text?.Trim() is { Length: > 0 } q ? q : null,
                            Ignore = tr.Ignore.IsChecked is true ? true : null,
                        }).Where(o => o.Name.Length > 0).ToList()
                        : null,
                };
            }).Where(d => d.SourceDatabase.Length > 0).ToList(),
            Tenants = _tenantRows.Select(r => new TenantEntry
            {
                TenantId = r.TenantId.Text?.Trim() ?? "",
                Schema = r.Schema.Text?.Trim() ?? "",
                Database = r.Database.Text?.Trim() ?? "",
                SourceSchema = r.SourceSchema.Text?.Trim() is { Length: > 0 } ss ? ss : null,
                CreateCompatibilityViews = r.CompatViews.IsChecked ?? false,
            }).Where(t => t.TenantId.Length > 0 || t.SourceSchema != null).ToList(),
        };

        Close(new NamedConfig { Name = name, Config = config });
    }
}

// Helper extension to allow inline property setting
file static class ControlExtensions
{
    public static T Also<T>(this T control, Action<T> configure) where T : Control
    {
        configure(control);
        return control;
    }
}