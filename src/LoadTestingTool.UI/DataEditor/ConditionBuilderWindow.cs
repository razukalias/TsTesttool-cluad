using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using LoadTestingTool.Editing;
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
    private readonly Func<IReadOnlyList<string>> _previousTargets;
    private readonly StackPanel _rowsPanel = new() { Spacing = 6 };
    private readonly List<RowView> _rows = [];
    private readonly TextBox _preview = new() { IsReadOnly = true, Tag = "cond-preview", TextWrapping = TextWrapping.Wrap, MinHeight = 44 };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "cond-status" };
    private readonly Button _ok = new() { Content = "Use this condition", Classes = { "accent" }, Tag = "cond-ok" };
    private bool _loading;
    private readonly TextEditor _script = new() { ShowLineNumbers = true, FontFamily = new FontFamily("Consolas"), FontSize = 14, Foreground = Brush.Parse("#E3EDF9"), MinHeight = 200, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly Control _wizardPane;
    private readonly Control _scriptPane;
    private readonly TextBlock _signature = new() { FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), FontFamily = new FontFamily("Consolas,Menlo,monospace"), TextWrapping = TextWrapping.Wrap, IsVisible = false, Tag = "condition-signature" };
    private readonly Avalonia.Controls.Primitives.ToggleButton _wizardMode = new() { Content = "Wizard", Tag = "cond-mode-wizard" };
    private readonly Avalonia.Controls.Primitives.ToggleButton _scriptMode = new() { Content = "C# script", Tag = "cond-mode-script" };
    private CompletionWindow? _completion;
    private bool _scriptActive;

    /// <summary>True while the C# script is the condition (otherwise the rows are).</summary>
    public bool ScriptMode => _scriptActive;

    /// <summary>The script's code without the cs: prefix.</summary>
    public string ScriptText { get => _script.Text ?? string.Empty; set => _script.Text = value; }
    internal TextEditor ScriptEditor => _script;

    /// <summary>The condition text to write into the step, or null when the window was cancelled.</summary>
    public string? Result { get; private set; }

    /// <summary>The condition as the rows stand now.</summary>
    public string CurrentText => _scriptActive ? (ScriptText.Trim().Length == 0 ? string.Empty : ConditionScript.Prefix + " " + ScriptText.Trim()) : RowsText;

    private string RowsText => ConditionParts.Build(_rows.Select((r, i) => r.ToPart(i == 0)));

    public int RowCount => _rows.Count;

    /// <summary>True when the text the window was opened with could be shown as rows (an empty text can).</summary>
    public bool OpenedAsRows { get; }

    public ConditionBuilderWindow(DataEditorViewModel? editor, string? stepName, string? initial, string title = "Build condition")
        : this(() => editor?.ReferenceTargets(stepName) ?? [], () => editor?.RequestDataTargets() ?? [], () => editor?.LoopVariablesFor(stepName) ?? ["env"], initial, title, () => editor?.PreviousStepTargets(stepName) ?? []) { }

    /// <summary>The wizard over any source of names: what earlier steps give (Step.path, Step.statusCode, Step.responseHeader.Name), the data row's columns and the loop variables.</summary>
    public ConditionBuilderWindow(Func<IReadOnlyList<string>> responseTargets, Func<IReadOnlyList<string>> dataTargets, Func<IReadOnlyList<string>> loopVariables, string? initial, string title = "Build condition", Func<IReadOnlyList<string>>? previousTargets = null)
    {
        _previousTargets = previousTargets ?? (() => []);
        _responseTargets = responseTargets;
        _dataTargets = dataTargets;
        _loopVariables = loopVariables;
        Title = title;
        Width = 1100;
        Height = 560;
        MinWidth = 760;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var startsAsScript = ConditionScript.IsScript(initial);
        var parts = startsAsScript ? [] : ConditionParts.Parse(initial);
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
        var wizard = new DockPanel();
        DockPanel.SetDock(help, Dock.Top);
        DockPanel.SetDock(header, Dock.Top);
        wizard.Children.Add(help);
        wizard.Children.Add(header);
        wizard.Children.Add(new ScrollViewer { Content = new StackPanel { Spacing = 8, Children = { _rowsPanel, add } }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        _wizardPane = wizard;

        var scriptHelp = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush.Parse("#8EA4BC"),
            Text = "For conditions the rows cannot express. Write C# that ends with a true/false expression (or use return). Type  _  to list the variables, or  <  to list values of earlier steps and the data row. "
                 + "Helpers: Is(\"gt\", a, b) with any condition verb, True(x), Has(\"name\"), Num(\"name\"), V(\"name\"); System.Linq and Regex are available. A value inside quotes becomes its text, outside quotes a string.",
        };
        DockPanel.SetDock(scriptHelp, Dock.Top);
        var convert = new Button { Content = "Fill from the wizard rows", Tag = "cond-convert", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
        ToolTip.SetTip(convert, "Replaces the script with C# that means the same as the rows");
        convert.Click += (_, _) => { _script.Text = ConditionScript.FromParts(_rows.Select((r, i) => r.ToPart(i == 0))); Refresh(); };
        DockPanel.SetDock(convert, Dock.Top);
        var script = new DockPanel();
        script.Children.Add(scriptHelp);
        script.Children.Add(convert);
        script.Children.Add(new Border { Child = _script, BorderBrush = Brush.Parse("#2A3B52"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });
        script.Children.Add(_signature);
        _scriptPane = script;
        _script.TextArea.TextEntered += OnScriptTextEntered;
        _script.TextArea.KeyDown += OnScriptKeyDown;
        _script.TextArea.Caret.PositionChanged += (_, _) => UpdateScriptSignature();
        _script.TextChanged += (_, _) => Refresh();
        _script.Document.TextChanged += (_, _) => Refresh();

        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 8), Children = { _wizardMode, _scriptMode } };
        ToolTip.SetTip(_scriptMode, "Write the condition as C# code, like the condition of an if or while in a program");
        ToolTip.SetTip(_wizardMode, "Build the condition from rows");
        _wizardMode.Click += (_, _) => SetMode(false);
        _scriptMode.Click += (_, _) => SetMode(true);
        DockPanel.SetDock(modes, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        var result = new StackPanel { Spacing = 4, Margin = new Thickness(0, 10, 0, 10), Children = { new TextBlock { Text = "THE CONDITION", FontSize = 11, Foreground = Brush.Parse("#8EA4BC") }, _preview, _status } };
        DockPanel.SetDock(result, Dock.Bottom);
        layout.Children.Add(modes);
        layout.Children.Add(buttons);
        layout.Children.Add(result);
        layout.Children.Add(new Panel { Children = { _wizardPane, _scriptPane } });
        if (startsAsScript) _script.Text = ConditionScript.Body(initial!);
        _scriptActive = startsAsScript;
        ApplyMode();
        Content = layout;
        Refresh();
    }

    /// <summary>Switches between the rows and the C# script. Going to the script for the first time with rows filled in offers their C# translation.</summary>
    public void SetMode(bool script)
    {
        if (script && !_scriptActive && ScriptText.Trim().Length == 0 && RowsText.Length > 0)
            _script.Text = ConditionScript.FromParts(_rows.Select((r, i) => r.ToPart(i == 0)));
        _scriptActive = script;
        ApplyMode();
        Refresh();
    }

    private void ApplyMode()
    {
        _wizardPane.IsVisible = !_scriptActive;
        _scriptPane.IsVisible = _scriptActive;
        _wizardMode.IsChecked = !_scriptActive;
        _scriptMode.IsChecked = _scriptActive;
    }

    /// <summary>What the completion list offers after the typed character: "_" lists the variables as _name_, "&lt;" the values of earlier steps and the data row as &lt;From_…&gt;. Nothing else is offered.</summary>
    public IReadOnlyList<(string Text, string Description)> CompletionItems(char trigger)
    {
        var items = new List<(string, string)>();
        if (trigger == '_')
            foreach (var name in _loopVariables().Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                items.Add(($"_{name}_", name.Equals("env", StringComparison.OrdinalIgnoreCase) ? "The selected environment." : "A variable: a loop counter or item, or an output a step saved."));
        else if (trigger == '<')
        {
            foreach (var t in _responseTargets().Distinct(StringComparer.OrdinalIgnoreCase)) items.Add((ResponseValuePicker.ReferenceFor(t), "From the response of an earlier step."));
            foreach (var t in _previousTargets().Distinct(StringComparer.OrdinalIgnoreCase)) items.Add(($"<From_response_@prev.{t}_r>", "From the step right before this one (follows it if steps move)."));
            foreach (var t in _dataTargets().Distinct(StringComparer.OrdinalIgnoreCase)) items.Add(($"<From_request_{t}_r>", "A column of the data row that is running."));
        }
        return items;
    }

    private void OnScriptTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is null || e.Text.Length != 1) return;
        var area = _script.TextArea;
        var offset = area.Caret.Offset;
        if (e.Text is "_" or "<")
        {
            if (e.Text == "_" && offset >= 2 && (char.IsLetterOrDigit(area.Document.GetCharAt(offset - 2)) || area.Document.GetCharAt(offset - 2) == '_')) return;
            var items = CompletionItems(e.Text[0]);
            if (items.Count == 0) return;
            ShowCompletion(offset - 1, items.Select(i => (i.Text, i.Description)).ToList());
            return;
        }
        // C#: members after a dot, and names once two letters are typed (the open list filters itself while typing)
        if (e.Text == "." || (_completion is null && char.IsLetter(e.Text[0]))) ShowEngineCompletion(forced: false);
    }

    /// <summary>Ctrl+Space: the C# suggestions for the caret even when nothing was typed.</summary>
    private void OnScriptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        ShowEngineCompletion(forced: true); e.Handled = true;
    }

    /// <summary>The C# suggestions for the code before the caret (members, names, keywords, snippets, the verbs inside Is(" )).</summary>
    public ScriptSuggestions? EngineSuggestions(bool forced = false) => ScriptCompletion.Suggest(ScriptKind.Condition, _script.Text ?? string.Empty, _script.TextArea.Caret.Offset, forced);

    private void ShowEngineCompletion(bool forced)
    {
        if (_completion is not null) { if (!forced) return; _completion.Close(); }
        if (EngineSuggestions(forced) is not { } found) return;
        ShowCompletion(found.Start, found.Items.Select(i => (i.Insert, $"{i.Label}  ·  {i.Detail}")).ToList());
    }

    private void ShowCompletion(int startOffset, IReadOnlyList<(string Text, string Description)> items)
    {
        var area = _script.TextArea;
        _completion = new CompletionWindow(area) { StartOffset = startOffset, CloseWhenCaretAtBeginning = true };
        foreach (var (text, description) in items) _completion.CompletionList.CompletionData.Add(new ScriptCompletionData(text, description));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    /// <summary>The signature of the call the caret is in, shown under the script.</summary>
    private void UpdateScriptSignature()
    {
        var sig = ScriptCompletion.Signature(ScriptKind.Condition, _script.Text ?? string.Empty, _script.TextArea.Caret.Offset);
        _signature.IsVisible = sig is not null;
        _signature.Text = sig is null ? string.Empty : sig.Text + (sig.Parameters.Count > 0 ? $"   ▸ {sig.Parameters[Math.Min(sig.Active, sig.Parameters.Count - 1)]}" : "");
    }

    private sealed class ScriptCompletionData(string text, string description) : ICompletionData
    {
        public string Text => text;
        public object Content => text;
        public object Description => description;
        public IImage? Image => null;
        public double Priority => 0;
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) => textArea.Document.Replace(completionSegment, text);
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
        if (_scriptActive)
        {
            var scriptProblem = text.Length == 0 ? "Write a condition, for example  Is(\"gt\", <From_response_Step.count_r>, \"3\")." : ConditionScript.Check(text, _loopVariables());
            if (scriptProblem is not null) { _status.Text = "⚠ " + scriptProblem; _status.Foreground = Brush.Parse("#F6C76B"); _ok.IsEnabled = false; }
            else { _status.Text = "✓ The script compiles. It runs, with the real values, when the step runs; it must give true or false."; _status.Foreground = Brush.Parse("#39D39B"); _ok.IsEnabled = true; }
            return;
        }
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
            case "prev":
                picker = new ResponseValuePicker(_previousTargets(), t => $"<From_response_@prev.{t}_r>", "Value from the previous step", "The step right before this one that sends a request. The reference follows the step if it is renamed or moved.");
                break;
            case "lastround":
                picker = new ResponseValuePicker(all, t => $"<From_response_@lastround.{t}_r>", "Value from the previous round of the loop", "What the step had at the end of the previous round (inside a loop). In the first round there is none and the step fails with a message saying so.");
                break;
            case "loop":
                picker = new ResponseValuePicker(_loopVariables(), t => $"_{t}_", "Variable", "The counter and item of the loops this step is in, the round of a While, the output variables steps save, and the environment name (env).");
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
        Item("Value from the previous step…", "prev");
        Item("Value from the previous round (in a loop)…", "lastround");
        Item("Cell of the current data row…", "data");
        Item("Variable: loop, output or environment…", "loop");
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
