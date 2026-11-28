using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI;

public sealed partial class MockServerWindow
{
    private const int KeepEntries = 2000;
    private long _lastSeq;
    private readonly List<MockLogEntry> _entries = [];
    private readonly ListBox _log = new() { MinHeight = 150 };
    private readonly TextBox _filter = new() { Watermark = "Filter: method, URL, status, route…", Width = 260 };
    private readonly CheckBox _pause = new() { Content = "Pause" };
    private readonly TextBlock _count = DashboardStyle.Text("", 12, false, DashboardStyle.Muted);
    private readonly TextBox _requestDetail = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas,Menlo,monospace"), TextWrapping = TextWrapping.NoWrap };
    private readonly TextBox _responseDetail = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas,Menlo,monospace"), TextWrapping = TextWrapping.NoWrap };
    private TabControl? _tabs;
    private readonly TextBlock _stateText = DashboardStyle.Text("", 12, false, DashboardStyle.Muted);

    internal IReadOnlyList<MockLogEntry> Entries => _entries;
    internal ListBox LogList => _log;
    internal TabControl? Tabs => _tabs;

    private Control BuildMonitor()
    {
        var clear = DashboardStyle.Action("Clear", async () => { if (_client is not null) await _client.ClearAsync(); _entries.Clear(); RebuildLog(); });
        var fromRequest = DashboardStyle.Action("Route from request", () => RouteFromSelected());
        var reset = DashboardStyle.Action("Reset state", async () =>
        {
            if (_client is null || !_running) { _status.Text = "Run the server first: its state lives in the running server."; return; }
            await _client.ResetStateAsync();
            _stateText.Text = "Scenarios: all Started; sequences start again";
        });
        ToolTip.SetTip(reset, "Every scenario back to Started and every answer sequence back to its first answer (databases are reset on the Databases tab)");
        ToolTip.SetTip(fromRequest, "Make a route out of the selected request, for example one no route fitted");
        _filter.TextChanged += (_, _) => RebuildLog();
        _pause.IsCheckedChanged += (_, _) => { if (_pause.IsChecked != true) RebuildLog(); };
        _log.SelectionChanged += (_, _) => ShowDetail();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 6), Children = { _filter, _pause, clear, fromRequest, reset, _count, _stateText } };
        foreach (var c in bar.Children) c.VerticalAlignment = VerticalAlignment.Center;
        var details = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 8, 0, 0), MinHeight = 220 };
        var request = Titled("Request", _requestDetail); var response = Titled("Response", _responseDetail);
        Grid.SetColumn(response, 1); details.Children.Add(request); details.Children.Add(response);
        request.Margin = new Thickness(0, 0, 6, 0); response.Margin = new Thickness(6, 0, 0, 0);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,2*") };
        Add(grid, bar, 0); Add(grid, _log, 1); Add(grid, details, 2);
        return grid;
    }

    private static Control Titled(string title, TextBox box)
    {
        var panel = new DockPanel { LastChildFill = true };
        var label = DashboardStyle.Text(title, 12, false, DashboardStyle.Muted);
        DockPanel.SetDock(label, Dock.Top);
        panel.Children.Add(label); panel.Children.Add(box);
        return panel;
    }

    internal void AddEntries(IEnumerable<MockLogEntry> fresh)
    {
        foreach (var entry in fresh) { _entries.Add(entry); _lastSeq = Math.Max(_lastSeq, entry.Seq); }
        if (_entries.Count > KeepEntries) _entries.RemoveRange(0, _entries.Count - KeepEntries);
        if (_pause.IsChecked != true) RebuildLog();
    }

    private static string Line(MockLogEntry e) =>
        $"{e.Time:HH:mm:ss.fff}  {e.Method,-7} {e.Url}   {(e.Status == 0 ? "dropped" : e.Status.ToString())}   {e.DurationMs} ms   {(e.Matched ? e.Route : "no route")}";

    private static IBrush Color(MockLogEntry e) => DashboardStyle.Brush(!e.Matched ? "#FFB36B" : e.Status == 0 || e.Status >= 500 ? "#FF7588" : e.Status >= 400 ? "#F6C76B" : "#C9D8EA");

    internal bool Fits(MockLogEntry e)
    {
        var text = _filter.Text?.Trim();
        return string.IsNullOrEmpty(text) || Line(e).Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildLog()
    {
        var selected = (_log.SelectedItem as Control)?.Tag as MockLogEntry;
        var shown = _entries.Where(Fits).ToList();
        var items = shown.Select(e => (Control)new TextBlock { Text = Line(e), Tag = e, Foreground = Color(e), FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12 }).ToList();
        _log.ItemsSource = items;
        if (selected is not null && items.FirstOrDefault(i => ReferenceEquals(i.Tag, selected)) is { } keep) _log.SelectedItem = keep;
        else if (items.Count > 0 && _pause.IsChecked != true && (_log.SelectedItem is null)) _log.ScrollIntoView(items[^1]);
        _count.Text = $"{shown.Count} of {_entries.Count} request(s)";
    }

    private void ShowDetail()
    {
        if ((_log.SelectedItem as Control)?.Tag is not MockLogEntry e) { _requestDetail.Text = ""; _responseDetail.Text = ""; return; }
        var why = e.Explanation.Count == 0 ? "" : (e.Matched ? "\n\nRoutes above it that did not fit:\n" : "\n\nWhy no route fitted:\n") + string.Join("\n", e.Explanation.Select(l => "• " + l));
        _requestDetail.Text = $"{e.Method} {e.Url}\n{string.Join("\n", e.RequestHeaders)}\n\n{e.RequestBody}{why}";
        _responseDetail.Text = (e.Status == 0 ? "No response: " + e.Note : $"HTTP {e.Status}  ({e.DurationMs} ms, {e.Size} bytes)") + (e.Note.Length > 0 && e.Status != 0 ? $"\n({e.Note})" : "") + $"\n{string.Join("\n", e.ResponseHeaders)}\n\n{e.ResponseBody}" + (e.Data.Length > 0 ? $"\n\nDatabase: {e.Data}" : "");
    }

    /// <summary>Makes a route out of the selected request: same method and path (the base path taken off), answering 200 with an empty JSON object.</summary>
    internal bool RouteFromSelected()
    {
        if ((_log.SelectedItem as Control)?.Tag is not MockLogEntry e) { _status.Text = "Select a request in the list first."; return false; }
        var path = e.Url.Split('?')[0];
        var basePath = _config.BasePath.TrimEnd('/');
        if (basePath.Length > 0 && path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) path = path.Length == basePath.Length ? "/" : path[basePath.Length..];
        _config.Routes.Add(new MockRoute { Name = "From request", Method = e.Method, Path = path, Response = new MockResponse { Body = "{}" } });
        _selected = _config.Routes.Count;
        MarkChanged();
        if (_tabs is not null) _tabs.SelectedIndex = 0;
        _status.Text = $"Route {e.Method} {path} added. Edit its response, then Save.";
        return true;
    }
}
