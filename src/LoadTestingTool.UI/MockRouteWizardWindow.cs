using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI;

/// <summary>
/// Makes routes in three steps: what kind of answer, its details, and a review. The kinds: a fixed answer, answers that depend on the
/// request, answers in turn, a login flow, and a REST API on a database table.
/// </summary>
public sealed class MockRouteWizardWindow : Window
{
    internal enum Kind { Fixed, ByRequest, InTurn, Login, RestApi }

    private static readonly (Kind Kind, string Title, string Text)[] Kinds =
    [
        (Kind.Fixed, "A fixed answer", "One method and path, always the same status and body (the body can still use {{path.id}}, {{json.x}} …). Example: GET /health → 200 {\"status\":\"ok\"}."),
        (Kind.ByRequest, "Different answers depending on the request", "Same method and path, but the answer depends on a value: missing field → 400, quantity over 99 → 422, a VIP customer → special price, everything else → 201."),
        (Kind.InTurn, "Answers in turn", "The 1st request gets one answer, the 2nd another …: a service that fails twice then works (to test retries), or a job that is 'running' then 'done'."),
        (Kind.Login, "A login flow", "POST /login, a protected path that answers 401 until someone logs in, and POST /logout. Uses a scenario state, so it remembers the login."),
        (Kind.RestApi, "A REST API on a database table", "List, get, create, update and delete rows of a mock database table: data you POST is really stored and comes back on GET.")
    ];

    private readonly string _serversFolder;
    private readonly ContentControl _page = new();
    private readonly Button _back;
    private readonly Button _next;
    private readonly TextBlock _problem = DashboardStyle.Text("", 12, false, DashboardStyle.Red);
    private readonly TextBlock _stepTitle = DashboardStyle.Text("", 18, true);
    private int _step;
    private Kind _kind = Kind.Fixed;
    private readonly ListBox _kindList = new();
    private readonly CheckBox _atTop = new() { Content = "Put the new routes at the top of the list (they are tried first)", IsChecked = true };

    // Shared fields
    private readonly TextBox _name = new() { Width = 220, Text = "Orders" };
    private readonly ComboBox _method = new() { ItemsSource = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "ANY" }, SelectedIndex = 0, Width = 110 };
    private readonly TextBox _path = new() { Width = 260, Text = "/orders/{id}" };
    private readonly NumericUpDown _status = new() { Minimum = 100, Maximum = 599, Value = 200, FormatString = "0", Width = 110 };
    private readonly TextBox _contentType = new() { Width = 200, Text = "application/json" };
    private readonly TextBox _body = Code("{\"id\":\"{{path.id}}\",\"status\":\"NEW\"}");
    // By request
    private readonly StackPanel _caseRows = new() { Spacing = 4 };
    private readonly List<(ComboBox Source, TextBox Name, ComboBox Op, TextBox Value, NumericUpDown Status, TextBox Body)> _cases = [];
    private readonly NumericUpDown _defaultStatus = new() { Minimum = 100, Maximum = 599, Value = 201, FormatString = "0", Width = 110 };
    private readonly TextBox _defaultBody = Code("{\"id\":\"{{guid}}\",\"customer\":\"{{json.customer}}\"}");
    // In turn
    private readonly StackPanel _answerRows = new() { Spacing = 4 };
    private readonly List<(NumericUpDown Status, TextBox Body)> _answers = [];
    private readonly NumericUpDown _afterStatus = new() { Minimum = 100, Maximum = 599, Value = 200, FormatString = "0", Width = 110 };
    private readonly TextBox _afterBody = Code("{\"status\":\"ok\"}");
    private readonly CheckBox _loop = new() { Content = "Start the list again after the last answer (instead of the 'after' answer)" };
    // Login
    private readonly TextBox _loginPath = new() { Width = 200, Text = "/login" };
    private readonly TextBox _protectedPath = new() { Width = 200, Text = "/account/**" };
    private readonly TextBox _logoutPath = new() { Width = 200, Text = "/logout" };
    private readonly TextBox _user = new() { Width = 160, Text = "eva", Watermark = "any user" };
    private readonly TextBox _password = new() { Width = 160, Watermark = "any password" };
    private readonly TextBox _protectedBody = Code("{\"name\":\"Eva Lind\",\"tier\":\"gold\"}");
    // REST API
    private readonly ComboBox _database = new() { Width = 220 };
    private readonly ComboBox _table = new() { Width = 220 };
    private readonly ComboBox _key = new() { Width = 160 };
    private readonly TextBox _basePath = new() { Width = 200, Text = "/orders" };
    private readonly TextBox _review = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas,Menlo,monospace"), TextWrapping = TextWrapping.NoWrap, MinHeight = 260 };
    private List<MockRoute> _built = [];

    /// <summary>The routes to add (null when cancelled) and whether they go to the top.</summary>
    public List<MockRoute>? Result { get; private set; }
    public bool AtTop => _atTop.IsChecked == true;
    internal int Step => _step;
    internal string ReviewText => _review.Text ?? "";
    internal string ProblemText => _problem.Text ?? "";

    public MockRouteWizardWindow(string serversFolder)
    {
        _serversFolder = serversFolder;
        Title = "Route wizard";
        Width = 920; Height = 680; MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _back = DashboardStyle.Action("◀ Back", () => Go(_step - 1));
        _next = DashboardStyle.Action("Next ▶", () => Go(_step + 1), "primary");
        var cancel = DashboardStyle.Action("Cancel", Close);
        // Plain text blocks: selectable text would take the click, and the list would not select the kind.
        _kindList.ItemsSource = Kinds.Select(k => (Control)new StackPanel { Margin = new Thickness(4, 6), Spacing = 2, Children =
        {
            new TextBlock { Text = k.Title, FontSize = 14, FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = k.Text, FontSize = 12, Foreground = DashboardStyle.Muted, TextWrapping = TextWrapping.Wrap }
        } }).ToList();
        _kindList.SelectedIndex = 0;
        _kindList.SelectionChanged += (_, _) => { if (_kindList.SelectedIndex >= 0) Choose(Kinds[_kindList.SelectedIndex].Kind); };
        _database.SelectionChanged += (_, _) => LoadTables();
        _table.SelectionChanged += (_, _) => LoadKeys();
        AddCase("json", "customer", "notexists", "", 400, "{\"error\":\"customer is required\"}");
        AddCase("json", "qty", "gt", "99", 422, "{\"error\":\"too many items\"}");
        AddAnswer(503, "{\"error\":\"busy, try again\"}");
        AddAnswer(503, "{\"error\":\"busy, try again\"}");
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(18) };
        root.Children.Add(_stepTitle);
        var scroll = new ScrollViewer { Content = _page, Margin = new Thickness(0, 10, 0, 6) };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Grid.SetRow(_problem, 2); root.Children.Add(_problem);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _back, _next } };
        Grid.SetRow(buttons, 3); root.Children.Add(buttons);
        Content = root;
        Go(0);
    }

    private static TextBox Code(string text) => new() { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), MinHeight = 60, MinWidth = 420 };
    private static TextBlock Wrap(TextBlock text) { text.TextWrapping = TextWrapping.Wrap; return text; }
    private static Control Field(string label, Control control) => new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 12, 8), Children = { DashboardStyle.Text(label, 11, false, DashboardStyle.Muted), control } };

    internal void Choose(Kind kind)
    {
        _kind = kind;
        _kindList.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == kind);
        // Sensible starting values per kind.
        switch (kind)
        {
            case Kind.Fixed: _method.SelectedItem = "GET"; _path.Text = "/health"; _name.Text = "Health"; _status.Value = 200; _body.Text = "{\"status\":\"ok\"}"; break;
            case Kind.ByRequest: _method.SelectedItem = "POST"; _path.Text = "/orders"; _name.Text = "Create order"; break;
            case Kind.InTurn: _method.SelectedItem = "GET"; _path.Text = "/jobs/{id}"; _name.Text = "Flaky job"; break;
        }
    }

    internal void Go(int step)
    {
        _problem.Text = "";
        if (step > _step && _step == 1)
        {
            try { _built = Build(); }
            catch (InvalidOperationException exception) { _problem.Text = exception.Message; return; }
            var problems = new MockServerConfig { Routes = _built }.Validate();
            if (problems.Count > 0) { _problem.Text = string.Join("  •  ", problems.Take(3)); return; }
        }
        if (step == 3) { Result = _built; Close(); return; }
        _step = Math.Clamp(step, 0, 2);
        _back.IsEnabled = _step > 0;
        _next.Content = _step == 2 ? "Add routes" : "Next ▶";
        _stepTitle.Text = _step switch { 0 => "1 · What should the mock do?", 1 => "2 · " + Kinds.First(k => k.Kind == _kind).Title, _ => "3 · Check and add" };
        _page.Content = _step switch { 0 => _kindList, 1 => Details(), _ => ReviewPage() };
    }

    private Control Details()
    {
        var panel = new StackPanel { Spacing = 8 };
        switch (_kind)
        {
            case Kind.Fixed:
                panel.Children.Add(new WrapPanel { Children = { Field("Name", _name), Field("Method", _method), Field("Path ({id} takes a value)", _path) } });
                panel.Children.Add(new WrapPanel { Children = { Field("Status", _status), Field("Content type", _contentType) } });
                panel.Children.Add(Field("Body – templates: {{path.id}} {{query.x}} {{json.a.b}} {{header.x}} {{guid}} {{now}}", _body));
                break;
            case Kind.ByRequest:
                panel.Children.Add(new WrapPanel { Children = { Field("Name", _name), Field("Method", _method), Field("Path", _path) } });
                panel.Children.Add(Wrap(DashboardStyle.Text("Cases, tried from the top: the first one whose condition holds answers. Source: where the value is (json path, query, header, path value …).", 12, false, DashboardStyle.Muted)));
                panel.Children.Add(_caseRows);
                panel.Children.Add(DashboardStyle.Action("＋ Case", () => AddCase("json", "field", "equals", "value", 400, "{\"error\":\"...\"}")));
                panel.Children.Add(DashboardStyle.Text("Everything else", 13, true));
                panel.Children.Add(new WrapPanel { Children = { Field("Status", _defaultStatus), Field("Body", _defaultBody) } });
                break;
            case Kind.InTurn:
                panel.Children.Add(new WrapPanel { Children = { Field("Name", _name), Field("Method", _method), Field("Path", _path) } });
                panel.Children.Add(Wrap(DashboardStyle.Text("Request 1 gets the first answer, request 2 the second … Reset state on the Monitor tab starts again from the first.", 12, false, DashboardStyle.Muted)));
                panel.Children.Add(_answerRows);
                panel.Children.Add(DashboardStyle.Action("＋ Answer", () => AddAnswer(200, "{\"status\":\"running\"}")));
                panel.Children.Add(DashboardStyle.Text("After the list", 13, true));
                panel.Children.Add(new WrapPanel { Children = { Field("Status", _afterStatus), Field("Body", _afterBody) } });
                panel.Children.Add(_loop);
                break;
            case Kind.Login:
                panel.Children.Add(new WrapPanel { Children = { Field("Login path (POST)", _loginPath), Field("Protected path (/** = everything below)", _protectedPath), Field("Logout path (POST)", _logoutPath) } });
                panel.Children.Add(new WrapPanel { Children = { Field("User name (json username)", _user), Field("Password (json password)", _password) } });
                panel.Children.Add(Field("Answer of the protected path when logged in", _protectedBody));
                panel.Children.Add(Wrap(DashboardStyle.Text("Login answers {\"token\":…}. The protected path answers 401 until a login, and again after logout.", 12, false, DashboardStyle.Muted)));
                break;
            case Kind.RestApi:
                LoadDatabases();
                panel.Children.Add(new WrapPanel { Children = { Field("Database", _database), Field("Table", _table), Field("Key column", _key), Field("Base path", _basePath) } });
                panel.Children.Add(Wrap(DashboardStyle.Text("Makes GET base (list, ?limit=), GET base/{id}, POST base (create, answers 201 with the stored row), PUT base/{id} (only the fields sent change) and DELETE base/{id}; a missing id answers 404. No database or table yet? Make one on the Databases tab (or press Sample there).", 12, false, DashboardStyle.Muted)));
                break;
        }
        return panel;
    }

    private Control ReviewPage()
    {
        _review.Text = string.Join("\n\n", _built.Select((r, i) =>
        {
            var lines = new List<string> { $"{i + 1}. {r.Method,-6} {r.Path}    {r.Name}" };
            foreach (var c in r.Conditions) lines.Add($"     when {MockRouteMatcher.Describe(c)}");
            if (r.Sequence.Count > 0) lines.Add($"     answers in turn: {string.Join(", ", r.Sequence.Select(s => s.Status))}, then {(r.SequenceLoop ? "again" : r.Response.Status.ToString())}");
            if (r.Data is { } d) lines.Add($"     database {d.Database}: {d.Sql.Replace("\n", " ")}" + (d.NotFoundStatus > 0 ? $"  (nothing found → {d.NotFoundStatus})" : ""));
            foreach (var s in r.SetState) lines.Add($"     then {s.Scenario} → {(s.State.Length == 0 ? MockStates.Initial : s.State)}");
            if (r.Sequence.Count == 0) lines.Add($"     → {r.Response.Status} {Short(r.Response.Body)}");
            return string.Join("\n", lines);
        }));
        return new StackPanel { Spacing = 8, Children = { Wrap(DashboardStyle.Text($"{_built.Count} route(s) will be added. You can change everything afterwards on the Routes tab; Save, then Run.", 12, false, DashboardStyle.Muted)), _atTop, _review } };
    }

    private static string Short(string body) => body.Length > 90 ? body[..90] + "…" : body;

    /// <summary>The routes the answers so far make.</summary>
    internal List<MockRoute> Build()
    {
        var method = _method.SelectedItem as string ?? "GET";
        var path = (_path.Text ?? "").Trim();
        var name = (_name.Text ?? "").Trim();
        return _kind switch
        {
            Kind.Fixed => MockRouteTemplates.Fixed(name, method, path, (int)(_status.Value ?? 200), _contentType.Text ?? "", _body.Text ?? ""),
            Kind.ByRequest => MockRouteTemplates.ByRequest(name, method, path,
                _cases.Select(c => new MockCase(new MockCondition { Source = c.Source.SelectedItem as string ?? "json", Name = c.Name.Text ?? "", Op = c.Op.SelectedItem as string ?? "equals", Value = c.Value.Text ?? "" }, (int)(c.Status.Value ?? 400), c.Body.Text ?? "")).ToList(),
                (int)(_defaultStatus.Value ?? 200), _defaultBody.Text ?? ""),
            Kind.InTurn => _answers.Count == 0 ? throw new InvalidOperationException("Add at least one answer.")
                : MockRouteTemplates.InTurn(name, method, path, _answers.Select(a => new MockAnswer((int)(a.Status.Value ?? 200), a.Body.Text ?? "")).ToList(), (int)(_afterStatus.Value ?? 200), _afterBody.Text ?? "", _loop.IsChecked == true),
            Kind.Login => MockRouteTemplates.LoginFlow((_loginPath.Text ?? "").Trim(), (_protectedPath.Text ?? "").Trim(), (_logoutPath.Text ?? "").Trim(), _user.Text ?? "", _password.Text ?? "", _protectedBody.Text ?? ""),
            _ => BuildRestApi()
        };
    }

    private List<MockRoute> BuildRestApi()
    {
        if (_database.SelectedItem is not string db) throw new InvalidOperationException("Choose a database (make one on the Databases tab first).");
        if (_table.SelectedItem is not string tableName) throw new InvalidOperationException("Choose a table (the database has none yet: design one on the Databases tab).");
        if (_key.SelectedItem is not string key) throw new InvalidOperationException("Choose the key column.");
        var table = MockDatabase.Tables(MockDatabase.PathOf(_serversFolder, db)).First(t => t.Name == tableName);
        return MockRouteTemplates.RestApi(table, key, MockDatabase.RouteReference(db), _basePath.Text ?? "/" + tableName);
    }

    // ----- REST API choices -----

    internal void LoadDatabases()
    {
        var names = MockDatabase.List(_serversFolder);
        var keep = _database.SelectedItem as string;
        _database.ItemsSource = names;
        _database.SelectedItem = names.Contains(keep ?? "") ? keep : names.FirstOrDefault();
    }

    private void LoadTables()
    {
        if (_database.SelectedItem is not string db) { _table.ItemsSource = null; return; }
        try
        {
            var tables = MockDatabase.Tables(MockDatabase.PathOf(_serversFolder, db)).Select(t => t.Name).ToList();
            _table.ItemsSource = tables;
            _table.SelectedItem = tables.FirstOrDefault(t => t.Equals("orders", StringComparison.OrdinalIgnoreCase)) ?? tables.FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException) { _problem.Text = "The database could not be read: " + exception.Message; }
    }

    private void LoadKeys()
    {
        if (_database.SelectedItem is not string db || _table.SelectedItem is not string tableName) { _key.ItemsSource = null; return; }
        var table = MockDatabase.Tables(MockDatabase.PathOf(_serversFolder, db)).First(t => t.Name == tableName);
        var columns = table.Columns.Select(c => c.Name).ToList();
        _key.ItemsSource = columns;
        _key.SelectedItem = table.Columns.FirstOrDefault(c => c.PrimaryKey)?.Name ?? columns.FirstOrDefault();
        _basePath.Text = "/" + tableName.Replace(' ', '-').Replace('_', '-');
    }

    internal void SelectRestTarget(string database, string table)
    {
        LoadDatabases();
        _database.SelectedItem = database;
        _table.SelectedItem = table;
    }

    // ----- rows -----

    private void AddCase(string source, string name, string op, string value, int status, string body)
    {
        var s = new ComboBox { ItemsSource = MockServerConfig.Sources, SelectedItem = source, Width = 95 };
        var n = new TextBox { Text = name, Width = 130, Watermark = "name / path" };
        var o = new ComboBox { ItemsSource = MockServerConfig.Ops, SelectedItem = op, Width = 110 };
        var v = new TextBox { Text = value, Width = 110, Watermark = "value" };
        var st = new NumericUpDown { Minimum = 100, Maximum = 599, Value = status, FormatString = "0", Width = 100 };
        var b = new TextBox { Text = body, Width = 260, FontFamily = new FontFamily("Consolas,Menlo,monospace") };
        var entry = (s, n, o, v, st, b);
        var remove = new Button { Content = "✕", Padding = new Thickness(8, 3) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { DashboardStyle.Text("when", 12, false, DashboardStyle.Muted), s, n, o, v, DashboardStyle.Text("→", 12, false, DashboardStyle.Muted), st, b, remove } };
        remove.Click += (_, _) => { _cases.Remove(entry); _caseRows.Children.Remove(row); };
        _cases.Add(entry);
        _caseRows.Children.Add(row);
    }

    private void AddAnswer(int status, string body)
    {
        var st = new NumericUpDown { Minimum = 100, Maximum = 599, Value = status, FormatString = "0", Width = 100 };
        var b = new TextBox { Text = body, Width = 420, FontFamily = new FontFamily("Consolas,Menlo,monospace") };
        var entry = (st, b);
        var remove = new Button { Content = "✕", Padding = new Thickness(8, 3) };
        var label = DashboardStyle.Text($"request {_answers.Count + 1}", 12, false, DashboardStyle.Muted);
        label.Width = 80;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { label, st, b, remove } };
        remove.Click += (_, _) => { _answers.Remove(entry); _answerRows.Children.Remove(row); Renumber(); };
        _answers.Add(entry);
        _answerRows.Children.Add(row);
    }

    private void Renumber()
    {
        for (var i = 0; i < _answerRows.Children.Count; i++)
            if (_answerRows.Children[i] is StackPanel { Children: [TextBlock label, ..] }) label.Text = $"request {i + 1}";
    }
}
