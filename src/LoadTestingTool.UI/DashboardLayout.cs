using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClosedXML.Excel;
using LoadTestingTool.Reporting;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoadTestingTool.UI;

internal static class DashboardStyle
{
    public const string Build = "Dashboard 2026.10.31.1";
    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
    public static IBrush Panel => Brush("#101E2E");
    public static IBrush Muted => Brush("#8EA4BC");
    public static IBrush Line => Brush("#23364B");
    public static IBrush Blue => Brush("#3291FF");
    public static IBrush Green => Brush("#39D39B");
    public static IBrush Red => Brush("#FF7588");
    public static TextBlock Text(string text, double size = 13, bool bold = false, IBrush? color = null) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = color ?? Brush("#E3EDF9"), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.None
    };
    public static Button Action(string label, Action action, string? style = null)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 8, 6), FontSize = 12 };
        if (style is not null) button.Classes.Add(style);
        button.Click += (_, _) => action();
        return button;
    }
    public static Border Card(Control child, Thickness? padding = null) => new()
    {
        Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        Padding = padding ?? new Thickness(14), Child = child
    };
    public static string Status(string value) => value.ToUpperInvariant() switch
    {
        "PASSED" => "Passed", "FAILED" => "Failed", "CANCELLED" => "Cancelled", _ => value
    };
    public static Border Badge(string value)
    {
        var status = Status(value);
        var (foreground, background) = status switch
        {
            "Passed" => ("#39D39B", "#143F35"),
            "Failed" or "Launch failed" or "Stop failed" => ("#FF7588", "#432333"),
            "Running" => ("#72B7FF", "#193B61"),
            "Stopping" or "Cancelled" or "Force stopped" or "Incomplete" => ("#F6C76B", "#443A25"),
            "Unexpected error" => ("#FFB36B", "#4A3220"),
            _ => ("#A6B9CE", "#24364B")
        };
        return new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 4),
            Background = Brush(background), HorizontalAlignment = HorizontalAlignment.Left,
            Child = Text(status, 11, true, Brush(foreground)) };
    }
}

internal sealed record ExecutedHistoryArtifact(string Path, string Testcase, string Environment, string Result, string Row);
internal sealed record ExecutedLogArtifact(string Path, string Testcase, string Environment, string Result);

internal static class ArtifactCatalog
{
    private static bool SameRunId(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) return true;
        var leftShort = left.Length <= 8 ? left : left[^8..];
        var rightShort = right.Length <= 8 ? right : right[^8..];
        return leftShort.Equals(rightShort, StringComparison.OrdinalIgnoreCase);
    }

    private static string LatestRunId(InstanceInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.CurrentRunId)) return item.CurrentRunId;
        if (!Directory.Exists(item.HistoryFolder)) return "";
        var aggregate = Directory.GetFiles(item.HistoryFolder, "Execution_History_*.xlsx", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        var name = aggregate is null ? "" : Path.GetFileNameWithoutExtension(aggregate);
        return name.StartsWith("Execution_History_", StringComparison.OrdinalIgnoreCase) ? name["Execution_History_".Length..] : "";
    }
    public static IReadOnlyList<ExecutedHistoryArtifact> History(InstanceInfo item)
    {
        var targetRunId = LatestRunId(item);
        if (string.IsNullOrWhiteSpace(targetRunId) || !Directory.Exists(item.HistoryFolder)) return [];
        var output = new List<ExecutedHistoryArtifact>();
        foreach (var path in Directory.GetFiles(item.HistoryFolder, "*.xlsx", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path).StartsWith("~$", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).StartsWith("Execution_History_", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var workbook = new XLWorkbook(path);
                var summary = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("RunSummary", StringComparison.OrdinalIgnoreCase));
                var workbookRunId = summary?.RowsUsed().Skip(1).FirstOrDefault()?.Cell(1).GetString();
                if (!SameRunId(workbookRunId, targetRunId)) continue;
                var name = Path.GetFileNameWithoutExtension(path); var rowMarker = name.IndexOf("_Row", StringComparison.OrdinalIgnoreCase);
                var testcase = rowMarker > 0 ? name[..rowMarker] : name;
                var rowEnd = rowMarker >= 0 ? name.IndexOf('_', rowMarker + 4) : -1;
                var row = rowMarker >= 0 ? (rowEnd > rowMarker ? name[rowMarker..rowEnd] : name[rowMarker..]) : "";
                var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                var result = parts.LastOrDefault(x => x.Equals("passed", StringComparison.OrdinalIgnoreCase) || x.Equals("failed", StringComparison.OrdinalIgnoreCase) || x.Equals("cancelled", StringComparison.OrdinalIgnoreCase)) ?? "unknown";
                var resultIndex = Array.FindIndex(parts, x => x.Equals(result, StringComparison.OrdinalIgnoreCase));
                var environment = resultIndex > 0 ? parts[resultIndex - 1] : "default";
                if (resultIndex >= 0 && resultIndex + 1 < parts.Length &&
                    parts[resultIndex - 1].Equals("History", StringComparison.OrdinalIgnoreCase))
                    environment = parts[resultIndex + 1];
                if (environment.Equals("History", StringComparison.OrdinalIgnoreCase) ||
                    environment.Equals("_aggregate", StringComparison.OrdinalIgnoreCase)) environment = "default";
                output.Add(new ExecutedHistoryArtifact(path, testcase, environment, DashboardStyle.Status(result), row));
            }
            catch { }
        }
        return output;
    }

    public static string? AggregateHistory(InstanceInfo item)
    {
        var runId = LatestRunId(item);
        if (string.IsNullOrWhiteSpace(runId) || !Directory.Exists(item.HistoryFolder)) return null;
        return Directory.GetFiles(item.HistoryFolder, "Execution_History_*.xlsx", SearchOption.AllDirectories)
            .Where(x => Path.GetFileName(x).Contains(runId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public static IReadOnlyList<ExecutedLogArtifact> Logs(InstanceInfo item)
    {
        var runId = LatestRunId(item);
        if (string.IsNullOrWhiteSpace(runId) || !Directory.Exists(item.LogsFolder)) return [];
        var shortRunId = runId.Length <= 8 ? runId : runId[^8..];
        var output = new List<ExecutedLogArtifact>();
        foreach (var path in Directory.GetFiles(item.LogsFolder, "*.log", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.Contains($"_Run_{shortRunId}", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_process", StringComparison.OrdinalIgnoreCase)) continue;
            var status = new[] { "passed", "failed", "cancelled" }.FirstOrDefault(x => name.Contains($"_{x}_", StringComparison.OrdinalIgnoreCase));
            if (status is null) continue;
            var prefix = name[..name.IndexOf($"_{status}_", StringComparison.OrdinalIgnoreCase)];
            var environment = item.EnvironmentOptions.OrderByDescending(x => x.Length).FirstOrDefault(x => prefix.EndsWith($"_{x}", StringComparison.OrdinalIgnoreCase));
            var testcase = environment is null ? prefix : prefix[..^(environment.Length + 1)];
            output.Add(new ExecutedLogArtifact(path, testcase, string.IsNullOrWhiteSpace(environment) ? "default" : environment, DashboardStyle.Status(status)));
        }
        return output;
    }
}

// The table only selects and summarizes an instance. Editable settings live in Configuration.
internal sealed class InstanceRow : Border
{
    private readonly InstanceInfo _item;
    private readonly CheckBox _check;
    private readonly TextBlock _testcases = DashboardStyle.Text("");
    private readonly TextBlock _mode = DashboardStyle.Text("");
    private readonly TextBlock _order = DashboardStyle.Text("");
    private readonly TextBlock _environment = DashboardStyle.Text("");
    private readonly Border _badgeHost = new();
    private Button _runButton = null!;
    public InstanceRow(InstanceInfo item, Action<InstanceInfo> select, Action<InstanceInfo> configure, Action<InstanceInfo> trySpreadsheet, Action<InstanceInfo> openWorkbook, Action<InstanceInfo> run, Action<InstanceInfo> stop, Action<InstanceInfo, string, string, string, string> openSelectedLog, Action<InstanceInfo> openLatestLog, Action<InstanceInfo> openAllLogs, Action<InstanceInfo, string, string, string, string, bool> openSelectedHistory, Action<InstanceInfo, bool> openLatestHistory, Action<string> log)
    {
        _item = item;
        Padding = new Thickness(8, 12);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        BorderBrush = DashboardStyle.Line;
        BorderThickness = new Thickness(0, 0, 0, 1);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,62,80,82,90,92,560"), MinWidth = 1290 };
        _check = new CheckBox { IsChecked = item.Selected, VerticalAlignment = VerticalAlignment.Center, MinWidth = 24 };
        _check.IsCheckedChanged += (_, _) => { item.Selected = _check.IsChecked == true; log($"Instance selection changed. instance={item.Name}; selected={item.Selected}"); select(item); Refresh(); };
        var name = DashboardStyle.Text(item.Name, 12, true);
        ToolTip.SetTip(name, item.WorkbookPath);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN, ItemHeight = double.NaN };
        _runButton = DashboardStyle.Action("Run", () => run(item), "primary");
        var runButton = _runButton;
        ToolTip.SetTip(runButton, "Run all enabled workbook steps using workbook defaults. Use Configure first for a custom selection.");
        var logButton = BuildInternalLogButton(item, openSelectedLog, openLatestLog, openAllLogs);
        var historyButton = BuildHistoryButton(item, openSelectedHistory, openLatestHistory);
        foreach (var button in new[] { runButton, DashboardStyle.Action("Stop", () => stop(item)), DashboardStyle.Action("Configure", () => configure(item)), DashboardStyle.Action("✎  Edit dataset", () => trySpreadsheet(item)), DashboardStyle.Action("Open workbook", () => openWorkbook(item)), logButton, historyButton }) { button.Margin = new Thickness(0, 0, 4, 4); button.Padding = new Thickness(7, 5); actions.Children.Add(button); }
        var cells = new Control[] { _check, name, _testcases, _mode, _order, _environment, _badgeHost, actions };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); cells[i].Margin = new Thickness(0, 0, 6, 0); grid.Children.Add(cells[i]); }
        Child = grid;
        PointerPressed += (_, _) => select(item);
        Refresh();
    }
    public void Refresh()
    {
        _check.IsChecked = _item.Selected;
        _testcases.Text = _item.TestcaseSelection == "0" ? "All" : _item.TestcaseSelection;
        _mode.Text = _item.RowsExecutionMode == "sequential" ? "Sequential" : "Parallel";
        _order.Text = _item.RunEnvironmentsParallel ? "Parallel" : "Sequential";
        _environment.Text = string.IsNullOrWhiteSpace(_item.EnvironmentSelection) ? "All" : _item.EnvironmentSelection;
        ToolTip.SetTip(_environment, string.Join(", ", _item.EnvironmentOptions));
        _badgeHost.Child = DashboardStyle.Badge(_item.Status);
        _runButton.IsEnabled = _item.Process is null || _item.Process.HasExited;
        Background = _item.Selected ? DashboardStyle.Brush("#173559") : Brushes.Transparent;
    }
    public void SetEnvironmentOptions(IEnumerable<string> options) => Refresh();

    private static Button BuildInternalLogButton(InstanceInfo item, Action<InstanceInfo, string, string, string, string> openSelected, Action<InstanceInfo> openLatest, Action<InstanceInfo> openAll)
    {
        var testcase = new ComboBox { Width = 220 };
        var environment = new ComboBox { Width = 180 };
        var result = new ComboBox { Width = 140 };
        var run = new ComboBox { ItemsSource = new[] { "Latest run" }, SelectedIndex = 0, Width = 140 };
        var content = new StackPanel { Spacing = 8, Width = 430, Children =
        {
            DashboardStyle.Text("Internal logs", 15, true),
            Field("Testcase", testcase), Field("Environment", environment), Field("Result", result), Field("Run", run),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
            {
                DashboardStyle.Action("Open selected log", () => openSelected(item, testcase.SelectedItem?.ToString() ?? "", environment.SelectedItem?.ToString() ?? "default", result.SelectedItem?.ToString() ?? "All", run.SelectedItem?.ToString() ?? "Latest run"), "primary"),
                DashboardStyle.Action("Open latest log", () => openLatest(item)),
                DashboardStyle.Action("Open all logs", () => openAll(item))
            }}
        }};
        var flyout = new Flyout { Content = content, Placement = PlacementMode.Bottom };
        var refreshing = false;
        void RefreshOptions()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
            static void SetOptions(ComboBox combo, IEnumerable<string> values)
            {
                var previous = combo.SelectedItem?.ToString();
                var options = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                combo.ItemsSource = options;
                combo.SelectedItem = options.FirstOrDefault(option => option.Equals(previous, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
            }
            var artifacts = ArtifactCatalog.Logs(item);
            var testcases = artifacts.Select(x => x.Testcase).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            SetOptions(testcase, testcases);
            var selectedTestcase = testcase.SelectedItem?.ToString();
            var scoped = artifacts.Where(x => string.Equals(x.Testcase, selectedTestcase, StringComparison.OrdinalIgnoreCase)).ToList();
            var environments = scoped.Select(x => x.Environment).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            SetOptions(environment, environments);
            var selectedEnvironment = environment.SelectedItem?.ToString();
            var results = scoped.Where(x => string.Equals(x.Environment, selectedEnvironment, StringComparison.OrdinalIgnoreCase)).Select(x => x.Result).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            SetOptions(result, new[] { "All" }.Concat(results));
            }
            finally { refreshing = false; }
        }
        flyout.Opened += (_, _) => RefreshOptions();
        testcase.SelectionChanged += (_, _) => RefreshOptions();
        environment.SelectionChanged += (_, _) => RefreshOptions();
        var button = new Button { Content = "Internal logs ▾", Padding = new Thickness(7, 5), Flyout = flyout };
        ToolTip.SetTip(button, "Select testcase, environment, result, and run, then open internal logs.");
        return button;
    }

    private static Control Field(string label, Control control) => new StackPanel { Spacing = 3, Children = { DashboardStyle.Text(label, 11, false, DashboardStyle.Muted), control } };

    private static Button BuildHistoryButton(InstanceInfo item, Action<InstanceInfo, string, string, string, string, bool> openSelected, Action<InstanceInfo, bool> openLatest)
    {
        var testcase = new ComboBox { Width = 220 };
        var environment = new ComboBox { Width = 180 };
        var result = new ComboBox { Width = 140 };
        var row = new ComboBox { Width = 140 };
        var run = new ComboBox { ItemsSource = new[] { "Latest run" }, SelectedIndex = 0, Width = 140 };
        var openWith = new ComboBox { ItemsSource = new[] { "Spreadsheet viewer", "Default Excel app" }, SelectedIndex = 0, Width = 170 };
        var content = new StackPanel { Spacing = 8, Width = 450, Children =
        {
            DashboardStyle.Text("History", 15, true),
            Field("Testcase", testcase), Field("Environment", environment), Field("Result", result), Field("Row", row), Field("Run", run), Field("Open with", openWith),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
            {
                DashboardStyle.Action("Open selected history", () => openSelected(item, testcase.SelectedItem?.ToString() ?? "", environment.SelectedItem?.ToString() ?? "default", result.SelectedItem?.ToString() ?? "All", row.SelectedItem?.ToString() ?? "All rows", openWith.SelectedIndex == 0), "primary"),
                DashboardStyle.Action("Open latest history", () => openLatest(item, openWith.SelectedIndex == 0))
            }}
        }};
        var flyout = new Flyout { Content = content, Placement = PlacementMode.Bottom };
        var refreshing = false;
        void RefreshOptions()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
            var artifacts = ArtifactCatalog.History(item);
            static void SetOptions(ComboBox combo, IEnumerable<string> values)
            {
                var previous = combo.SelectedItem?.ToString();
                var options = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                combo.ItemsSource = options;
                combo.SelectedItem = options.FirstOrDefault(value => value.Equals(previous, StringComparison.OrdinalIgnoreCase))
                    ?? options.FirstOrDefault();
            }
            var testcases = artifacts.Select(x => x.Testcase).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            SetOptions(testcase, testcases);
            var selectedTestcase = testcase.SelectedItem?.ToString();
            var scoped = artifacts.Where(x => string.Equals(x.Testcase, selectedTestcase, StringComparison.OrdinalIgnoreCase)).ToList();
            var environments = scoped.Select(x => x.Environment).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            SetOptions(environment, environments);
            var selectedEnvironment = environment.SelectedItem?.ToString();
            scoped = scoped.Where(x => string.Equals(x.Environment, selectedEnvironment, StringComparison.OrdinalIgnoreCase)).ToList();
            var results = scoped.Select(x => x.Result).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            SetOptions(result, new[] { "All" }.Concat(results));
            var rows = scoped.Select(x => x.Row).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var aggregateRows = ArtifactCatalog.AggregateHistory(item) is null ? Array.Empty<string>() : new[] { "All rows" };
            SetOptions(row, aggregateRows.Concat(rows));
            }
            finally { refreshing = false; }
        }
        flyout.Opened += (_, _) => RefreshOptions();
        testcase.SelectionChanged += (_, _) => RefreshOptions();
        environment.SelectionChanged += (_, _) => RefreshOptions();
        var button = new Button { Content = "History ▾", Padding = new Thickness(7, 5), Flyout = flyout };
        ToolTip.SetTip(button, "Select testcase, environment, result, row, run, and opening method for history.");
        return button;
    }
}

public sealed partial class MainWindow
{
    private readonly StackPanel _overviewPanel = new() { Spacing = 8 };
    private readonly StackPanel _runSummaryPanel = new() { Spacing = 10 };
    private readonly StackPanel _configurationPanel = new() { Spacing = 12 };
    private readonly StackPanel _artifactPanel = new() { Spacing = 10 };
    private readonly StackPanel _historyPanel = new() { Spacing = 10 };
    private readonly TextBox _historySearch = new() { Watermark = "Filter instance, testcase, step, Data ID, status…", Width = 330 };
    private readonly ComboBox _historyStatus = new() { ItemsSource = new[] { "All statuses", "Passed", "Failed", "Cancelled" }, SelectedIndex = 0, Width = 140 };
    private readonly StackPanel _historyResults = new() { Spacing = 5 };
    private readonly List<HistoryEntry> _historyEntries = [];
    private readonly HashSet<string> _historySelected = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _historyCompare = DashboardStyle.Text("Select two history files to compare.", 12, false, DashboardStyle.Muted);
    private bool _historyRefreshing;
    private readonly StackPanel _eventPanel = new() { Spacing = 3 };
    private readonly StackPanel _manualItems = new() { Spacing = 8 };
    private readonly ComboBox _manualEnvironment = new() { Width = 180 };
    private readonly ComboBox _manualTestcase = new() { Width = 220 };
    private readonly TextBox _manualFilter = new() { Width = 280, Watermark = "Filter actions: term1 & term2…" };
    private TabItem _manualTab = null!;
    private readonly TextBox _overviewFilter = new() { Watermark = "Filter overview: term1 & term2…", Width = 260 };
    private readonly TextBox _assertionSearch = new() { Watermark = "Filter assertions: term1 & term2…", Width = 260 };
    private readonly TextBlock _selectedTitle = DashboardStyle.Text("Selected instance", 16, true);
    private readonly TextBlock _pageTitle = DashboardStyle.Text("Instances", 18, true);
    private readonly TextBlock _listTitle = DashboardStyle.Text("Test instances", 15, true);
    private readonly TextBlock _footer = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly TextBox _search = new() { Watermark = "Search instances…", Width = 200, FontSize = 12 };
    private readonly Dictionary<string, Button> _navigation = [];
    private readonly List<(string Text, string Severity)> _validationLog = [];
    private readonly Grid _workspace = new() { RowDefinitions = new RowDefinitions("Auto,*") };
    private readonly TabControl _detailTabs = new();
    private Button _runButton = null!;
    private Button _chooseButton = null!;
    private Button _validateButton = null!;
    private Button _stopButton = null!;
    private Button _forceButton = null!;
    private string _page = "Instances";
    private Grid _body = null!;
    private Control _settingsPage = null!;
    private Control _historyPage = null!;
    private string _detailSignature = "";
    private double _logFontSize = 11;
    private const int DashboardPageSize = 200;
    private int _overviewPage = 1;
    private int _assertionPage = 1;
    private readonly TextBlock _assertionPageLabel = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private Button _assertionPreviousButton = null!;
    private Button _assertionNextButton = null!;

    private void BuildLayout()
    {
        Background = DashboardStyle.Brush("#0A1421");
        Foreground = DashboardStyle.Brush("#E3EDF9");
        Width = 1440; Height = 900; MinWidth = 1280; MinHeight = 700;
        Title = $"Load Testing Tool • {DashboardStyle.Build}";
        var sidebar = new DockPanel { Background = DashboardStyle.Brush("#0D1B2C"), LastChildFill = true };
        var brand = new StackPanel { Margin = new Thickness(18, 24, 12, 24), Spacing = 7,
            Children = { DashboardStyle.Text("Load Testing Tool", 14, true), DashboardStyle.Text(DashboardStyle.Build, 10, false, DashboardStyle.Muted) } };
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var sideFooter = new StackPanel { Margin = new Thickness(18, 16), Spacing = 6,
            Children = { DashboardStyle.Text("Local runner", 12, true, DashboardStyle.Green), DashboardStyle.Text("Windows / Avalonia", 11, false, DashboardStyle.Muted) } };
        DockPanel.SetDock(sideFooter, Dock.Bottom); sidebar.Children.Add(sideFooter);
        var links = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0) };
        foreach (var page in new[] { "Instances", "Active runs", "History", "Settings" })
        {
            var button = DashboardStyle.Action(page, () => Navigate(page));
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0); button.Padding = new Thickness(14, 12); button.Classes.Add("nav");
            _navigation[page] = button; links.Children.Add(button);
        }
        sidebar.Children.Add(links);

        var heading = new StackPanel { Spacing = 5, Children = { _pageTitle, DashboardStyle.Text("Manage test instances and inspect execution results", 12, false, DashboardStyle.Muted) } };
        _chooseButton = DashboardStyle.Action("Choose what to run", () => _ = ChooseRunSelectionAsync(_selectedInstance ?? _instances.FirstOrDefault(x => x.Selected)));
        _runButton = DashboardStyle.Action("▶  Run selected instances", () => RunSelected(promptForMissingSelection: false), "primary");
        _stopButton = DashboardStyle.Action("Stop selected", () => StopSelected(false));
        _forceButton = DashboardStyle.Action("Force stop", () => StopSelected(true), "danger");
        _validateButton = DashboardStyle.Action("✓ Validate all", OpenGlobalValidation, "primary");
        var environmentButton = DashboardStyle.Action("🌐 Manage environments", OpenEnvironmentManager, "primary");
        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { environmentButton, _validateButton, _chooseButton, _runButton, _stopButton, _forceButton } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 16) };
        header.Children.Add(heading); Grid.SetColumn(commands, 1); header.Children.Add(commands);

        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"), Margin = new Thickness(0, 0, 0, 14) };
        var labels = new[] { "Ready", "Running", "Passed", "Failed", "Requests / sec" };
        var values = new[] { _readyValue, _runningValue, _passedValue, _failedValue, _rpsValue };
        var colors = new[] { "#A6B9CE", "#3291FF", "#39D39B", "#FF7588", "#7295FF" };
        for (var i = 0; i < values.Length; i++)
        {
            values[i].FontSize = 25; values[i].FontWeight = FontWeight.SemiBold; values[i].Foreground = DashboardStyle.Brush(colors[i]);
            var card = DashboardStyle.Card(new StackPanel { Spacing = 6, Children = { DashboardStyle.Text(labels[i], 12, false, DashboardStyle.Muted), values[i] } });
            card.Margin = new Thickness(0, 0, i == 4 ? 0 : 10, 0); Grid.SetColumn(card, i); stats.Children.Add(card);
        }
        _workspace.Children.Add(stats);
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12) };
        toolbar.Children.Add(_listTitle);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Children = { _search,
            DashboardStyle.Action("New instance", () => _ = CreateInstanceAsync(), "primary"), DashboardStyle.Action("Refresh", RefreshInstances), DashboardStyle.Action("All", () => { foreach (var item in _instances) item.Selected = true; RefreshRows(); }),
            DashboardStyle.Action("Clear", () => { foreach (var item in _instances) item.Selected = false; RefreshRows(); }) } };
        _search.Margin = new Thickness(0, 0, 8, 6); _search.TextChanged += (_, _) => FilterInstances();
        Grid.SetColumn(tools, 1); toolbar.Children.Add(tools);
        var columnHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,62,80,82,90,92,430"), Margin = new Thickness(8, 8), MinWidth = 1160 };
        var columns = new[] { "", "Instance", "Cases", "Mode", "Order", "Env.", "Status", "Actions" };
        for (var i = 0; i < columns.Length; i++) { var text = DashboardStyle.Text(columns[i], 11, false, DashboardStyle.Muted); Grid.SetColumn(text, i); columnHeader.Children.Add(text); }
        _instanceList.Background = Brushes.Transparent; _instanceList.BorderThickness = new Thickness(0);
        _instanceList.ItemTemplate = new FuncDataTemplate<InstanceInfo>((item, _) => item is null ? new Border() : GetOrCreateRow(item));
        _instanceList.SelectionChanged += (_, _) => ShowDetails(_instanceList.SelectedItem as InstanceInfo);
        _instanceList.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Avalonia.Styling.Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Avalonia.Styling.Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
        });
        var table = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        table.Children.Add(toolbar); Grid.SetRow(columnHeader, 1); table.Children.Add(columnHeader); Grid.SetRow(_instanceList, 2); table.Children.Add(_instanceList);
        var tableCard = DashboardStyle.Card(table, new Thickness(0));
        var eventHeader = DashboardStyle.Text("Log", 13, true);
        var events = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var logTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { eventHeader,
            DashboardStyle.Action("−", () => ChangeLogFont(-1)), DashboardStyle.Action("+", () => ChangeLogFont(1)), DashboardStyle.Action("Clear", ClearUnifiedLog) } };
        events.Children.Add(logTools);
        var eventScroll = new ScrollViewer { Content = _eventPanel, Margin = new Thickness(0, 10, 0, 0) };
        Grid.SetRow(eventScroll, 1); events.Children.Add(eventScroll);
        var eventCard = DashboardStyle.Card(events); eventCard.Margin = new Thickness(0, 12, 0, 0);
        var left = new Grid { RowDefinitions = new RowDefinitions("3*,2*") };
        left.Children.Add(tableCard); Grid.SetRow(eventCard, 1); left.Children.Add(eventCard);

        _failedOnly.IsCheckedChanged += (_, _) => RefreshAssertionList();
        _environmentFilter.Width = 175; _environmentFilter.Margin = new Thickness(0);
        _environmentFilter.SelectionChanged += (_, _) => RefreshAssertionList();
        _environmentFilter.ItemsSource = new[] { "All environments" }; _environmentFilter.SelectedIndex = 0;
        _overviewFilter.TextChanged += (_, _) => RefreshDashboardDetails(true);
        _overviewStepFilter.TextChanged += (_, _) =>
        {
            if (_overviewStepFilterRefreshing) return;
            var caret = _overviewStepFilter.CaretIndex;
            RefreshDashboardDetails(true);
            Dispatcher.UIThread.Post(() =>
            {
                _overviewStepFilter.Focus();
                _overviewStepFilter.CaretIndex = Math.Min(caret, _overviewStepFilter.Text?.Length ?? 0);
            });
        };
        _overviewStepStatus.SelectionChanged += (_, _) => { if (!_overviewStepFilterRefreshing) RefreshDashboardDetails(true); };
        _overviewStepEnvironment.SelectionChanged += (_, _) => { if (!_overviewStepFilterRefreshing) RefreshDashboardDetails(true); };
        _overviewStepTestcase.SelectionChanged += (_, _) => { if (!_overviewStepFilterRefreshing) RefreshDashboardDetails(true); };
        _assertionSearch.TextChanged += (_, _) => RefreshAssertionList();
        var assertionActions = new WrapPanel { Children = { _assertionSearch, DashboardStyle.Action("Open Assertion Workbench", OpenAssertionWorkbench, "primary"), DashboardStyle.Action("Copy assertions", () => _ = CopyAssertionsAsync()), DashboardStyle.Action("Select failed", () => { if (_selectedInstance is not null) foreach (var a in _selectedInstance.Assertions.Where(a => a.Result == "FAIL")) a.Selected = true; RefreshAssertionList(); }),
            DashboardStyle.Action("Clear", () => { if (_selectedInstance is not null) foreach (var a in _selectedInstance.Assertions) a.Selected = false; RefreshAssertionList(); }),
            DashboardStyle.Action("Apply actual values", ApplySelectedFixes), DashboardStyle.Action("Export comparison", () => _ = GenerateAssertionComparisonAsync()), DashboardStyle.Action("Open comparison", OpenLatestAssertionComparison) } };
        var assertionPanel = new DockPanel { Margin = new Thickness(12) };
        var filters = new StackPanel { Spacing = 10, Children = { _failedOnly, _environmentFilter, assertionActions } };
        DockPanel.SetDock(filters, Dock.Top); assertionPanel.Children.Add(filters);
        assertionPanel.Children.Add(new ScrollViewer { Content = _assertionItems, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var assertionPaging = CreatePaginationBar(false);
        DockPanel.SetDock(assertionPaging, Dock.Bottom); assertionPanel.Children.Add(assertionPaging);
        _manualTab = new TabItem { Header = BuildActionManualTabHeader(0), Content = BuildActionManualPanel() };
        _detailTabs.ItemsSource = new[]
        {
            new TabItem { Header = "Overview", Content = Scroll(_overviewPanel) },
            _manualTab,
            new TabItem { Header = "Run Summary", Content = Scroll(_runSummaryPanel) },
            new TabItem { Header = "Assertions", Content = assertionPanel },
            new TabItem { Header = "Artifacts", Content = Scroll(_artifactPanel) }
        };
        _overviewPanel.Children.Insert(0, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _overviewFilter, DashboardStyle.Action("Copy overview", () => _ = CopyOverviewAsync()) } });
        var selectedHeader = new StackPanel { Spacing = 6, Margin = new Thickness(14), Children = { _selectedTitle,
            DashboardStyle.Text("Select a row to inspect. Tick a box to run.", 11, false, DashboardStyle.Muted) } };
        var right = new DockPanel(); DockPanel.SetDock(selectedHeader, Dock.Top); right.Children.Add(selectedHeader); right.Children.Add(_detailTabs);
        var rightCard = DashboardStyle.Card(right, new Thickness(0)); rightCard.Margin = new Thickness(14, 0, 0, 0);
        var splitter = new GridSplitter { Width = 5, Background = DashboardStyle.Line, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, HorizontalAlignment = HorizontalAlignment.Stretch };
        _body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,375") };
        _body.Children.Add(left); Grid.SetColumn(splitter, 1); _body.Children.Add(splitter); Grid.SetColumn(rightCard, 2); _body.Children.Add(rightCard);
        Grid.SetRow(_body, 1); _workspace.Children.Add(_body);
        _workspace.Margin = new Thickness(18, 0, 18, 0);
        _settingsPage = CreateSettingsPage(); _historyPage = Scroll(_historyPanel);
        _details.TextWrapping = TextWrapping.Wrap; _details.Foreground = DashboardStyle.Muted; _details.FontSize = 11;
        var diagnostics = new ScrollViewer { Content = _details, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var bottom = new StackPanel { Margin = new Thickness(18, 10), Spacing = 4, Children = { _footer, diagnostics } };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        content.Children.Add(header); Grid.SetRow(_workspace, 1); content.Children.Add(_workspace); Grid.SetRow(bottom, 2); content.Children.Add(bottom);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
        root.Children.Add(sidebar); Grid.SetColumn(content, 1); root.Children.Add(content);
        Content = root;
        Navigate("Instances");
    }

    private static ScrollViewer Scroll(Control control) => new() { Content = control, Padding = new Thickness(14), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private void Navigate(string page)
    {
        if (page.Equals("History", StringComparison.OrdinalIgnoreCase))
        {
            LaunchHistoryWorkspace();
            return;
        }
        _page = page; _pageTitle.Text = page;
        foreach (var (name, button) in _navigation) { if (name == page) button.Classes.Add("active"); else button.Classes.Remove("active"); }
        _workspace.Children.Remove(_body); _workspace.Children.Remove(_settingsPage); _workspace.Children.Remove(_historyPage);
        var view = page == "Settings" ? _settingsPage : page == "History" ? _historyPage : _body;
        Grid.SetRow(view, 1); _workspace.Children.Add(view);
        if (page == "History") RefreshHistoryPanel();
        FilterInstances();
    }

    private void LaunchHistoryWorkspace(string? instance = null, string? testcase = null)
    {
        try
        {
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The dashboard process path is unavailable.");
            var start = new ProcessStartInfo { FileName = processPath, UseShellExecute = false, CreateNoWindow = true };
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(entryAssembly) && (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))) start.ArgumentList.Add(entryAssembly);
            start.ArgumentList.Add("--history"); start.ArgumentList.Add(Path.GetFullPath(_config.InstancesRoot));
            if (!string.IsNullOrWhiteSpace(instance)) { start.ArgumentList.Add("--history-instance"); start.ArgumentList.Add(instance); }
            if (!string.IsNullOrWhiteSpace(testcase)) { start.ArgumentList.Add("--history-testcase"); start.ArgumentList.Add(testcase); }
            var history = Process.Start(start) ?? throw new InvalidOperationException("The History workspace process could not be started.");
            history.EnableRaisingEvents = true;
            history.Exited += (_, _) => Dispatcher.UIThread.Post(() => UiLog($"History workspace closed. exitCode={history.ExitCode}"));
            UiLog($"History workspace started. processId={history.Id}; root={_config.InstancesRoot}");
        }
        catch (Exception ex) { _details.Text = $"Could not start History workspace: {ex.Message}"; UiLog($"History workspace failed to start. error={ex}"); }
    }
    private void FilterInstances()
    {
        var selected = _selectedInstance;
        var query = _search.Text?.Trim() ?? "";
        var visible = _instances.Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(x => _page != "Active runs" || x.Process is { HasExited: false }).ToList();
        _instanceList.ItemsSource = visible;
        _listTitle.Text = $"{(_page == "Active runs" ? "Active runs" : "Test instances")} ({visible.Count})";
        _instanceList.SelectedItem = visible.Contains(selected!) ? selected : visible.FirstOrDefault();
    }
    private Control BuildActionManualPanel()
    {
        _manualEnvironment.ItemsSource = new[] { "All environments" }; _manualEnvironment.SelectedIndex = 0;
        _manualTestcase.ItemsSource = new[] { "All testcases" }; _manualTestcase.SelectedIndex = 0;
        _manualEnvironment.SelectionChanged += (_, _) => RefreshActionManualPanel();
        _manualTestcase.SelectionChanged += (_, _) => RefreshActionManualPanel();
        _manualFilter.TextChanged += (_, _) => RefreshActionManualPanel();
        var filters = new WrapPanel { Orientation = Orientation.Horizontal, Children = { DashboardStyle.Text("Environment", 11, false, DashboardStyle.Muted), _manualEnvironment, DashboardStyle.Text("Testcase", 11, false, DashboardStyle.Muted), _manualTestcase, _manualFilter } };
        var actionScroll = new ScrollViewer
        {
            Content = _manualItems,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(12) };
        panel.Children.Add(filters);
        Grid.SetRow(actionScroll, 1); panel.Children.Add(actionScroll);
        return panel;
    }

    private static Control BuildActionManualTabHeader(int pendingCount)
    {
        var title = DashboardStyle.Text("ActionManual", 12, true);
        var count = DashboardStyle.Badge(pendingCount.ToString());
        count.Background = DashboardStyle.Brush(pendingCount > 0 ? "#E05260" : "#8EA5BC");
        count.Padding = new Thickness(6, 2);
        if (count.Child is TextBlock countText) countText.Foreground = DashboardStyle.Brush(pendingCount > 0 ? "#FFFFFF" : "#102033");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { title, count } };
    }

    private void RefreshActionManualPanel()
    {
        if (_manualItems is null) return;
        _manualItems.Children.Clear();
        var item = _selectedInstance;
        var actions = item?.ManualActions ?? [];
        var environments = new[] { "All environments" }.Concat(actions.Select(x => string.IsNullOrWhiteSpace(x.Environment) ? "default" : x.Environment).Distinct(StringComparer.OrdinalIgnoreCase)).ToList();
        var testcases = new[] { "All testcases" }.Concat(actions.Select(x => x.Testcase).Distinct(StringComparer.OrdinalIgnoreCase)).ToList();
        if (_manualEnvironment.ItemsSource is not IEnumerable<string> existingEnvironments || !existingEnvironments.SequenceEqual(environments, StringComparer.OrdinalIgnoreCase)) { _manualEnvironment.ItemsSource = environments; _manualEnvironment.SelectedIndex = 0; }
        if (_manualTestcase.ItemsSource is not IEnumerable<string> existingTestcases || !existingTestcases.SequenceEqual(testcases, StringComparer.OrdinalIgnoreCase)) { _manualTestcase.ItemsSource = testcases; _manualTestcase.SelectedIndex = 0; }
        var env = _manualEnvironment.SelectedItem?.ToString(); var tc = _manualTestcase.SelectedItem?.ToString();
        var visible = actions.Where(x => x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .Where(x => env is null or "All environments" || (string.IsNullOrWhiteSpace(x.Environment) ? "default" : x.Environment).Equals(env, StringComparison.OrdinalIgnoreCase))
            .Where(x => tc is null or "All testcases" || x.Testcase.Equals(tc, StringComparison.OrdinalIgnoreCase))
            .Where(x => MatchesTerms($"{x.Environment} {x.Testcase} {x.DataId} {x.StepName} {x.ManualInfo}", _manualFilter.Text)).ToList();
        var pendingCount = actions.Count(x => x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
        _manualTab.Header = BuildActionManualTabHeader(pendingCount);
        if (visible.Count == 0) { _manualItems.Children.Add(DashboardStyle.Text(actions.Count == 0 ? "No pending manual actions." : "No actions match the current filters.", 12, false, DashboardStyle.Muted)); return; }
        foreach (var action in visible)
        {
            var message = new TextBox { Text = action.ManualInfo, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = DashboardStyle.Brush("#E3EDF9") };
            var comment = new TextBox { Watermark = "Operator comment (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 45 };
            var pass = DashboardStyle.Action("Pass", () => RespondToManual(action, "pass", comment.Text ?? ""), "primary");
            var fail = DashboardStyle.Action("Fail", () => RespondToManual(action, "fail", comment.Text ?? "")); fail.Classes.Add("danger");
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DashboardStyle.Badge("Pending"), DashboardStyle.Text($"{action.Environment}  /  {action.Testcase}  /  Row: {action.DataId}  /  {action.StepName}", 12, true) } };
            var card = DashboardStyle.Card(new StackPanel { Spacing = 7, Children = { header, DashboardStyle.Text($"Timeout: {action.TimeoutMinutes} minute(s)", 11, false, DashboardStyle.Muted), message, comment, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { pass, fail } } } });
            _manualItems.Children.Add(card);
        }
    }

    private void RespondToManual(ActionManualInfo action, string result, string comment)
    {
        try
        {
            var item = _selectedInstance;
            var folder = action.ControlFolder;
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("The runner control folder is unavailable. Wait for the pending action to refresh.");
            ManualActionControl.WriteResponse(folder, action.RequestId, result, comment);
            action.Status = result.Equals("pass", StringComparison.OrdinalIgnoreCase) ? "Passed" : "Failed";
            action.Comment = comment;
            UiLog($"ActionManual response sent. instance={item?.Name}; requestId={action.RequestId}; result={result}; commentLength={comment.Length}");
            RefreshActionManualPanel();
        }
        catch (Exception ex) { _details.Text = $"Could not respond to ActionManual: {ex.Message}"; UiLog($"ActionManual response failed. error={ex}"); }
    }

    private Control CreateSettingsPage()
    {
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(DashboardStyle.Text("Local runner settings", 20, true));
        panel.Children.Add(DashboardStyle.Text("Global paths are stored in the central appsettings.json. Instance folders are derived as InstancesRoot/<instance name>.", 12, false, DashboardStyle.Muted));
        var instancesRoot = new TextBox { Text = _config.InstancesRoot, HorizontalAlignment = HorizontalAlignment.Stretch };
        var workbookName = new TextBox { Text = _config.WorkbookName, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(Field("Instances root", instancesRoot));
        panel.Children.Add(Field("Global workbook filename", workbookName));
        panel.Children.Add(DashboardStyle.Action("Save global paths", () => SaveGlobalPathSettings(instancesRoot.Text, workbookName.Text), "primary"));
        panel.Children.Add(DashboardStyle.Text("Runner", 12, false, DashboardStyle.Muted));
        panel.Children.Add(KeyValue("Path", Path.GetFullPath(_config.RunnerDll)));
        panel.Children.Add(KeyValue("UI log", Path.GetFullPath(_config.UiLogFile)));
        panel.Children.Add(KeyValue("Documentation", Path.GetFullPath(_config.DocumentationFile)));
        panel.Children.Add(DashboardStyle.Text("Files and folders open with your system default applications. Set file associations in your OS settings.", 12, false, DashboardStyle.Muted));
        panel.Children.Add(_enableInternalLog);
        panel.Children.Add(DashboardStyle.Action("Open UI log", () => OpenFile(Path.GetFullPath(_config.UiLogFile), "UI log")));
        panel.Children.Add(DashboardStyle.Action("Open user manual", () => OpenFile(Path.GetFullPath(_config.DocumentationFile), "user manual")));
        panel.Children.Add(DashboardStyle.Text("The dashboard reads and writes the central appsettings.json beside the application installation.", 12, false, DashboardStyle.Muted));
        return Scroll(panel);
    }
    private void SaveGlobalPathSettings(string? instancesRootText, string? workbookNameText)
    {
        var workbookName = Path.GetFileName((workbookNameText ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(workbookName) || workbookName.Equals(".", StringComparison.Ordinal) || workbookName.Equals("..", StringComparison.Ordinal) || !workbookName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            _details.Text = "Global workbook filename must be a valid .xlsx filename, for example DataEngine.xlsx.";
            return;
        }
        try
        {
            var settingsDirectory = Path.GetDirectoryName(_config.SettingsFile)!;
            var fullRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(instancesRootText) ? _config.InstancesRoot : instancesRootText.Trim(), settingsDirectory);
            var storedRoot = Path.GetRelativePath(settingsDirectory, fullRoot).Replace('\\', '/');
            var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
            root ??= new JsonObject();
            root["InstancesRoot"] = storedRoot;
            root["WorkbookName"] = workbookName;
            if (root["Instances"] is JsonObject instances)
                foreach (var pair in instances)
                    if (pair.Value is JsonObject instance)
                    {
                        instance.Remove("Folder");
                        instance.Remove("Workbook");
                    }
            File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            _config.InstancesRoot = fullRoot;
            _config.WorkbookName = workbookName;
            RefreshInstances();
            _details.Text = $"Saved global paths. Instances use {fullRoot}/<instance name>/{workbookName}. Existing workbook names remain supported until migrated.";
        }
        catch (Exception ex) { _details.Text = $"Could not save global paths: {ex.Message}"; }
    }

    private void OpenEnvironmentManager()
    {
        var manager = new EnvironmentManagerWindow(_config.Environments, _config.RunEnvironmentsParallel, SaveGlobalEnvironmentSettings, ApplyEnvironmentsToAllInstances);
        manager.Closed += (_, _) => RefreshInstances();
        manager.Show(this);
    }

    private void SaveGlobalEnvironmentSettings(IReadOnlyList<string> environments, bool runEnvironmentsParallel)
    {
        var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
        root ??= new JsonObject();
        var environmentArray = new JsonArray();
        foreach (var environment in environments) environmentArray.Add(environment);
        root["Environments"] = environmentArray;
        root["RunEnvironmentsParallel"] = runEnvironmentsParallel;
        if (root["Instances"] is JsonObject instances)
            foreach (var pair in instances)
                if (pair.Value is JsonObject instance)
                {
                    instance.Remove("RunEnvironmentsParallel");
                    if (instance["EnvironmentSelection"] is JsonValue selectedValue)
                    {
                        var selected = selectedValue.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(value => environments.Contains(value, StringComparer.OrdinalIgnoreCase));
                        instance["EnvironmentSelection"] = string.Join(',', selected);
                    }
                }
        File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        _config.Environments = environments.ToList();
        _config.RunEnvironmentsParallel = runEnvironmentsParallel;
        foreach (var item in _instances) item.RunEnvironmentsParallel = runEnvironmentsParallel;
        RefreshRows();
    }

    private void ApplyEnvironmentsToAllInstances(IReadOnlyList<string> environments)
    {
        var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
        root ??= new JsonObject();
        var instances = root["Instances"] as JsonObject ?? new JsonObject();
        var selection = string.Join(',', environments);
        foreach (var pair in instances)
        {
            if (pair.Value is not JsonObject instance) continue;
            instance["EnvironmentSelection"] = selection;
            instance["EnvironmentSelectionJson"] = JsonSerializer.Serialize(environments);
        }
        root["Instances"] = instances;
        File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        foreach (var item in _instances)
        {
            item.EnvironmentSelection = selection;
            item.EnvironmentSelectionJson = JsonSerializer.Serialize(environments);
            item.HasAppliedSelection = true;
            if (_config.Instances.TryGetValue(item.Name, out var central)) central.EnvironmentSelection = selection;
        }
        _details.Text = $"Applied environments to {_instances.Count} instance(s): {selection}.";
        UiLog($"Applied global environment selection to all instances. environments={selection}; instances={_instances.Count}");
        RefreshRows();
        RefreshDashboardDetails(force: true);
    }
    private static Control KeyValue(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("105,*") };
        grid.Children.Add(DashboardStyle.Text(label, 12, false, DashboardStyle.Muted));
        var text = DashboardStyle.Text(value, 12); text.TextWrapping = TextWrapping.Wrap; ToolTip.SetTip(text, value);
        Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
    }

    private Control CreatePaginationBar(bool overview, int totalItems = 0)
    {
        // Overview is rebuilt on every refresh. Always create fresh controls for it;
        // Avalonia controls cannot be attached to a second visual parent.
        var label = overview ? DashboardStyle.Text("", 11, false, DashboardStyle.Muted) : _assertionPageLabel;
        var previous = DashboardStyle.Action("Previous", () =>
        {
            if (overview) _overviewPage = Math.Max(1, _overviewPage - 1);
            else _assertionPage = Math.Max(1, _assertionPage - 1);
            if (overview) RefreshDashboardDetails(true); else RefreshAssertionList();
        });
        var next = DashboardStyle.Action("Next", () =>
        {
            if (overview) _overviewPage++;
            else _assertionPage++;
            if (overview) RefreshDashboardDetails(true); else RefreshAssertionList();
        });
        if (!overview) { _assertionPreviousButton = previous; _assertionNextButton = next; }
        if (overview)
        {
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)DashboardPageSize));
            _overviewPage = Math.Clamp(_overviewPage, 1, totalPages);
            UpdatePagination(label, previous, next, _overviewPage, totalItems, DashboardPageSize);
        }
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 4),
            Children = { previous, label, next }
        };
    }

    private static string FormatRunDuration(InstanceInfo item)
    {
        if (item.StartedAt is not { } started) return "Not started";
        var ended = item.CompletedAt ?? DateTimeOffset.Now;
        var duration = ended - started;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static void UpdatePagination(TextBlock label, Button previous, Button next, int page, int totalItems, int pageSize)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        var currentPage = Math.Clamp(page, 1, totalPages);
        var first = totalItems == 0 ? 0 : ((currentPage - 1) * pageSize) + 1;
        var last = totalItems == 0 ? 0 : Math.Min(currentPage * pageSize, totalItems);
        label.Text = $"Page {currentPage} of {totalPages}  ·  Showing {first}–{last} of {totalItems}";
        previous.IsEnabled = currentPage > 1;
        next.IsEnabled = currentPage < totalPages;
    }
    private void BuildConfiguration()
    {
        _configurationPanel.Children.Clear(); var item = _selectedInstance;
        if (item is null) { _configurationPanel.Children.Add(DashboardStyle.Text("No instance selected", color: DashboardStyle.Muted)); return; }
        _configurationPanel.Children.Add(DashboardStyle.Text("Run configuration", 16, true));
        if (!string.IsNullOrWhiteSpace(item.SelectionReadError))
        {
            var error = DashboardStyle.Text("Cannot read run choices: " + item.SelectionReadError, 12, false, DashboardStyle.Red);
            error.TextWrapping = TextWrapping.Wrap; _configurationPanel.Children.Add(error); return;
        }
        _configurationPanel.Children.Add(DashboardStyle.Text("Choose named testcases, steps and centrally defined environments using the picker below.", 12, false, DashboardStyle.Muted));
        _configurationPanel.Children.Add(DashboardStyle.Action("Choose tests, steps, environments…", () => _ = ChooseRunSelectionAsync(), "primary"));
        var draft = new RunSelectionDraft(item.TestcaseOptions, item.EnvironmentOptions, item.TestcaseSelection, item.StepSelectionJson, item.EnvironmentSelection, item.EnvironmentSelectionJson);
        _configurationPanel.Children.Add(KeyValue("Testcases", string.Join(", ", item.TestcaseOptions.Where(c => draft.Cases.Contains(c.Index)).Select(c => $"{c.Index}: {c.Name}"))));
        _configurationPanel.Children.Add(KeyValue("Test steps", string.IsNullOrWhiteSpace(item.StepSelectionJson) ? "All enabled steps" : string.Join(", ", item.TestcaseOptions.Where(c => draft.Cases.Contains(c.Index)).SelectMany(c => c.Steps.Where(s => draft.Steps[c.Index].Contains(s.Name)).Select(s => $"{c.Index}: {s.Name}")))));
        _configurationPanel.Children.Add(KeyValue("Environments", string.IsNullOrWhiteSpace(item.EnvironmentSelection) ? "All global environments" : item.EnvironmentSelection));
        var mode = new ComboBox { ItemsSource = new[] { "Parallel", "Sequential" }, SelectedIndex = item.RowsExecutionMode == "sequential" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.SelectionChanged += (_, _) => item.RowsExecutionMode = mode.SelectedIndex == 1 ? "sequential" : "parallel";
        _configurationPanel.Children.Add(Field("Row execution mode", mode));
        var order = new ComboBox { ItemsSource = new[] { "Sequential environments", "Parallel environments" }, SelectedIndex = item.RunEnvironmentsParallel ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        order.SelectionChanged += (_, _) => item.RunEnvironmentsParallel = order.SelectedIndex == 1;
        _configurationPanel.Children.Add(Field("Testcase order", order));
        _configurationPanel.Children.Add(DashboardStyle.Text("Global execution settings for this instance (stored in central appsettings.json; not in the workbook config sheet).", 12, false, DashboardStyle.Muted));
        var maxParallelRows = new TextBox { Text = item.MaxParallelRows.ToString(), Watermark = "MaxParallelRows", Width = 150 };
        var iterations = new TextBox { Text = item.Iterations.ToString(), Watermark = "Iterations", Width = 150 };
        var threadInterval = new TextBox { Text = item.ParallelRowStartIntervalMs.ToString(), Watermark = "Parallel-row start interval (ms)", Width = 180 };
        var iterationInterval = new TextBox { Text = item.IterationIntervalMs.ToString(), Watermark = "Iteration interval (ms)", Width = 190 };
        var executionGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        var executionFields = new[] { Field("MaxParallelRows", maxParallelRows), Field("Iterations", iterations), Field("Parallel-row start interval (ms)", threadInterval), Field("Iteration interval (ms)", iterationInterval) };
        for (var i = 0; i < executionFields.Length; i++) { Grid.SetColumn(executionFields[i], i); executionGrid.Children.Add(executionFields[i]); }
        _configurationPanel.Children.Add(executionGrid);
        var spoolResults = new CheckBox { Content = "Spool completed rows to disk (NDJSON)", IsChecked = item.SpoolResultsToDisk };
        _configurationPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children =
        {
            spoolResults,
            DashboardStyle.Action("Save row storage", () => SaveBoundedRowSettings(item, item.MaxParallelRows.ToString(), spoolResults.IsChecked == true), "primary")
        }});
        var generateResults = new CheckBox { Content = "Generate technical Results artifacts", IsChecked = item.GenerateResults };
        _configurationPanel.Children.Add(generateResults);
        _configurationPanel.Children.Add(DashboardStyle.Text("When disabled, the Excel History files and live UI events remain available, but persistent Results run folders and metrics.json are not generated.", 11, false, DashboardStyle.Muted));
        _configurationPanel.Children.Add(DashboardStyle.Action("Save execution settings", () => SaveInstanceExecutionSettings(item, maxParallelRows.Text, iterations.Text, threadInterval.Text, iterationInterval.Text, generateResults.IsChecked == true, item.RowsExecutionMode, item.RunEnvironmentsParallel), "primary"));
        _configurationPanel.Children.Add(KeyValue("Data IDs", string.IsNullOrWhiteSpace(item.DataIdSelection) ? "All data rows" : item.DataIdSelection));
        _configurationPanel.Children.Add(DashboardStyle.Text("Workbook actions are available directly on the instance row: Add step, Edit step, Edit dataset, and Open workbook. The legacy editor is available in Handcraft.", 12, false, DashboardStyle.Muted));
    }

    private void OpenGlobalValidation()
    {
        _ = RunGlobalValidationAsync();
    }

    private async Task RunGlobalValidationAsync()
    {
        _validateButton.IsEnabled = false;
        _validateButton.Content = "… Validating";
        _validateButton.Background = DashboardStyle.Brush("#245A8D");
        _validationLog.Clear();
        _validationLog.Add(("[validate]", "INFO"));
        _validationLog.Add(("Validation started for all instances. Requests will not be executed.", "INFO"));
        RenderUnifiedLog();
        try
        {
            var results = await Task.Run(() => GlobalValidationService.ValidateAll(_instances.ToList(), _config.Environments.ToList()));
            var errors = results.Sum(x => x.Issues.Count(i => i.Severity == "ERROR"));
            var warnings = results.Sum(x => x.Issues.Count(i => i.Severity == "WARNING"));
            var status = errors > 0 ? "ERROR" : warnings > 0 ? "WARNING" : "PASS";
            _validationLog.Add(($"Validation completed: {errors} error(s), {warnings} warning(s) across {results.Count} instance(s).", status));
            foreach (var result in results)
            {
                var instance = _instances.FirstOrDefault(x => x.Name.Equals(result.Instance, StringComparison.OrdinalIgnoreCase));
                if (instance is not null)
                {
                    instance.Issues.RemoveAll(x => x.Category.Equals("Validation", StringComparison.OrdinalIgnoreCase));
                    foreach (var issue in result.Issues)
                        instance.Issues.Add(new RunIssueInfo { Severity = issue.Severity.Equals("WARNING", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Error", Category = "Validation", Message = $"{issue.Location}: {issue.Problem} Fix: {issue.Fix}", Testcase = issue.Location, OccurredAt = DateTimeOffset.Now });
                }
                _validationLog.Add(($"\n{(result.HasErrors ? "FAILED" : result.HasWarnings ? "WARNINGS" : "PASSED")}  {result.Instance}", result.HasErrors ? "ERROR" : result.HasWarnings ? "WARNING" : "PASS"));
                foreach (var issue in result.Issues)
                    _validationLog.Add((issue.ToString(), issue.Severity));
            }
            _validationLog.Add(("[end validate]", status));
            SetValidationButton(status, errors, warnings);
            RefreshDashboardDetails(force: true);
            UiLog($"Global validation completed. instances={results.Count}; errors={errors}; warnings={warnings}");
        }
        catch (Exception ex)
        {
            _validationLog.Add(($"[ERROR] Validation could not complete.\n{ex.Message}\nFix: Review central appsettings.json and validate again.", "ERROR"));
            _validationLog.Add(("[end validate]", "ERROR"));
            SetValidationButton("ERROR", 1, 0);
            UiLog($"Global validation failed: {ex}");
        }
        finally
        {
            _validateButton.IsEnabled = true;
            RenderUnifiedLog();
        }
    }

    private void SetValidationButton(string status, int errors, int warnings)
    {
        _validateButton.Content = status switch
        {
            "ERROR" => $"✕ Validate all ({errors} errors)",
            "WARNING" => $"⚠ Validate all ({warnings} warnings)",
            "PASS" => "✓ Validate all (passed)",
            _ => "✓ Validate all"
        };
        _validateButton.Background = DashboardStyle.Brush(status switch
        {
            "ERROR" => "#8B2635",
            "WARNING" => "#8A5A18",
            "PASS" => "#176B50",
            _ => "#245A8D"
        });
    }

    private void ChangeLogFont(double delta)
    {
        _logFontSize = Math.Clamp(_logFontSize + delta, 9, 24);
        RenderUnifiedLog();
    }

    private void ClearUnifiedLog()
    {
        _validationLog.Clear();
        if (_selectedInstance is not null) _selectedInstance.EventMessages.Clear();
        SetValidationButton("READY", 0, 0);
        RenderUnifiedLog();
    }

    private void RenderUnifiedLog()
    {
        if (_eventPanel is null) return;
        _eventPanel.Children.Clear();
        var lines = new List<(string Text, string Severity)>();
        if (_selectedInstance is not null)
            lines.AddRange(_selectedInstance.EventMessages.TakeLast(100).Reverse().Select(x => (x, "INFO")));
        lines.AddRange(_validationLog);
        if (lines.Count == 0)
        {
            _eventPanel.Children.Add(DashboardStyle.Text("No log entries yet. Events and validation output will appear here.", 12, false, DashboardStyle.Muted));
            return;
        }
        foreach (var (text, severity) in lines)
        {
            var box = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontSize = _logFontSize,
                FontFamily = new FontFamily("monospace"),
                Foreground = severity switch
                {
                    "ERROR" => DashboardStyle.Red,
                    "WARNING" => DashboardStyle.Brush("#F6A623"),
                    "PASS" => DashboardStyle.Green,
                    _ => DashboardStyle.Muted
                },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(2, 1)
            };
            _eventPanel.Children.Add(box);
        }
    }
    private void OpenDataEditor()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; return; }
        try
        {
            var editor = DataEditor.DataEditorWindow.Open(_instances.Select(x => (x.Name, x.WorkbookPath)), _selectedInstance.WorkbookPath);
            editor.Shell.RunHandler = RunFromEditorAsync;
            editor.Shell.HistoryHandler = LaunchHistoryWorkspace;
            editor.Shell.CreateTestcaseHandler = (name, draft) => { if (_instances.FirstOrDefault(x => x.Name == name) is { } target) CreateTestcaseForInstance(target, draft); };
            editor.Closed += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(editor.Shell.LastBackupPath)) return;
                RefreshInstances();
                _details.Text = $"Workbook saved. Backup created: {Path.GetFileName(editor.Shell.LastBackupPath)}";
            };
            editor.Show(this);
        }
        catch (Exception ex)
        {
            _details.Text = $"Cannot open the data editor: {ex.Message}";
        }
    }

    private async Task EditWorkbookAsync()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; return; }
        var editor = new WorkbookEditorWindow(_selectedInstance.WorkbookPath, _selectedInstance.Name);
        editor.Closed += (_, _) => { if (!string.IsNullOrWhiteSpace(editor.SavedBackup)) { RefreshInstances(); _details.Text = $"Workbook saved. Backup created: {Path.GetFileName(editor.SavedBackup)}"; } };
        editor.Show(this);
        await Task.CompletedTask;
    }

    private void OpenInstanceConfiguration(InstanceInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.SelectionReadError)) { _details.Text = $"Cannot read run choices: {item.SelectionReadError}"; return; }
        var form = new InstanceConfigurationWindow(item, ApplyRunSelection, SaveInstanceExecutionSettings, SaveInstanceReportingSettings, CreateTestcaseForInstance);
        form.Show(this);
    }

    private void ApplyRunSelection(InstanceInfo item, RunSelectionResult result)
    {
        item.TestcaseSelection = result.Testcases; item.StepSelectionJson = result.StepsJson; item.EnvironmentSelection = result.Environments;
        item.EnvironmentSelectionJson = result.EnvironmentsJson; item.DataIdSelection = result.DataIds; item.RowsExecutionMode = result.RowsExecutionMode; item.RunEnvironmentsParallel = result.ScenariosParallel; item.MaxParallelRows = result.MaxParallelRows; item.Iterations = result.Iterations; item.ParallelRowStartIntervalMs = result.ParallelRowStartIntervalMs; item.IterationIntervalMs = result.IterationIntervalMs; item.HasAppliedSelection = true; item.Selected = true;
        _config.RunEnvironmentsParallel = result.ScenariosParallel;
        foreach (var other in _instances) other.RunEnvironmentsParallel = result.ScenariosParallel;
        SaveInstanceExecutionSettings(item, result.MaxParallelRows.ToString(), result.Iterations.ToString(), result.ParallelRowStartIntervalMs.ToString(), result.IterationIntervalMs.ToString());
        try
        {
            var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
            root ??= new JsonObject();
            var instances = root["Instances"] as JsonObject ?? new JsonObject();
            var instance = instances[item.Name] as JsonObject ?? new JsonObject();
            instance["TestcaseSelection"] = result.Testcases;
            instance["StepSelection"] = result.StepsJson;
            instance["EnvironmentSelection"] = result.Environments;
            instance["DataIdSelection"] = result.DataIds;
            instance["RowsExecutionMode"] = result.RowsExecutionMode;
            instance["MaxParallelRows"] = result.MaxParallelRows;
            instance["Iterations"] = result.Iterations;
            instance["ParallelRowStartIntervalMs"] = result.ParallelRowStartIntervalMs;
            instance["IterationIntervalMs"] = result.IterationIntervalMs;
            instances[item.Name] = instance; root["Instances"] = instances;
            root["RunEnvironmentsParallel"] = result.ScenariosParallel;
            foreach (var pair in instances)
                if (pair.Value is JsonObject configuredInstance) configuredInstance.Remove("RunEnvironmentsParallel");
            File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            if (_config.Instances.TryGetValue(item.Name, out var central))
            {
                central.TestcaseSelection = result.Testcases;
                central.StepSelection = result.StepsJson;
                central.EnvironmentSelection = result.Environments;
                central.DataIdSelection = result.DataIds;
                central.RowsExecutionMode = result.RowsExecutionMode;
                central.MaxParallelRows = result.MaxParallelRows;
                central.Iterations = result.Iterations;
                central.ParallelRowStartIntervalMs = result.ParallelRowStartIntervalMs;
                central.IterationIntervalMs = result.IterationIntervalMs;
            }
            _details.Text = $"Selection saved for {item.Name}. Testcases: {result.Testcases}; Data IDs: {(string.IsNullOrWhiteSpace(result.DataIds) ? "all rows" : result.DataIds)}";
        }
        catch (Exception ex) { _details.Text = $"Selection applied, but could not save it: {ex.Message}"; UiLog($"Could not save selection profile for {item.Name}: {ex}"); }
        UiLog($"Run selection applied. instance={item.Name}; testcases={result.Testcases}; steps={result.StepsJson}; environments={result.Environments}; dataIds={result.DataIds}");
        BuildConfiguration(); RefreshRows(); RefreshDashboardDetails(true);
    }

    private async Task<bool> ChooseRunSelectionAsync(InstanceInfo? target = null)
    {
        var item = target ?? _selectedInstance;
        if (item is null) { _details.Text = "Select an instance row first."; return false; }
        if (!string.IsNullOrWhiteSpace(item.SelectionReadError)) { _details.Text = $"Cannot read run choices: {item.SelectionReadError}. Fix the workbook and click Refresh."; return false; }
        try
        {
            var result = await new RunSelectionWindow(item).ShowDialog<RunSelectionResult?>(this);
            if (result is null) return false;
            ApplyRunSelection(item, result); _details.Text = "Selection applied. Click Run selected to execute the checked choices."; return true;
        }
        catch (Exception ex) { _details.Text = $"Could not show run choices: {ex.Message}"; UiLog($"Run selection dialog failed: {ex}"); return false; }
    }

    private static Control Field(string label, Control control) => new StackPanel { Spacing = 6, Children = { DashboardStyle.Text(label, 12, false, DashboardStyle.Muted), control } };

    private void RefreshRunSummary(InstanceInfo? item)
    {
        _runSummaryPanel.Children.Clear();
        if (item is null)
        {
            _runSummaryPanel.Children.Add(DashboardStyle.Text("Select an instance to inspect its planned execution.", color: DashboardStyle.Muted));
            return;
        }

        RunPlanSummary summary;
        try { summary = BuildRunPlanSummary(item); }
        catch (Exception ex)
        {
            _runSummaryPanel.Children.Add(DashboardStyle.Badge("Unexpected error"));
            _runSummaryPanel.Children.Add(DashboardStyle.Text($"Could not calculate the run summary: {ex.Message}", 12, false, DashboardStyle.Red));
            UiLog($"Run Summary calculation failed. instance={item.Name}; error={ex}");
            return;
        }

        var state = item.Process is { HasExited: false } ? "Running" : item.Status;
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 4) };
        title.Children.Add(DashboardStyle.Text("Run Summary", 17, true));
        var stateBadge = DashboardStyle.Badge(string.IsNullOrWhiteSpace(state) ? "Ready" : state); Grid.SetColumn(stateBadge, 1); title.Children.Add(stateBadge);
        _runSummaryPanel.Children.Add(title);
        _runSummaryPanel.Children.Add(DashboardStyle.Text("Selection preview. dontrun skips individual steps inside a row; it does not automatically remove the whole row.", 11, false, DashboardStyle.Muted));

        Border Metric(string label, long value, IBrush color) => DashboardStyle.Card(new StackPanel { Spacing = 4, Children = { DashboardStyle.Text(label, 10, false, DashboardStyle.Muted), DashboardStyle.Text(value.ToString("N0"), 20, true, color) } }, new Thickness(10));
        Grid MetricRow(params Control[] controls)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 0, 0, 8) };
            for (var i = 0; i < controls.Length; i++) { controls[i].Margin = new Thickness(0, 0, i == controls.Length - 1 ? 0 : 8, 0); Grid.SetColumn(controls[i], i); row.Children.Add(controls[i]); }
            return row;
        }
        _runSummaryPanel.Children.Add(MetricRow(
            Metric("Testcases selected", summary.Testcases, DashboardStyle.Blue),
            Metric("Steps selected", summary.SelectedSteps, DashboardStyle.Blue),
            Metric("Rows selected", summary.RowsSelected, DashboardStyle.Blue)));
        _runSummaryPanel.Children.Add(MetricRow(
            Metric("Environments", summary.Environments, DashboardStyle.Blue),
            Metric("Max concurrent rows", summary.MaxParallelRows, DashboardStyle.Blue),
            Metric("Iterations", summary.Iterations, DashboardStyle.Blue)));
        _runSummaryPanel.Children.Add(MetricRow(
            Metric("Step executions planned", summary.PlannedSteps, DashboardStyle.Green),
            Metric("Steps skipped by dontrun", summary.SkippedSteps, DashboardStyle.Brush("#F6C76B")),
            Metric("Rows fully skipped", summary.RowsFullySkipped, DashboardStyle.Red)));
        var passedRequests = item.Requests.Where(request => request.Result.Equals("PASS", StringComparison.OrdinalIgnoreCase)).ToList();
        var failedRequests = item.Requests.Where(request => request.Result.Equals("FAIL", StringComparison.OrdinalIgnoreCase) && !IsUnexpectedRequest(request)).ToList();
        var unexpectedRequests = item.Requests.Where(IsUnexpectedRequest).ToList();
        _runSummaryPanel.Children.Add(MetricRow(
            Metric("Passed environments", passedRequests.Select(x => x.Environment).Distinct(StringComparer.OrdinalIgnoreCase).Count(), DashboardStyle.Green),
            Metric("Passed testcases", passedRequests.Select(x => x.Testcase).Distinct(StringComparer.OrdinalIgnoreCase).Count(), DashboardStyle.Green),
            Metric("Passed steps", passedRequests.Count, DashboardStyle.Green)));
        _runSummaryPanel.Children.Add(MetricRow(
            Metric("Failed environments", failedRequests.Select(x => x.Environment).Distinct(StringComparer.OrdinalIgnoreCase).Count(), DashboardStyle.Red),
            Metric("Failed testcases", failedRequests.Select(x => x.Testcase).Distinct(StringComparer.OrdinalIgnoreCase).Count(), DashboardStyle.Red),
            Metric("Failed steps", failedRequests.Count, DashboardStyle.Red)));
        _runSummaryPanel.Children.Add(MetricRow(
            Metric("Error environments", unexpectedRequests.Select(x => x.Environment).Distinct(StringComparer.OrdinalIgnoreCase).Count(), DashboardStyle.Brush("#FFB36B")),
            Metric("Error testcases", unexpectedRequests.Select(x => x.Testcase).Distinct(StringComparer.OrdinalIgnoreCase).Count(), DashboardStyle.Brush("#FFB36B")),
            Metric("Error steps", unexpectedRequests.Count, DashboardStyle.Brush("#FFB36B"))));

        Control ResultColumn(string title, IReadOnlyList<DashboardRequest> requests, IBrush color, string emptyText)
        {
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(DashboardStyle.Text(title, 13, true, color));
            if (requests.Count == 0)
                content.Children.Add(DashboardStyle.Text(emptyText, 11, false, DashboardStyle.Muted));
            else
                foreach (var environmentGroup in requests.GroupBy(x => string.IsNullOrWhiteSpace(x.Environment) ? "default" : x.Environment, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    content.Children.Add(DashboardStyle.Text(environmentGroup.Key, 11, true, color));
                    foreach (var testcaseGroup in environmentGroup.GroupBy(x => x.Testcase, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        content.Children.Add(DashboardStyle.Text($"  {testcaseGroup.Key}", 11, true));
                        foreach (var stepGroup in testcaseGroup.GroupBy(x => x.StepName, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            var rowCount = stepGroup.Select(x => x.DataId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                            var stepLine = DashboardStyle.Text($"    {stepGroup.Key} — {rowCount:N0} row(s)", 11, false, DashboardStyle.Muted);
                            stepLine.TextWrapping = TextWrapping.Wrap;
                            content.Children.Add(stepLine);
                        }
                    }
                }
            return DashboardStyle.Card(new ScrollViewer { Content = content, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, new Thickness(10));
        }
        var resultColumns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 0, 0, 8) };
        var failedColumn = ResultColumn("Failed results", failedRequests, DashboardStyle.Red, "No failed environments, testcases, or steps.");
        var passedColumn = ResultColumn("Passed results", passedRequests, DashboardStyle.Green, "No passed environments, testcases, or steps yet.");
        failedColumn.Margin = new Thickness(0, 0, 8, 0); passedColumn.Margin = new Thickness(0);
        resultColumns.Children.Add(failedColumn); Grid.SetColumn(passedColumn, 1); resultColumns.Children.Add(passedColumn);
        _runSummaryPanel.Children.Add(resultColumns);

        var warnings = new List<string>();
        if (summary.RowsWithDontrun > 0) warnings.Add($"{summary.RowsWithDontrun:N0} row(s) contain dontrun steps; {summary.SkippedSteps:N0} step execution(s) will be skipped.");
        if (summary.RowsFullySkipped > 0) warnings.Add($"{summary.RowsFullySkipped:N0} row(s) have every selected step listed in dontrun and will produce no step executions.");
        if (summary.RowsWithoutAssertions > 0) warnings.Add($"{summary.RowsWithoutAssertions:N0} selected row(s) have no response assertions.");
        foreach (var warning in warnings) _runSummaryPanel.Children.Add(DashboardStyle.Text($"⚠ {warning}", 11, false, DashboardStyle.Brush("#F6C76B")));
        if (summary.PlannedExecutions == 0) _runSummaryPanel.Children.Add(DashboardStyle.Text("✗ No executable step executions are planned for the current selection.", 11, true, DashboardStyle.Red));
        else if (warnings.Count == 0) _runSummaryPanel.Children.Add(DashboardStyle.Text("✓ Selection is ready: every selected row has at least one executable step.", 11, true, DashboardStyle.Green));

        _runSummaryPanel.Children.Add(new Border { Height = 1, Background = DashboardStyle.Line, Margin = new Thickness(0, 4) });
        _runSummaryPanel.Children.Add(DashboardStyle.Text("Status summary", 13, true));
        _runSummaryPanel.Children.Add(DashboardStyle.Text("Expand a status to see its environments, testcase names, step names, data IDs, counts, and errors.", 11, false, DashboardStyle.Muted));
        var statusEntries = new[]
        {
            (Name: "Passed", Color: DashboardStyle.Green, Match: new Func<DashboardRequest, bool>(request => request.Result.Equals("PASS", StringComparison.OrdinalIgnoreCase))),
            (Name: "Failed", Color: DashboardStyle.Red, Match: new Func<DashboardRequest, bool>(request => request.Result.Equals("FAIL", StringComparison.OrdinalIgnoreCase) && !IsUnexpectedRequest(request))),
            (Name: "Unexpected errors", Color: DashboardStyle.Brush("#FFB36B"), Match: new Func<DashboardRequest, bool>(IsUnexpectedRequest)),
            (Name: "Skipped by dontrun", Color: DashboardStyle.Brush("#F6C76B"), Match: new Func<DashboardRequest, bool>(_ => false))
        };
        foreach (var entry in statusEntries)
        {
            var values = item.Requests.Where(entry.Match).ToList();
            if (entry.Name == "Skipped by dontrun")
            {
                var skippedHeader = DashboardStyle.Text($"●  {entry.Name}  ·  {summary.SkippedSteps:N0} step(s) / {summary.RowsWithDontrun:N0} row(s)", 12, true, entry.Color);
                var skippedDetails = DashboardStyle.Text(summary.Rows.Count(x => x.SkippedSteps > 0) == 0 ? "No selected rows contain dontrun steps." : string.Join("\n", summary.Rows.Where(x => x.SkippedSteps > 0).GroupBy(x => x.Testcase, StringComparer.OrdinalIgnoreCase).Select(group => $"{group.Key}: {group.Count():N0} row(s), {group.Sum(x => x.SkippedSteps):N0} skipped step(s)")), 11, false, DashboardStyle.Muted);
                skippedDetails.TextWrapping = TextWrapping.Wrap;
                _runSummaryPanel.Children.Add(new Expander { Header = skippedHeader, Content = skippedDetails, IsExpanded = summary.SkippedSteps > 0 });
                continue;
            }
            var environments = values.Select(x => string.IsNullOrWhiteSpace(x.Environment) ? "default" : x.Environment).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var testcases = values.Select(x => x.Testcase).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var steps = values.Select(x => x.StepName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var rows = values.Select(x => $"{x.Testcase} / {x.DataId}").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var details = new StackPanel { Spacing = 3 };
            string GroupCounts(Func<DashboardRequest, string> keySelector)
                => string.Join(", ", values.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase).Select(group => $"{group.Key} — {group.Count():N0} step(s) / {group.Select(x => x.DataId).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} row(s)"));
            details.Children.Add(DashboardStyle.Text($"Environments ({environments.Count}): {(environments.Count == 0 ? "—" : GroupCounts(x => string.IsNullOrWhiteSpace(x.Environment) ? "default" : x.Environment))}", 11, false, DashboardStyle.Muted));
            details.Children.Add(DashboardStyle.Text($"Testcases ({testcases.Count}): {(testcases.Count == 0 ? "—" : GroupCounts(x => x.Testcase))}", 11, false, DashboardStyle.Muted));
            details.Children.Add(DashboardStyle.Text($"Steps ({steps.Count}): {(steps.Count == 0 ? "—" : GroupCounts(x => x.StepName))}", 11, false, DashboardStyle.Muted));
            details.Children.Add(DashboardStyle.Text($"Rows / data IDs ({rows.Count}): {(rows.Count == 0 ? "—" : string.Join(", ", rows.Take(30)))}{(rows.Count > 30 ? $" … +{rows.Count - 30:N0} more" : "")}", 11, false, DashboardStyle.Muted));
            var errors = values.Select(x => x.Error).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            if (errors.Count > 0) details.Children.Add(DashboardStyle.Text($"Errors:\n{string.Join("\n", errors)}", 11, false, entry.Color));
            var headerText = $"●  {entry.Name}  ·  {values.Count:N0} step(s) / {rows.Count:N0} row(s)";
            _runSummaryPanel.Children.Add(new Expander { Header = DashboardStyle.Text(headerText, 12, true, entry.Color), Content = details, IsExpanded = values.Count > 0 });
        }

        _runSummaryPanel.Children.Add(new Border { Height = 1, Background = DashboardStyle.Line, Margin = new Thickness(0, 4) });
        _runSummaryPanel.Children.Add(DashboardStyle.Text("Per-testcase breakdown", 13, true));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,80,105,105,105,110"), Margin = new Thickness(8, 4) };
        foreach (var (text, column) in new[] { ("Testcase", 0), ("Rows", 1), ("dontrun rows", 2), ("Skipped steps", 3), ("Planned steps", 4), ("Status", 5) })
        {
            var label = DashboardStyle.Text(text, 10, true, DashboardStyle.Muted); Grid.SetColumn(label, column); header.Children.Add(label);
        }
        _runSummaryPanel.Children.Add(header);
        foreach (var group in summary.Rows.GroupBy(x => x.Testcase, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToList();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,80,105,105,105,110"), Margin = new Thickness(8, 3) };
            var values = new Control[]
            {
                DashboardStyle.Text(group.Key, 11, true),
                DashboardStyle.Text(rows.Count.ToString("N0"), 11),
                DashboardStyle.Text(rows.Count(x => x.SkippedSteps > 0).ToString("N0"), 11),
                DashboardStyle.Text(rows.Sum(x => x.SkippedSteps).ToString("N0"), 11, false, DashboardStyle.Brush("#F6C76B")),
                DashboardStyle.Text(rows.Sum(x => x.ExecutableSteps).ToString("N0"), 11, false, DashboardStyle.Green),
                DashboardStyle.Badge(rows.All(x => x.ExecutableSteps == 0) ? "Skipped" : rows.Any(x => x.SkippedSteps > 0) ? "Warning" : "Ready")
            };
            for (var i = 0; i < values.Length; i++) { Grid.SetColumn(values[i], i); row.Children.Add(values[i]); }
            _runSummaryPanel.Children.Add(row);
        }

        _runSummaryPanel.Children.Add(new Border { Height = 1, Background = DashboardStyle.Line, Margin = new Thickness(0, 4) });
        _runSummaryPanel.Children.Add(DashboardStyle.Text("Execution parameters", 13, true));
        _runSummaryPanel.Children.Add(KeyValue("Mode / order", $"{(item.RowsExecutionMode == "sequential" ? "Loop" : "Parallel")} / {(item.RunEnvironmentsParallel ? "Parallel" : "Sequential")}"));
        _runSummaryPanel.Children.Add(KeyValue("Max concurrent rows / iterations", $"{Math.Clamp(item.MaxParallelRows, 1, 100)} / {Math.Max(1, item.Iterations)}"));
        _runSummaryPanel.Children.Add(KeyValue("Environments", summary.EnvironmentNames.Count == 0 ? "None" : string.Join(", ", summary.EnvironmentNames)));
        _runSummaryPanel.Children.Add(KeyValue("Data IDs", string.IsNullOrWhiteSpace(item.DataIdSelection) ? "All workbook rows" : item.DataIdSelection));
        _runSummaryPanel.Children.Add(KeyValue("Assertions", $"{summary.RowsWithAssertions:N0} row(s) with assertions / {summary.RowsWithoutAssertions:N0} without assertions"));
    }

    private RunPlanSummary BuildRunPlanSummary(InstanceInfo item)
    {
        if (!File.Exists(item.WorkbookPath)) throw new FileNotFoundException("Workbook was not found.", item.WorkbookPath);
        var draft = new RunSelectionDraft(item.TestcaseOptions, item.EnvironmentOptions, item.TestcaseSelection, item.StepSelectionJson, item.EnvironmentSelection, item.EnvironmentSelectionJson);
        var selectedCases = draft.Cases.ToHashSet();
        var selectedSteps = draft.Steps.ToDictionary(x => x.Key, x => x.Value, EqualityComparer<int>.Default);
        var environments = draft.AvailableEnvironments.Where(x => draft.Environments.Contains(x)).Select(x => string.IsNullOrWhiteSpace(x) ? "Default" : x).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var dataIds = DataIdSelectionCodec.Parse(item.DataIdSelection);
        using var workbook = new XLWorkbook(item.WorkbookPath);
        var config = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The workbook has no config sheet.");
        var request = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The workbook has no request sheet.");
        var response = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("response", StringComparison.OrdinalIgnoreCase));
        var configHeaders = config.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? throw new InvalidDataException("The config sheet has no header row.");
        var requestHeaders = request.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? throw new InvalidDataException("The request sheet has no header row.");
        string ConfigText(IXLRow row, string key, string fallback = "") => configHeaders.TryGetValue(key, out var column) ? row.Cell(column).GetString().Trim() : fallback;
        string RequestText(IXLRow row, string key, string fallback = "") => requestHeaders.TryGetValue(key, out var column) ? row.Cell(column).GetString().Trim() : fallback;
        var stepNames = new Dictionary<int, List<(string Name, bool Enabled)>>();
        foreach (var row in config.RowsUsed().Skip(1))
        {
            if (!int.TryParse(ConfigText(row, "testcaseindex"), out var index) || !selectedCases.Contains(index)) continue;
            var name = ConfigText(row, "stepname"); if (string.IsNullOrWhiteSpace(name)) continue;
            var enabled = !bool.TryParse(ConfigText(row, "enabled"), out var flag) || flag;
            if (!stepNames.TryGetValue(index, out var list)) stepNames[index] = list = [];
            list.Add((name, enabled));
        }
        var rows = new List<RunPlanRow>();
        var rowsWithAssertions = 0;
        var responseHeaders = response?.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var row in request.RowsUsed().Skip(1))
        {
            if (!int.TryParse(RequestText(row, "testcaseindex"), out var index) || !selectedCases.Contains(index)) continue;
            var dataId = RequestText(row, "dataid");
            if (dataIds.TryGetValue(index, out var chosenIds) && chosenIds.Count > 0 && !chosenIds.Contains(dataId)) continue;
            var selected = stepNames.GetValueOrDefault(index, []).Where(x => x.Enabled && selectedSteps.GetValueOrDefault(index, []).Contains(x.Name)).Select(x => x.Name).ToList();
            var skippedNames = RequestText(row, "dontrun").Split(',', '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var skipped = selected.Count(x => skippedNames.Contains(x));
            var executable = Math.Max(0, selected.Count - skipped);
            var testcase = RequestText(row, "testcase", index.ToString());
            var hasAssertions = false;
            if (response is not null)
            {
                var responseRow = response.RowsUsed().Skip(1).FirstOrDefault(x => int.TryParse(x.Cell(FindHeader(response, "testcaseindex")).GetString(), out var responseIndex) && responseIndex == index && x.Cell(FindHeader(response, "dataid")).GetString().Trim().Equals(dataId, StringComparison.OrdinalIgnoreCase));
                hasAssertions = responseRow is not null && responseHeaders.Any(x => !x.Key.Equals("testcaseindex", StringComparison.OrdinalIgnoreCase) && !x.Key.Equals("testcase", StringComparison.OrdinalIgnoreCase) && !x.Key.Equals("dataid", StringComparison.OrdinalIgnoreCase) && selected.Any(step => x.Key.TrimStart('{').Contains(step + ".", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(responseRow.Cell(x.Value).GetString()));
            }
            if (hasAssertions) rowsWithAssertions++;
            rows.Add(new RunPlanRow(testcase, skipped, executable, hasAssertions));
        }
        var plannedBase = rows.Sum(x => x.ExecutableSteps); var skippedBase = rows.Sum(x => x.SkippedSteps);
        var multiplier = Math.Max(1, environments.Count) * Math.Max(1, item.Iterations);
        return new RunPlanSummary(selectedCases.Count, selectedCases.Sum(index => selectedSteps.GetValueOrDefault(index, []).Count), rows.Count, rows.Count(x => x.SkippedSteps > 0), rows.Count(x => x.ExecutableSteps == 0), skippedBase * multiplier, plannedBase * multiplier, environments.Count, Math.Clamp(item.MaxParallelRows, 1, 100), Math.Max(1, item.Iterations), rowsWithAssertions, rows.Count - rowsWithAssertions, environments, rows);
    }

    private static int FindHeader(IXLWorksheet sheet, string name)
        => sheet.FirstRowUsed()?.CellsUsed().FirstOrDefault(c => c.GetString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase))?.Address.ColumnNumber ?? throw new InvalidDataException($"Worksheet '{sheet.Name}' is missing '{name}'.");

    private void RefreshOverviewStepFilters(InstanceInfo item)
    {
        _overviewStepFilterRefreshing = true;
        try
        {
        static void Preserve(ComboBox combo, IEnumerable<string> values)
        {
            var previous = combo.SelectedItem?.ToString();
            var options = values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            combo.ItemsSource = options;
            combo.SelectedItem = options.FirstOrDefault(x => x.Equals(previous, StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
        }
        Preserve(_overviewStepEnvironment, new[] { "All environments" }.Concat(item.Requests.Select(x => string.IsNullOrWhiteSpace(x.Environment) ? "default" : x.Environment)));
        Preserve(_overviewStepTestcase, new[] { "All testcases" }.Concat(item.Requests.Select(x => x.Testcase)));
        }
        finally { _overviewStepFilterRefreshing = false; }
    }

    private bool MatchesOverviewStep(DashboardRequest request)
    {
        var status = _overviewStepStatus.SelectedItem?.ToString() ?? "All statuses";
        var actualStatus = IsUnexpectedRequest(request) ? "Unexpected error" : request.Result.Equals("PASS", StringComparison.OrdinalIgnoreCase) ? "Passed" : request.Result.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase) ? "Skipped" : "Failed";
        if (!status.Equals("All statuses", StringComparison.OrdinalIgnoreCase) && !actualStatus.Equals(status, StringComparison.OrdinalIgnoreCase)) return false;
        var environment = _overviewStepEnvironment.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(environment) && !environment.Equals("All environments", StringComparison.OrdinalIgnoreCase) && !environment.Equals(string.IsNullOrWhiteSpace(request.Environment) ? "default" : request.Environment, StringComparison.OrdinalIgnoreCase)) return false;
        var testcase = _overviewStepTestcase.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(testcase) && !testcase.Equals("All testcases", StringComparison.OrdinalIgnoreCase) && !testcase.Equals(request.Testcase, StringComparison.OrdinalIgnoreCase)) return false;
        return MatchesTerms($"{request.StepType} {request.StepName} {request.Testcase} {request.DataId} {request.Environment} {request.Result} {request.FailureCategory} {request.Error}", _overviewStepFilter.Text);
    }

    private void LaunchStepDetails(DashboardRequest request)
    {
        if (_selectedInstance is null) return;
        var item = _selectedInstance;
        var log = FindInternalLogs(item)
            .Where(path => FindTestcaseLabel(path, item).Equals(request.Testcase, StringComparison.OrdinalIgnoreCase))
            .Where(path => FindEnvironmentLabel(path, item).Equals(string.IsNullOrWhiteSpace(request.Environment) ? "default" : request.Environment, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (log is null)
        {
            _details.Text = $"No internal log was found for {request.Testcase} / {request.DataId} / {request.StepName} / {request.Environment}.";
            UiLog($"Step details ignored. reason=internal-log-not-found; testcase={request.Testcase}; dataId={request.DataId}; step={request.StepName}; environment={request.Environment}");
            return;
        }
        try
        {
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The dashboard process path is unavailable.");
            var start = new ProcessStartInfo { FileName = processPath, UseShellExecute = false, CreateNoWindow = true };
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrWhiteSpace(entryAssembly) && (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))) start.ArgumentList.Add(entryAssembly);
            start.ArgumentList.Add("--step-details");
            foreach (var value in new[] { log, request.Testcase, request.DataId, request.StepName, string.IsNullOrWhiteSpace(request.Environment) ? "default" : request.Environment, request.StepType }) start.ArgumentList.Add(value);
            var process = Process.Start(start) ?? throw new InvalidOperationException("The step details process could not be started.");
            UiLog($"Step details process started. processId={process.Id}; testcase={request.Testcase}; dataId={request.DataId}; step={request.StepName}; environment={request.Environment}; log={log}");
        }
        catch (Exception ex) { _details.Text = $"Could not open step details: {ex.Message}"; UiLog($"Step details process failed: {ex}"); }
    }

    private void RefreshDashboardDetails(bool force = false)
    {
        var item = _selectedInstance;
        var signature = item is null ? "empty" : $"{item.Name}|{item.Status}|{item.Requests.Count}|{item.Assertions.Count}|{item.Issues.Count}|{item.LastEvent}|{item.P95Ms}|{item.StartedAt}|{item.CompletedAt}|{item.TestcaseSelection}|{item.EnvironmentSelection}|{item.RowsExecutionMode}|{item.RunEnvironmentsParallel}|{item.StepSelectionJson}";
        if (!force && signature == _detailSignature) return;
        _detailSignature = signature;
        _selectedTitle.Text = item?.Name ?? "Selected instance";
        ToolTip.SetTip(_selectedTitle, item?.Name);
        static void Detach(Control control)
        {
            if (control.Parent is Panel panel) panel.Children.Remove(control);
        }
        Detach(_overviewFilter);
        Detach(_overviewStepStatus);
        Detach(_overviewStepEnvironment);
        Detach(_overviewStepTestcase);
        Detach(_overviewStepFilter);
        _overviewPanel.Children.Clear();
        _overviewPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _overviewFilter, DashboardStyle.Action("Copy overview", () => _ = CopyOverviewAsync()) } });
        if (item is null)
        {
            _overviewPanel.Children.Add(DashboardStyle.Text("Select an instance to inspect its run.", color: DashboardStyle.Muted));
            RefreshRunSummary(null);
            RenderUnifiedLog();
            _artifactPanel.Children.Clear(); return;
        }
        RefreshRunSummary(item);
        RefreshOverviewStepFilters(item);
        void AddOverview(Control control, string searchable) { if (MatchesTerms(searchable, _overviewFilter.Text)) _overviewPanel.Children.Add(control); }
        AddOverview(DashboardStyle.Badge(item.Status), item.Status);
        AddOverview(KeyValue("Environment", string.IsNullOrWhiteSpace(item.EnvironmentSelection) ? "All global environments" : item.EnvironmentSelection), item.EnvironmentSelection);
        AddOverview(KeyValue("Mode / order", $"{(item.RowsExecutionMode == "sequential" ? "Loop" : "Parallel")} / {(item.RunEnvironmentsParallel ? "Parallel" : "Sequential")}"), $"{item.RowsExecutionMode} {(item.RunEnvironmentsParallel ? "Parallel" : "Sequential")}");
        AddOverview(KeyValue("Started", item.StartedAt?.ToString("HH:mm:ss") ?? "Not started"), item.StartedAt?.ToString() ?? "Not started");
        AddOverview(KeyValue("Requests", (item.CompletedRequests ?? item.Requests.Count).ToString()), $"{item.CompletedRequests} {item.Requests.Count}");
        var average = item.AverageMs ?? (item.Requests.Count == 0 ? (double?)null : item.Requests.Average(r => r.DurationMs));
        AddOverview(KeyValue("Avg. latency", average is null ? "—" : $"{average:F1} ms"), average?.ToString() ?? "Available");
        AddOverview(KeyValue("p95 latency", item.P95Ms is null ? "Available on completion" : $"{item.P95Ms} ms"), item.P95Ms?.ToString() ?? "Available");
        AddOverview(KeyValue("Throughput", item.RequestsPerSecond is null ? "Available on completion" : $"{item.RequestsPerSecond:F2} requests / sec"), item.RequestsPerSecond?.ToString() ?? "Available");
        AddOverview(KeyValue("Assertions", $"{item.Assertions.Count(a => a.Result == "PASS")} passed / {item.Assertions.Count(a => a.Result == "FAIL")} failed"), $"{item.Assertions.Count} pass fail");
        if (item.Issues.Count > 0)
        {
            _overviewPanel.Children.Add(DashboardStyle.Text($"Run issues  ·  {item.Issues.Count(i => i.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))} error(s) / {item.Issues.Count(i => i.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase))} warning(s)", 13, true));
            foreach (var issue in item.Issues.Where(i => MatchesTerms($"{i.Severity} {i.Category} {i.Environment} {i.Testcase} {i.DataId} {i.StepName} {i.Message}", _overviewFilter.Text)).OrderByDescending(i => i.OccurredAt))
            {
                var color = issue.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? DashboardStyle.Brush("#FFB36B") : DashboardStyle.Red;
                var scope = string.Join(" / ", new[] { issue.Environment, issue.Testcase, issue.DataId, issue.StepName }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var content = new StackPanel { Spacing = 3, Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DashboardStyle.Badge(issue.Severity), DashboardStyle.Text($"{issue.Category}{(string.IsNullOrWhiteSpace(scope) ? "" : $" · {scope}")}", 11, true) } },
                    DashboardStyle.Text(issue.Message, 11, false, color),
                    DashboardStyle.Text($"{issue.OccurredAt:HH:mm:ss} · Informational issue; warnings do not affect failure totals.", 10, false, DashboardStyle.Muted)
                }};
                _overviewPanel.Children.Add(DashboardStyle.Card(content, new Thickness(8)));
            }
        }
        _overviewPanel.Children.Add(DashboardStyle.Text($"Step execution timeline  ·  Total duration: {FormatRunDuration(item)}", 13, true));
        var stepFilterBar = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN, ItemHeight = double.NaN, Children =
        {
            _overviewStepStatus, _overviewStepEnvironment, _overviewStepTestcase, _overviewStepFilter
        }};
        foreach (var control in stepFilterBar.Children) control.Margin = new Thickness(0, 0, 8, 6);
        _overviewPanel.Children.Add(stepFilterBar);
        _overviewPanel.Children.Add(DashboardStyle.Text("Filters apply to completed steps only. Use & between terms to require all terms; matching text remains visible in the details viewer.", 10, false, DashboardStyle.Muted));
        var stepPositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stepTotals = item.Requests
            .GroupBy(request => $"{request.Testcase}\u001F{request.DataId}\u001F{request.Environment}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var filteredRequests = item.Requests.Where(MatchesOverviewStep).ToList();
        var overviewTotalPages = Math.Max(1, (int)Math.Ceiling(filteredRequests.Count / (double)DashboardPageSize));
        _overviewPage = Math.Clamp(_overviewPage, 1, overviewTotalPages);
        var pageRequests = filteredRequests.Skip((_overviewPage - 1) * DashboardPageSize).Take(DashboardPageSize);
        foreach (var request in item.Requests)
        {
            var rowKey = $"{request.Testcase}\u001F{request.DataId}\u001F{request.Environment}";
            stepPositions[rowKey] = stepPositions.GetValueOrDefault(rowKey) + 1;
        }
        var overviewPaging = CreatePaginationBar(true, filteredRequests.Count);
        foreach (var request in pageRequests)
        {
            var rowKey = $"{request.Testcase}\u001F{request.DataId}\u001F{request.Environment}";
            var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var stepTitle = DashboardStyle.Text($"{request.StepType}  ·  {request.StepName}", 11, true);
            stepTitle.TextWrapping = TextWrapping.Wrap;
            top.Children.Add(stepTitle);
            var statusText = IsUnexpectedRequest(request) ? "Unexpected error" : request.Result == "PASS" ? "Passed" : request.Result == "SKIPPED" ? "Skipped" : "Failed";
            top.Children.Add(DashboardStyle.Badge(statusText));
            top.Children.Add(DashboardStyle.Action("View details", () => LaunchStepDetails(request), "primary"));
            var position = stepPositions[rowKey];
            var context = DashboardStyle.Text($"{request.Testcase} / Row {request.DataId} · {request.Environment} · Step {position}/{stepTotals.GetValueOrDefault(rowKey, position)} · {request.DurationMs} ms", 10, false, DashboardStyle.Muted);
            context.TextWrapping = TextWrapping.Wrap;
            var line = new StackPanel { Spacing = 3, Children = { top, context } };
            if (!string.IsNullOrWhiteSpace(request.FailureCategory) && request.FailureCategory != "None") { var error = DashboardStyle.Text($"{request.FailureCategory}: {request.Error}", 10, false, DashboardStyle.Red); error.TextWrapping = TextWrapping.Wrap; line.Children.Add(error); }
            _overviewPanel.Children.Add(DashboardStyle.Card(line, new Thickness(8)));
        }
        _overviewPanel.Children.Add(overviewPaging);
        if (item.Requests.Count > 0)
        {
            _overviewPanel.Children.Add(DashboardStyle.Text("Latency trend  /  last 30 requests", 13, true));
            _overviewPanel.Children.Add(new LatencyChart(item.Requests.TakeLast(30).Select(r => (double)r.DurationMs).ToArray()) { Height = 90 });
            _overviewPanel.Children.Add(DashboardStyle.Text($"Range: 0–{item.Requests.TakeLast(30).Max(r => r.DurationMs)} ms / observed completion order", 10, false, DashboardStyle.Muted));
        }
        RenderUnifiedLog();
        RefreshAssertionList(); RefreshArtifacts();
    }
    private static bool IsUnexpectedRequest(DashboardRequest request)
        => !string.IsNullOrWhiteSpace(request.Error) || (!string.IsNullOrWhiteSpace(request.FailureCategory) && request.FailureCategory is not "None" and not "Assertion" and not "HttpStatus");
    private static IReadOnlyList<DashboardRowSummary> BuildDashboardRowSummaries(IReadOnlyList<DashboardRequest> requests)
    {
        return requests.GroupBy(request => $"{request.Testcase}\u001F{request.DataId}\u001F{request.Environment}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var values = group.ToList();
                var unexpected = values.Count(IsUnexpectedRequest);
                var failed = values.Count(x => x.Result.Equals("FAIL", StringComparison.OrdinalIgnoreCase) && !IsUnexpectedRequest(x));
                var passed = values.Count(x => x.Result.Equals("PASS", StringComparison.OrdinalIgnoreCase));
                var skipped = values.Count(x => x.Result.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase));
                var status = unexpected > 0 ? "Unexpected error" : failed > 0 ? "Failed" : skipped > 0 && passed == 0 ? "Cancelled" : passed == values.Count && values.Count > 0 ? "Passed" : "Incomplete";
                var details = string.Join("  |  ", values.Where(x => !string.IsNullOrWhiteSpace(x.Error) || (!string.IsNullOrWhiteSpace(x.FailureCategory) && x.FailureCategory != "None")).Select(x => $"{x.StepName}: {x.FailureCategory} {x.Error}"));
                var first = values[0];
                return new DashboardRowSummary(first.Testcase, first.DataId, string.IsNullOrWhiteSpace(first.Environment) ? "default" : first.Environment, values.Count, passed, failed, unexpected, skipped, status, details);
            })
            .OrderBy(x => x.Testcase, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DataId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Environment, StringComparer.OrdinalIgnoreCase).ToList();
    }
    private void RefreshArtifacts()
    {
        _artifactPanel.Children.Clear(); var item = _selectedInstance;
        if (item is null) return;
        _artifactPanel.Children.Add(DashboardStyle.Text("Workbooks and run artifacts", 16, true));
        _artifactPanel.Children.Add(DashboardStyle.Action("Edit data", OpenDataEditor, "primary"));
        _artifactPanel.Children.Add(DashboardStyle.Action("Handcraft: Edit workbook (legacy)", () => _ = EditWorkbookAsync()));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open latest result workbook", OpenLatestResultWorkbook));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open Templates folder", OpenTemplatesFolder));
        _artifactPanel.Children.Add(DashboardStyle.Action("Open Results folder", () =>
        {
            if (!item.GenerateResults && !Directory.Exists(item.ResultsFolder))
            {
                _details.Text = "Technical Results generation is disabled for this instance; no Results folder was created.";
                return;
            }
            OpenFolder(item.ResultsFolder, "results folder");
        }));
        _artifactPanel.Children.Add(KeyValue("Workbook", item.WorkbookPath));
        _artifactPanel.Children.Add(KeyValue("Results", item.ResultsFolder));
        _artifactPanel.Children.Add(KeyValue("Current run", item.CurrentRunFolder));
    }
    private void OpenSpreadsheetPreview()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; return; }
        LaunchSpreadsheetProcess(_selectedInstance.WorkbookPath, "dataset editor");
    }

    private void LaunchSpreadsheetProcess(string workbookPath, string description)
    {
        try
        {
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The dashboard process path is unavailable.");
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            var start = new ProcessStartInfo { FileName = processPath, UseShellExecute = false, CreateNoWindow = true };
            if (!string.IsNullOrWhiteSpace(entryAssembly) && (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)))
            {
                start.ArgumentList.Add(entryAssembly);
            }
            start.ArgumentList.Add("--dataset-editor"); start.ArgumentList.Add(workbookPath);
            var editor = Process.Start(start) ?? throw new InvalidOperationException("The dataset editor process could not be started.");
            editor.EnableRaisingEvents = true;
            editor.Exited += (_, _) => Dispatcher.UIThread.Post(() => { RefreshInstances(); _details.Text = $"{description} closed for {Path.GetFileName(workbookPath)}."; UiLog($"Spreadsheet subprocess exited. description={description}; workbook={workbookPath}; exitCode={editor.ExitCode}"); });
            UiLog($"Spreadsheet subprocess started. description={description}; workbook={workbookPath}; processId={editor.Id}");
        }
        catch (Exception ex) { _details.Text = $"Could not start {description}: {ex.Message}"; UiLog($"Spreadsheet subprocess failed to start. description={description}; workbook={workbookPath}; error={ex}"); }
    }
    private async Task CopyOverviewAsync()
    {
        if (_selectedInstance is null) return;
        var item = _selectedInstance;
        var lines = new List<string>
        {
            $"Instance: {item.Name}", $"Status: {item.Status}", $"Environment: {item.EnvironmentSelection}",
            $"Run ID: {item.CurrentRunId}", $"Last event: {item.LastEvent}"
        };
        lines.AddRange(item.Requests.Select(r => $"{r.Result} | {r.Testcase} | {r.DataId} | {r.StepType}/{r.StepName} | {r.DurationMs} ms | {r.Error}"));
        await CopyTextAsync(string.Join(Environment.NewLine, lines));
    }
    private async Task CopyAssertionsAsync()
    {
        if (_selectedInstance is null) return;
        var lines = _selectedInstance.Assertions.Select(a => $"{a.Result} | {a.Environment} | {a.Testcase} | {a.DataId} | {a.StepName} | {a.ResponsePath} {a.AssertionVerb} | type={a.AssertionType} | meaning={a.AssertionMeaning} | expected={a.ExpectedValue} | actual={a.ActualValue}");
        await CopyTextAsync(string.Join(Environment.NewLine, lines));
    }
    private async Task CopyTextAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text);
        _details.Text = "Copied dashboard details to the clipboard.";
    }
    private async void RefreshHistoryPanel()
    {
        if (_historyRefreshing) return;
        _historyRefreshing = true;
        try
        {
            var sources = _instances.Where(x => Directory.Exists(x.HistoryFolder)).Select(x => (Item: x, Paths: Directory.GetFiles(x.HistoryFolder, "*.xlsx", SearchOption.AllDirectories).Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTimeUtc).ToArray())).ToArray();
            var loaded = await Task.Run(() => sources.SelectMany(source => source.Paths.Select(path => (source.Item, Path: path))).Select(source => { try { return ReadHistoryEntry(source.Item, source.Path); } catch { return null; } }).Where(x => x is not null).Cast<HistoryEntry>().ToList());
            _historyEntries.Clear(); _historyEntries.AddRange(loaded); _historySelected.RemoveWhere(path => !File.Exists(path));
        }
        catch (Exception ex)
        {
            UiLog($"History refresh failed safely: {ex}");
            _historyEntries.Clear();
            _historyPanel.Children.Clear();
            _historyPanel.Children.Add(DashboardStyle.Text("History could not be loaded. The dashboard is still available; fix the file or permission problem and click Refresh.", color: DashboardStyle.Red));
            _historyPanel.Children.Add(DashboardStyle.Text(ex.Message, 11, false, DashboardStyle.Muted));
            return;
        }
        finally { _historyRefreshing = false; }
        _historyPanel.Children.Clear();
        _historyPanel.Children.Add(DashboardStyle.Text("History Explorer", 20, true));
        _historyPanel.Children.Add(DashboardStyle.Text("Browse complete runs, testcase rows, steps, Data IDs, assertions, request/response data, and compare two executions.", 12, false, DashboardStyle.Muted));
        var filterTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _historySearch, _historyStatus, DashboardStyle.Action("Refresh", RefreshHistoryPanel), DashboardStyle.Action("Clear comparison", () => { _historySelected.Clear(); RenderHistoryEntries(); }) } };
        _historySearch.TextChanged -= HistoryFilterChanged; _historySearch.TextChanged += HistoryFilterChanged;
        _historyStatus.SelectionChanged -= HistoryFilterChanged; _historyStatus.SelectionChanged += HistoryFilterChanged;
        _historyPanel.Children.Add(filterTools);
        var compare = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DashboardStyle.Action("Compare selected", CompareHistory), _historyCompare } };
        _historyPanel.Children.Add(DashboardStyle.Card(compare, new Thickness(10)));
        _historyPanel.Children.Add(new Border { Height = 1, Background = DashboardStyle.Line });
        _historyPanel.Children.Add(new ScrollViewer { Content = _historyResults, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        RenderHistoryEntries();
    }

    private void HistoryFilterChanged(object? sender, EventArgs e) => RenderHistoryEntries();

    private void RenderHistoryEntries()
    {
        _historyResults.Children.Clear();
        var query = _historySearch.Text?.Trim() ?? "";
        var status = _historyStatus.SelectedItem?.ToString() ?? "All statuses";
        var visible = _historyEntries.Where(x => status == "All statuses" || x.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(query) || x.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var entry in visible.Take(200))
        {
            var selected = _historySelected.Contains(entry.Path);
            var check = new CheckBox { IsChecked = selected, VerticalAlignment = VerticalAlignment.Top };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) _historySelected.Add(entry.Path); else _historySelected.Remove(entry.Path); _historyCompare.Text = $"{_historySelected.Count} history file(s) selected; choose exactly two to compare."; };
            var title = DashboardStyle.Text($"{entry.Instance}  ·  {entry.Status}  ·  {entry.RunId}", 13, true); title.TextWrapping = TextWrapping.Wrap;
            var summary = DashboardStyle.Text($"{entry.Started:yyyy-MM-dd HH:mm:ss}  ·  {entry.Requests.Count} step rows  ·  {entry.Passed} passed  ·  {entry.Failed} failed  ·  p95 {entry.P95Ms} ms", 11, false, DashboardStyle.Muted); summary.TextWrapping = TextWrapping.Wrap;
            var open = DashboardStyle.Action("Open workbook", () => OpenExcelFile(entry.Path, "history workbook"));
            var details = new StackPanel { Spacing = 4, Children = { title, summary } };
            foreach (var row in entry.Requests.Take(8)) { var line = DashboardStyle.Text($"{row.Result,-5}  {row.Testcase} / {row.DataId} / {row.Step}  {row.DurationMs} ms  HTTP {row.HttpStatus}", 11, false, row.Result == "FAIL" ? DashboardStyle.Red : DashboardStyle.Muted); line.TextWrapping = TextWrapping.Wrap; details.Children.Add(line); }
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto"), Children = { check, details, open } }; Grid.SetColumn(details, 1); Grid.SetColumn(open, 2);
            _historyResults.Children.Add(DashboardStyle.Card(grid, new Thickness(10)));
        }
        if (visible.Count == 0) _historyResults.Children.Add(DashboardStyle.Text("No history matches the current filters.", color: DashboardStyle.Muted));
        else if (visible.Count > 200) _historyResults.Children.Add(DashboardStyle.Text($"Showing the first 200 of {visible.Count} matches. Use filters to narrow the list.", color: DashboardStyle.Muted));
        _historyCompare.Text = $"{_historySelected.Count} history file(s) selected; choose exactly two to compare.";
    }

    private void CompareHistory()
    {
        var selected = _historyEntries.Where(x => _historySelected.Contains(x.Path)).ToList();
        if (selected.Count != 2) { _historyCompare.Text = "Select exactly two history files first."; return; }
        var left = selected[0]; var right = selected[1];
        var byKey = left.Requests.Concat(right.Requests).GroupBy(x => $"{x.Testcase}|{x.DataId}|{x.Step}", StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key);
        var changed = 0; var requestLines = new List<string> { $"Comparison: {Path.GetFileName(left.Path)}  vs  {Path.GetFileName(right.Path)}" };
        var responseLines = new List<string>(); var configLines = new List<string>(); var assertionLines = new List<string>();
        var variableLines = CompareSheet(left.Path, right.Path, "Variables");
        var dataRowLines = CompareSheet(left.Path, right.Path, "DataEngineRequestRows");
        configLines.AddRange(CompareSheet(left.Path, right.Path, "DataEngineConfig"));
        foreach (var group in byKey)
        {
            var a = group.FirstOrDefault(x => left.Requests.Contains(x)); var b = group.FirstOrDefault(x => right.Requests.Contains(x));
            if (a is null || b is null || a.Result != b.Result || a.HttpStatus != b.HttpStatus || a.DurationMs != b.DurationMs || a.RequestBody != b.RequestBody || a.ResponseBody != b.ResponseBody || a.RequestHeaders != b.RequestHeaders || a.ResponseHeaders != b.ResponseHeaders || a.StepConfig != b.StepConfig)
            { changed++; requestLines.Add($"{group.Key}: {(a?.Result ?? "missing")} / {(b?.Result ?? "missing")}; HTTP {a?.HttpStatus.ToString() ?? "-"} / {b?.HttpStatus.ToString() ?? "-"}; {a?.DurationMs.ToString() ?? "-"} ms / {b?.DurationMs.ToString() ?? "-"} ms"); configLines.Add($"{group.Key}:\nA: {a?.StepConfig ?? "missing"}\nB: {b?.StepConfig ?? "missing"}"); responseLines.Add($"{group.Key}:\nA headers: {a?.ResponseHeaders ?? "missing"}\nB headers: {b?.ResponseHeaders ?? "missing"}\nA body: {a?.ResponseBody ?? "missing"}\nB body: {b?.ResponseBody ?? "missing"}"); }
        }
        var assertionKeys = left.Assertions.Concat(right.Assertions).GroupBy(x => $"{x.Testcase}|{x.DataId}|{x.Step}|{x.Path}", StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key);
        foreach (var group in assertionKeys)
        {
            var a = group.FirstOrDefault(x => left.Assertions.Contains(x)); var b = group.FirstOrDefault(x => right.Assertions.Contains(x));
            if (a is null || b is null || a.Result != b.Result || a.Verb != b.Verb || a.Expected != b.Expected || a.Actual != b.Actual) assertionLines.Add($"{group.Key}: {(a?.Result ?? "missing")} / {(b?.Result ?? "missing")}; type {a?.Type ?? "-"} / {b?.Type ?? "-"}; verb {a?.Verb ?? "-"} / {b?.Verb ?? "-"}; expected {a?.Expected ?? "-"} / {b?.Expected ?? "-"}; actual {a?.Actual ?? "-"} / {b?.Actual ?? "-"}; meaning {a?.Meaning ?? b?.Meaning ?? "-"}");
        }
        _historyCompare.Text = $"Comparison complete: {changed} changed row(s).";
        var dialog = new Window { Title = "History comparison", Width = 1100, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var search = new TextBox { Watermark = "Filter differences…", Margin = new Thickness(0, 0, 0, 8) };
        var tabs = new TabControl();
        void AddTab(string header, IEnumerable<string> values)
        {
            var source = values.ToList(); var host = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
            void Render() { host.Children.Clear(); var filtered = source.Where(x => x.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList(); foreach (var line in filtered) host.Children.Add(DiffVisual(line)); if (filtered.Count == 0) host.Children.Add(DashboardStyle.Text("No differences in this tab.", color: DashboardStyle.Muted)); }
            search.TextChanged += (_, _) => Render(); Render();
            tabs.Items.Add(new TabItem { Header = header, Content = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } });
        }
        AddTab("Request", requestLines); AddTab("Response", responseLines); AddTab("Assertions", assertionLines); AddTab("Variables", variableLines); AddTab("DataEngine rows", dataRowLines); AddTab("Config", configLines);
        var close = DashboardStyle.Action("Close", dialog.Close);
        var comparisonGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18) };
        comparisonGrid.Children.Add(search); Grid.SetRow(tabs, 1); comparisonGrid.Children.Add(tabs); Grid.SetRow(close, 2); comparisonGrid.Children.Add(close); dialog.Content = comparisonGrid;
        dialog.Show(this);
    }

    private static Control DiffVisual(string line)
    {
        var panel = new StackPanel { Spacing = 2 };
        var heading = DashboardStyle.Text(line.Split('\n')[0], 11, true); heading.TextWrapping = TextWrapping.Wrap; panel.Children.Add(heading);
        foreach (var part in line.Split('\n').Skip(1))
        {
            var color = part.StartsWith("A:", StringComparison.OrdinalIgnoreCase) ? DashboardStyle.Red : part.StartsWith("B:", StringComparison.OrdinalIgnoreCase) ? DashboardStyle.Green : DashboardStyle.Muted;
            var value = DashboardStyle.Text(part, 11, false, color); value.TextWrapping = TextWrapping.Wrap; panel.Children.Add(value);
        }
        return DashboardStyle.Card(panel, new Thickness(7));
    }

    private static List<string> CompareSheet(string leftPath, string rightPath, string sheetName)
    {
        try
        {
            using var aBook = new XLWorkbook(leftPath); using var bBook = new XLWorkbook(rightPath);
            var a = aBook.Worksheets.FirstOrDefault(x => x.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase)); var b = bBook.Worksheets.FirstOrDefault(x => x.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
            if (a is null || b is null) return [];
            var aRows = a.RowsUsed().Select(r => string.Join(" | ", r.CellsUsed().Select(c => c.GetString()))).Skip(1).ToHashSet(StringComparer.Ordinal);
            var bRows = b.RowsUsed().Select(r => string.Join(" | ", r.CellsUsed().Select(c => c.GetString()))).Skip(1).ToHashSet(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var row in aRows.Except(bRows)) result.Add($"{sheetName} row changed\nA: {row}\nB: <removed or changed>");
            foreach (var row in bRows.Except(aRows)) result.Add($"{sheetName} row changed\nA: <removed or changed>\nB: {row}");
            return result;
        }
        catch { return []; }
    }

    private static HistoryEntry ReadHistoryEntry(InstanceInfo item, string path)
    {
        using var workbook = new XLWorkbook(path); var summary = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("RunSummary", StringComparison.OrdinalIgnoreCase)); var metrics = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("Metrics", StringComparison.OrdinalIgnoreCase)); var requestSheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("RequestHistory", StringComparison.OrdinalIgnoreCase));
        var headers = requestSheet?.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
        string Cell(IXLRow row, string name) => headers.TryGetValue(name, out var c) ? row.Cell(c).GetString() : "";
        var requests = requestSheet is null ? [] : requestSheet.RowsUsed().Skip(1).Select(row => new HistoryRequest(Cell(row, "testcase"), Cell(row, "dataid"), Cell(row, "stepname"), Cell(row, "requestresult"), long.TryParse(Cell(row, "durationms"), out var d) ? d : 0, int.TryParse(Cell(row, "httpstatus"), out var s) ? s : 0, Cell(row, "requestbody"), Cell(row, "responsebody"), Cell(row, "requestheaders"), Cell(row, "responseheaders"), Cell(row, "stepconfig"))).ToList();
        var assertionSheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("AssertionHistory", StringComparison.OrdinalIgnoreCase)); var ah = assertionSheet?.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? []; string ACell(IXLRow row, string name) => ah.TryGetValue(name, out var c) ? row.Cell(c).GetString() : "";
        var assertions = assertionSheet is null ? [] : assertionSheet.RowsUsed().Skip(1).Select(row => new HistoryAssertion(ACell(row, "testcase"), ACell(row, "dataid"), ACell(row, "stepname"), ACell(row, "responsepath"), ACell(row, "assertionverb"), ACell(row, "assertiontype"), ACell(row, "assertionmeaning"), ACell(row, "expectedvalue"), ACell(row, "actualvalue"), ACell(row, "result"))).ToList();
        var result = summary?.Cell(2, 16).GetString() ?? "UNKNOWN"; var runId = summary?.Cell(2, 1).GetString() ?? Path.GetFileNameWithoutExtension(path); var started = DateTime.TryParse(summary?.Cell(2, 2).GetString(), out var date) ? date : File.GetLastWriteTime(path); var p95 = metrics?.RowsUsed().FirstOrDefault(r => r.Cell(1).GetString().Equals("p95DurationMs", StringComparison.OrdinalIgnoreCase))?.Cell(2).GetString() ?? "0";
        return new HistoryEntry(path, item.Name, result, runId, started, requests, assertions, requests.Count(x => x.Result == "PASS"), requests.Count(x => x.Result == "FAIL"), p95, string.Join(" ", item.Name, result, runId, requests.Select(x => $"{x.Testcase} {x.DataId} {x.Step}")));
    }
    private static void RecordDashboardEvent(InstanceInfo item, JsonElement e)
    {
        string Str(string name) => e.TryGetProperty(name, out var value) ? value.ToString() : "";
        long Long(string name) => e.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
        double? Number(string name) => e.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
        var type = Str("eventType");
        if (type == "request-completed")
        {
            item.Requests.Add(new DashboardRequest(Str("stepName"), Str("stepType"), Str("testcase"), Str("dataId"), Str("environment"), Str("result"), Long("durationMs"), Str("failureCategory"), Str("errorMessage")));
            item.EventMessages.Add($"{DateTime.Now:HH:mm:ss}  {Str("result"),-5}  {Str("stepType")} / {Str("stepName")} / {Long("durationMs")} ms / {Str("failureCategory")}");
        }
        else if (type == "run-completed")
        {
            item.CompletedRequests = (int)Long("totalRequests"); item.AverageMs = Number("averageDurationMs");
            item.P95Ms = Long("p95DurationMs"); item.RequestsPerSecond = Number("requestsPerSecond"); item.CompletedAt = DateTimeOffset.Now;
            item.EventMessages.Add($"{DateTime.Now:HH:mm:ss}  RUN {Str("result")} / {item.CompletedRequests} requests / p95 {item.P95Ms} ms");
        }
        if (item.EventMessages.Count > 200) item.EventMessages.RemoveRange(0, item.EventMessages.Count - 200);
    }
}

public sealed record DashboardRequest(string StepName, string StepType, string Testcase, string DataId, string Environment, string Result, long DurationMs, string FailureCategory, string Error);
internal sealed record DashboardRowSummary(string Testcase, string DataId, string Environment, int Steps, int Passed, int Failed, int Unexpected, int Skipped, string Status, string Details);
internal sealed record RunPlanRow(string Testcase, int SkippedSteps, int ExecutableSteps, bool HasAssertions);
internal sealed record RunPlanSummary(int Testcases, int SelectedSteps, int RowsSelected, int RowsWithDontrun, int RowsFullySkipped, long SkippedSteps, long PlannedSteps, int Environments, int MaxParallelRows, int Iterations, int RowsWithAssertions, int RowsWithoutAssertions, IReadOnlyList<string> EnvironmentNames, IReadOnlyList<RunPlanRow> Rows)
{
    public long PlannedExecutions => PlannedSteps;
};
internal sealed record HistoryRequest(string Testcase, string DataId, string Step, string Result, long DurationMs, int HttpStatus, string RequestBody, string ResponseBody, string RequestHeaders, string ResponseHeaders, string StepConfig);
internal sealed record HistoryAssertion(string Testcase, string DataId, string Step, string Path, string Verb, string Type, string Meaning, string Expected, string Actual, string Result);
internal sealed record HistoryEntry(string Path, string Instance, string Status, string RunId, DateTime Started, IReadOnlyList<HistoryRequest> Requests, IReadOnlyList<HistoryAssertion> Assertions, int Passed, int Failed, string P95Ms, string SearchText);
internal sealed class LatencyChart(double[] values) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width; var height = Bounds.Height;
        var pen = new Pen(DashboardStyle.Line, 1);
        for (var i = 0; i < 3; i++) context.DrawLine(pen, new Point(0, height * i / 2), new Point(width, height * i / 2));
        if (values.Length == 0) return;
        var max = Math.Max(1, values.Max());
        for (var i = 0; i < values.Length; i++)
        {
            var x = values.Length == 1 ? width / 2 : width * i / (values.Length - 1);
            var y = height - 5 - (height - 10) * values[i] / max;
            context.DrawEllipse(DashboardStyle.Blue, null, new Point(x, y), 2, 2);
            if (i > 0) context.DrawLine(new Pen(DashboardStyle.Blue, 2), new Point(width * (i - 1) / (values.Length - 1), height - 5 - (height - 10) * values[i - 1] / max), new Point(x, y));
        }
    }
}
