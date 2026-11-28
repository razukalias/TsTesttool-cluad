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
    /// <summary>A text the user can select with the mouse and copy (a SelectableTextBlock is a TextBlock, so callers do not change).</summary>
    public static TextBlock Text(string text, double size = 13, bool bold = false, IBrush? color = null) => new SelectableTextBlock()
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
    /// <summary>A figure tile: small caption, big value, optional small note under it; a coloured strip on the left carries the meaning.</summary>
    public static Border Tile(string label, string value, IBrush? color = null, string? note = null, double width = 128)
    {
        var caption = Text(label, 10, false, Muted); caption.TextWrapping = TextWrapping.Wrap;
        var body = new StackPanel { Spacing = 2, Children = { caption, Text(value, 20, true, color ?? Brush("#E3EDF9")) } };
        if (!string.IsNullOrWhiteSpace(note)) body.Children.Add(Text(note, 10, false, Muted));
        var tile = new Border { Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Width = width, Margin = new Thickness(0, 0, 8, 8), Child = body };
        if (color is not null) tile.BorderBrush = color is SolidColorBrush solid ? new SolidColorBrush(solid.Color, 0.45) : Line;
        return tile;
    }
    /// <summary>A section heading with a thin rule, to separate blocks of the tabs.</summary>
    public static Control Section(string title, string? hint = null)
    {
        var head = new StackPanel { Spacing = 2, Margin = new Thickness(0, 10, 0, 6) };
        head.Children.Add(Text(title.ToUpperInvariant(), 10, true, Muted));
        if (!string.IsNullOrWhiteSpace(hint)) head.Children.Add(Text(hint, 10, false, Muted));
        head.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 4, 0, 0) });
        foreach (var text in head.Children.OfType<TextBlock>()) text.TextWrapping = TextWrapping.Wrap;
        return head;
    }
    /// <summary>A small rounded label such as "Environment  All global environments".</summary>
    public static Border Pill(string label, string value)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Text(label, 11, false, Muted), Text(value, 11, true) } };
        ToolTip.SetTip(line, value);
        return new Border { Background = Brush("#17283B"), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 8, 6), Child = line };
    }
    /// <summary>One bar split in proportion to the given amounts (a run of zero amounts shows an empty track).</summary>
    public static Control ProportionBar(params (long Amount, IBrush Color)[] parts)
    {
        var shown = parts.Where(p => p.Amount > 0).ToList();
        var grid = new Grid { Height = 8 };
        if (shown.Count == 0) { grid.Children.Add(new Border { Background = Line, CornerRadius = new CornerRadius(4) }); return grid; }
        grid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", shown.Select(p => $"{p.Amount}*")));
        for (var i = 0; i < shown.Count; i++)
        {
            var segment = new Border { Background = shown[i].Color, Margin = new Thickness(0, 0, i == shown.Count - 1 ? 0 : 2, 0),
                CornerRadius = new CornerRadius(i == 0 ? 4 : 0, i == shown.Count - 1 ? 4 : 0, i == shown.Count - 1 ? 4 : 0, i == 0 ? 4 : 0) };
            Grid.SetColumn(segment, i); grid.Children.Add(segment);
        }
        return grid;
    }
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

    internal static string LatestRunId(InstanceInfo item)
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
    private readonly Pager _historyPager = new();
    private PagerBar? _historyPagerBar;
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
    private readonly ComboBox _overviewRun = new() { Width = 300, Tag = "overview-run" };
    private bool _overviewRunRefreshing;
    private readonly TextBox _overviewFilter = new() { Watermark = "Filter overview: term1 & term2…", Width = 260 };
    private readonly TextBox _assertionSearch = new() { Watermark = "Filter assertions: term1 & term2…", Width = 260 };
    private readonly TextBlock _selectedTitle = DashboardStyle.Text("Selected instance", 16, true);
    private readonly TextBlock _listTitle = DashboardStyle.Text("Test instances", 15, true);
    private readonly TextBlock _footer = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly TextBox _search = new() { Watermark = "Search instances…", Width = 200, FontSize = 12 };
    private readonly List<(string Text, string Severity)> _validationLog = [];
    private readonly TabControl _detailTabs = new();
    private string _page = "Instances";
    private string _detailSignature = "";
    private double _logFontSize = 11;
    private readonly Pager _overviewPager = new();
    private readonly Pager _assertionPager = new();
    private PagerBar? _assertionBar;
    private readonly Pager _artifactPager = new();
    private readonly Pager _logPager = new();
    private PagerBar? _logBar;
    private string _overviewFilterSignature = "", _assertionFilterSignature = "";

    private void BuildLayout()
    {
        Background = DashboardStyle.Brush("#0A1421");
        Foreground = DashboardStyle.Brush("#E3EDF9");
        Width = 1440; Height = 900; MinWidth = 1280; MinHeight = 700;
        Title = $"Load Testing Tool • {DashboardStyle.Build}";
        _failedOnly.IsCheckedChanged += (_, _) => RefreshAssertionList();
        foreach (var filter in new[] { _environmentFilter, _assertionTestcaseFilter, _assertionStepFilter, _assertionResultFilter, _assertionVerbFilter })
            filter.Changed += (_, _) => RefreshAssertionList();
        _assertionExclude.TextChanged += (_, _) => RefreshAssertionList();
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
        ToolTip.SetTip(_overviewStepExclude, "Hide the completed steps that contain every term (separate terms with &). Clear the box and they are back. Nothing is removed from the run or remembered");
        _overviewStepExclude.TextChanged += (_, _) =>
        {
            if (_overviewStepFilterRefreshing) return;
            var caret = _overviewStepExclude.CaretIndex;
            RefreshDashboardDetails(true);
            Dispatcher.UIThread.Post(() =>
            {
                _overviewStepExclude.Focus();
                _overviewStepExclude.CaretIndex = Math.Min(caret, _overviewStepExclude.Text?.Length ?? 0);
            });
        };
        _overviewStepStatus.SetOptions(["Passed", "Failed", "Unexpected error", "Skipped"]);
        foreach (var filter in new[] { _overviewStepStatus, _overviewStepEnvironment, _overviewStepTestcase, _overviewStepName })
            filter.Changed += (_, _) => { if (!_overviewStepFilterRefreshing) RefreshDashboardDetails(true); };
        _assertionSearch.TextChanged += (_, _) => RefreshAssertionList();
        var assertionActions = new WrapPanel { Children = { DashboardStyle.Action("Compare", () => _ = GenerateAssertionComparisonAsync(openInEditor: true)) } };
        var assertionPanel = new DockPanel { Margin = new Thickness(12) };
        var assertionFilterBar = new WrapPanel { Children = { _environmentFilter, _assertionTestcaseFilter, _assertionStepFilter, _assertionResultFilter, _assertionVerbFilter, _assertionSearch, _assertionExclude } };
        foreach (var control in assertionFilterBar.Children) control.Margin = new Thickness(0, 0, 8, 6);
        _assertionSearch.Watermark = "Show: assertion text, term1 & term2…"; _assertionSearch.Width = 230; _assertionSearch.Tag = "assertion-include";
        var filters = new StackPanel { Spacing = 6, Children = { _assertionSummary, _failedOnly, assertionFilterBar, _assertionChips, assertionActions } };
        var filtersHost = DetailTextSize.Wrap(filters); DockPanel.SetDock(filtersHost, Dock.Top); assertionPanel.Children.Add(filtersHost);
        // the bar is added before the list: the last child of a DockPanel fills the rest, so the list must be last (the bar docks to the bottom)
        var assertionPaging = CreatePaginationBar(false);
        var pagingHost = DetailTextSize.Wrap(assertionPaging); DockPanel.SetDock(pagingHost, Dock.Bottom); assertionPanel.Children.Add(pagingHost);
        assertionPanel.Children.Add(new ScrollViewer { Content = DetailTextSize.Wrap(_assertionItems), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _manualTab = new TabItem { Header = BuildActionManualTabHeader(0), Content = BuildActionManualPanel() };
        _bugsTab = new TabItem { Header = "Bugs (0)", Content = Scroll(_bugItems) };
        _detailTabs.ItemsSource = new[]
        {
            new TabItem { Header = "Overview", Content = Scroll(_overviewPanel, wrap: true) },
            _manualTab,
            new TabItem { Header = "Run Summary", Content = Scroll(_runSummaryPanel, wrap: true) },
            new TabItem { Header = "Assertions", Content = assertionPanel },
            _bugsTab,
            new TabItem { Header = "Environments", Content = Scroll(_environmentCompareItems, wrap: true) },
            new TabItem { Header = "Artifacts", Content = Scroll(_artifactPanel, wrap: true) }
        };
        _overviewPanel.Children.Insert(0, new WrapPanel { Children = { _overviewFilter, DashboardStyle.Action("Copy overview", () => _ = CopyOverviewAsync()) } });
        Content = _detailTabs;
        _details.TextWrapping = TextWrapping.Wrap; _details.Foreground = DashboardStyle.Muted; _details.FontSize = 11;
    }

    private static ScrollViewer Scroll(Control control, bool wrap = false) => new() { Content = new Border { Padding = new Thickness(14), Child = DetailTextSize.Wrap(control) }, HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto };

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
        title.ClearValue(TextBlock.ForegroundProperty);   // follow the tab's own colour: gray when inactive, like the other tabs
        var count = DashboardStyle.Badge(pendingCount.ToString());
        count.Background = DashboardStyle.Brush(pendingCount > 0 ? "#E05260" : "#8EA5BC");
        count.Padding = new Thickness(6, 2);
        if (count.Child is TextBlock countText) countText.Foreground = DashboardStyle.Brush(pendingCount > 0 ? "#FFFFFF" : "#102033");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { title, count } };
    }

    private void RefreshActionManualPanel()
    {
        if (_manualItems is null || _refreshingManual) return;
        // Filling the two filter lists raises SelectionChanged, which calls this method again. Without the guard that inner call
        // built the cards and the outer one then built them a second time, so every pending action showed twice.
        _refreshingManual = true;
        try { RefreshActionManualPanelCore(); }
        finally { _refreshingManual = false; }
    }

    private bool _refreshingManual;

    private void RefreshActionManualPanelCore()
    {
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
        _manualItems.Children.Clear();
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

    private void OpenStorageCleanup()
    {
        var window = new StorageCleanupWindow(_config.DataEngineBackupFolder, _instances.Select(x => x.FolderPath));
        InstanceInfo? Owner(string workbook) => _instances.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.FolderPath).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetDirectoryName(workbook)!).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
        window.RestoreGuard = workbook =>
        {
            if (Owner(workbook) is not { } item) return null;
            if (item.Process is { HasExited: false }) return "it is running";
            return (_hostWindow as DataEditor.DataEditorWindow)?.Shell.ReplaceBlockedReason(item.Name);
        };
        window.Restored = workbook =>
        {
            if (Owner(workbook) is not { } item) return;
            Dispatcher.UIThread.Post(() => (_hostWindow as DataEditor.DataEditorWindow)?.Shell.ReloadAfterExternalChange(item.Name, "by a restored backup"), DispatcherPriority.Background);
        };
        window.Show(Host);
    }

    internal void ShowStorageCleanup() => OpenStorageCleanup();

    private MockServerWindow? _mockServerWindow;

    /// <summary>Opens (or brings forward) the Mock server window. Its files live in a MockServers folder beside the central settings file.</summary>
    internal void ShowMockServer()
    {
        if (_mockServerWindow is { } open) { open.Activate(); return; }
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_config.SettingsFile))!, "MockServers");
        var window = new MockServerWindow(Path.GetFullPath(_config.RunnerDll), folder);
        window.SettingsFile = Path.GetFullPath(_config.SettingsFile);
        window.UseInInstance = (origin, all) => (_hostWindow as DataEditor.DataEditorWindow)?.Shell.PointSelectedInstanceAt(origin, all) ?? "Open the editor with an instance first.";
        window.Closed += (_, _) => _mockServerWindow = null;
        _mockServerWindow = window;
        if (_hostWindow is { } host) window.Show(host); else window.Show();
    }

    /// <summary>Opens the "History &amp; logs" form for an instance (the selected one when no name is given).</summary>
    internal void ShowHistoryLogs(string? instanceName = null)
    {
        var item = instanceName is null ? _selectedInstance : _instances.FirstOrDefault(x => x.Name == instanceName);
        if (item is null) { _details.Text = "Select an instance first."; return; }
        var current = new Reporting.HistoryOptions(item.InternalLoggingEnabled, item.InternalLogErrorsOnly, item.GenerateAggregateHistory, item.GenerateRowHistory, item.SaveRequestBodyToHistory,
            item.SaveResponseBodyToHistory, item.MaskSensitiveData, item.HistoryDetail, item.HistorySampleEvery, new Dictionary<string, bool>(item.HistorySheets, StringComparer.OrdinalIgnoreCase), item.HistoryStorage);
        var window = new HistoryLogsWindow(item.Name, current, Reporting.HistoryProfiles.RunIsLarge(1, item.Iterations, item.MaxParallelRows));
        window.Closed += (_, _) =>
        {
            if (window.Result is not { } chosen) return;
            item.InternalLoggingEnabled = chosen.InternalLog; item.InternalLogErrorsOnly = chosen.InternalLogErrorsOnly;
            item.GenerateAggregateHistory = chosen.AggregateHistory; item.GenerateRowHistory = chosen.RowHistory;
            item.SaveRequestBodyToHistory = chosen.SaveRequestBodies; item.SaveResponseBodyToHistory = chosen.SaveResponseBodies; item.MaskSensitiveData = chosen.MaskSensitive;
            item.HistoryDetail = chosen.Detail; item.HistorySampleEvery = chosen.SampleEvery;
            item.HistoryStorage = chosen.Storage;
            if (chosen.Storage != HistoryStorage.Files)
                try { new SqliteHistoryStore(item.HistoryFolder).EnsureCreated(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { _details.Text = $"The history database could not be created: {ex.Message}"; }
            foreach (var sheet in chosen.Sheets) item.HistorySheets[sheet.Key] = sheet.Value;
            SaveInstanceReportingSettings(item, true);
        };
        window.Show(Host);
    }
    /// <summary>The first three log files that matched no run, then "and N more".</summary>
    private static string UnmatchedLogNames(LoadTestingTool.Reporting.SqliteHistoryStore.LogImportResult r)
    {
        var names = (r.NoRunFiles ?? []).Take(3).ToList();
        var more = r.NoRun - names.Count;
        return string.Join(", ", names) + (more > 0 ? (names.Count > 0 ? " and " : string.Empty) + more + " more" : string.Empty);
    }

    private static Control KeyValue(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("105,*") };
        var name = DashboardStyle.Text(label, 12, false, DashboardStyle.Muted); name.VerticalAlignment = VerticalAlignment.Top; grid.Children.Add(name);
        var text = DashboardStyle.Text(value, 12); text.VerticalAlignment = VerticalAlignment.Top; text.TextWrapping = TextWrapping.Wrap; ToolTip.SetTip(text, value);
        Grid.SetColumn(text, 1); grid.Children.Add(text); return grid;
    }

    /// <summary>The Overview and Assertions lists page like the history lists: 100 a page, first / previous / next / last, jump, page size.</summary>
    private Control CreatePaginationBar(bool overview)
    {
        // Overview is rebuilt on every refresh, so it gets a fresh bar each time (a control cannot have two parents); the pager keeps the page.
        var pager = overview ? _overviewPager : _assertionPager;
        pager.Changed -= overview ? OverviewPageChanged : AssertionPageChanged;
        pager.Changed += overview ? OverviewPageChanged : AssertionPageChanged;
        var bar = new PagerBar(pager);
        if (!overview) _assertionBar = bar;
        return bar;
    }

    private void OverviewPageChanged() => RefreshDashboardDetails(true);
    private void AssertionPageChanged() => RefreshAssertionList();

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
        _configurationPanel.Children.Add(DashboardStyle.Action("Pipeline help", () => { if (_hostWindow is Window helpOwner) ContextHelpWindow.Show(helpOwner, CiHelp.Title, CiHelp.Text(item.Name)); }));
        _configurationPanel.Children.Add(KeyValue("Data IDs", string.IsNullOrWhiteSpace(item.DataIdSelection) ? "All data rows" : item.DataIdSelection));
        _configurationPanel.Children.Add(DashboardStyle.Text("Workbook actions are available directly on the instance row: Add step, Edit step, Edit dataset, and Open workbook. The legacy editor is available in Handcraft.", 12, false, DashboardStyle.Muted));
    }

    /// <summary>A new run starts clean: the earlier run's issues go. Validation issues describe the workbook, not a run, so they stay until it is validated again.</summary>
    internal static void ClearRunIssues(InstanceInfo instance)
        => instance.Issues.RemoveAll(x => !x.Category.Equals("Validation", StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces an instance's earlier validation issues with a fresh result. Issues from runs stay as they are.</summary>
    internal static void ApplyValidationIssues(InstanceInfo instance, InstanceValidationResult result)
    {
        instance.Issues.RemoveAll(x => x.Category.Equals("Validation", StringComparison.OrdinalIgnoreCase));
        // Only real problems are kept: the "PASS … No action required" and INFO lines are for the validation window, and as issues they showed up as errors.
        foreach (var issue in result.Issues.Where(i => i.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase) || i.Severity.Equals("WARNING", StringComparison.OrdinalIgnoreCase)))
            instance.Issues.Add(new RunIssueInfo { Severity = issue.Severity.Equals("WARNING", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Error", Category = "Validation", Message = $"{issue.Location}: {issue.Problem} Fix: {issue.Fix}", Testcase = issue.Location, OccurredAt = DateTimeOffset.Now });
    }

    /// <summary>After a save in the editor: validates just that instance again (no requests are sent), replaces its validation issues and refreshes the Validate all button count.</summary>
    internal async Task RevalidateAfterSaveAsync(string workbookPath)
    {
        var full = Path.GetFullPath(workbookPath);
        var instance = _instances.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.WorkbookPath) && Path.GetFullPath(x.WorkbookPath) == full);
        if (instance is null) return;
        try
        {
            ReloadWorkbookCatalog(instance);   // new testcases / steps / data rows saved in the editor
            var environments = _config.Environments.ToList();
            var result = await Task.Run(() => GlobalValidationService.ValidateInstance(instance, environments));
            ApplyValidationIssues(instance, result);
            var errors = _instances.Sum(x => x.Issues.Count(i => i.Category.Equals("Validation", StringComparison.OrdinalIgnoreCase) && i.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)));
            var warnings = _instances.Sum(x => x.Issues.Count(i => i.Category.Equals("Validation", StringComparison.OrdinalIgnoreCase) && i.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase)));
            SetValidationButton(errors > 0 ? "ERROR" : warnings > 0 ? "WARNING" : "PASS", errors, warnings);
            RefreshDashboardDetails(force: true);
            UiLog($"Revalidated {instance.Name} after save. errors={result.Issues.Count(i => i.Severity == "ERROR")}; warnings={result.Issues.Count(i => i.Severity == "WARNING")}");
        }
        catch (Exception ex) { UiLog($"Revalidation after save failed for {instance.Name}: {ex.Message}"); }
    }

    private void SetValidationButton(string status, int errors, int warnings) { }

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
            _logPager.Slice(lines); _logBar?.Update();
            return;
        }
        foreach (var (text, severity) in _logPager.Slice(lines))
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
        _logBar?.Update();
    }
    private void OpenDataEditor()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; return; }
        // One editor at a time: a second click brings the open one forward instead of stacking another window.
        if (_hostWindow is DataEditor.DataEditorWindow { IsVisible: true } open)
        {
            open.Activate();
            if (open.Shell.Instances.FirstOrDefault(n => string.Equals(System.IO.Path.GetFullPath(n.Path), System.IO.Path.GetFullPath(_selectedInstance.WorkbookPath), StringComparison.OrdinalIgnoreCase)) is { } node)
                open.Shell.SelectedNode = node;
            return;
        }
        CreateDataEditor()?.Show(Host);
    }

    /// <summary>The data editor as the application's only window: the dashboard is built but never shown, and still runs tests and tracks results for the editor.</summary>
    internal DataEditor.DataEditorWindow? CreateStandaloneEditor()
    {
        // Open the editor even with no instances yet (empty Instances folder), so the
        // user can create one from it instead of landing on a bare leftover window.
        _selectedInstance ??= _instances.FirstOrDefault();
        return CreateDataEditor();
    }

    private Window? _hostWindow;

    /// <summary>The window that owns dialogs opened from the dashboard's panels: the data editor while it is open (the dashboard itself may be hidden, and a hidden window cannot own a window).</summary>
    internal Window Host => _hostWindow is { IsVisible: true } ? _hostWindow : this;

    private DataEditor.DataEditorWindow? CreateDataEditor()
    {
        try
        {
            var editor = DataEditor.DataEditorWindow.Open(_instances.Select(x => (x.Name, x.WorkbookPath)), _selectedInstance?.WorkbookPath);
            editor.Shell.RunHandler = RunFromEditorAsync;
            editor.Shell.EnvironmentSettings = new EditorEnvironmentSettings(this);
            editor.Shell.HistoryHandler = LaunchHistoryWorkspace;
            editor.Shell.RerunContextProvider = name => _instances.FirstOrDefault(x => x.Name == name) is { } target ? new DataEditor.RerunContext(target.HistoryFolder, target.EnvironmentOptions.ToList(), target.HistoryStorage) : null;
            editor.Shell.WorkbookSaved += path => Dispatcher.UIThread.Post(() => _ = RevalidateAfterSaveAsync(path));
            editor.Shell.RunDetails = new EditorRunDetails(this);
            _hostWindow = editor;
            editor.Shell.CreateTestcaseHandler = (name, draft) => { if (_instances.FirstOrDefault(x => x.Name == name) is { } target) CreateTestcaseForInstance(target, draft); };
            editor.Closed += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(editor.Shell.LastBackupPath)) return;
                RefreshInstances();
                _details.Text = $"Workbook saved. Backup created: {Path.GetFileName(editor.Shell.LastBackupPath)}";
            };
            return editor;
        }
        catch (Exception ex)
        {
            _details.Text = $"Cannot open the data editor: {ex.Message}";
            UiLog($"Cannot open the data editor: {ex}");
            return null;
        }
    }

    private static TextBlock Wrapped(TextBlock text) { text.TextWrapping = TextWrapping.Wrap; return text; }
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
        if (ActiveScope(item) is { } summaryScope) _runSummaryPanel.Children.Add(Wrapped(DashboardStyle.Text($"Preview for {ScopeText(summaryScope)}: what its ▶ button would run.", 12, true)));
        _runSummaryPanel.Children.Add(Wrapped(DashboardStyle.Text("Selection preview. dontrun skips individual steps inside a row; it does not automatically remove the whole row.", 11, false, DashboardStyle.Muted)));

        var amber = DashboardStyle.Brush("#FFB36B"); var yellow = DashboardStyle.Brush("#F6C76B");
        var passedRequests = item.Requests.Where(request => request.Result.Equals("PASS", StringComparison.OrdinalIgnoreCase)).ToList();
        var failedRequests = item.Requests.Where(request => request.Result.Equals("FAIL", StringComparison.OrdinalIgnoreCase) && !IsUnexpectedRequest(request)).ToList();
        var unexpectedRequests = item.Requests.Where(IsUnexpectedRequest).ToList();

        // the plan: what the selection would run, as tiles that wrap to the width of the pane
        _runSummaryPanel.Children.Add(DashboardStyle.Section("Plan", "what the current selection would run"));
        var plan = new WrapPanel();
        plan.Children.Add(DashboardStyle.Tile("Testcases", summary.Testcases.ToString("N0"), DashboardStyle.Blue));
        plan.Children.Add(DashboardStyle.Tile("Steps", summary.SelectedSteps.ToString("N0"), DashboardStyle.Blue));
        plan.Children.Add(DashboardStyle.Tile("Rows", summary.RowsSelected.ToString("N0"), DashboardStyle.Blue));
        plan.Children.Add(DashboardStyle.Tile("Environments", summary.Environments.ToString("N0"), DashboardStyle.Blue));
        plan.Children.Add(DashboardStyle.Tile("Iterations", summary.Iterations.ToString("N0"), DashboardStyle.Blue));
        plan.Children.Add(DashboardStyle.Tile("Max concurrent rows", summary.MaxParallelRows.ToString("N0"), DashboardStyle.Blue));
        plan.Children.Add(DashboardStyle.Tile("Step executions", summary.PlannedSteps.ToString("N0"), DashboardStyle.Green));
        plan.Children.Add(DashboardStyle.Tile("Skipped (dontrun)", summary.SkippedSteps.ToString("N0"), summary.SkippedSteps > 0 ? yellow : null));
        plan.Children.Add(DashboardStyle.Tile("Rows fully skipped", summary.RowsFullySkipped.ToString("N0"), summary.RowsFullySkipped > 0 ? DashboardStyle.Red : null));
        _runSummaryPanel.Children.Add(plan);

        // the outcome of the run: one bar and one small table (what, environments, testcases, steps)
        _runSummaryPanel.Children.Add(DashboardStyle.Section("Result", "environments, testcases and steps by outcome"));
        var outcome = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64,64,64") };
        for (var r = 0; r < 4; r++) outcome.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        void Cell(int row, int column, string text, IBrush? color, bool bold, bool right = true)
        {
            var cell = DashboardStyle.Text(text, 12, bold, color); cell.Margin = new Thickness(0, 3); if (right) cell.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); outcome.Children.Add(cell);
        }
        Cell(0, 1, "Envs", DashboardStyle.Muted, false); Cell(0, 2, "Testcases", DashboardStyle.Muted, false); Cell(0, 3, "Steps", DashboardStyle.Muted, false);
        void Line(int row, string name, IBrush color, IReadOnlyList<DashboardRequest> list)
        {
            Cell(row, 0, $"●  {name}", color, true, false);
            Cell(row, 1, list.Select(x => x.Environment).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0"), list.Count > 0 ? color : DashboardStyle.Muted, list.Count > 0);
            Cell(row, 2, list.Select(x => x.Testcase).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0"), list.Count > 0 ? color : DashboardStyle.Muted, list.Count > 0);
            Cell(row, 3, list.Count.ToString("N0"), list.Count > 0 ? color : DashboardStyle.Muted, list.Count > 0);
        }
        Line(1, "Passed", DashboardStyle.Green, passedRequests); Line(2, "Failed", DashboardStyle.Red, failedRequests); Line(3, "Unexpected error", amber, unexpectedRequests);
        _runSummaryPanel.Children.Add(DashboardStyle.Card(new StackPanel { Spacing = 10, Children =
        {
            DashboardStyle.ProportionBar((passedRequests.Count, DashboardStyle.Green), (failedRequests.Count, DashboardStyle.Red), (unexpectedRequests.Count, amber)),
            outcome
        }}, new Thickness(12, 10)));

        Control ResultColumn(string title, IReadOnlyList<DashboardRequest> requests, IBrush color, string emptyText)
        {
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(DashboardStyle.Text(title, 13, true, color));
            if (requests.Count == 0)
                content.Children.Add(Wrapped(DashboardStyle.Text(emptyText, 11, false, DashboardStyle.Muted)));
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
        _runSummaryPanel.Children.Add(DashboardStyle.Section("Details"));
        _runSummaryPanel.Children.Add(resultColumns);

        var warnings = new List<string>();
        if (summary.RowsWithDontrun > 0) warnings.Add($"{summary.RowsWithDontrun:N0} row(s) contain dontrun steps; {summary.SkippedSteps:N0} step execution(s) will be skipped.");
        if (summary.RowsFullySkipped > 0) warnings.Add($"{summary.RowsFullySkipped:N0} row(s) have every selected step listed in dontrun and will produce no step executions.");
        if (summary.RowsWithoutAssertions > 0) warnings.Add($"{summary.RowsWithoutAssertions:N0} selected row(s) have no response assertions.");
        foreach (var warning in warnings) _runSummaryPanel.Children.Add(DashboardStyle.Text($"⚠ {warning}", 11, false, DashboardStyle.Brush("#F6C76B")));
        if (summary.PlannedExecutions == 0) _runSummaryPanel.Children.Add(DashboardStyle.Text("✗ No executable step executions are planned for the current selection.", 11, true, DashboardStyle.Red));
        else if (warnings.Count == 0) _runSummaryPanel.Children.Add(DashboardStyle.Text("✓ Selection is ready: every selected row has at least one executable step.", 11, true, DashboardStyle.Green));

        _runSummaryPanel.Children.Add(DashboardStyle.Section("Status summary", "Expand a status to see its environments, testcase names, step names, data IDs, counts, and errors."));
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
        var scope = ActiveScope(item);
        var testcaseSelection = scope is null ? item.TestcaseSelection : scope.TestcaseIndex.ToString();
        var stepSelection = scope is null ? item.StepSelectionJson : scope.Step is null ? "" : JsonSerializer.Serialize(new Dictionary<string, string[]> { [scope.TestcaseIndex.ToString()] = [scope.Step] });
        var draft = new RunSelectionDraft(item.TestcaseOptions, item.EnvironmentOptions, testcaseSelection, stepSelection, item.EnvironmentSelection, item.EnvironmentSelectionJson);
        var selectedCases = draft.Cases.ToHashSet();
        var selectedSteps = draft.Steps.ToDictionary(x => x.Key, x => x.Value, EqualityComparer<int>.Default);
        var environments = draft.AvailableEnvironments.Where(x => draft.Environments.Contains(x)).Select(x => string.IsNullOrWhiteSpace(x) ? "Default" : x).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var dataIds = DataIdSelectionCodec.Parse(scope is null ? item.DataIdSelection : "");
        using var workbook = new XLWorkbook(item.WorkbookPath);
        var config = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The workbook has no config sheet.");
        var request = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The workbook has no request sheet.");
        var response = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("response", StringComparison.OrdinalIgnoreCase));
        var configHeaders = config.FirstRowUsed()?.CellsUsed(LoadTestingTool.Excel.HeaderCells.HasText).ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? throw new InvalidDataException("The config sheet has no header row.");
        var requestHeaders = request.FirstRowUsed()?.CellsUsed(LoadTestingTool.Excel.HeaderCells.HasText).ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? throw new InvalidDataException("The request sheet has no header row.");
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
        var responseHeaders = response?.FirstRowUsed()?.CellsUsed(LoadTestingTool.Excel.HeaderCells.HasText).ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
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
            _overviewStepEnvironment.SetOptions(item.Requests.Select(x => string.IsNullOrWhiteSpace(x.Environment) ? "default" : x.Environment));
            _overviewStepTestcase.SetOptions(item.Requests.Select(x => x.Testcase));
            _overviewStepName.SetOptions(item.Requests.Select(x => x.StepName));
        }
        finally { _overviewStepFilterRefreshing = false; }
    }

    // ---- Scope: the run views follow the selection in the data editor (one testcase or one step) ----
    private DataEditor.RunScope? _scope;
    private string _scopeInstance = string.Empty;

    private DataEditor.RunScope? ActiveScope(InstanceInfo? item) => item is not null && _scope is not null && _scopeInstance == item.Name ? _scope : null;

    private static bool InScope(DataEditor.RunScope? scope, string testcase, string step)
        => scope is null || (testcase.Equals(scope.Testcase, StringComparison.OrdinalIgnoreCase) && (scope.Step is null || step.Equals(scope.Step, StringComparison.OrdinalIgnoreCase)));

    private List<DashboardRequest> ScopedRequests(InstanceInfo item)
    {
        ApplyBugTags(item);   // the workbook's bug tags as they are now, not only as they were when the run started
        return ActiveScope(item) is { } scope ? item.Requests.Where(r => InScope(scope, r.Testcase, r.StepName)).ToList() : item.Requests;
    }

    private List<AssertionInfo> ScopedAssertions(InstanceInfo item)
    {
        ApplyBugTags(item);
        return ActiveScope(item) is { } scope ? item.Assertions.Where(a => InScope(scope, a.Testcase, a.StepName)).ToList() : item.Assertions;
    }

    private static string ScopeText(DataEditor.RunScope scope) => scope.Step is null ? $"testcase {scope.Testcase}" : $"step {scope.Step} of {scope.Testcase}";

    /// <summary>"Run:" with every saved run of the instance by date and time; choosing one shows its status below.</summary>
    private Control RunPicker(InstanceInfo item)
    {
        var runs = SavedRuns(item);
        _overviewRunRefreshing = true;
        var labels = runs.Select(r => r.Label).ToList();
        if (_overviewRun.ItemsSource is not List<string> shown || !shown.SequenceEqual(labels)) _overviewRun.ItemsSource = labels;   // a refresh must not close an open list
        _overviewRun.SelectedIndex = runs.ToList().FindIndex(r => r.RunId.Equals(item.CurrentRunId, StringComparison.OrdinalIgnoreCase));
        _overviewRun.IsEnabled = runs.Count > 0 && item.Process is not { HasExited: false };
        _overviewRunRefreshing = false;
        _overviewRun.SelectionChanged -= OnOverviewRunChosen;
        _overviewRun.SelectionChanged += OnOverviewRunChosen;
        var newest = runs.FirstOrDefault();
        var older = newest is not null && !newest.RunId.Equals(item.CurrentRunId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.CurrentRunId);
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        line.Children.Add(DashboardStyle.Text("Run", color: DashboardStyle.Muted));
        line.Children.Add(_overviewRun);
        if (older) line.Children.Add(DashboardStyle.Action("Show the latest run", () => { if (newest is not null && LoadSavedRun(item, newest)) RefreshDashboardDetails(true); }));
        if (older) line.Children.Add(DashboardStyle.Text("Showing an earlier run, not the latest.", color: DashboardStyle.Muted));
        return line;
    }

    private void OnOverviewRunChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_overviewRunRefreshing || _selectedInstance is not { } item) return;
        var runs = SavedRuns(item);
        if (_overviewRun.SelectedIndex < 0 || _overviewRun.SelectedIndex >= runs.Count) return;
        var run = runs[_overviewRun.SelectedIndex];
        if (run.RunId.Equals(item.CurrentRunId, StringComparison.OrdinalIgnoreCase)) return;
        if (LoadSavedRun(item, run)) RefreshDashboardDetails(true);
    }

    private string StepSearchText(DashboardRequest request)
        => $"{request.StepType} {request.StepName} {request.Testcase} {request.DataId} {request.Environment} {request.Result} {request.FailureCategory} {request.Error} {AssertionTextOf(request)}";

    // What the step's assertions say (path, verb, expected, actual, message), so the show / exclude boxes can look for a value
    private Dictionary<string, string> _assertionTextByStep = new(StringComparer.OrdinalIgnoreCase);
    private object? _assertionTextFor;
    private int _assertionTextCount = -1;

    private static string StepTextKey(string testcase, string dataId, string environment, string step) => $"{testcase}\u001F{dataId}\u001F{environment}\u001F{step}";

    private string AssertionTextOf(DashboardRequest request)
    {
        if (_selectedInstance is not { } item) return string.Empty;
        if (!ReferenceEquals(_assertionTextFor, item.Assertions) || _assertionTextCount != item.Assertions.Count)
        {
            _assertionTextByStep = item.Assertions.GroupBy(a => StepTextKey(a.Testcase, a.DataId, a.Environment, a.StepName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(a => $"{a.ResponsePath} {a.AssertionVerb} {a.ExpectedValue} {a.ActualValue} {a.FailureMessage}")), StringComparer.OrdinalIgnoreCase);
            _assertionTextFor = item.Assertions; _assertionTextCount = item.Assertions.Count;
        }
        return _assertionTextByStep.GetValueOrDefault(StepTextKey(request.Testcase, request.DataId, request.Environment, request.StepName), string.Empty);
    }

    /// <summary>True when the step is hidden by the exclude box: it contains every term written there.</summary>
    private bool IsExcludedStep(DashboardRequest request)
        => !string.IsNullOrWhiteSpace(_overviewStepExclude.Text) && MatchesTerms(StepSearchText(request), _overviewStepExclude.Text);

    private bool MatchesOverviewStep(DashboardRequest request, bool applyExclude = true, bool forComparison = false)
    {
        if (applyExclude && IsExcludedStep(request)) return false;
        if (!forComparison)
        {
            var actualStatus = IsUnexpectedRequest(request) ? "Unexpected error" : request.Result.Equals("PASS", StringComparison.OrdinalIgnoreCase) ? "Passed" : request.Result.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase) ? "Skipped" : "Failed";
            if (!_overviewStepStatus.Matches(actualStatus)) return false;
            if (!_overviewStepEnvironment.Matches(string.IsNullOrWhiteSpace(request.Environment) ? "default" : request.Environment)) return false;
            if (!_overviewStepName.Matches(request.StepName)) return false;
        }
        if (!_overviewStepTestcase.Matches(request.Testcase)) return false;
        return MatchesTerms(StepSearchText(request), _overviewStepFilter.Text);
    }

    /// <summary>The run's log for this step's testcase and environment, written to a temporary file for the details window (which removes it), or null when the database has none.</summary>
    internal static string? TryLogFromDatabase(InstanceInfo item, DashboardRequest request, out string? sourceLabel)
    {
        sourceLabel = null;
        if (item.HistoryStorage == HistoryStorage.Files) return null;
        try
        {
            var store = new SqliteHistoryStore(item.HistoryFolder);
            if (!store.Exists) return null;
            var runId = DataEditor.ShownRunChoice.Get(item.ResultsFolder);
            if (string.IsNullOrWhiteSpace(runId)) runId = string.IsNullOrWhiteSpace(item.CurrentRunId) ? ArtifactCatalog.LatestRunId(item) : item.CurrentRunId;
            if (string.IsNullOrWhiteSpace(runId)) runId = store.ListRuns(0, 1).FirstOrDefault()?.RunId;
            if (string.IsNullOrWhiteSpace(runId) || store.LoadLogFile(runId, request.Testcase, request.Environment) is not { } stored) return null;
            var folder = Path.Combine(Path.GetTempPath(), "stepdetails_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, stored.Name);
            File.WriteAllText(path, stored.Text);
            sourceLabel = $"history database {store.Path_} (run {runId}, {stored.Name})";
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { return null; }
    }

    private void LaunchStepDetails(DashboardRequest request)
    {
        if (_selectedInstance is null) return;
        var item = _selectedInstance;
        // a finished run keeps its logs in the history database (when the instance stores history there): they win over a file, and are the only source once the files are gone
        string? sourceLabel = null;
        var log = TryLogFromDatabase(item, request, out sourceLabel);
        log ??= FindInternalLogs(item)
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
            foreach (var value in new[] { log, request.Testcase, request.DataId, request.StepName, string.IsNullOrWhiteSpace(request.Environment) ? "default" : request.Environment, request.StepType }.Concat(sourceLabel is null ? [] : [sourceLabel])) start.ArgumentList.Add(value);
            var process = Process.Start(start) ?? throw new InvalidOperationException("The step details process could not be started.");
            UiLog($"Step details process started. processId={process.Id}; testcase={request.Testcase}; dataId={request.DataId}; step={request.StepName}; environment={request.Environment}; log={log}");
        }
        catch (Exception ex) { _details.Text = $"Could not open step details: {ex.Message}"; UiLog($"Step details process failed: {ex}"); }
    }

    private void RefreshDashboardDetails(bool force = false)
    {
        var item = _selectedInstance;
        var signature = item is null ? "empty" : $"{ActiveScope(item)}|{item.Name}|{item.Status}|{item.Requests.Count}|{item.Assertions.Count}|{item.Issues.Count}|{item.LastEvent}|{item.P95Ms}|{item.StartedAt}|{item.CompletedAt}|{item.TestcaseSelection}|{item.EnvironmentSelection}|{item.RowsExecutionMode}|{item.RunEnvironmentsParallel}|{item.StepSelectionJson}";
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
        Detach(_overviewStepName);
        Detach(_overviewStepFilter);
        Detach(_overviewStepExclude);
        _overviewPanel.Children.Clear();
        Detach(_overviewRun);
        _overviewPanel.Children.Add(new WrapPanel { Children = { _overviewFilter, DashboardStyle.Action("Copy overview", () => _ = CopyOverviewAsync()) } });
        if (item is not null) _overviewPanel.Children.Add(RunPicker(item));
        if (item is null)
        {
            _overviewPanel.Children.Add(DashboardStyle.Text("Select an instance to inspect its run.", color: DashboardStyle.Muted));
            RefreshRunSummary(null);
            _environmentCompareItems.Children.Clear();
            RenderUnifiedLog();
            _artifactPanel.Children.Clear(); return;
        }
        RefreshRunSummary(item);
        RefreshOverviewStepFilters(item);
        var requests = ScopedRequests(item);
        var assertions = ScopedAssertions(item);
        RefreshEnvironmentCompare(requests, assertions);
        var scoped = ActiveScope(item) is not null;
        var issues = scoped ? item.Issues.Where(i => InScope(ActiveScope(item), i.Testcase, i.StepName)).ToList() : item.Issues;
        if (ActiveScope(item) is { } active)
        {
            _overviewPanel.Children.Add(DashboardStyle.Text($"Showing only {ScopeText(active)}", 12, true));
            if (requests.Count == 0)
                _overviewPanel.Children.Add(DashboardStyle.Text(item.Requests.Count > 0 ? "Not run yet — this was not part of the last run of the instance." : "No results yet — run it to see them here.", 12, false, DashboardStyle.Muted));
        }
        void AddOverview(Control control, string searchable) { if (MatchesTerms(searchable, _overviewFilter.Text)) _overviewPanel.Children.Add(control); }
        // header: the state of the run and when it ran, then the settings as small labels, then the figures as tiles and the assertions as one bar
        var headline = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var stateBadge = DashboardStyle.Badge(item.Status); stateBadge.Margin = new Thickness(0, 0, 10, 6); headline.Children.Add(stateBadge);
        var when = DashboardStyle.Text($"{(item.StartedAt is { } startedAt ? $"Started {startedAt:HH:mm:ss}" : "Not started")}  ·  Duration {FormatRunDuration(item)}", 12, false, DashboardStyle.Muted); when.Margin = new Thickness(0, 0, 0, 6);
        headline.Children.Add(when);
        AddOverview(headline, $"{item.Status} {item.StartedAt}");
        var settings = new WrapPanel();
        settings.Children.Add(DashboardStyle.Pill("Environment", string.IsNullOrWhiteSpace(item.EnvironmentSelection) ? "All global environments" : item.EnvironmentSelection));
        settings.Children.Add(DashboardStyle.Pill("Rows", item.RowsExecutionMode == "sequential" ? "Loop" : "Parallel"));
        settings.Children.Add(DashboardStyle.Pill("Environments", item.RunEnvironmentsParallel ? "Parallel" : "Sequential"));
        AddOverview(settings, $"Environment {item.EnvironmentSelection} {item.RowsExecutionMode} {(item.RunEnvironmentsParallel ? "Parallel" : "Sequential")} Mode order");
        var average = (scoped ? null : item.AverageMs) ?? (requests.Count == 0 ? (double?)null : requests.Average(r => r.DurationMs));
        var figures = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, 0, 0) };
        figures.Children.Add(DashboardStyle.Tile("Requests", ((scoped ? null : item.CompletedRequests) ?? requests.Count).ToString("N0"), DashboardStyle.Blue, width: double.NaN));
        figures.Children.Add(DashboardStyle.Tile("Avg. latency", average is null ? "—" : $"{average:F0} ms", width: double.NaN));
        figures.Children.Add(DashboardStyle.Tile("p95 latency", scoped ? "—" : item.P95Ms is null ? "…" : $"{item.P95Ms} ms", width: double.NaN));
        figures.Children.Add(DashboardStyle.Tile("Throughput", scoped ? "—" : item.RequestsPerSecond is null ? "…" : $"{item.RequestsPerSecond:F1}/s", width: double.NaN));
        AddOverview(figures, $"Requests Avg. latency p95 latency Throughput {item.CompletedRequests} {requests.Count} {average} {item.P95Ms} {item.RequestsPerSecond}");
        var passedAssertions = assertions.Count(a => a.Result == "PASS"); var failedAssertions = assertions.Count(a => a.Result == "FAIL"); var bugAssertions = assertions.Count(a => a.Result == "KNOWN_BUG");
        var assertionLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children =
        {
            DashboardStyle.Text($"{passedAssertions:N0} passed", 12, true, DashboardStyle.Green),
            DashboardStyle.Text($"{failedAssertions:N0} failed", 12, true, failedAssertions > 0 ? DashboardStyle.Red : DashboardStyle.Muted)
        }};
        if (bugAssertions > 0) assertionLine.Children.Add(DashboardStyle.Text($"{bugAssertions:N0} known bugs", 12, true, DashboardStyle.Brush("#F6C76B")));
        var assertionCard = DashboardStyle.Card(new StackPanel { Spacing = 8, Children =
        {
            DashboardStyle.Text("Assertions", 10, false, DashboardStyle.Muted),
            DashboardStyle.ProportionBar((passedAssertions, DashboardStyle.Green), (failedAssertions, DashboardStyle.Red), (bugAssertions, DashboardStyle.Brush("#F6C76B"))),
            assertionLine
        }}, new Thickness(12, 10));
        assertionCard.Margin = new Thickness(0, 0, 0, 4);
        AddOverview(assertionCard, $"Assertions {assertions.Count} pass fail known bugs");
        if (issues.Count > 0)
        {
            _overviewPanel.Children.Add(DashboardStyle.Section("Run issues", $"{issues.Count(i => i.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))} error(s) / {issues.Count(i => i.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase))} warning(s)"));
            foreach (var issue in issues.Where(i => MatchesTerms($"{i.Severity} {i.Category} {i.Environment} {i.Testcase} {i.DataId} {i.StepName} {i.Message}", _overviewFilter.Text)).OrderByDescending(i => i.OccurredAt))
            {
                var color = issue.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? DashboardStyle.Brush("#FFB36B") : DashboardStyle.Red;
                var scope = string.Join(" / ", new[] { issue.Environment, issue.Testcase, issue.DataId, issue.StepName }.Where(x => !string.IsNullOrWhiteSpace(x)));
                var content = new StackPanel { Spacing = 3, Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DashboardStyle.Badge(issue.Severity), DashboardStyle.Text($"{issue.Category}{(string.IsNullOrWhiteSpace(scope) ? "" : $" · {scope}")}", 11, true) } },
                    DashboardStyle.Text(issue.Message, 11, false, color),
                    DashboardStyle.Text(issue.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? $"{issue.OccurredAt:HH:mm:ss} · Warning; it does not affect failure totals." : $"{issue.OccurredAt:HH:mm:ss} · Error reported by the runner; the run may be incomplete.", 10, false, DashboardStyle.Muted)
                }};
                _overviewPanel.Children.Add(DashboardStyle.Card(content, new Thickness(8)));
            }
        }
        _overviewPanel.Children.Add(DashboardStyle.Section("Step execution timeline", $"Total duration {FormatRunDuration(item)}"));
        var stepFilterBar = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN, ItemHeight = double.NaN, Children =
        {
            _overviewStepStatus, _overviewStepEnvironment, _overviewStepTestcase, _overviewStepName, _overviewStepFilter, _overviewStepExclude
        }};
        foreach (var control in stepFilterBar.Children) control.Margin = new Thickness(0, 0, 8, 6);
        _overviewPanel.Children.Add(stepFilterBar);
        if (OverviewChips() is { } overviewChips) _overviewPanel.Children.Add(overviewChips);
        var hiddenByExclude = string.IsNullOrWhiteSpace(_overviewStepExclude.Text) ? 0 : requests.Count(r => IsExcludedStep(r) && MatchesOverviewStep(r, applyExclude: false));
        if (hiddenByExclude > 0) _overviewPanel.Children.Add(DashboardStyle.Text($"{hiddenByExclude} {(hiddenByExclude == 1 ? "step is" : "steps are")} hidden by the exclude box. Clear it to see {(hiddenByExclude == 1 ? "it" : "them")} again.", 11, false, DashboardStyle.Muted));
        _overviewPanel.Children.Add(Wrapped(DashboardStyle.Text("Filters apply to completed steps only. Use & between terms to require all terms; matching text remains visible in the details viewer.", 10, false, DashboardStyle.Muted)));
        var stepPositions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stepTotals = requests
            .GroupBy(request => $"{request.Testcase}\u001F{request.DataId}\u001F{request.Environment}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var filteredRequests = DashboardRequest.InWorkbookOrder(requests.Where(r => MatchesOverviewStep(r)));
        var overviewSignature = $"{_overviewStepFilter.Text}|{_overviewStepExclude.Text}";
        if (overviewSignature != _overviewFilterSignature) { _overviewFilterSignature = overviewSignature; _overviewPager.Reset(); }
        var pageRequests = _overviewPager.Slice(filteredRequests);
        foreach (var request in requests)
        {
            var rowKey = $"{request.Testcase}\u001F{request.DataId}\u001F{request.Environment}";
            stepPositions[rowKey] = stepPositions.GetValueOrDefault(rowKey) + 1;
        }
        var overviewPaging = CreatePaginationBar(true);
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
            var context = DashboardStyle.Text($"{request.Testcase} / Row {request.DataId} · {request.Environment} · Step {position}/{stepTotals.GetValueOrDefault(rowKey, position)}{(request.LoopRound.Length > 0 ? $" · {request.LoopRound}" : "")} · {request.DurationMs} ms", 10, false, DashboardStyle.Muted);
            context.TextWrapping = TextWrapping.Wrap;
            var line = new StackPanel { Spacing = 3, Children = { top, context } };
            var hasCategory = !string.IsNullOrWhiteSpace(request.FailureCategory) && request.FailureCategory != "None";
            if (hasCategory || !string.IsNullOrWhiteSpace(request.Error) || request.Result.Equals("FAIL", StringComparison.OrdinalIgnoreCase))
            {
                var text = !string.IsNullOrWhiteSpace(request.Error) ? request.Error : FailedAssertionText(item, request);
                var error = DashboardStyle.Text(hasCategory ? $"{request.FailureCategory}: {text}" : text, 10, false, DashboardStyle.Red);
                error.TextWrapping = TextWrapping.Wrap;
                line.Children.Add(error);
            }
            _overviewPanel.Children.Add(DashboardStyle.Card(line, new Thickness(8)));
        }
        _overviewPanel.Children.Add(overviewPaging);
        if (requests.Count > 0)
        {
            _overviewPanel.Children.Add(DashboardStyle.Section("Latency trend", "last 30 requests"));
            _overviewPanel.Children.Add(new LatencyChart(requests.TakeLast(30).Select(r => (double)r.DurationMs).ToArray()) { Height = 90 });
            _overviewPanel.Children.Add(DashboardStyle.Text($"Range: 0–{requests.TakeLast(30).Max(r => r.DurationMs)} ms / observed completion order", 10, false, DashboardStyle.Muted));
        }
        RenderUnifiedLog();
        RefreshAssertionList(); RefreshArtifacts();
    }
    /// <summary>The failed assertions of a request, as text, for failures that carry no error message of their own.</summary>
    private static string FailedAssertionText(InstanceInfo item, DashboardRequest request)
    {
        var failed = item.Assertions.Where(a => a.Result == "FAIL" && a.StepName.Equals(request.StepName, StringComparison.OrdinalIgnoreCase)
            && a.Testcase.Equals(request.Testcase, StringComparison.OrdinalIgnoreCase) && a.DataId.Equals(request.DataId, StringComparison.OrdinalIgnoreCase)
            && a.Environment.Equals(request.Environment, StringComparison.OrdinalIgnoreCase)).ToList();
        if (failed.Count == 0) return "The step failed without an error message. Open View details for the request and response.";
        var shown = failed.Take(3).Select(a => !string.IsNullOrWhiteSpace(a.FailureMessage) ? a.FailureMessage : $"{a.ResponsePath} expected {a.ExpectedValue} but was {a.ActualValue}");
        return string.Join(" | ", shown) + (failed.Count > 3 ? $" | +{failed.Count - 3} more (see Assertions)" : "");
    }

    private static bool IsUnexpectedRequest(DashboardRequest request)
        => !string.IsNullOrWhiteSpace(request.Error) || (!string.IsNullOrWhiteSpace(request.FailureCategory) && request.FailureCategory is not "None" and not "Assertion" and not "HttpStatus");
    /// <summary>Old assertion tools kept out of the way: switched off, the dataset editor does their work (Compare, failed-cell list, assign actual).</summary>
    private Control LegacyAssertionTools()
    {
        var buttons = new WrapPanel { Tag = "legacy-assertion-tools" };
        foreach (var name in new[] { "Apply actual values", "Select failed", "Clear" })
        {
            var button = DashboardStyle.Action(name + " (legacy)", () => { });
            button.IsEnabled = false;
            buttons.Children.Add(button);
        }
        return new StackPanel { Spacing = 4, Children = { DashboardStyle.Text("Legacy assertion functions (disabled)", 13, true), buttons } };
    }

    private readonly CalendarDatePicker _importFrom = new() { Watermark = "From (optional)", Width = 160 };
    private readonly CalendarDatePicker _importTo = new() { Watermark = "To (optional)", Width = 160 };
    private readonly TextBox _importFolder = new() { MinWidth = 340, Watermark = "Folder with old history workbooks" };
    private readonly ProgressBar _importBar = new() { Minimum = 0, Maximum = 1, Height = 6, IsVisible = false };
    private readonly TextBlock _importText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = DashboardStyle.Muted };
    private bool _importing;
    private readonly TextBox _exchangeFolder = new() { Watermark = "Folder for run files (to hand a run to someone, or to bring one in)", Tag = "history-exchange-folder" };
    // what was typed / said is kept per instance (the controls are rebuilt for whichever instance is selected)
    private readonly Dictionary<string, string> _exchangeFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _upkeepMessages = new(StringComparer.OrdinalIgnoreCase);
    private string? _exchangeOwner;

    /// <summary>The history database of the selected instance: what it holds, and the functions that fill it from old history workbooks and compare it with them.</summary>
    private Control HistoryDatabaseSection(InstanceInfo item)
    {
        // the controls are kept between refreshes (an import keeps reporting into them), so they leave the previous section before they join the new one
        foreach (var kept in new Control[] { _importFolder, _importFrom, _importTo, _importBar, _importText }) if (kept.Parent is Panel owner) owner.Children.Remove(kept);
        var store = new SqliteHistoryStore(item.HistoryFolder);
        var storage = item.HistoryStorage switch { HistoryStorage.Both => "database and workbooks", HistoryStorage.Database => "database only", _ => "history workbooks only" };
        string held;
        try { var s = store.Summary(); held = store.Exists ? $"{s.Runs} runs, {s.Rows} rows{(s.Newest is { } n ? $", newest {n.ToLocalTime():yyyy-MM-dd HH:mm}" : "")}, {s.Bytes / 1024.0 / 1024.0:0.0} MB" : "not created yet"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { held = $"cannot be read: {ex.Message}"; }
        if (string.IsNullOrWhiteSpace(_importFolder.Text) || _importFolder.Tag as string != item.HistoryFolder) { _importFolder.Text = item.HistoryFolder; _importFolder.Tag = item.HistoryFolder; }   // a new instance (folder) starts from its own History folder

        Control Fix(Control control, double width) { control.Width = width; return control; }
        var import = DashboardStyle.Action("Import runs", () => _ = RunImportAsync(item, compare: false), "primary");
        var compare = DashboardStyle.Action("Compare with workbooks", () => _ = RunImportAsync(item, compare: true));
        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                KeyValue("Stored in", storage), KeyValue("Database", $"{store.Path_}  ({held})"),
                DashboardStyle.Text("Copy runs of old history workbooks into the database, from one date to another. Runs already in the database are skipped, and the workbooks are not changed.", 12, false, DashboardStyle.Muted),
                _importFolder,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _importFrom, _importTo, import, compare } },
                _importBar, _importText,
                UpkeepControls(item, store),
            }
        };
        return new StackPanel { Spacing = 6, Children = { DashboardStyle.Section("History database"), DashboardStyle.Card(panel, new Thickness(12, 10)) } };
    }

    private static Control DockedLeft(Control control) { DockPanel.SetDock(control, Dock.Right); control.Margin = new Thickness(6, 0, 0, 0); return control; }

    /// <summary>Retention and clean-up, one run as a file of its own, and the workbook as it was in a run.</summary>
    private Control UpkeepControls(InstanceInfo item, SqliteHistoryStore store)
    {
        if (_exchangeFolder.Parent is Panel owner) owner.Children.Remove(_exchangeFolder);
        // the one text box is shared: what is in it belongs to the instance it was last filled for, and is kept for that instance before it is filled for this one
        if (_exchangeOwner is not null) _exchangeFolders[_exchangeOwner] = _exchangeFolder.Text ?? "";
        _exchangeOwner = item.FolderPath;
        _exchangeFolder.Text = _exchangeFolders.TryGetValue(item.FolderPath, out var typed) && !string.IsNullOrWhiteSpace(typed) ? typed : System.IO.Path.Combine(item.HistoryFolder, "Exchange");
        var message = new TextBlock { Text = _upkeepMessages.GetValueOrDefault(item.FolderPath, ""), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = DashboardStyle.Muted, Tag = "history-upkeep-message" };
        void Say(string text) { _upkeepMessages[item.FolderPath] = text; message.Text = text; }
        string? Shown() => DataEditor.ShownRunChoice.Get(item.ResultsFolder) ?? (store.Exists ? store.ListRuns(0, 1).FirstOrDefault()?.RunId : null);

        var days = new NumericUpDown { Minimum = 0, Maximum = 36500, Increment = 1, Value = item.HistoryRetentionDays, Width = 120, FormatString = "0", Tag = "history-retention-days" };
        var runs = new NumericUpDown { Minimum = 0, Maximum = 1000000, Increment = 1, Value = item.HistoryRetentionRuns, Width = 120, FormatString = "0", Tag = "history-retention-runs" };
        var megabytes = new NumericUpDown { Minimum = 0, Maximum = 10000000, Increment = 10, Value = item.HistoryRetentionMaxMb, Width = 120, FormatString = "0", Tag = "history-retention-mb" };
        var keep = DashboardStyle.Action("Save", () =>
        {
            item.HistoryRetentionDays = (int)(days.Value ?? 0);
            item.HistoryRetentionRuns = (int)(runs.Value ?? 0);
            item.HistoryRetentionMaxMb = (int)(megabytes.Value ?? 0);
            SaveInstanceReportingSettings(item, true);
            Say(item.HistoryRetentionDays == 0 && item.HistoryRetentionRuns == 0 && item.HistoryRetentionMaxMb == 0 ? "Runs are kept for ever."
                : $"After each run, " + string.Join(" and ", new[] { item.HistoryRetentionDays > 0 ? $"runs older than {item.HistoryRetentionDays} days" : null, item.HistoryRetentionRuns > 0 ? $"all but the newest {item.HistoryRetentionRuns} runs" : null, item.HistoryRetentionMaxMb > 0 ? $"the oldest runs beyond {item.HistoryRetentionMaxMb} MB" : null }.Where(x => x is not null)) + " are removed.");
        });
        var clean = DashboardStyle.Action("Clean up now", () =>
        {
            try
            {
                var before = store.Summary().Bytes;
                var removed = store.ApplyRetention(item.HistoryRetentionDays) + store.KeepNewestRuns(item.HistoryRetentionRuns) + store.KeepUnderSize(item.HistoryRetentionMaxMb * 1024L * 1024L);
                store.Vacuum();
                var after = store.Summary().Bytes;
                Say($"{removed} runs removed. Database {before / 1024.0 / 1024.0:0.0} MB → {after / 1024.0 / 1024.0:0.0} MB." + (item.HistoryRetentionDays == 0 && item.HistoryRetentionRuns == 0 && item.HistoryRetentionMaxMb == 0 ? " (Nothing is set to be removed.)" : ""));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { Say($"Clean-up failed: {ex.Message}"); }
            Dispatcher.UIThread.Post(RefreshArtifacts);
        });
        var export = DashboardStyle.Action("Export shown run", () =>
        {
            try
            {
                if (Shown() is not { } run) { Say("There is no run in the database to export."); return; }
                Directory.CreateDirectory(_exchangeFolder.Text!);
                var file = System.IO.Path.Combine(_exchangeFolder.Text!, $"Run_{run}.runhistory.sqlite");
                store.ExportRun(run, file);
                Say($"Run {run} written to {file}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException) { Say($"Export failed: {ex.Message}"); }
        });
        var toExcel = DashboardStyle.Action("Export shown run to Excel", () =>
        {
            try
            {
                if (Shown() is not { } run) { Say("There is no run in the database to export."); return; }
                Directory.CreateDirectory(_exchangeFolder.Text!);
                var file = System.IO.Path.Combine(_exchangeFolder.Text!, $"Run_{run}.xlsx");
                store.ExportRunToExcel(run, file);
                Say($"Run {run} written to {file}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException) { Say($"Export failed: {ex.Message}"); }
        });
        Button? delete = null;
        string? armed = null;
        delete = DashboardStyle.Action("Delete shown run", () =>
        {
            try
            {
                if (Shown() is not { } run) { Say("There is no run in the database to delete."); return; }
                if (armed != run) { armed = run; delete!.Content = $"Click again to delete {run}"; Say($"This removes run {run} and its workbook copy from the database. Export it first if you may need it again."); return; }
                armed = null;
                DataEditor.ShownRunChoice.Set(item.ResultsFolder, null);
                store.DeleteRuns([run]);
                Say($"Run {run} deleted.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { Say($"Delete failed: {ex.Message}"); }
            Dispatcher.UIThread.Post(RefreshArtifacts);
        });
        Button? replace = null;
        string? replaceArmed = null;
        replace = DashboardStyle.Action("Replace the live workbook with the shown run's copy", () =>
        {
            try
            {
                if (Shown() is not { } run || store.SnapshotOf(run) is null) { Say("The shown run has no workbook copy (it was made before copies were kept, or copies are off)."); return; }
                if (replaceArmed != run) { replaceArmed = run; replace!.Content = $"Click again to replace the workbook with the copy of {run}"; Say($"This overwrites {item.WorkbookPath}. It is first copied next to it as …_before_restore_<time>.xlsx. Close the workbook in Excel first; unsaved changes in the data editor are lost."); return; }
                replaceArmed = null; replace!.Content = "Replace the live workbook with the shown run's copy";
                var backup = store.ReplaceWithSnapshot(run, item.WorkbookPath);
                Say(backup is { Length: > 0 } ? $"The workbook is now the copy of run {run}. The one it replaced is kept as {backup}." : $"The workbook is now the copy of run {run}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { Say($"Could not replace it (is it open in Excel?): {ex.Message}. The live workbook was not changed."); }
        });
        var browse = DashboardStyle.Action("Browse…", async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Folder for run files", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].Path is { IsFile: true } uri) _exchangeFolder.Text = uri.LocalPath;
        });
        var import = DashboardStyle.Action("Import run files", () =>
        {
            try
            {
                var folder = _exchangeFolder.Text?.Trim() ?? "";
                if (!Directory.Exists(folder)) { Say("That folder does not exist."); return; }
                int added = 0, skipped = 0, bad = 0;
                foreach (var file in Directory.GetFiles(folder, "*.runhistory.sqlite"))
                {
                    try { var r = store.ImportRunFile(file); added += r.Added; skipped += r.Skipped; }
                    catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException) { bad++; }
                }
                Say($"{added} runs added, {skipped} already there" + (bad > 0 ? $", {bad} files could not be read." : "."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say($"Import failed: {ex.Message}"); }
            Dispatcher.UIThread.Post(RefreshArtifacts);
        });
        var restore = DashboardStyle.Action("Save the workbook as it was in the shown run", () =>
        {
            try
            {
                if (Shown() is not { } run || store.SnapshotOf(run) is null) { Say("The shown run has no workbook copy (it was made before copies were kept, or copies are off)."); return; }
                var dir = System.IO.Path.GetDirectoryName(item.WorkbookPath)!;
                var name = System.IO.Path.GetFileNameWithoutExtension(item.WorkbookPath);
                var target = System.IO.Path.Combine(dir, $"{name}_as_of_{run}.xlsx");
                if (File.Exists(target)) { Say($"{target} is already there."); return; }
                store.RestoreSnapshot(run, target);
                Say($"Saved {target}. The live workbook was not touched.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { Say($"Could not save it: {ex.Message}"); }
        });
        var storeLogs = new CheckBox { Content = "Keep each run's log in the database (step details read it from there)", IsChecked = item.HistoryLogsInDatabase, Tag = "history-logs-in-db" };
        var deleteLogs = new CheckBox { Content = "Delete a log file after it is stored in the database", IsChecked = item.HistoryDeleteLogFiles, Tag = "history-delete-log-files" };
        void SaveLogSettings() { item.HistoryLogsInDatabase = storeLogs.IsChecked == true; item.HistoryDeleteLogFiles = deleteLogs.IsChecked == true; SaveInstanceReportingSettings(item, true); Say("Log settings saved."); }
        storeLogs.IsCheckedChanged += (_, _) => { if (storeLogs.IsChecked != item.HistoryLogsInDatabase) SaveLogSettings(); };
        deleteLogs.IsCheckedChanged += (_, _) => { if (deleteLogs.IsChecked != item.HistoryDeleteLogFiles) SaveLogSettings(); };
        var importLogs = DashboardStyle.Action("Import logs of existing runs", () =>
        {
            try
            {
                var r = store.ImportLogFiles(item.LogsFolder, item.EnvironmentOptions);
                Say($"{r.Added} logs stored, {r.Skipped} already there" + (r.NoRun > 0 ? $", {r.NoRun} belong to runs that are not in the database ({UnmatchedLogNames(r)})." : "."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { Say($"Import failed: {ex.Message}"); }
            Dispatcher.UIThread.Post(RefreshArtifacts);
        });
        var logTotals = store.LogTotals();
        var totals = store.SnapshotTotals();
        return new StackPanel
        {
            Spacing = 8, Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                DashboardStyle.Text("Upkeep", 13, true),
                DashboardStyle.Text("Keep runs for this many days, only the newest runs, and/or at most this many MB (0 = no limit; the newest run is always kept). Older runs are removed after each run; “Clean up now” also gives the room back to the disk.", 12, false, DashboardStyle.Muted),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { days, DashboardStyle.Text("days, and the newest", 12, false, DashboardStyle.Muted), runs, DashboardStyle.Text("runs, and at most", 12, false, DashboardStyle.Muted), megabytes, DashboardStyle.Text("MB", 12, false, DashboardStyle.Muted), keep, clean } },
                DashboardStyle.Text($"Workbook copies: {totals.Versions} versions, {totals.Bytes / 1024.0 / 1024.0:0.0} MB (the same content is stored once).", 12, false, DashboardStyle.Muted),
                new WrapPanel { Children = { restore, replace } },
                store.NoteCount() > 0 ? DashboardStyle.Text($"{store.NoteCount()} runs were rebuilt from their event files after a crash (search \"recovered\" in the History Explorer). They have no bodies, headers or generated values.", 12, false, DashboardStyle.Muted) : new Border(),
                DashboardStyle.Text($"Logs: {logTotals.Logs} stored, {logTotals.TextBytes / 1024.0 / 1024.0:0.0} MB of text taking {logTotals.StoredBytes / 1024.0 / 1024.0:0.0} MB.", 12, false, DashboardStyle.Muted),
                storeLogs, deleteLogs, importLogs,
                DashboardStyle.Text("One run in a file of its own, to hand over or to bring in:", 12, false, DashboardStyle.Muted),
                new DockPanel { LastChildFill = true, Children = { DockedLeft(browse), _exchangeFolder } },
                new WrapPanel { Children = { export, toExcel, import, delete } },
                message,
            }
        };
    }

    private async Task RunImportAsync(InstanceInfo item, bool compare)
    {
        if (_importing) { _importText.Text = "An import is already running."; return; }
        var folder = _importFolder.Text?.Trim() ?? "";
        if (!Directory.Exists(folder)) { _importText.Text = "That folder does not exist."; return; }
        var from = _importFrom.SelectedDate?.Date; var to = _importTo.SelectedDate?.Date;
        if (from is { } f && to is { } t && f > t) { _importText.Text = "The From date is after the To date."; return; }
        _importing = true; _importBar.IsVisible = true; _importBar.Value = 0;
        var store = new SqliteHistoryStore(item.HistoryFolder);
        try
        {
            if (compare)
            {
                _importText.Text = "Comparing…";
                var differences = await Task.Run(() => store.CompareWithWorkbooks(folder));
                _importText.Text = differences.Count == 0 ? "The database and the workbooks agree." : $"{differences.Count} differences, for example:\n" + string.Join("\n", differences.Take(8));
            }
            else
            {
                var progress = new Progress<(int Done, int Total, string File)>(p => { _importBar.Value = p.Total == 0 ? 1 : (double)p.Done / p.Total; _importText.Text = p.File.Length == 0 ? "Finishing…" : $"Reading {p.File} ({p.Done + 1} of {p.Total})"; });
                var result = await Task.Run(() => store.ImportWorkbooks(folder, from, to, progress));
                _importText.Text = $"Read {result.Files} workbooks: {result.RunsAdded} runs added, {result.RunsSkipped} already there." + (result.Problems.Count > 0 ? $" {result.Problems.Count} could not be read: {result.Problems[0]}" : "");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { _importText.Text = $"Failed: {ex.Message}"; }
        finally { _importing = false; _importBar.IsVisible = false; Dispatcher.UIThread.Post(RefreshArtifacts); }
    }

    private void RefreshArtifacts()
    {
        _artifactPanel.Children.Clear(); var item = _selectedInstance;
        if (item is null) return;
        _artifactPanel.Children.Add(DashboardStyle.Text("Workbooks and run artifacts", 17, true));
        _artifactPanel.Children.Add(DashboardStyle.Section("Open"));
        var openButtons = new WrapPanel();
        if (_hostWindow is not { IsVisible: true }) openButtons.Children.Add(DashboardStyle.Action("Edit data", OpenDataEditor, "primary"));
        openButtons.Children.Add(DashboardStyle.Action("Re-run center…", () => OpenRerunCenter()));
        openButtons.Children.Add(DashboardStyle.Action("Open latest result workbook", OpenLatestResultWorkbook));
        openButtons.Children.Add(DashboardStyle.Action("Open Templates folder", OpenTemplatesFolder));
        openButtons.Children.Add(DashboardStyle.Action("Open Results folder", () =>
        {
            if (!item.GenerateResults && !Directory.Exists(item.ResultsFolder))
            {
                _details.Text = "Technical Results generation is disabled for this instance; no Results folder was created.";
                return;
            }
            OpenFolder(item.ResultsFolder, "results folder");
        }));
        _artifactPanel.Children.Add(openButtons);
        _artifactPanel.Children.Add(HistoryDatabaseSection(item));
        _artifactPanel.Children.Add(LegacyAssertionTools());
        _artifactPanel.Children.Add(DashboardStyle.Section("Locations"));
        _artifactPanel.Children.Add(DashboardStyle.Card(new StackPanel { Spacing = 6, Children = { KeyValue("Workbook", item.WorkbookPath), KeyValue("Results", item.ResultsFolder), KeyValue("Current run", item.CurrentRunFolder) } }, new Thickness(12, 10)));
        if (ActiveScope(item) is { } scope)
        {
            _artifactPanel.Children.Add(DashboardStyle.Section($"Files of {ScopeText(scope)}", "in the last run"));
            var files = new List<string>();
            try
            {
                if (!string.IsNullOrWhiteSpace(item.CurrentRunFolder) && Directory.Exists(item.CurrentRunFolder))
                    files = Directory.EnumerateFiles(item.CurrentRunFolder, "*", SearchOption.AllDirectories)
                        .Where(f => f.Contains(scope.Testcase, StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (files.Count == 0) _artifactPanel.Children.Add(DashboardStyle.Text("No result files for this yet.", 12, false, DashboardStyle.Muted));
            _artifactPager.Changed -= RefreshArtifacts; _artifactPager.Changed += RefreshArtifacts;
            var artifactPage = _artifactPager.Slice(files);
            if (files.Count > _artifactPager.PageSize || _artifactPager.Page > 1) _artifactPanel.Children.Add(new PagerBar(_artifactPager));
            foreach (var file in artifactPage)
            {
                _artifactPanel.Children.Add(KeyValue(Path.GetFileName(file), file));
                _artifactPanel.Children.Add(DashboardStyle.Action("Open folder", () => OpenFolder(Path.GetDirectoryName(file) ?? file, "result folder")));
            }
        }
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
    private async Task CopyTextAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(Host)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text);
        _details.Text = "Copied dashboard details to the clipboard.";
    }
    private async void RefreshHistoryPanel()
    {
        if (_historyRefreshing) return;
        _historyRefreshing = true;
        try
        {
            // An instance that keeps its history in the database only is listed from there (a run is one entry); the others from their history workbooks.
            var inDatabase = _instances.Where(x => x.HistoryStorage == HistoryStorage.Database).ToArray();
            var sources = _instances.Where(x => x.HistoryStorage != HistoryStorage.Database && Directory.Exists(x.HistoryFolder)).Select(x => (Item: x, Paths: Directory.GetFiles(x.HistoryFolder, "*.xlsx", SearchOption.AllDirectories).Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTimeUtc).ToArray())).ToArray();
            var loaded = await Task.Run(() => sources.SelectMany(source => source.Paths.Select(path => (source.Item, Path: path))).Select(source => { try { return ReadHistoryEntry(source.Item, source.Path); } catch { return null; } }).Where(x => x is not null).Cast<HistoryEntry>()
                .Concat(inDatabase.SelectMany(item => { try { return ReadDatabaseEntries(item); } catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException) { UiLog($"History database could not be read. instance={item.Name}; error={ex.Message}"); return []; } })).ToList());
            _historyEntries.Clear(); _historyEntries.AddRange(loaded); _historySelected.RemoveWhere(path => !File.Exists(path) && !IsDatabaseEntry(path));
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
        _historyPagerBar ??= new PagerBar(_historyPager);
        _historyPager.Changed -= RenderHistoryEntries; _historyPager.Changed += RenderHistoryEntries;
        _historyPanel.Children.Add(_historyPagerBar);
        _historyPanel.Children.Add(new ScrollViewer { Content = _historyResults, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        RenderHistoryEntries();
    }

    private void HistoryFilterChanged(object? sender, EventArgs e) { _historyPager.Reset(); RenderHistoryEntries(); }

    private void RenderHistoryEntries()
    {
        _historyResults.Children.Clear();
        var query = _historySearch.Text?.Trim() ?? "";
        var status = _historyStatus.SelectedItem?.ToString() ?? "All statuses";
        var visible = _historyEntries.Where(x => status == "All statuses" || x.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(query) || x.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var entry in _historyPager.Slice(visible))
        {
            var selected = _historySelected.Contains(entry.Path);
            var check = new CheckBox { IsChecked = selected, VerticalAlignment = VerticalAlignment.Top };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) _historySelected.Add(entry.Path); else _historySelected.Remove(entry.Path); _historyCompare.Text = $"{_historySelected.Count} history file(s) selected; choose exactly two to compare."; };
            var title = DashboardStyle.Text($"{entry.Instance}  ·  {entry.Status}  ·  {entry.RunId}", 13, true); title.TextWrapping = TextWrapping.Wrap;
            var summary = DashboardStyle.Text($"{entry.Started:yyyy-MM-dd HH:mm:ss}  ·  {entry.Requests.Count} step rows  ·  {entry.Passed} passed  ·  {entry.Failed} failed  ·  p95 {entry.P95Ms} ms", 11, false, DashboardStyle.Muted); summary.TextWrapping = TextWrapping.Wrap;
            Control open = entry.InDatabase ? DashboardStyle.Text("in the database", 11, false, DashboardStyle.Muted) : DashboardStyle.Action("Open workbook", () => OpenExcelFile(entry.Path, "history workbook"));
            var details = new StackPanel { Spacing = 4, Children = { title, summary } };
            foreach (var row in entry.Requests.Take(8)) { var line = DashboardStyle.Text($"{row.Result,-5}  {row.Testcase} / {row.DataId} / {row.Step}  {row.DurationMs} ms  HTTP {row.HttpStatus}", 11, false, row.Result == "FAIL" ? DashboardStyle.Red : DashboardStyle.Muted); line.TextWrapping = TextWrapping.Wrap; details.Children.Add(line); }
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto"), Children = { check, details, open } }; Grid.SetColumn(details, 1); Grid.SetColumn(open, 2);
            _historyResults.Children.Add(DashboardStyle.Card(grid, new Thickness(10)));
        }
        if (visible.Count == 0) _historyResults.Children.Add(DashboardStyle.Text("No history matches the current filters.", color: DashboardStyle.Muted));
        _historyPagerBar?.Update();
        _historyCompare.Text = $"{_historySelected.Count} history file(s) selected; choose exactly two to compare.";
    }

    private void CompareHistory()
    {
        var selected = _historyEntries.Where(x => _historySelected.Contains(x.Path)).ToList();
        if (selected.Count != 2) { _historyCompare.Text = "Select exactly two history files first."; return; }
        var left = FullEntry(selected[0]); var right = FullEntry(selected[1]);   // a run from the database is read again with its bodies and assertions
        var byKey = left.Requests.Concat(right.Requests).GroupBy(x => $"{x.Testcase}|{x.DataId}|{x.Step}", StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key);
        var changed = 0; var requestLines = new List<string> { $"Comparison: {EntryLabel(left)}  vs  {EntryLabel(right)}" };
        var responseLines = new List<string>(); var configLines = new List<string>(); var assertionLines = new List<string>();
        var sheets = !left.InDatabase && !right.InDatabase;   // the Variables, DataEngine rows and DataEngineConfig sheets exist only in the workbooks
        var variableLines = sheets ? CompareSheet(left.Path, right.Path, "Variables") : [];
        var dataRowLines = sheets ? CompareSheet(left.Path, right.Path, "DataEngineRequestRows") : [];
        if (sheets) configLines.AddRange(CompareSheet(left.Path, right.Path, "DataEngineConfig"));
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
            var pager = new Pager(); var bar = new PagerBar(pager);
            void Render()
            {
                host.Children.Clear();
                var filtered = source.Where(x => x.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var line in pager.Slice(filtered)) host.Children.Add(DiffVisual(line));
                if (filtered.Count == 0) host.Children.Add(DashboardStyle.Text("No differences match the filter.", 12, false, DashboardStyle.Muted));
                bar.Update();
            }
            pager.Changed += Render;
            search.TextChanged += (_, _) => { pager.Reset(); Render(); }; Render();
            var tab = new DockPanel();
            DockPanel.SetDock(bar, Dock.Top); tab.Children.Add(bar);
            tab.Children.Add(new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            tabs.Items.Add(new TabItem { Header = header, Content = tab });
        }
        AddTab("Request", requestLines); AddTab("Response", responseLines); AddTab("Assertions", assertionLines); AddTab("Variables", variableLines); AddTab("DataEngine rows", dataRowLines); AddTab("Config", configLines);
        var close = DashboardStyle.Action("Close", dialog.Close);
        var comparisonGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(18) };
        comparisonGrid.Children.Add(search); Grid.SetRow(tabs, 1); comparisonGrid.Children.Add(tabs); Grid.SetRow(close, 2); comparisonGrid.Children.Add(close); dialog.Content = comparisonGrid;
        dialog.Show(Host);
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

    private static bool IsDatabaseEntry(string path) => path.Contains(SqliteHistoryStore.FileName + "#", StringComparison.Ordinal);

    private static string EntryLabel(HistoryEntry entry) => entry.InDatabase ? $"{entry.Instance} · {entry.RunId}" : Path.GetFileName(entry.Path);

    /// <summary>The runs of an instance's history database as History Explorer entries (newest first). A list shows no bodies or assertions; <see cref="FullEntry"/> loads them.</summary>
    internal static List<HistoryEntry> ReadDatabaseEntries(InstanceInfo item, int limit = 500)
    {
        var store = new SqliteHistoryStore(item.HistoryFolder);
        var entries = new List<HistoryEntry>();
        foreach (var run in store.ListRuns(0, limit))
        {
            var requests = store.LoadRequests(run.RunId, withBodies: false).Select(r => new HistoryRequest(r.Testcase, r.DataId, r.Step, r.Result, r.DurationMs, r.HttpStatus, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty)).ToList();
            var status = run.Result.ToUpperInvariant();
            entries.Add(new HistoryEntry($"{store.Path_}#{run.RunId}", item.Name, status, run.RunId, run.Started.LocalDateTime, requests, [], run.Passed, run.Failed, run.P95Ms.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Join(" ", item.Name, status, run.RunId, store.NoteOf(run.RunId) is null ? "" : "recovered crashed", requests.Select(x => $"{x.Testcase} {x.DataId} {x.Step}")), InDatabase: true));
        }
        return entries;
    }

    /// <summary>A database entry with its bodies and assertions (what a comparison needs); a workbook entry already has everything.</summary>
    private HistoryEntry FullEntry(HistoryEntry entry)
    {
        if (!entry.InDatabase || _instances.FirstOrDefault(i => i.Name == entry.Instance) is not { } item) return entry;
        var store = new SqliteHistoryStore(item.HistoryFolder);
        var requests = store.LoadRequests(entry.RunId).Select(r => new HistoryRequest(r.Testcase, r.DataId, r.Step, r.Result, r.DurationMs, r.HttpStatus, r.RequestBody, r.ResponseBody, r.RequestHeaders, r.ResponseHeaders, r.StepConfig)).ToList();
        var assertions = store.LoadAssertions(entry.RunId).Select(a => new HistoryAssertion(a.Testcase, a.DataId, a.Step, a.Path, a.Verb, a.Type, a.Meaning, a.Expected, a.Actual, a.Result)).ToList();
        return entry with { Requests = requests, Assertions = assertions };
    }

    private static HistoryEntry ReadHistoryEntry(InstanceInfo item, string path)
    {
        using var workbook = new XLWorkbook(path); var summary = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("RunSummary", StringComparison.OrdinalIgnoreCase)); var metrics = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("Metrics", StringComparison.OrdinalIgnoreCase)); var requestSheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("RequestHistory", StringComparison.OrdinalIgnoreCase));
        var headers = requestSheet?.FirstRowUsed()?.CellsUsed(LoadTestingTool.Excel.HeaderCells.HasText).ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
        string Cell(IXLRow row, string name) => headers.TryGetValue(name, out var c) ? row.Cell(c).GetString() : "";
        var requests = requestSheet is null ? [] : requestSheet.RowsUsed().Skip(1).Select(row => new HistoryRequest(Cell(row, "testcase"), Cell(row, "dataid"), Cell(row, "stepname"), Cell(row, "requestresult"), long.TryParse(Cell(row, "durationms"), out var d) ? d : 0, int.TryParse(Cell(row, "httpstatus"), out var s) ? s : 0, Cell(row, "requestbody"), Cell(row, "responsebody"), Cell(row, "requestheaders"), Cell(row, "responseheaders"), Cell(row, "stepconfig"))).ToList();
        var assertionSheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("AssertionHistory", StringComparison.OrdinalIgnoreCase)); var ah = assertionSheet?.FirstRowUsed()?.CellsUsed(LoadTestingTool.Excel.HeaderCells.HasText).ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? []; string ACell(IXLRow row, string name) => ah.TryGetValue(name, out var c) ? row.Cell(c).GetString() : "";
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
            item.Requests.Add(new DashboardRequest(Str("stepName"), Str("stepType"), Str("testcase"), Str("dataId"), Str("environment"), Str("result"), Long("durationMs"), Str("failureCategory"), Str("errorMessage"), (int)Long("testcaseIndex"), (int)Long("excelRow"), (int)Long("stepOrder"), "", Str("loopRound")));
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

/// <param name="TestcaseIndex">The testcase's number in the workbook, with <paramref name="ExcelRow"/> and <paramref name="StepOrder"/> the sort key that lists steps in workbook order (0 when an older run did not record them).</param>
public sealed record DashboardRequest(string StepName, string StepType, string Testcase, string DataId, string Environment, string Result, long DurationMs, string FailureCategory, string Error, int TestcaseIndex = 0, int ExcelRow = 0, int StepOrder = 0, string RunResult = "", string LoopRound = "")
{
    /// <summary>Steps in workbook order: environment, then testcase, then the data row's position in the sheet, then the step's position. Ties keep the order they finished in.</summary>
    public static List<DashboardRequest> InWorkbookOrder(IEnumerable<DashboardRequest> requests)
        => requests.OrderBy(r => string.IsNullOrWhiteSpace(r.Environment) ? "default" : r.Environment, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.TestcaseIndex).ThenBy(r => r.ExcelRow).ThenBy(r => r.StepOrder).ToList();
}
internal sealed record DashboardRowSummary(string Testcase, string DataId, string Environment, int Steps, int Passed, int Failed, int Unexpected, int Skipped, string Status, string Details);
internal sealed record RunPlanRow(string Testcase, int SkippedSteps, int ExecutableSteps, bool HasAssertions);
internal sealed record RunPlanSummary(int Testcases, int SelectedSteps, int RowsSelected, int RowsWithDontrun, int RowsFullySkipped, long SkippedSteps, long PlannedSteps, int Environments, int MaxParallelRows, int Iterations, int RowsWithAssertions, int RowsWithoutAssertions, IReadOnlyList<string> EnvironmentNames, IReadOnlyList<RunPlanRow> Rows)
{
    public long PlannedExecutions => PlannedSteps;
};
internal sealed record HistoryRequest(string Testcase, string DataId, string Step, string Result, long DurationMs, int HttpStatus, string RequestBody, string ResponseBody, string RequestHeaders, string ResponseHeaders, string StepConfig);
internal sealed record HistoryAssertion(string Testcase, string DataId, string Step, string Path, string Verb, string Type, string Meaning, string Expected, string Actual, string Result);
internal sealed record HistoryEntry(string Path, string Instance, string Status, string RunId, DateTime Started, IReadOnlyList<HistoryRequest> Requests, IReadOnlyList<HistoryAssertion> Assertions, int Passed, int Failed, string P95Ms, string SearchText, bool InDatabase = false);
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
