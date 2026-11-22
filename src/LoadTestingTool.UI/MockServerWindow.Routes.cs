using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI;

public sealed partial class MockServerWindow
{
    /// <summary>0 is the no-match response; route n is item n.</summary>
    private int _selected;
    private bool _refreshing;
    private readonly ListBox _routeList = new() { MinWidth = 250 };
    private readonly StackPanel _routeOnly = new() { Spacing = 8 };
    private readonly TextBox _routeName = new();
    private readonly CheckBox _enabled = new() { Content = "Enabled" };
    private readonly AutoCompleteBox _method = new() { ItemsSource = Verbs, MinimumPrefixLength = 0, Width = 130 };
    private readonly TextBox _routePath = new() { Watermark = "/orders/{id}", MinWidth = 280 };
    private readonly StackPanel _conditionRows = new() { Spacing = 4 };
    private readonly NumericUpDown _failPercent = new() { Minimum = 0, Maximum = 100, Increment = 5, FormatString = "0", Width = 100 };
    private readonly CheckBox _drop = new() { Content = "drop connection instead of 500" };
    private readonly NumericUpDown _statusCode = new() { Minimum = 100, Maximum = 599, Increment = 1, FormatString = "0", Width = 110 };
    private readonly TextBox _contentType = new() { Width = 220, Watermark = "application/json" };
    private readonly NumericUpDown _delay = new() { Minimum = 0, Maximum = 600000, Increment = 100, FormatString = "0", Width = 120 };
    private readonly StackPanel _headerRows = new() { Spacing = 4 };
    private readonly TextBox _body = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 170 };
    private readonly TextBox _bodyFile = new() { Watermark = "optional: file in the server's folder, replaces the body", MinWidth = 320 };

    private MockRoute? CurrentRoute => _selected >= 1 && _selected <= _config.Routes.Count ? _config.Routes[_selected - 1] : null;
    internal MockResponse CurrentResponse => CurrentRoute?.Response ?? _config.Fallback;

    internal int SelectedItem { get => _selected; set { _selected = value; RefreshRouteList(); } }
    internal ListBox RouteList => _routeList;

    private static string Row(MockRoute route) => $"{(route.Enabled ? "●" : "○")}  {route.Method.ToUpperInvariant(),-7} {route.Path}" + (string.IsNullOrWhiteSpace(route.Name) ? "" : $"   · {route.Name}");

    private void RefreshRouteList()
    {
        _refreshing = true;
        _selected = Math.Clamp(_selected, 0, _config.Routes.Count);
        _routeList.ItemsSource = new[] { "◌  (no route matched)" }.Concat(_config.Routes.Select(Row)).ToList();
        _routeList.SelectedIndex = _selected;
        _refreshing = false;
        LoadSelected();
    }

    private Control BuildRoutes()
    {
        var add = Small("＋", "Add a route", () => { _config.Routes.Add(new MockRoute { Name = "New route", Method = "GET", Path = "/new", Response = new MockResponse { Body = "{}" } }); _selected = _config.Routes.Count; MarkChanged(); });
        var copy = Small("⧉", "Duplicate the selected route", () =>
        {
            if (CurrentRoute is not { } route) return;
            var clone = MockServerConfig.FromJson(new MockServerConfig { Routes = [route] }.ToJson()).Routes[0];
            clone.Name = string.IsNullOrWhiteSpace(route.Name) ? "" : route.Name + " copy";
            _config.Routes.Insert(_selected, clone); _selected++; MarkChanged();
        });
        var remove = Small("✕", "Delete the selected route", () => { if (CurrentRoute is null) return; _config.Routes.RemoveAt(_selected - 1); _selected = Math.Min(_selected, _config.Routes.Count); MarkChanged(); });
        var up = Small("▲", "Move up: routes are tried from the top", () => Move(-1));
        var down = Small("▼", "Move down", () => Move(1));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 0, 6), Children = { add, copy, remove, up, down } };
        _routeList.SelectionChanged += (_, _) => { if (_refreshing || _routeList.SelectedIndex < 0) return; _selected = _routeList.SelectedIndex; LoadSelected(); };
        var left = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 12, 0), Children = { tools, _routeList } };
        DockPanel.SetDock(tools, Dock.Top);

        _routeName.TextChanged += (_, _) => EditRoute(r => r.Name = _routeName.Text ?? "");
        _enabled.IsCheckedChanged += (_, _) => EditRoute(r => r.Enabled = _enabled.IsChecked == true);
        _method.PropertyChanged += (_, e) => { if (e.Property == AutoCompleteBox.TextProperty) EditRoute(r => r.Method = (_method.Text ?? "").Trim()); };
        _routePath.TextChanged += (_, _) => EditRoute(r => r.Path = _routePath.Text?.Trim() ?? "");
        _failPercent.ValueChanged += (_, _) => EditRoute(r => r.FailurePercent = (int)(_failPercent.Value ?? 0));
        _drop.IsCheckedChanged += (_, _) => EditRoute(r => r.DropConnection = _drop.IsChecked == true);
        _statusCode.ValueChanged += (_, _) => EditResponse(r => r.Status = (int)(_statusCode.Value ?? 200));
        _contentType.TextChanged += (_, _) => EditResponse(r => r.ContentType = _contentType.Text?.Trim() ?? "");
        _delay.ValueChanged += (_, _) => EditResponse(r => r.DelayMs = (int)(_delay.Value ?? 0));
        _body.TextChanged += (_, _) => EditResponse(r => r.Body = _body.Text ?? "");
        _bodyFile.TextChanged += (_, _) => EditResponse(r => r.BodyFile = _bodyFile.Text?.Trim() ?? "");

        var addCondition = DashboardStyle.Action("＋ Condition", () => { if (CurrentRoute is { } route) { route.Conditions.Add(new MockCondition()); MarkEdited(); BuildConditionRows(); } });
        var addHeader = DashboardStyle.Action("＋ Header", () => { CurrentResponse.Headers.Add(new MockHeader()); MarkEdited(); BuildHeaderRows(); });
        _routeOnly.Children.Add(new WrapPanel { Children = { Labeled("Name", _routeName), Labeled("Method", _method), Labeled("Path", _routePath), Pad(_enabled) } });
        _routeOnly.Children.Add(Section("Only when (conditions: all must hold)", addCondition, _conditionRows));
        _routeOnly.Children.Add(new WrapPanel { Children = { Labeled("Fail % of requests", _failPercent), Pad(_drop) } });

        var editor = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 6, 12) };
        editor.Children.Add(_routeOnly);
        editor.Children.Add(DashboardStyle.Text("Response", 14, true));
        editor.Children.Add(new WrapPanel { Children = { Labeled("Status", _statusCode), Labeled("Content type", _contentType), Labeled("Delay (ms)", _delay) } });
        editor.Children.Add(Section("Headers", addHeader, _headerRows));
        editor.Children.Add(Labeled("Body  ({{path.id}}, {{query.x}}, {{header.x}}, {{json.a.b}}, {{body}}, {{guid}}, {{now}} …)", _body));
        editor.Children.Add(Labeled("Body file", _bodyFile));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("270,*") };
        grid.Children.Add(left);
        var scroll = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); grid.Children.Add(scroll);
        return grid;
    }

    private static Button Small(string text, string tip, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 4), FontSize = 12 };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private static Control Section(string title, Button add, Control rows) => new StackPanel { Spacing = 4, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { DashboardStyle.Text(title, 12, false, DashboardStyle.Muted), add } }, rows } };

    private void Move(int by)
    {
        var index = _selected - 1;
        var target = index + by;
        if (CurrentRoute is not { } route || target < 0 || target >= _config.Routes.Count) return;
        _config.Routes.RemoveAt(index); _config.Routes.Insert(target, route);
        _selected = target + 1; MarkChanged();
    }

    /// <summary>The list changed (add, delete, move): redraw it and note the edit.</summary>
    private void MarkChanged() { MarkEdited(); RefreshRouteList(); }

    private void MarkEdited() => Edit(() => { });

    private void EditRoute(Action<MockRoute> change)
    {
        if (_loading || CurrentRoute is not { } route) return;
        Edit(() => change(route));
        RefreshRowText();
    }

    private void EditResponse(Action<MockResponse> change)
    {
        if (_loading) return;
        var response = CurrentResponse;
        Edit(() => change(response));
    }

    /// <summary>Updates the selected row's text without rebuilding the list (typing must not lose the caret).</summary>
    private void RefreshRowText()
    {
        if (CurrentRoute is not { } route || _routeList.ItemsSource is not List<string> rows || _selected >= rows.Count) return;
        rows[_selected] = Row(route);
        _refreshing = true;
        _routeList.ItemsSource = null; _routeList.ItemsSource = rows; _routeList.SelectedIndex = _selected;
        _refreshing = false;
    }

    private void LoadSelected()
    {
        _loading = true;
        var route = CurrentRoute;
        _routeOnly.IsVisible = route is not null;
        if (route is not null)
        {
            _routeName.Text = route.Name; _enabled.IsChecked = route.Enabled; _method.Text = route.Method; _routePath.Text = route.Path;
            _failPercent.Value = route.FailurePercent; _drop.IsChecked = route.DropConnection;
        }
        var response = CurrentResponse;
        _statusCode.Value = response.Status; _contentType.Text = response.ContentType; _delay.Value = response.DelayMs; _body.Text = response.Body; _bodyFile.Text = response.BodyFile;
        _loading = false;
        BuildConditionRows();
        BuildHeaderRows();
    }

    private void BuildConditionRows()
    {
        _conditionRows.Children.Clear();
        if (CurrentRoute is not { } route) return;
        foreach (var condition in route.Conditions.ToList())
        {
            var source = new ComboBox { ItemsSource = Sources, SelectedItem = condition.Source, Width = 100 };
            var name = new TextBox { Text = condition.Name, Width = 190, Watermark = "name / json path", IsEnabled = !condition.Source.Equals("body", StringComparison.OrdinalIgnoreCase) };
            var op = new ComboBox { ItemsSource = Ops, SelectedItem = condition.Op, Width = 120 };
            var value = new TextBox { Text = condition.Value, Width = 220, Watermark = "value", IsEnabled = !condition.Op.EndsWith("exists", StringComparison.OrdinalIgnoreCase) };
            var delete = Small("✕", "Remove this condition", () => { route.Conditions.Remove(condition); MarkEdited(); BuildConditionRows(); });
            source.SelectionChanged += (_, _) => { if (source.SelectedItem is string s) { Edit(() => condition.Source = s); name.IsEnabled = s != "body"; } };
            op.SelectionChanged += (_, _) => { if (op.SelectedItem is string o) { Edit(() => condition.Op = o); value.IsEnabled = !o.EndsWith("exists", StringComparison.Ordinal); } };
            name.TextChanged += (_, _) => Edit(() => condition.Name = name.Text ?? "");
            value.TextChanged += (_, _) => Edit(() => condition.Value = value.Text ?? "");
            _conditionRows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { source, name, op, value, delete } });
        }
    }

    private void BuildHeaderRows()
    {
        _headerRows.Children.Clear();
        var response = CurrentResponse;
        foreach (var header in response.Headers.ToList())
        {
            var name = new TextBox { Text = header.Name, Width = 200, Watermark = "Header-Name" };
            var value = new TextBox { Text = header.Value, Width = 340, Watermark = "value (templates allowed)" };
            var delete = Small("✕", "Remove this header", () => { response.Headers.Remove(header); MarkEdited(); BuildHeaderRows(); });
            name.TextChanged += (_, _) => Edit(() => header.Name = name.Text ?? "");
            value.TextChanged += (_, _) => Edit(() => header.Value = value.Text ?? "");
            _headerRows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { name, value, delete } });
        }
    }
}
