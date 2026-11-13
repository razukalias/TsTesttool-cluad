using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI;

internal sealed record HistoryFileItem(
    string Path,
    string Instance,
    string Kind,
    string Testcase,
    string Environment,
    string Status,
    string RunId,
    string Row,
    DateTime Timestamp)
{
    public string SearchText => string.Join(" ", Instance, Kind, Testcase, Environment, Status, RunId, Row, System.IO.Path.GetFileName(Path));
}

public sealed class HistoryWorkspaceWindow : Window
{
    private readonly string _instancesRoot;
    private readonly string _settingsPath;
    private readonly HistoryComparisonSettings _comparisonSettings;
    private readonly StackPanel _results = new() { Spacing = 8 };
    private readonly TextBox _search = new() { Watermark = "Search instance, testcase, environment, filename, run ID…", MinWidth = 360 };
    private readonly TextBox _instance = new() { Watermark = "Instance" };
    private readonly TextBox _status = new() { Watermark = "Status" };
    private readonly TextBox _environment = new() { Watermark = "Environment" };
    private readonly TextBox _testcase = new() { Watermark = "Testcase" };
    private readonly TextBox _runId = new() { Watermark = "Run ID" };
    private readonly CalendarDatePicker _dateFrom = new() { Width = 150, Watermark = "From date", SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "yyyy-MM-dd" };
    private readonly CalendarDatePicker _dateTo = new() { Width = 150, Watermark = "To date", SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "yyyy-MM-dd" };
    private readonly TextBlock _summary = DashboardStyle.Text("Loading history…", 12, false, DashboardStyle.Muted);
    private readonly TextBlock _message = DashboardStyle.Text("", 12, false, DashboardStyle.Muted);
    private readonly TabControl _views = new();
    private readonly List<HistoryFileItem> _files = [];
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private bool _grouped;
    private bool _loading;

    public HistoryWorkspaceWindow(string instancesRoot)
    {
        _instancesRoot = Path.GetFullPath(instancesRoot);
        _settingsPath = ResolveSettingsPath(_instancesRoot);
        _comparisonSettings = HistoryComparisonSettings.Load(_settingsPath);
        Title = "LoadTestingTool • History Explorer";
        Width = 1450; Height = 900; MinWidth = 1050; MinHeight = 650;
        WindowState = WindowState.Maximized;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = BuildView();
        Opened += (_, _) =>
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is not null)
            {
                Position = screen.WorkingArea.Position;
                Width = screen.WorkingArea.Width;
                Height = screen.WorkingArea.Height;
            }
            WindowState = WindowState.Maximized;
            RefreshAsync();
        };
    }

    private Control BuildView()
    {
        var title = DashboardStyle.Text("History Explorer", 22, true);
        var help = DashboardStyle.Text("Browse executions as grouped runs or as individual history files. The initial scan reads filenames only, so damaged workbooks cannot freeze the dashboard.", 12, false, DashboardStyle.Muted);
        var viewButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
        {
            DashboardStyle.Action("Grouped runs", () => { _grouped = true; Render(); }, "primary"),
            DashboardStyle.Action("Individual files", () => { _grouped = false; Render(); }),
            DashboardStyle.Action("Refresh", RefreshAsync),
            DashboardStyle.Action("Clear filters", ClearFilters)
        }};
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(new StackPanel { Spacing = 4, Children = { title, help } }); Grid.SetColumn(viewButtons, 1); top.Children.Add(viewButtons);

        foreach (var field in new[] { _search, _instance, _status, _environment, _testcase, _runId }) field.TextChanged += (_, _) => Render();
        _dateFrom.SelectedDateChanged += (_, _) => Render();
        _dateTo.SelectedDateChanged += (_, _) => Render();
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 12, 0, 4) };
        AddFilter(filters, _search, 0, 0, 8);
        AddFilter(filters, _instance, 0, 1); AddFilter(filters, _status, 1, 1); AddFilter(filters, _environment, 2, 1); AddFilter(filters, _testcase, 3, 1); AddFilter(filters, _runId, 4, 1); AddFilter(filters, _dateFrom, 5, 1); AddFilter(filters, _dateTo, 6, 1);
        _summary.Margin = new Thickness(0, 6);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
        {
            DashboardStyle.Action("Open selected in viewer", () => OpenSelected(false), "primary"),
            DashboardStyle.Action("Open selected in Excel", () => OpenSelected(true)),
            DashboardStyle.Action("Compare selected", CompareSelected),
            DashboardStyle.Action("Clear selection", () => { _selected.Clear(); Render(); })
        }};
        var header = new StackPanel { Spacing = 4, Children = { top, filters, _summary, actions, _message, new Border { Height = 1, Background = DashboardStyle.Line, Margin = new Thickness(0, 4) } } };
        _views.HorizontalAlignment = HorizontalAlignment.Stretch;
        _views.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _views.ItemsSource = new[] { new TabItem { Header = "Results", HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = new ScrollViewer { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = _results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(20) };
        layout.Children.Add(header); Grid.SetRow(_views, 1); layout.Children.Add(_views);
        return layout;
    }

    private static void AddFilter(Grid grid, Control control, int column, int row, int columnSpan = 1)
    {
        Grid.SetColumn(control, column); Grid.SetRow(control, row); if (columnSpan > 1) Grid.SetColumnSpan(control, columnSpan); control.Margin = new Thickness(0, 3, 6, 3); grid.Children.Add(control);
    }

    private void ClearFilters()
    {
        foreach (var field in new[] { _search, _instance, _status, _environment, _testcase, _runId }) field.Text = "";
        _dateFrom.SelectedDate = null; _dateTo.SelectedDate = null;
    }

    private async void RefreshAsync()
    {
        if (_loading) return;
        _loading = true; _message.Text = "Scanning history folders…"; _results.Children.Clear(); _results.Children.Add(DashboardStyle.Text("Scanning…", 13, false, DashboardStyle.Muted));
        try
        {
            var loaded = await Task.Run(ScanFiles);
            _files.Clear(); _files.AddRange(loaded); _selected.RemoveWhere(path => !_files.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
            _message.Text = $"Scan complete. {_files.Count} history file(s) found.";
        }
        catch (Exception ex) { _message.Text = $"History scan failed safely: {ex.Message}"; _message.Foreground = DashboardStyle.Red; }
        finally { _loading = false; Render(); }
    }

    private IReadOnlyList<HistoryFileItem> ScanFiles()
    {
        var output = new List<HistoryFileItem>();
        if (!Directory.Exists(_instancesRoot)) return output;
        foreach (var instanceFolder in Directory.EnumerateDirectories(_instancesRoot))
        {
            var history = Path.Combine(instanceFolder, "History"); if (!Directory.Exists(history)) continue;
            IEnumerable<string> paths;
            try { paths = Directory.EnumerateFiles(history, "*.xlsx", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var path in paths)
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(path); if (name.StartsWith("~$", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                    var status = parts.LastOrDefault(x => x.Equals("passed", StringComparison.OrdinalIgnoreCase) || x.Equals("failed", StringComparison.OrdinalIgnoreCase) || x.Equals("cancelled", StringComparison.OrdinalIgnoreCase)) ?? "Unknown";
                    var historyIndex = Array.FindIndex(parts, x => x.Equals("History", StringComparison.OrdinalIgnoreCase));
                    var statusIndex = Array.FindIndex(parts, x => x.Equals(status, StringComparison.OrdinalIgnoreCase));
                    var environment = ResolveEnvironment(parts, historyIndex, statusIndex);
                    var aggregate = name.StartsWith("Execution_History_", StringComparison.OrdinalIgnoreCase);
                    var runId = aggregate ? name["Execution_History_".Length..] : MatchRunId(name);
                    var row = Regex.Match(name, "_Row(?<row>\\d+)", RegexOptions.IgnoreCase).Groups["row"].Value;
                    var rowMarker = name.IndexOf("_Row", StringComparison.OrdinalIgnoreCase);
                    var testcase = aggregate ? "All testcases" : rowMarker > 0 ? name[..rowMarker] : name;
                    if (string.IsNullOrWhiteSpace(runId)) runId = $"legacy-{File.GetLastWriteTimeUtc(path):yyyyMMddHHmm}";
                    output.Add(new HistoryFileItem(path, Path.GetFileName(instanceFolder), aggregate ? "Aggregate" : "Row", testcase, environment, DashboardStyle.Status(status), runId, row, File.GetLastWriteTimeUtc(path).ToLocalTime()));
                }
                catch { /* one inaccessible or malformed file must not stop the scan */ }
            }
        }
        return output.OrderByDescending(x => x.Timestamp).ToList();
    }

    private static string ResolveEnvironment(string[] parts, int historyIndex, int statusIndex)
    {
        if (statusIndex >= 0 && statusIndex + 1 < parts.Length - 1 && !parts[statusIndex + 1].Equals("_aggregate", StringComparison.OrdinalIgnoreCase)) return parts[statusIndex + 1];
        if (statusIndex > 0 && !parts[statusIndex - 1].Equals("History", StringComparison.OrdinalIgnoreCase) && !parts[statusIndex - 1].Equals("_aggregate", StringComparison.OrdinalIgnoreCase)) return parts[statusIndex - 1];
        if (historyIndex >= 0 && historyIndex + 1 < parts.Length - 1 && !parts[historyIndex + 1].Equals("_aggregate", StringComparison.OrdinalIgnoreCase) && !parts[historyIndex + 1].Equals("passed", StringComparison.OrdinalIgnoreCase) && !parts[historyIndex + 1].Equals("failed", StringComparison.OrdinalIgnoreCase) && !parts[historyIndex + 1].Equals("cancelled", StringComparison.OrdinalIgnoreCase)) return parts[historyIndex + 1];
        return "default";
    }

    private static string MatchRunId(string name)
    {
        var match = Regex.Match(name, "(?:_Run_|_run_)(?<id>[A-Za-z0-9-]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["id"].Value : "";
    }

    private void Render()
    {
        if (_loading) return;
        _results.Children.Clear();
        var visible = ApplyFilters(_files).ToList();
        _summary.Text = $"{visible.Count} matching file(s) of {_files.Count}. View: {(_grouped ? "Grouped runs" : "Individual files")}. Selected: {_selected.Count}.";
        if (_grouped)
        {
            foreach (var group in visible.GroupBy(x => $"{x.Instance}|{x.RunId}").OrderByDescending(x => x.Max(y => y.Timestamp))) _results.Children.Add(BuildGroup(group.Key, group.ToList()));
        }
        else foreach (var file in visible) _results.Children.Add(BuildFile(file));
        if (_results.Children.Count == 0) _results.Children.Add(DashboardStyle.Text("No history files match the current filters.", 13, false, DashboardStyle.Muted));
    }

    private IEnumerable<HistoryFileItem> ApplyFilters(IEnumerable<HistoryFileItem> source)
    {
        var fields = new[] { (Text: _search.Text, Select: (Func<HistoryFileItem, string>)(x => x.SearchText)), (Text: _instance.Text, Select: x => x.Instance), (Text: _status.Text, Select: x => x.Status), (Text: _environment.Text, Select: x => x.Environment), (Text: _testcase.Text, Select: x => x.Testcase), (Text: _runId.Text, Select: x => x.RunId) };
        foreach (var field in fields) if (!string.IsNullOrWhiteSpace(field.Text)) source = source.Where(x => MatchTerms(field.Select(x), field.Text!));
        if (_dateFrom.SelectedDate is { } from) source = source.Where(x => x.Timestamp.Date >= from.Date);
        if (_dateTo.SelectedDate is { } to) source = source.Where(x => x.Timestamp.Date <= to.Date);
        return source;
    }

    private static bool MatchTerms(string value, string filter) =>
        filter.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(andTerm => andTerm.Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(orTerm => value.Contains(orTerm, StringComparison.OrdinalIgnoreCase)));

    private Control BuildGroup(string key, List<HistoryFileItem> files)
    {
        var first = files[0]; var groupSelected = files.All(x => _selected.Contains(x.Path));
        var groupCheck = new CheckBox { IsChecked = groupSelected, VerticalAlignment = VerticalAlignment.Top };
        groupCheck.IsCheckedChanged += (_, _) => { foreach (var file in files) if (groupCheck.IsChecked == true) _selected.Add(file.Path); else _selected.Remove(file.Path); Render(); };
        var groupTitle = DashboardStyle.Text($"{first.Instance}  ·  {first.Status}  ·  Run {first.RunId}", 14, true);
        var groupSummary = DashboardStyle.Text($"{first.Timestamp:yyyy-MM-dd HH:mm:ss}  ·  {files.Count} matching file(s)  ·  {files.Select(x => x.Environment).Distinct(StringComparer.OrdinalIgnoreCase).Count()} environment(s)", 11, false, DashboardStyle.Muted);
        var children = new StackPanel { Spacing = 4 };
        foreach (var file in files.OrderBy(x => x.Kind).ThenBy(x => x.Testcase).ThenBy(x => x.Row)) children.Children.Add(BuildFile(file, true));
        var expander = new Expander { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Header = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { groupCheck, new StackPanel { Spacing = 3, Children = { groupTitle, groupSummary } } } } } }, Content = children, IsExpanded = false };
        return DashboardStyle.Card(expander, new Thickness(10));
    }

    private Control BuildFile(HistoryFileItem file, bool compact = false)
    {
        var check = new CheckBox { IsChecked = _selected.Contains(file.Path), VerticalAlignment = VerticalAlignment.Top };
        check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) _selected.Add(file.Path); else _selected.Remove(file.Path); Render(); };
        var color = file.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ? DashboardStyle.Red : file.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? DashboardStyle.Green : DashboardStyle.Muted;
        var title = DashboardStyle.Text($"{file.Kind}  ·  {file.Testcase}  ·  {file.Environment}  ·  {file.Status}", compact ? 11 : 13, true, color); title.TextWrapping = TextWrapping.Wrap;
        var timestamp = DashboardStyle.Text($"{file.Timestamp:yyyy-MM-dd HH:mm:ss}  ·  Row {RowOrDash(file)}  ·  Run {file.RunId}", 11, false, DashboardStyle.Muted); timestamp.TextWrapping = TextWrapping.Wrap;
        var filename = new TextBox { Text = Path.GetFileName(file.Path), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Foreground = DashboardStyle.Muted };
        ToolTip.SetTip(filename, file.Path);
        var open = DashboardStyle.Action("Open", () => OpenFile(file, false)); open.Padding = new Thickness(8, 5);
        var detailsPanel = new StackPanel { Spacing = 3, Children = { title, timestamp, filename } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto"), Margin = compact ? new Thickness(8, 4) : new Thickness(0) };
        grid.Children.Add(check); Grid.SetColumn(detailsPanel, 1); grid.Children.Add(detailsPanel); Grid.SetColumn(open, 2); grid.Children.Add(open);
        return compact ? grid : DashboardStyle.Card(grid, new Thickness(10));
    }

    private void OpenSelected(bool excel)
    {
        var files = _files.Where(x => _selected.Contains(x.Path)).ToList(); if (files.Count == 0) { _message.Text = "Select at least one history file."; return; }
        foreach (var file in files) OpenFile(file, excel);
    }

    private void OpenFile(HistoryFileItem file, bool excel)
    {
        try
        {
            if (excel) Process.Start(new ProcessStartInfo { FileName = file.Path, UseShellExecute = true });
            else
            {
                var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The History process path is unavailable.");
                var start = new ProcessStartInfo { FileName = processPath, UseShellExecute = false, CreateNoWindow = true };
                var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrWhiteSpace(assembly) && (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))) start.ArgumentList.Add(assembly);
                start.ArgumentList.Add("--dataset-editor"); start.ArgumentList.Add(file.Path); Process.Start(start);
            }
        }
        catch (Exception ex) { _message.Text = $"Could not open {Path.GetFileName(file.Path)}: {ex.Message}"; _message.Foreground = DashboardStyle.Red; }
    }

    private void CompareSelected()
    {
        var files = _files.Where(x => _selected.Contains(x.Path)).ToList();
        if (files.Count < 2) { _message.Text = "Select at least two history files to compare."; _message.Foreground = DashboardStyle.Red; return; }
        _ = ConfigureAndCompareAsync(files);
    }

    private async Task ConfigureAndCompareAsync(IReadOnlyList<HistoryFileItem> files)
    {
        try
        {
            var sheets = await Task.Run(() => DiscoverSheets(files));
            var defaults = _comparisonSettings.SelectedSheets.Where(s => sheets.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            if (defaults.Count == 0) defaults = sheets.ToList();
            var form = new HistoryComparisonWindow(files, sheets, defaults);
            var options = await form.ShowDialog<HistoryComparisonOptions?>(this);
            if (options is null) return;
            _comparisonSettings.SelectedSheets = options.Sheets.ToList();
            _comparisonSettings.Save(_settingsPath);
            var folder = Path.Combine(_instancesRoot, "_comparisons");
            Directory.CreateDirectory(folder);
            var outputPath = options.OpenInExcel
                ? Path.Combine(folder, $"HistoryComparison_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx")
                : Path.Combine(Path.GetTempPath(), $"TSLoadTestingTool_HistoryComparison_{Guid.NewGuid():N}.xlsx");
            await Task.Run(() => HistoryComparisonWorkbook.Create(files, options.Sheets, outputPath));
            if (options.OpenInExcel) Process.Start(new ProcessStartInfo { FileName = outputPath, UseShellExecute = true });
            else OpenComparisonInEditor(outputPath);
            _message.Foreground = DashboardStyle.Green;
            _message.Text = options.OpenInExcel ? $"Comparison workbook created and opened in Excel:\n{outputPath}" : "Comparison opened in the spreadsheet editor.";
        }
        catch (Exception ex) { _message.Foreground = DashboardStyle.Red; _message.Text = $"Could not compare selected history files: {ex.Message}"; }
    }

    private static IReadOnlyList<string> DiscoverSheets(IReadOnlyList<HistoryFileItem> files)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook(file.Path);
            foreach (var sheet in workbook.Worksheets) names.Add(sheet.Name);
        }
        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void OpenComparisonInEditor(string path)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The History process path is unavailable.");
        var start = new ProcessStartInfo { FileName = processPath, UseShellExecute = false, CreateNoWindow = true };
        var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assembly) && (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))) start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--dataset-editor"); start.ArgumentList.Add(path); Process.Start(start);
    }

    private static string ResolveSettingsPath(string instancesRoot)
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "appsettings.json"), Path.Combine(Directory.GetParent(instancesRoot)?.FullName ?? instancesRoot, "appsettings.json"), Path.Combine(Environment.CurrentDirectory, "appsettings.json") };
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && current is not null; i++, current = current.Parent) candidates.Add(Path.Combine(current.FullName, "appsettings.json"));
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string RowOrDash(HistoryFileItem file) => string.IsNullOrWhiteSpace(file.Row) ? "—" : file.Row;
}
