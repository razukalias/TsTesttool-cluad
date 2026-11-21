using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The condition wizard: one row per comparison (value, verb, value), joined with and / or / xor, with "not" and brackets, so any condition of an If, a While or the
/// stop-when / skip-when of a loop can be built without typing it. A ＋ adds a row; the values come from a ⋯ menu (response value, status code or header of an
/// earlier step, a cell of the current data row, a loop variable). The text it builds is shown live and checked.
/// </summary>
public sealed class ConditionBuilderWindow : Window
{
    private static readonly string[] Verbs = ["eq", "ne", "gt", "gte", "lt", "lte", "contains", "notcontains", "startswith", "endswith", "regex", "in", "size", "exists", "notexists", "empty", "notempty"];
    private static readonly string IsTrue = "is true";
    private static readonly (string Text, bool Not, int Open)[] OpenChoices = [("", false, 0), ("(", false, 1), ("((", false, 2), ("(((", false, 3), ("not (", true, 1), ("not ((", true, 2)];
    private static readonly string[] CloseChoices = ["", ")", "))", ")))"];

    private readonly Func<IReadOnlyList<string>> _responseTargets;
    private readonly Func<IReadOnlyList<string>> _dataTargets;
    private readonly Func<IReadOnlyList<string>> _loopVariables;
    private readonly StackPanel _rowsPanel = new() { Spacing = 6 };
    private readonly List<RowView> _rows = [];
    private readonly TextBox _preview = new() { IsReadOnly = true, Tag = "cond-preview", TextWrapping = TextWrapping.Wrap, MinHeight = 44 };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "cond-status" };
    private readonly Button _ok = new() { Content = "Use this condition", Classes = { "accent" }, Tag = "cond-ok" };
    private bool _loading;

    /// <summary>The condition text to write into the step, or null when the window was cancelled.</summary>
    public string? Result { get; private set; }

    /// <summary>The condition as the rows stand now.</summary>
    public string CurrentText => ConditionParts.Build(_rows.Select((r, i) => r.ToPart(i == 0)));

    public int RowCount => _rows.Count;

    /// <summary>True when the text the window was opened with could be shown as rows (an empty text can).</summary>
    public bool OpenedAsRows { get; }

    public ConditionBuilderWindow(DataEditorViewModel? editor, string? stepName, string? initial, string title = "Build condition")
        : this(() => editor?.ReferenceTargets(stepName) ?? [], () => editor?.RequestDataTargets() ?? [], () => editor?.LoopVariablesFor(stepName) ?? ["env"], initial, title) { }

    /// <summary>The wizard over any source of names: what earlier steps give (Step.path, Step.statusCode, Step.responseHeader.Name), the data row's columns and the loop variables.</summary>
    public ConditionBuilderWindow(Func<IReadOnlyList<string>> responseTargets, Func<IReadOnlyList<string>> dataTargets, Func<IReadOnlyList<string>> loopVariables, string? initial, string title = "Build condition")
    {
        _responseTargets = responseTargets;
        _dataTargets = dataTargets;
        _loopVariables = loopVariables;
        Title = title;
        Width = 1100;
        Height = 560;
        MinWidth = 760;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var parts = ConditionParts.Parse(initial);
        OpenedAsRows = parts is not null;
        _loading = true;
        if (parts is { Count: > 0 }) foreach (var part in parts) AddRow(part);
        else AddRow(new ConditionPart("", false, 0, false, "", "eq", "", 0));
        _loading = false;

        var add = new Button { Content = "＋ Add condition", Tag = "cond-add", HorizontalAlignment = HorizontalAlignment.Left };
        ToolTip.SetTip(add, "Another comparison, joined to the ones above with and, or or xor");
        add.Click += (_, _) => { AddRow(new ConditionPart("and", false, 0, false, "", "eq", "", 0)); Refresh(); };
        var cancel = new Button { Content = "Cancel", Tag = "cond-cancel", Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close(null);
        _ok.Click += (_, _) => { Result = CurrentText; Close(Result); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _ok } };

        var help = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush.Parse("#8EA4BC"),
            Text = "Each row is one comparison. Rows are joined with and, or, xor (not binds tightest, then and, then xor, then or). Use the bracket boxes to group rows like ( … ) and “not (” to turn a whole group around. "
                 + (OpenedAsRows ? "" : "The condition this step has now cannot be shown as rows, so you start from an empty row; Cancel keeps it as it is."),
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("84,96,48,*,126,*,72,34"), Margin = new Thickness(0, 6, 0, 2) };
        string[] heads = ["Join", "Open", "Not", "Value", "Verb", "Compare with", "Close", ""];
        for (var i = 0; i < heads.Length; i++) { var h = new TextBlock { Text = heads[i], FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), Margin = new Thickness(4, 0, 0, 0) }; Grid.SetColumn(h, i); header.Children.Add(h); }

        var layout = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(help, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var result = new StackPanel { Spacing = 4, Margin = new Thickness(0, 10, 0, 10), Children = { new TextBlock { Text = "THE CONDITION", FontSize = 11, Foreground = Brush.Parse("#8EA4BC") }, _preview, _status } };
        DockPanel.SetDock(result, Dock.Bottom);
        layout.Children.Add(help);
        layout.Children.Add(header);
        layout.Children.Add(buttons);
        layout.Children.Add(result);
        layout.Children.Add(new ScrollViewer { Content = new StackPanel { Spacing = 8, Children = { _rowsPanel, add } }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = layout;
        Refresh();
    }

    /// <summary>Adds a row for a comparison (the wizard's ＋ does this; tests use it too).</summary>
    public void AddRow(ConditionPart part)
    {
        var row = new RowView(this, part);
        _rows.Add(row);
        _rowsPanel.Children.Add(row.Layout);
        UpdateFirstRow();
    }

    private void RemoveRow(RowView row)
    {
        if (_rows.Count <= 1) { row.Clear(); Refresh(); return; }
        _rows.Remove(row);
        _rowsPanel.Children.Remove(row.Layout);
        UpdateFirstRow();
        Refresh();
    }

    private void UpdateFirstRow() { for (var i = 0; i < _rows.Count; i++) _rows[i].SetFirst(i == 0); }

    /// <summary>Sets one row's fields (for tests and for filling from outside).</summary>
    public void SetRow(int index, ConditionPart part) { _loading = true; _rows[index].Load(part); _loading = false; Refresh(); }

    private void Refresh()
    {
        if (_loading) return;
        var text = CurrentText;
        _preview.Text = text;
        var problem = ConditionEvaluator.Check(text);
        if (problem is not null) { _status.Text = "⚠ " + problem; _status.Foreground = Brush.Parse("#F6C76B"); _ok.IsEnabled = false; return; }
        _ok.IsEnabled = text.Length > 0;
        var unresolved = text.Contains("<From_", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(text, @"_[\p{L}\p{N}._-]+_");
        if (text.Length == 0) { _status.Text = "Choose a value and a verb in the first row."; _status.Foreground = Brush.Parse("#8EA4BC"); }
        else if (unresolved) { _status.Text = "✓ Well formed. It reads values from the run, so it is evaluated when the step runs."; _status.Foreground = Brush.Parse("#39D39B"); }
        else { _status.Text = "✓ Well formed. With these values it is " + (ConditionEvaluator.Evaluate(text) ? "true." : "false."); _status.Foreground = Brush.Parse("#39D39B"); }
    }

    private async Task PickAsync(TextBox target, string kind)
    {
        ResponseValuePicker? picker = null;
        var all = _responseTargets();
        var header = AssertionEngine.ResponseHeaderPrefix;
        switch (kind)
        {
            case "response":
                picker = new ResponseValuePicker(all.Where(t => !t.EndsWith(".statusCode", StringComparison.OrdinalIgnoreCase) && !t.Contains("." + header, StringComparison.OrdinalIgnoreCase)).ToList(), ResponseValuePicker.ReferenceFor, "Value from a step's response", "The fields of each step's expected columns and of its last stored response.");
                break;
            case "status":
                picker = new ResponseValuePicker(all.Where(t => t.EndsWith(".statusCode", StringComparison.OrdinalIgnoreCase)).ToList(), ResponseValuePicker.ReferenceFor, "Status code of a step", "The HTTP status code the step answered with.");
                break;
            case "header":
                picker = new ResponseValuePicker(all.Where(t => t.Contains("." + header, StringComparison.OrdinalIgnoreCase)).ToList(), ResponseValuePicker.ReferenceFor, "Response header of a step", "Headers of the last stored response of each step; run the testcase with response history on to see them.");
                break;
            case "data":
                picker = new ResponseValuePicker(_dataTargets(), t => $"<From_request_{t}_r>", "Cell of the current data row", "The request columns of the workbook; the value is the one in the data row that is running.");
                break;
            case "loop":
                picker = new ResponseValuePicker(_loopVariables(), t => $"_{t}_", "Loop variable or environment", "The counter and item of the loops this step is in, the round of a While, and the environment name (env).");
                break;
        }
        if (picker is null) return;
        var chosen = await picker.ShowDialog<string?>(this);
        if (chosen is null) return;
        var text = target.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(target.SelectionStart, target.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(target.SelectionStart, target.SelectionEnd), start, text.Length);
        target.Text = text[..start] + chosen + text[end..];
        target.CaretIndex = start + chosen.Length;
    }

    private MenuFlyout SourceMenu(TextBox target)
    {
        var flyout = new MenuFlyout();
        void Item(string header, string kind)
        {
            var item = new MenuItem { Header = header, Tag = "cond-source-" + kind };
            item.Click += async (_, _) => await PickAsync(target, kind);
            flyout.Items.Add(item);
        }
        Item("Value from a step's response…", "response");
        Item("Status code of a step…", "status");
        Item("Response header of a step…", "header");
        Item("Cell of the current data row…", "data");
        Item("Loop variable or environment…", "loop");
        return flyout;
    }

    private sealed class RowView
    {
        private readonly ConditionBuilderWindow _owner;
        public Grid Layout { get; } = new() { ColumnDefinitions = new ColumnDefinitions("84,96,48,*,126,*,72,34"), Tag = "cond-row" };
        private readonly ComboBox _join = new() { ItemsSource = ConditionParts.Joins, Width = 78, Tag = "cond-join" };
        private readonly ComboBox _open = new() { ItemsSource = OpenChoices.Select(c => c.Text).ToList(), Width = 92, Tag = "cond-open" };
        private readonly CheckBox _not = new() { Tag = "cond-not", HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBox _left = new() { Tag = "cond-left", Watermark = "value, for example a response field" };
        private readonly ComboBox _verb = new() { ItemsSource = new[] { IsTrue }.Concat(Verbs).ToList(), Width = 122, Tag = "cond-verb" };
        private readonly TextBox _right = new() { Tag = "cond-right", Watermark = "value to compare with" };
        private readonly ComboBox _close = new() { ItemsSource = CloseChoices, Width = 68, Tag = "cond-close" };
        private readonly Button _remove = new() { Content = "✕", Tag = "cond-remove", Padding = new Thickness(6, 2) };

        public RowView(ConditionBuilderWindow owner, ConditionPart part)
        {
            _owner = owner;
            var leftBox = Wrap(_left, "left");
            var rightBox = Wrap(_right, "right");
            Control[] controls = [_join, _open, _not, leftBox, _verb, rightBox, _close, _remove];
            for (var i = 0; i < controls.Length; i++) { controls[i].Margin = new Thickness(i == 0 ? 0 : 2, 0, 2, 0); Grid.SetColumn(controls[i], i); Layout.Children.Add(controls[i]); }
            ToolTip.SetTip(_not, "Turn this comparison around (true becomes false)");
            ToolTip.SetTip(_open, "Start a group; “not (” turns the whole group around");
            ToolTip.SetTip(_remove, "Remove this row");
            Load(part);
            _join.SelectionChanged += (_, _) => owner.Refresh();
            _open.SelectionChanged += (_, _) => owner.Refresh();
            _close.SelectionChanged += (_, _) => owner.Refresh();
            _verb.SelectionChanged += (_, _) => { UpdateRightVisible(); owner.Refresh(); };
            _not.IsCheckedChanged += (_, _) => owner.Refresh();
            _left.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) owner.Refresh(); };
            _right.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) owner.Refresh(); };
            _remove.Click += (_, _) => owner.RemoveRow(this);
        }

        private Control Wrap(TextBox box, string name)
        {
            var more = new Button { Content = "⋯", Tag = "cond-more-" + name, Padding = new Thickness(8, 2), Margin = new Thickness(4, 0, 0, 0), Flyout = _owner.SourceMenu(box) };
            ToolTip.SetTip(more, "Insert a value from a step, the data row or a loop");
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            grid.Children.Add(box);
            Grid.SetColumn(more, 1);
            grid.Children.Add(more);
            return grid;
        }

        public void SetFirst(bool first) { _join.IsVisible = !first; }

        public void Clear() => Load(new ConditionPart("", false, 0, false, "", "eq", "", 0));

        public void Load(ConditionPart part)
        {
            _join.SelectedItem = part.Join is "or" or "xor" ? part.Join : "and";
            var open = OpenChoices.ToList().FindIndex(c => c.Not == part.NotBefore && c.Open == part.Open);
            _open.SelectedIndex = open >= 0 ? open : Math.Clamp(part.Open, 0, 3);
            _not.IsChecked = part.Not;
            _left.Text = part.Left;
            _verb.SelectedItem = part.Verb.Length == 0 ? IsTrue : (Verbs.Contains(part.Verb) ? part.Verb : "eq");
            _right.Text = part.Right;
            _close.SelectedIndex = Math.Clamp(part.Close, 0, 3);
            UpdateRightVisible();
        }

        private void UpdateRightVisible()
        {
            var verb = _verb.SelectedItem as string ?? string.Empty;
            _right.IsEnabled = verb != IsTrue && !ConditionParts.NoRightSide(verb);
            if (_right.Parent is Control wrapper) wrapper.IsVisible = _right.IsEnabled;
        }

        public ConditionPart ToPart(bool first)
        {
            var choice = OpenChoices[Math.Clamp(_open.SelectedIndex, 0, OpenChoices.Length - 1)];
            var verb = _verb.SelectedItem as string ?? IsTrue;
            return new ConditionPart(first ? "" : _join.SelectedItem as string ?? "and", choice.Not, choice.Open, _not.IsChecked == true, (_left.Text ?? string.Empty).Trim(), verb == IsTrue ? "" : verb, (_right.Text ?? string.Empty).Trim(), Math.Clamp(_close.SelectedIndex, 0, 3));
        }
    }
}
