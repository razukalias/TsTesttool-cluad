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
    private readonly NumericUpDown _delayMax = new() { Minimum = 0, Maximum = 600000, Increment = 100, FormatString = "0", Width = 150 };
    private readonly StackPanel _headerRows = new() { Spacing = 4 };
    private readonly TextBox _body = new() { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 170 };
    private readonly TextBox _bodyFile = new() { Watermark = "optional: file in the server's folder, replaces the body", MinWidth = 320 };
    // Sequence: which answer the response fields edit (-1: the normal response).
    private int _answerIndex = -1;
    private readonly ComboBox _answerPicker = new() { MinWidth = 260 };
    private readonly CheckBox _sequenceLoop = new() { Content = "start the list again after the last answer" };
    private readonly StackPanel _sequencePanel = new() { Spacing = 6 };
    private readonly StackPanel _stateRows = new() { Spacing = 4 };
    // Database action
    private readonly CheckBox _dataOn = new() { Content = "Work with a mock database before answering" };
    private readonly StackPanel _dataPanel = new() { Spacing = 6 };
    private readonly ComboBox _dataDb = new() { MinWidth = 260 };
    private readonly TextBox _dataSql = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 70, Watermark = "select * from orders where id = @id" };
    private readonly StackPanel _dataParamRows = new() { Spacing = 4 };
    private readonly NumericUpDown _notFoundStatus = new() { Minimum = 0, Maximum = 599, Increment = 1, FormatString = "0", Width = 110 };
    private readonly TextBox _notFoundBody = new() { MinWidth = 360, Watermark = "{\"error\":\"not found\"}" };
    private readonly TextBlock _sqlHint = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);

    private MockRoute? CurrentRoute => _selected >= 1 && _selected <= _config.Routes.Count ? _config.Routes[_selected - 1] : null;
    internal MockResponse CurrentResponse => CurrentRoute is { } route
        ? (_answerIndex >= 0 && _answerIndex < route.Sequence.Count ? route.Sequence[_answerIndex] : route.Response)
        : _config.Fallback;
    /// <summary>-1: the response fields edit the route's normal response; n: answer n+1 of its sequence.</summary>
    internal int AnswerIndex { get => _answerIndex; set { _answerIndex = value; LoadSelected(); } }

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
        var graphQl = Small("GQL", "Add a GraphQL operation: POST /graphql answered when the request asks for this operation", () =>
        {
            var name = "getOrder";
            _config.Routes.Add(new MockRoute
            {
                Name = "GraphQL " + name, Method = "POST", Path = "/graphql",
                Conditions = [new MockCondition { Source = "graphql", Name = "operation", Op = "equals", Value = name }],
                Response = new MockResponse { Body = "{\"data\":{\"order\":{\"id\":\"{{json.variables.id}}\",\"status\":\"NEW\"}}}" }
            });
            _selected = _config.Routes.Count; MarkChanged();
        });
        var soap = Small("SOAP", "Add a SOAP operation (POST /Orders.svc, answered for one operation) and a ?wsdl route", () =>
        {
            _config.Routes.Add(new MockRoute
            {
                Name = "SOAP GetOrder", Method = "POST", Path = "/Orders.svc",
                Conditions = [new MockCondition { Source = "soap", Name = "operation", Op = "equals", Value = "GetOrder" }],
                Response = new MockResponse { ContentType = "text/xml; charset=utf-8", Body = SoapSample.Response }
            });
            _config.Routes.Add(new MockRoute
            {
                Name = "WSDL", Method = "GET", Path = "/Orders.svc",
                Conditions = [new MockCondition { Source = "query", Name = "wsdl", Op = "exists" }],
                Response = new MockResponse { ContentType = "text/xml; charset=utf-8", Body = SoapSample.Wsdl }
            });
            _selected = _config.Routes.Count - 1; MarkChanged();
        });
        var wsdl = Small("WSDL…", "Import a WSDL: a route per operation (answered with a sample reply) and a ?wsdl route that serves it", () =>
        {
            var window = new WsdlImportWindow("Import a WSDL into the mock", "Add routes", true, (loaded, operations) =>
            {
                var routes = LoadTestingTool.MockServer.WsdlMock.Routes(loaded.Info, loaded.Text, operations.Select(o => o.Name).ToList());
                if (routes.Count == 0) return;
                _config.Routes.AddRange(routes); _selected = _config.Routes.Count; MarkChanged();
            });
            window.Show(this);
        });
        var up = Small("▲", "Move up: routes are tried from the top", () => Move(-1));
        var down = Small("▼", "Move down", () => Move(1));
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Children = { add, graphQl, soap, wsdl, copy, remove, up, down } };
        foreach (var tool in tools.Children) tool.Margin = new Thickness(0, 0, 4, 4);
        _routeList.SelectionChanged += (_, _) => { if (_refreshing || _routeList.SelectedIndex < 0) return; _selected = _routeList.SelectedIndex; _answerIndex = -1; LoadSelected(); };
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
        _delayMax.ValueChanged += (_, _) => EditResponse(r => r.DelayMaxMs = (int)(_delayMax.Value ?? 0));
        _body.TextChanged += (_, _) => EditResponse(r => r.Body = _body.Text ?? "");
        _bodyFile.TextChanged += (_, _) => EditResponse(r => r.BodyFile = _bodyFile.Text?.Trim() ?? "");

        var addCondition = DashboardStyle.Action("＋ Condition", () => { if (CurrentRoute is { } route) { route.Conditions.Add(new MockCondition()); MarkEdited(); BuildConditionRows(); } });
        var addHeader = DashboardStyle.Action("＋ Header", () => { CurrentResponse.Headers.Add(new MockHeader()); MarkEdited(); BuildHeaderRows(); });
        var addState = DashboardStyle.Action("＋ State change", () => { if (CurrentRoute is { } route) { route.SetState.Add(new MockStateChange { Scenario = "scenario", State = "next" }); MarkEdited(); BuildStateRows(); } });
        _routeOnly.Children.Add(new WrapPanel { Children = { Labeled("Name", _routeName), Labeled("Method", _method), Labeled("Path", _routePath), Pad(_enabled) } });
        _routeOnly.Children.Add(Section("Only when – conditions without a group must all hold; in a group (same name) one is enough", addCondition, _conditionRows));
        _routeOnly.Children.Add(new WrapPanel { Children = { Labeled("Fail % of requests", _failPercent), Pad(_drop) } });
        _routeOnly.Children.Add(Section("After answering, move scenarios to a new state (test it with a 'state' condition)", addState, _stateRows));
        _routeOnly.Children.Add(BuildDataSection());

        var editor = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 6, 12) };
        editor.Children.Add(_routeOnly);
        editor.Children.Add(DashboardStyle.Text("Response", 14, true));
        editor.Children.Add(BuildSequenceSection());
        editor.Children.Add(new WrapPanel { Children = { Labeled("Status", _statusCode), Labeled("Content type", _contentType), Labeled("Response time from (ms)", _delay), Labeled("to (ms, random; 0 = fixed)", _delayMax) } });
        editor.Children.Add(Section("Headers", addHeader, _headerRows));
        editor.Children.Add(Labeled("Body  ({{path.id}}, {{query.x}}, {{header.x}}, {{cookie.x}}, {{json.a.b}}, {{body}}, {{guid}}, {{now}}, {{count}}, {{state.x}}; with a database: {{row}}, {{row.col}}, {{rows}}, {{rowcount}}, {{affected}}, {{lastid}})", _body));
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
            if (_answerIndex >= route.Sequence.Count) _answerIndex = -1;
            _sequenceLoop.IsChecked = route.SequenceLoop;
            _answerPicker.ItemsSource = new[] { route.Sequence.Count == 0 ? "Every request (no sequence)" : $"After the sequence (request {route.Sequence.Count + 1} on)" }
                .Concat(route.Sequence.Select((a, i) => $"Request {i + 1}: {a.Status}")).ToList();
            _answerPicker.SelectedIndex = _answerIndex + 1;
            _dataOn.IsChecked = route.Data is not null;
            _dataPanel.IsVisible = route.Data is not null;
            LoadDataFields(route);
        }
        _sequencePanel.IsVisible = route is not null;
        var response = CurrentResponse;
        _statusCode.Value = response.Status; _contentType.Text = response.ContentType; _delay.Value = response.DelayMs; _delayMax.Value = response.DelayMaxMs; _body.Text = response.Body; _bodyFile.Text = response.BodyFile;
        _loading = false;
        BuildConditionRows();
        BuildHeaderRows();
        BuildStateRows();
        BuildDataParamRows();
    }

    private void BuildConditionRows()
    {
        _conditionRows.Children.Clear();
        if (CurrentRoute is not { } route) return;
        foreach (var condition in route.Conditions.ToList())
        {
            var source = new ComboBox { ItemsSource = Sources, SelectedItem = condition.Source.ToLowerInvariant(), Width = 100 };
            ToolTip.SetTip(source, "Where the value comes from: query, header, cookie, path ({value} of the path), method, body, json, xml, graphql, soap, or state (a scenario's state)");
            var name = new TextBox { Text = condition.Name, Width = 170, Watermark = NameHint(condition.Source), IsEnabled = NeedsName(condition.Source) };
            var op = new ComboBox { ItemsSource = Ops, SelectedItem = condition.Op.ToLowerInvariant(), Width = 120 };
            ToolTip.SetTip(op, "equals / notequals, contains / notcontains, startswith, endswith, regex; gt ge lt le compare numbers or dates; between 10..20; in / notin A,B,C; exists / notexists");
            var value = new TextBox { Text = condition.Value, Width = 190, Watermark = ValueHint(condition.Op), IsEnabled = !condition.Op.EndsWith("exists", StringComparison.OrdinalIgnoreCase) };
            var anyCase = new CheckBox { Content = "Aa", IsChecked = condition.IgnoreCase, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(anyCase, "Ignore upper/lower case (contains always does)");
            var group = new TextBox { Text = condition.Group, Width = 80, Watermark = "or-group" };
            ToolTip.SetTip(group, "Empty: this condition must hold. A name: the route needs only one of the conditions with that name (\"or\").");
            var delete = Small("✕", "Remove this condition", () => { route.Conditions.Remove(condition); MarkEdited(); BuildConditionRows(); });
            source.SelectionChanged += (_, _) => { if (source.SelectedItem is string s) { Edit(() => condition.Source = s); name.IsEnabled = NeedsName(s); name.Watermark = NameHint(s); } };
            op.SelectionChanged += (_, _) => { if (op.SelectedItem is string o) { Edit(() => condition.Op = o); value.IsEnabled = !o.EndsWith("exists", StringComparison.Ordinal); value.Watermark = ValueHint(o); } };
            name.TextChanged += (_, _) => Edit(() => condition.Name = name.Text ?? "");
            value.TextChanged += (_, _) => Edit(() => condition.Value = value.Text ?? "");
            anyCase.IsCheckedChanged += (_, _) => Edit(() => condition.IgnoreCase = anyCase.IsChecked == true);
            group.TextChanged += (_, _) => Edit(() => condition.Group = group.Text?.Trim() ?? "");
            _conditionRows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { source, name, op, value, anyCase, group, delete } });
        }
    }

    private static bool NeedsName(string source) => source.ToLowerInvariant() is not ("body" or "method");

    private static string NameHint(string source) => source.ToLowerInvariant() switch
    {
        "json" => "json path: order.lines[0].sku", "xml" => "xml path: GetOrder.Id", "path" => "path value: id",
        "graphql" => "operation / type / field", "soap" => "operation / action / version", "state" => "scenario name",
        "body" or "method" => "(no name)", _ => "name"
    };

    private static string ValueHint(string op) => op.ToLowerInvariant() switch
    {
        "between" => "10..20", "in" or "notin" => "A,B,C", "gt" or "ge" or "lt" or "le" => "number or date", "regex" => "regular expression",
        "exists" or "notexists" => "(no value)", _ => "value"
    };

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
