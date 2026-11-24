using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// JSON view: the rows of the table seen through a tree. The left side is the tree of one step (response fields with their results, template variables,
/// or the other request columns); picking an item shows that item for every row shown, on the right, as the table cell would be: the value with its
/// wizard, and what each environment got. Filters, row selection and the row operations are the table's own.
/// </summary>
public sealed class JsonView : UserControl
{
    private static readonly IBrush Muted = Brush.Parse("#8EA4BC");
    private static readonly IBrush Line = Brush.Parse("#22364F");

    private readonly DataEditorViewModel _editor;
    private readonly TreeView _tree = new() { Tag = "json-tree" };
    private readonly ListBox _rows = new() { Tag = "json-rows", SelectionMode = SelectionMode.Multiple, Background = Brushes.Transparent };
    private readonly ComboBox _step = new() { Tag = "json-step", MinWidth = 180 };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4, Tag = "json-tabs" };
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Tag = "json-hint" };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 4, Tag = "json-actions" };
    private readonly List<ToggleButton> _tabButtons = [];
    private readonly ToggleButton _rawToggle = new() { Content = "‹/›  Raw", Tag = "json-raw", Padding = new Thickness(8, 3), MinHeight = 28 };
    private readonly ComboBox _rawRow = new() { Tag = "json-raw-row", MinWidth = 150, MinHeight = 28 };
    private readonly TextBox _rawText = new() { Tag = "json-raw-text", IsReadOnly = false, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, BorderThickness = new Thickness(0) };
    private readonly DockPanel _rawPanel = new() { Tag = "json-raw-panel", IsVisible = false };
    private readonly ComboBox _fillChoice = new() { Tag = "json-fill-choice", ItemsSource = new[] { "Leave the cells empty", "Fill the row shown", "Fill every row from its own response" }, SelectedIndex = 1, MinWidth = 210 };
    private JsonTab _tab = JsonTab.Response;
    private JsonViewNode? _root;
    private JsonViewNode? _selected;
    private string _selectedPath = string.Empty;
    private bool _refreshQueued;
    private bool _building;

    /// <summary>Opens the table's cell wizard for a row's cell (set by the window): the row, the other selected rows, the column and the control to anchor to.</summary>
    public Action<DataRowViewModel, IReadOnlyList<DataRowViewModel>, GridColumn, Control>? OpenWizard { get; set; }

    public JsonView(DataEditorViewModel editor)
    {
        _editor = editor;
        Focusable = true;
        foreach (var (tab, text) in new[] { (JsonTab.Response, "Response"), (JsonTab.Template, "Request › Template"), (JsonTab.Config, "Request › Config") })
        {
            var button = new ToggleButton { Content = text, Tag = "json-tab-" + tab.ToString().ToLowerInvariant(), IsChecked = tab == _tab, Padding = new Thickness(12, 5) };
            button.Click += (_, _) => { _tab = tab; foreach (var b in _tabButtons) b.IsChecked = ReferenceEquals(b, button); _selected = null; _selectedPath = string.Empty; _selectedStep = string.Empty; Refresh(); };
            _tabButtons.Add(button);
            _tabs.Children.Add(button);
        }
        _step.SelectionChanged += (_, _) =>
        {
            if (_building || _step.SelectedItem is not string step || string.Equals(step, _editor.StepFilter, StringComparison.OrdinalIgnoreCase)) return;
            _selected = null; _selectedPath = string.Empty; _selectedStep = string.Empty;
            _editor.StepFilter = step;
        };

        var top = new DockPanel { Margin = new Thickness(14, 10, 14, 8), LastChildFill = false };
        var stepLabel = new TextBlock { Text = "Step", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _tabs, new Border { Width = 14 }, stepLabel, _step } });
        DockPanel.SetDock(top, Dock.Top);

        _tree.ItemTemplate = new FuncTreeDataTemplate<JsonViewNode>((node, _) => Header(node), node => node.Children);
        _tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>()) { Setters = { new Setter(TreeViewItem.IsExpandedProperty, true) } });
        _tree.SelectionChanged += (_, _) =>
        {
            if (_building) return;
            _selected = _tree.SelectedItem as JsonViewNode;
            _selectedPath = _selected?.Path ?? string.Empty;
            _selectedStep = _selected?.StepOf() ?? string.Empty;
            BuildRows();
        };
        var left = new Border { BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Margin = new Thickness(14, 0, 6, 14), Child = new ScrollViewer { Content = _tree } };

        _rows.ItemTemplate = new FuncDataTemplate<DataRowViewModel>((row, _) => RowLine(row));
        _rows.KeyDown += OnKeyDown;
        _tree.KeyDown += OnTreeKeyDown;
        _rows.SelectionChanged += (_, _) =>
        {
            if (_building || _syncing) return;
            if (_rows.SelectedItems?.OfType<DataRowViewModel>().OrderBy(r => _editor.VisibleRows.IndexOf(r)).FirstOrDefault() is { } first && !ReferenceEquals(_editor.SelectedRow, first))
            {
                _syncing = true;
                try { _editor.SelectedRow = first; } finally { _syncing = false; }
            }
        };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = false };
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _title, _hint } });
        DockPanel.SetDock(_actions, Dock.Right);
        header.Children.Add(_actions);
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_rawRow, Dock.Top);
        _rawPanel.Children.Add(_rawRow);
        _rawPanel.Children.Add(_rawText);
        _rawToggle.Click += (_, _) => BuildRows();
        _rawRow.SelectionChanged += (_, _) => { if (!_building) FillRaw(); };
        var right = new Border { BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(6, 0, 14, 14), Child = new DockPanel { Children = { header, _rows, _rawPanel } } };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*") };
        body.Children.Add(left);
        Grid.SetColumn(right, 1);
        body.Children.Add(right);
        Content = new DockPanel { Children = { top, body } };

        editor.VisibleRows.CollectionChanged += (_, _) => QueueRefresh();
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DataEditorViewModel.SelectedRow)) { SyncSelectedRow(); return; }
            if (e.PropertyName is nameof(DataEditorViewModel.ShowJson) or nameof(DataEditorViewModel.StepFilter) or nameof(DataEditorViewModel.RunSummaryText)) QueueRefresh();
        };
        Refresh();
    }

    /// <summary>The tab that is showing (for tests).</summary>
    public JsonTab Tab => _tab;

    /// <summary>Shows a tab and rebuilds (for tests and for the window).</summary>
    public void ShowTab(JsonTab tab) { _tab = tab; foreach (var b in _tabButtons) b.IsChecked = (string?)b.Tag == "json-tab-" + tab.ToString().ToLowerInvariant(); _selected = null; _selectedPath = string.Empty; _selectedStep = string.Empty; Refresh(); }

    public JsonViewNode? Root => _root;

    /// <summary>Selects an item by its path below the step.</summary>
    public void Select(string path)
    {
        _selectedPath = path;
        Refresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || !_editor.ShowJson) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() => { _refreshQueued = false; Refresh(); });
    }

    private string _selectedStep = string.Empty;

    /// <summary>The step of the item (the step chosen, or the step the item sits under in the all-steps tree).</summary>
    private string StepOf(JsonViewNode? node) => node?.StepOf() is { Length: > 0 } s ? s : Step;

    /// <summary>The step that actions without an item (raw text, paste) work on: the selected item's step, else the chosen step, else the first.</summary>
    private string CurrentStep => _selected is not null && StepOf(_selected).Length > 0 ? StepOf(_selected) : Step.Length > 0 ? Step : _editor.JsonSteps().FirstOrDefault() ?? string.Empty;

    private string Step => _editor.StepFilter is { } s && s != DataEditorViewModel.AllSteps ? s : string.Empty;

    private void Refresh()
    {
        _building = true;
        try
        {
            var steps = new List<string> { DataEditorViewModel.AllSteps };
            steps.AddRange(_editor.JsonSteps());
            if (!_step.ItemsSource?.Cast<string>().SequenceEqual(steps) ?? true) _step.ItemsSource = steps;
            _step.SelectedItem = steps.FirstOrDefault(s => s.Equals(Step.Length == 0 ? DataEditorViewModel.AllSteps : Step, StringComparison.OrdinalIgnoreCase));
            if (Step.Length > 0)
            {
                _root = _editor.BuildJsonTree(_tab, Step);
                if (_tab == JsonTab.Response) _editor.TallyJsonTree(_root, Step);
            }
            else
            {
                _root = new JsonViewNode { Name = "(all steps)" };
                foreach (var step in _editor.JsonSteps())
                {
                    var top = _editor.BuildJsonTree(_tab, step);
                    top.StepName = step;
                    if (_tab == JsonTab.Response) _editor.TallyJsonTree(top, step);
                    if (top.Children.Count > 0) _root.Children.Add(top);
                }
            }
            _tree.ItemsSource = _root.Children.ToList();
            _selected = _selectedPath.Length == 0 && _selectedStep.Length == 0 ? null : Descendants(_root).FirstOrDefault(n => n.Path.Equals(_selectedPath, StringComparison.OrdinalIgnoreCase) && n.StepOf().Equals(_selectedStep, StringComparison.OrdinalIgnoreCase) && (_selectedPath.Length > 0 || n.StepName.Length > 0));
            _tree.SelectedItem = _selected;
        }
        finally { _building = false; }
        BuildRows();
    }

    private static IEnumerable<JsonViewNode> Descendants(JsonViewNode node) => node.Children.SelectMany(c => new[] { c }.Concat(Descendants(c)));

    // ---- tree ------------------------------------------------------------------------------------

    private Control Header(JsonViewNode node)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Tag = "json-node" };
        var name = new TextBlock { Text = node.Name, FontWeight = node.IsLeaf ? FontWeight.Normal : FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (node.Extra) name.Foreground = Brush.Parse("#8CC1FF");
        if (node.Missing) { name.Foreground = Brush.Parse("#FF7588"); name.TextDecorations = TextDecorations.Strikethrough; }
        panel.Children.Add(name);
        if (node.Variable.Length > 0 && !node.Variable.Equals(node.Name, StringComparison.OrdinalIgnoreCase)) panel.Children.Add(new TextBlock { Text = "← " + node.Variable, Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        if (node.Constant) panel.Children.Add(new TextBlock { Text = "= " + (node.Sample.Length > 24 ? node.Sample[..24] + "…" : node.Sample), Foreground = Brush.Parse("#5E7590"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        if (!node.IsLeaf) panel.Children.Add(new TextBlock { Text = node.Children.All(c => c.Name.StartsWith('[')) ? $"[ ] {node.Children.Count}" : $"{{ }} {node.Children.Count}", Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        void Badge(string text, string color, string tip) => panel.Children.Add(new TextBlock { Text = text, Foreground = Brush.Parse(color), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Tag = "json-badge", [ToolTip.TipProperty] = tip });
        if (node.Passed > 0) Badge($"✓ {node.Passed}", DataRowViewModel.ColorOf("PASS"), $"{node.Passed} passed in the rows shown");
        if (node.Failed > 0) Badge($"✗ {node.Failed}", DataRowViewModel.ColorOf("FAIL"), $"{node.Failed} failed in the rows shown");
        if (node.Errors > 0) Badge($"⚠ {node.Errors}", DataRowViewModel.ColorOf("ERROR"), $"{node.Errors} where the step broke");
        if (node.Bugs > 0) Badge($"⚑ {node.Bugs}", DataRowViewModel.ColorOf("BUG"), $"{node.Bugs} known bugs");
        if (node.EnvDiffers) Badge("▲", "#F6C76B", "The environments disagree on at least one row");
        if (node.Extra) Badge("extra", "#8CC1FF", _tab == JsonTab.Response ? "In the last response, but there is no column for it yet" : "The template uses it, but there is no column for it yet");
        if (node.Missing) Badge("missing", "#FF7588", "There is a column, but the last responses do not have this field");
        if (node.Shared) Badge("shared", "#8EA4BC", "A column without a step: every step of the row can use it");
        return panel;
    }

    // ---- rows of the selected item ---------------------------------------------------------------------

    private IReadOnlyList<DataRowViewModel> SelectedRows => _rows.SelectedItems?.OfType<DataRowViewModel>().OrderBy(r => _editor.VisibleRows.IndexOf(r)).ToList() ?? [];

    /// <summary>Shows the raw text (or the rows) and keeps the row picker of the raw text in step with the rows shown.</summary>
    private void UpdateRaw()
    {
        var raw = _rawToggle.IsChecked == true && _tab != JsonTab.Config;
        _rawPanel.IsVisible = raw;
        _rawText.IsReadOnly = _tab != JsonTab.Response;   // only a response can be typed: the template text shows the row's values and is not a source
        _rows.IsVisible = !raw;
        if (!raw) return;
        var ids = _editor.VisibleRows.Select(r => r.CurrentDataId).ToList();
        var keep = _rawRow.SelectedItem as string ?? (_rows.SelectedItems?.OfType<DataRowViewModel>().FirstOrDefault() ?? _editor.VisibleRows.FirstOrDefault())?.CurrentDataId;
        var wasBuilding = _building;
        _building = true;
        try
        {
            _rawRow.ItemsSource = ids;
            _rawRow.SelectedItem = ids.FirstOrDefault(i => i == keep) ?? ids.FirstOrDefault();
        }
        finally { _building = wasBuilding; }
        FillRaw();
    }

    private void FillRaw()
    {
        var row = _editor.VisibleRows.FirstOrDefault(r => r.CurrentDataId == _rawRow.SelectedItem as string);
        _rawText.Text = row is null ? "No rows shown." : _editor.JsonRawText(_tab, CurrentStep, row);
    }

    private void BuildRows()
    {
        _actions.Children.Clear();
        _actions.Children.Add(_rawToggle);
        _rawToggle.IsVisible = _tab != JsonTab.Config;
        if (_tab == JsonTab.Response && CurrentStep.Length > 0)
            AddAction("Paste JSON…", "Use the JSON or XML on the clipboard as the response of the selected row: its fields show here as extra items, ready to become columns", () => _ = PasteResponseAsync(), "json-paste-response");
        if (_rawToggle.IsChecked == true && _tab == JsonTab.Response && CurrentStep.Length > 0)
            AddAction("Use this text", "Take the text below as the response of the row picked above (JSON or XML): its fields show as items, ready to become columns", () => UseRawText(), "json-raw-apply");
        UpdateRaw();
        var node = _selected;
        _rows.ItemsSource = null;
        if (node is null)
        {
            _title.Text = _root is { Children.Count: 0 } && Step.Length == 0 ? "No step has items on this tab" : _root is { Children.Count: 0 } ? "Nothing to show for this step on this tab" : "Pick an item in the tree";
            _hint.Text = $"{_editor.VisibleRows.Count} rows shown";
            AddExtrasAction(_root);
            AddAction("＋ Row", "Add a row to the testcase", () => _editor.AddRows(1), "json-add-row");
            return;
        }
        _title.Text = (_tab == JsonTab.Response || node.StepOf().Length > 0 ? StepOf(node) + (node.Path.Length > 0 ? "." : string.Empty) : string.Empty) + node.Path + (node.Variable.Length > 0 && !node.Variable.Equals(node.Name, StringComparison.OrdinalIgnoreCase) ? " ← " + node.Variable : string.Empty);
        _hint.Text = node.Constant ? "Fixed text in the template, no variable to fill: " + node.Sample : $"{_editor.VisibleRows.Count} rows shown";
        if (node.Constant) { AddExtrasAction(_root); return; }
        _rows.ItemsSource = _editor.VisibleRows.ToList();
        SyncSelectedRow();

        AddExtrasAction(node);
        AddAction("＋ Row", "Add a row to the testcase", () => _editor.AddRows(1), "json-add-row");
        AddAction("⧉ Duplicate", "Duplicate the selected rows", () => { if (SelectedRows is { Count: > 0 } rows) _editor.DuplicateRows(rows); }, "json-duplicate");
        AddAction("✕ Delete", "Delete the selected rows (Undo brings them back)", () => { if (SelectedRows is { Count: > 0 } rows) _editor.RemoveRows(rows); }, "json-delete");
        if (node.Column is not null)
        {
            AddAction("⤓ Fill down", "Copy the first selected row's value into the other selected rows (Ctrl+D)", FillDown, "json-fill");
            AddAction("Copy", "Copy the values of the selected rows (all rows shown when none is selected) (Ctrl+C)", Copy, "json-copy");
            AddAction("Paste", "Paste lines into the selected rows: one line goes to all of them, several go in order (Ctrl+V)", () => _ = PasteAsync(), "json-paste");
        }
    }

    /// <summary>"＋ Add N columns" for the extra items under a node (an item, a branch or the whole step), with the response window's fill choice.</summary>
    private void AddExtrasAction(JsonViewNode? scope)
    {
        if (scope is null) return;
        var extras = DataEditorViewModel.JsonExtras(scope);
        if (extras.Count == 0 || _tab == JsonTab.Config) return;
        if (_tab == JsonTab.Response) _actions.Children.Add(_fillChoice);
        AddAction(extras.Count == 1 ? "＋ Add column" : $"＋ Add {extras.Count} columns", _tab == JsonTab.Response ? "Create the expected columns for the extra items, and fill their cells from the responses as chosen" : "Create the request columns for the template variables that have none", () =>
        {
            var fill = _fillChoice.SelectedIndex switch { 0 => ResponseFill.None, 2 => ResponseFill.AllRows, _ => ResponseFill.ThisRow };
            var current = (_rows.SelectedItems?.OfType<DataRowViewModel>().FirstOrDefault() ?? _editor.VisibleRows.FirstOrDefault())?.CurrentDataId;
            if (scope.StepOf().Length == 0 && Step.Length == 0) foreach (var top in scope.Children) _editor.AddJsonExtras(_tab, top.StepName, top, fill, current);   // all steps: one step after the other
            else _editor.AddJsonExtras(_tab, StepOf(scope), scope, fill, current);
        }, "json-add-column");
    }

    private void AddAction(string text, string tip, Action action, string tag)
    {
        var button = new Button { Content = text, Tag = tag, Padding = new Thickness(8, 3), MinHeight = 28 };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        _actions.Children.Add(button);
    }

    private Control RowLine(DataRowViewModel? row)
    {
        if (row is null) return new Border();
        var node = _selected;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*,Auto"), Margin = new Thickness(0, 2), Tag = "json-row" };
        var number = new TextBlock { Text = $"{_editor.VisibleRows.IndexOf(row) + 1} · {row.CurrentDataId}", Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        grid.Children.Add(number);

        if (node is { Column: { } column } && node.IsLeaf)
        {
            var cell = column.CellIndex;
            var box = new TextBox { Text = row.Cells[cell] ?? string.Empty, MinWidth = 160, Tag = "json-cell", FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            void Commit() { if (box.Text != (row.Cells[cell] ?? string.Empty)) row.Cells[cell] = box.Text ?? string.Empty; }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { Commit(); e.Handled = true; } };
            box.GotFocus += (_, _) => { if (_rows.SelectedItems?.Contains(row) != true) _rows.SelectedItem = row; };
            NotifyCollectionChangedEventHandler follow = (_, e) => { if (e.NewStartingIndex == cell && !box.IsFocused) box.Text = row.Cells[cell] ?? string.Empty; };
            row.Cells.CollectionChanged += follow;
            box.DetachedFromVisualTree += (_, _) => row.Cells.CollectionChanged -= follow;
            var wizard = new Button { Content = "✦", Tag = "json-wizard", Padding = new Thickness(6, 2), Margin = new Thickness(6, 0, 0, 0), MinHeight = 26 };
            ToolTip.SetTip(wizard, "Open the value wizard (the same as on a table cell); with several rows selected the result goes to all of them");
            wizard.Click += (_, _) =>
            {
                var others = SelectedRows.Where(r => !ReferenceEquals(r, row)).ToList();
                OpenWizard?.Invoke(row, others, column, wizard);
            };
            var value = new DockPanel { LastChildFill = true, Children = { wizard, box } };
            DockPanel.SetDock(wizard, Dock.Right);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            if (_tab == JsonTab.Response)
            {
                var results = Results(row, node);
                Grid.SetColumn(results, 2);
                grid.Children.Add(results);
            }
        }
        else if (node is { IsLeaf: false })
        {
            var summary = Results(row, node);
            Grid.SetColumn(summary, 1);
            grid.Children.Add(summary);
        }
        return new Border { Child = grid, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 2) };
    }

    /// <summary>What the run says for this item on this row: one result while the environments agree, one per environment when they differ.</summary>
    private Control Results(DataRowViewModel row, JsonViewNode node)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "json-result" };
        TextBlock Chip(string text, string state) => new() { Text = text, Foreground = Brush.Parse(DataRowViewModel.ColorOf(state)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        if (!node.IsLeaf)
        {
            var results = node.Leaves().Where(l => l.Column is not null).SelectMany(l => _editor.JsonCellResults(row, StepOf(l), l)).ToList();
            foreach (var state in new[] { "PASS", "FAIL", "ERROR", "BUG" })
                if (results.Count(r => r.State == state) is > 0 and var n) panel.Children.Add(Chip($"{DataRowViewModel.SymbolOf(state)} {n}", state));
            return panel;
        }
        var cells = _editor.JsonCellResults(row, StepOf(node), node);
        if (cells.Count == 0) { panel.Children.Add(new TextBlock { Text = "—", Foreground = Brush.Parse("#5E7590") }); return panel; }
        var agree = cells.Select(c => (c.State, c.Actual)).Distinct().Count() == 1;
        string Text(JsonCellResult c) => $"{DataRowViewModel.SymbolOf(c.State)}{(c.State is "FAIL" or "BUG" && c.Actual.Length > 0 ? " got " + (c.Actual.Length > 30 ? c.Actual[..30] + "…" : c.Actual) : string.Empty)}";
        if (agree) { var chip = Chip(Text(cells[0]), cells[0].State); ToolTip.SetTip(chip, cells[0].Message); Detail(chip, row, node, cells[0]); panel.Children.Add(chip); }
        else
        {
            foreach (var cell in cells)
            {
                var chip = Chip($"{cell.Environment} {Text(cell)}", cell.State);
                ToolTip.SetTip(chip, cell.Message.Length > 0 ? cell.Message : cell.State);
                Detail(chip, row, node, cell);
                panel.Children.Add(chip);
            }
            panel.Children.Add(new TextBlock { Text = "▲", Foreground = Brush.Parse("#F6C76B"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        }
        return panel;
    }

    /// <summary>Clicking a result opens its detail: environment, state, what the run got and its message, and a way to the row in Table view.</summary>
    private void Detail(Control chip, DataRowViewModel row, JsonViewNode node, JsonCellResult cell)
    {
        chip.Cursor = new Cursor(StandardCursorType.Hand);
        chip.Tag = "json-result-chip";
        chip.PointerPressed += (_, e) =>
        {
            var text = $"{StepOf(node)}.{node.Path}  ·  Data ID {row.CurrentDataId}" + (cell.Environment.Length > 0 ? $"  ·  {cell.Environment}" : string.Empty)
                + $"\n{cell.State}" + (cell.Actual.Length > 0 ? $"\nGot: {cell.Actual}" : string.Empty) + (cell.Message.Length > 0 ? $"\n\n{cell.Message}" : string.Empty);
            var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, MaxHeight = 260, Tag = "json-detail-text" };
            var show = new Button { Content = "Show the row in Table view", Tag = "json-detail-table", Margin = new Thickness(0, 6, 0, 0) };
            var flyout = new Flyout { Content = new StackPanel { Spacing = 4, Children = { box, show } } };
            show.Click += (_, _) => { flyout.Hide(); _editor.SelectedRow = row; _editor.ViewModeIndex = 0; };
            flyout.ShowAt(chip);
            e.Handled = true;
        };
    }

    // ---- row operations ---------------------------------------------------------------------------

    private int CellIndex => _selected?.Column?.CellIndex ?? -1;

    private void FillDown()
    {
        if (CellIndex < 1) return;
        _editor.FillDown(SelectedRows, CellIndex);
        BuildRows();
    }

    private void Copy()
    {
        if (CellIndex < 1) return;
        var rows = SelectedRows.Count > 0 ? SelectedRows : _editor.VisibleRows.ToList();
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(DataEditorViewModel.CopyColumnValues(rows, CellIndex));
    }

    private async Task PasteAsync()
    {
        if (CellIndex < 1 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        var text = await clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        var rows = SelectedRows.Count > 0 ? SelectedRows : _editor.VisibleRows.Take(text.Split('\n').Length).ToList();
        _editor.PasteColumnValues(rows, CellIndex, text);
        BuildRows();
    }

    private bool _syncing;

    /// <summary>The edited raw text becomes the response of the row picked in the raw view.</summary>
    public void UseRawText()
    {
        var dataId = _rawRow.SelectedItem as string ?? string.Empty;
        var problem = _editor.UsePastedResponse(CurrentStep, dataId, _rawText.Text ?? string.Empty);
        if (problem.Length > 0) { _hint.Text = problem; return; }
        Refresh();
        _hint.Text = $"The edited text is now the response of {dataId}; nothing is saved until you add columns and Save";
    }

    private async Task PasteResponseAsync()
    {
        var text = TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? await clipboard.GetTextAsync() : null;
        UsePasted(text ?? string.Empty);
    }

    /// <summary>Uses the text as the response of the selected row (the first row shown when none is selected) and shows what it brought.</summary>
    public void UsePasted(string text)
    {
        var row = _editor.SelectedRow ?? _editor.VisibleRows.FirstOrDefault();
        var problem = _editor.UsePastedResponse(CurrentStep, row?.CurrentDataId ?? string.Empty, text);
        Refresh();
        if (problem.Length > 0) _hint.Text = problem;
        else _hint.Text = $"Pasted response used for {row!.CurrentDataId}; nothing is saved until you add columns and Save";
    }

    /// <summary>The row the table has selected is the row selected here, and the other way round.</summary>
    private void SyncSelectedRow()
    {
        if (_syncing || _editor.SelectedRow is not { } row || _rows.ItemsSource is null) return;
        if (_rows.SelectedItems?.Count == 1 && ReferenceEquals(_rows.SelectedItem, row)) return;
        if (_rows.SelectedItems?.Contains(row) == true && _rows.SelectedItems.Count > 1) return;
        _syncing = true;
        try { _rows.SelectedItem = row; }
        finally { _syncing = false; }
    }

    /// <summary>The items in the order the tree shows them.</summary>
    public List<JsonViewNode> Flat() => _root is null ? [] : Descendants(_root).ToList();

    /// <summary>Moves the selection to the previous (-1) or next (+1) item of the tree.</summary>
    public void MoveItem(int delta)
    {
        var flat = Flat();
        if (flat.Count == 0) return;
        var index = _selected is null ? -1 : flat.FindIndex(n => ReferenceEquals(n, _selected) || n.Path.Equals(_selected.Path, StringComparison.OrdinalIgnoreCase));
        var next = flat[Math.Clamp(index + delta, 0, flat.Count - 1)];
        _selected = next;
        _selectedPath = next.Path;
        _selectedStep = next.StepOf();
        _building = true;
        try { _tree.SelectedItem = next; }
        finally { _building = false; }
        BuildRows();
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (ctrl && e.Key is Key.Up or Key.Down) { MoveItem(e.Key == Key.Up ? -1 : 1); e.Handled = true; }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.Up or Key.Down && e.Source is not TextBox) { MoveItem(e.Key == Key.Up ? -1 : 1); e.Handled = true; return; }
        if (e.Source is TextBox) return;   // typing in a value keeps its own shortcuts
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (ctrl && e.Key == Key.C) { Copy(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { _ = PasteAsync(); e.Handled = true; }
        else if (ctrl && e.Key == Key.D) { FillDown(); e.Handled = true; }
        else if (ctrl && e.Key == Key.A) { _rows.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Delete && CellIndex >= 1) { _editor.ClearColumnValues(SelectedRows, CellIndex); BuildRows(); e.Handled = true; }
    }
}
