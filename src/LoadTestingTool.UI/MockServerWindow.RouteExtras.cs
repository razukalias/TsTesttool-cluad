using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI;

// The parts of a route beyond one fixed answer: answers in turn (a sequence), scenario state changes, and a database action.
public sealed partial class MockServerWindow
{
    private Control BuildSequenceSection()
    {
        var add = Small("＋ Answer", "Add an answer for the next request in turn (the 1st request gets answer 1, the 2nd answer 2 …)", () =>
        {
            if (CurrentRoute is not { } route) return;
            var copy = MockServerConfig.FromJson(new MockServerConfig { Fallback = CurrentResponse }.ToJson()).Fallback;
            route.Sequence.Add(copy);
            _answerIndex = route.Sequence.Count - 1;
            MarkEdited(); LoadSelected();
        });
        var remove = Small("✕ Answer", "Remove the answer shown from the sequence", () =>
        {
            if (CurrentRoute is not { } route || _answerIndex < 0 || _answerIndex >= route.Sequence.Count) return;
            route.Sequence.RemoveAt(_answerIndex);
            _answerIndex = Math.Min(_answerIndex, route.Sequence.Count - 1);
            MarkEdited(); LoadSelected();
        });
        _answerPicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _answerPicker.SelectedIndex < 0) return;
            _answerIndex = _answerPicker.SelectedIndex - 1;
            LoadSelected();
        };
        _sequenceLoop.IsCheckedChanged += (_, _) => EditRoute(r => r.SequenceLoop = _sequenceLoop.IsChecked == true);
        ToolTip.SetTip(_answerPicker, "A route can answer differently in turn: request 1 gets the first answer, request 2 the second … After the list, the route answers with its normal response (or starts the list again). Reset state (Monitor tab) starts every list from the top.");
        _sequencePanel.Children.Add(new WrapPanel { Children = { Labeled("Answer shown below", _answerPicker), Pad(add), Pad(remove), Pad(_sequenceLoop) } });
        return _sequencePanel;
    }

    private void BuildStateRows()
    {
        _stateRows.Children.Clear();
        if (CurrentRoute is not { } route) return;
        foreach (var change in route.SetState.ToList())
        {
            var scenario = new TextBox { Text = change.Scenario, Width = 170, Watermark = "scenario (e.g. auth)" };
            var state = new TextBox { Text = change.State, Width = 170, Watermark = "new state (empty: Started)" };
            var delete = Small("✕", "Remove this state change", () => { route.SetState.Remove(change); MarkEdited(); BuildStateRows(); });
            scenario.TextChanged += (_, _) => Edit(() => change.Scenario = scenario.Text?.Trim() ?? "");
            state.TextChanged += (_, _) => Edit(() => change.State = state.Text?.Trim() ?? "");
            _stateRows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { DashboardStyle.Text("set", 12, false, DashboardStyle.Muted), scenario, DashboardStyle.Text("to", 12, false, DashboardStyle.Muted), state, delete } });
        }
    }

    // ----- database action -----

    private Control BuildDataSection()
    {
        _dataOn.IsCheckedChanged += (_, _) =>
        {
            if (_loading || CurrentRoute is not { } route) return;
            var on = _dataOn.IsChecked == true;
            Edit(() => route.Data = on ? route.Data ?? new MockDataAction { Database = DatabaseChoices().FirstOrDefault() ?? "", Sql = "select * from mytable" } : null);
            _dataPanel.IsVisible = on;
            LoadDataFields(route); BuildDataParamRows();
        };
        _dataDb.SelectionChanged += (_, _) => { if (!_loading && _dataDb.SelectedItem is string db) EditData(d => d.Database = db); };
        _dataSql.TextChanged += (_, _) => { EditData(d => d.Sql = _dataSql.Text ?? ""); UpdateSqlHint(); };
        _notFoundStatus.ValueChanged += (_, _) => EditData(d => d.NotFoundStatus = (int)(_notFoundStatus.Value ?? 0));
        _notFoundBody.TextChanged += (_, _) => EditData(d => d.NotFoundBody = _notFoundBody.Text ?? "");
        var addParam = Small("＋ Parameter", "A @name in the SQL, filled from the request: {{json.customer}}, {{query.page}} … (path values such as @id are filled by themselves)", () =>
        {
            if (CurrentRoute?.Data is not { } data) return;
            var name = "param" + (data.Parameters.Count + 1);
            data.Parameters[name] = "{{json." + name + "}}";
            MarkEdited(); BuildDataParamRows();
        });
        var refresh = Small("⟳", "Read the list of databases again", RefreshDatabaseChoices);
        var manage = Small("Databases…", "Open the Databases tab to make databases and tables", () => { if (_tabs is not null) _tabs.SelectedIndex = 1; });
        ToolTip.SetTip(_notFoundStatus, "When the SQL finds or changes no row, answer this status instead (404 for a missing order). 0: always answer normally.");
        _dataPanel.Children.Add(new WrapPanel { Children = { Labeled("Database", _dataDb), Pad(refresh), Pad(manage) } });
        _dataPanel.Children.Add(Labeled("SQL (one or more statements; @name parameters)", _dataSql));
        _dataPanel.Children.Add(_sqlHint);
        _dataPanel.Children.Add(Section("Parameters", addParam, _dataParamRows));
        _dataPanel.Children.Add(new WrapPanel { Children = { Labeled("When nothing is found: status (0 = off)", _notFoundStatus), Labeled("and body", _notFoundBody) } });
        _dataPanel.Children.Add(DashboardStyle.Text("In the response: {{row}} the first row as JSON, {{row.status}} one of its values, {{rows}} all rows as a JSON array, {{rowcount}}, {{affected}} (rows changed), {{lastid}} (id of an inserted row).", 11, false, DashboardStyle.Muted));
        var box = new StackPanel { Spacing = 6, Children = { _dataOn, _dataPanel } };
        return box;
    }

    private void EditData(Action<MockDataAction> change)
    {
        if (_loading || CurrentRoute?.Data is not { } data) return;
        Edit(() => change(data));
    }

    /// <summary>The databases on the Databases tab, as a route refers to them (Databases/name.db).</summary>
    internal List<string> DatabaseChoices() => MockDatabase.List(_serversFolder).Select(MockDatabase.RouteReference).ToList();

    private void RefreshDatabaseChoices()
    {
        if (CurrentRoute is { } route) { _loading = true; LoadDataFields(route); _loading = false; }
    }

    private void LoadDataFields(MockRoute route)
    {
        var data = route.Data;
        var choices = DatabaseChoices();
        if (data is not null && data.Database.Length > 0 && !choices.Contains(data.Database, StringComparer.OrdinalIgnoreCase)) choices.Insert(0, data.Database);
        _dataDb.ItemsSource = choices;
        _dataDb.SelectedItem = data is null ? null : choices.FirstOrDefault(c => c.Equals(data.Database, StringComparison.OrdinalIgnoreCase));
        _dataSql.Text = data?.Sql ?? "";
        _notFoundStatus.Value = data?.NotFoundStatus ?? 0;
        _notFoundBody.Text = data?.NotFoundBody ?? "";
        UpdateSqlHint();
    }

    /// <summary>Names the @parameters of the SQL that neither the path nor the parameter list fills.</summary>
    private void UpdateSqlHint()
    {
        if (CurrentRoute is not { Data: { } data } route) { _sqlHint.Text = ""; return; }
        var used = System.Text.RegularExpressions.Regex.Matches(data.Sql, @"[@$:]([A-Za-z_][A-Za-z0-9_]*)").Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var fromPath = MockPath.Compile(route.Path)?.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToList() ?? [];
        var open = used.Where(u => !fromPath.Contains(u, StringComparer.OrdinalIgnoreCase) && !data.Parameters.ContainsKey(u)).ToList();
        _sqlHint.Text = used.Count == 0 ? "" : open.Count == 0
            ? "Parameters: " + string.Join(", ", used.Select(u => "@" + u + (fromPath.Contains(u, StringComparer.OrdinalIgnoreCase) ? " (from the path)" : "")))
            : "Not filled yet: " + string.Join(", ", open.Select(u => "@" + u)) + " – add them as parameters (an unfilled parameter is an error).";
        _sqlHint.Foreground = open.Count == 0 ? DashboardStyle.Muted : DashboardStyle.Red;
    }

    private void BuildDataParamRows()
    {
        _dataParamRows.Children.Clear();
        if (CurrentRoute?.Data is not { } data) return;
        foreach (var name in data.Parameters.Keys.ToList())
        {
            var current = name;
            var key = new TextBox { Text = name, Width = 150, Watermark = "name (without @)" };
            var value = new TextBox { Text = data.Parameters[name], Width = 300, Watermark = "{{json.customer}}" };
            var delete = Small("✕", "Remove this parameter", () => { data.Parameters.Remove(current); MarkEdited(); BuildDataParamRows(); UpdateSqlHint(); });
            key.LostFocus += (_, _) =>
            {
                var fresh = (key.Text ?? "").Trim().TrimStart('@');
                if (fresh.Length == 0 || fresh.Equals(current, StringComparison.Ordinal) || data.Parameters.ContainsKey(fresh)) { key.Text = current; return; }
                Edit(() => { var v = data.Parameters[current]; data.Parameters.Remove(current); data.Parameters[fresh] = v; });
                current = fresh; UpdateSqlHint();
            };
            value.TextChanged += (_, _) => Edit(() => data.Parameters[current] = value.Text ?? "");
            _dataParamRows.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { DashboardStyle.Text("@", 12, false, DashboardStyle.Muted), key, DashboardStyle.Text("=", 12, false, DashboardStyle.Muted), value, delete } });
        }
    }
}
