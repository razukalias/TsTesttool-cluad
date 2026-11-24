using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The response part of the "Template and response" window. Left: the JSON of one row's response as a tree; tick a node to take it as one header (a value, or the
/// count of an array or object), right-click it to flatten what is below it to a depth or take more array elements. Right: the headers that produces, with a verb
/// each, and how the cells are filled. The row switcher shows another row's response.
/// </summary>
public sealed class ResponseColumnsPane : Grid
{
    private static readonly string[] Depths = ["1 level", "2 levels", "3 levels", "5 levels", "all levels"];
    private static readonly int[] DepthValues = [1, 2, 3, 5, 0];
    private static readonly string[] ElementTexts = ["first 3 elements", "first 5 elements", "first 10 elements", $"all elements (up to {ResponseTree.MaxElements})"];
    private static readonly int[] ElementValues = [3, 5, 10, ResponseTree.MaxElements];

    private readonly ComboBox _step = new() { MinWidth = 200, Tag = "response-step" };
    private readonly ComboBox _row = new() { MinWidth = 150, Tag = "response-row" };
    private readonly ComboBox _defaultVerb = new() { MinWidth = 100, Tag = "response-default-verb" };
    private readonly TextBlock _source = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Tag = "response-source" };
    private readonly TextBlock _note = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "response-note" };
    private readonly TreeView _tree = new() { Tag = "response-tree" };
    private readonly ItemsControl _results = new() { Tag = "response-results" };
    private readonly TextBlock _summary = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "response-summary" };
    private readonly RadioButton _fillNone = new() { Content = "Leave the cells empty", GroupName = "fill", Tag = "fill-none" };
    private readonly RadioButton _fillThis = new() { Content = "Fill the row shown", GroupName = "fill", IsChecked = true, Tag = "fill-this" };
    private readonly RadioButton _fillAll = new() { Content = "Fill every row from its own response", GroupName = "fill", Tag = "fill-all" };
    private readonly List<Action> _refreshers = [];
    private bool _loading;
    private readonly ToggleButton _showRaw = new() { Content = "‹/›  Raw response", Tag = "response-raw" };
    private readonly TextBox _raw = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, Tag = "response-raw-text", BorderThickness = new Thickness(0) };

    /// <summary>The Tree / Raw switch: the raw response is the text as the server sent it, indented (XML or JSON), read-only.</summary>
    private void ShowRawOrTree()
    {
        var raw = _showRaw.IsChecked == true;
        _raw.IsVisible = raw; _tree.IsVisible = !raw;
        _raw.Text = raw && Session?.BodyFor(Session.CurrentRow) is { } body ? ResponseText.Pretty(body) : string.Empty;
    }

    public ResponseColumnsSession? Session { get; private set; }
    private readonly DataEditorViewModel _editor;

    public ResponseColumnsPane(DataEditorViewModel editor, IReadOnlyList<string> steps, string? initialStep)
    {
        _editor = editor;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.3, GridUnitType.Star)));

        _step.ItemsSource = steps;
        _defaultVerb.ItemsSource = DataEditorViewModel.AssertionVerbs;
        _defaultVerb.SelectedItem = "eq";

        var back = new Button { Content = "◀", Tag = "response-prev", MinWidth = 34 };
        var next = new Button { Content = "▶", Tag = "response-next", MinWidth = 34 };
        var paste = new Button { Content = "Paste a response…", Tag = "response-paste" };
        var file = new Button { Content = "Pick a file…", Tag = "response-file" };
        back.Click += (_, _) => { Session?.Move(-1); ShowRow(); };
        next.Click += (_, _) => { Session?.Move(+1); ShowRow(); };
        paste.Click += async (_, _) => await PasteAsync();
        file.Click += async (_, _) => await PickFileAsync();
        _showRaw.IsCheckedChanged += (_, _) => ShowRawOrTree();
        var bar = new WrapPanel
        {
            Margin = new Thickness(0, 6, 0, 6),
            Children = { Pad(Labeled("Step", _step)), Pad(Labeled("Row", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { back, _row, next } })), Pad(Labeled("Response", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { paste, file } })), Pad(Labeled("Verb for new headers", _defaultVerb)), Pad(Labeled("View", _showRaw)) },
        };
        SetRow(bar, 0); SetColumnSpan(bar, 3);
        Children.Add(bar);
        var info = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 6), Children = { _source, _note } };
        SetRow(info, 1); SetColumnSpan(info, 3);
        Children.Add(info);

        _tree.ItemTemplate = new FuncTreeDataTemplate<ResponseNode>((node, _) => NodeView(node), node => node.Children);
        var treeStyle = new Style(x => x.OfType<TreeViewItem>());
        treeStyle.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, new Binding("Depth") { Converter = new FuncValueConverter<int, bool>(depth => depth < 1), Mode = BindingMode.OneTime }));
        _tree.Styles.Add(treeStyle);
        _raw.IsVisible = false;
        var left = new Border { BorderBrush = Brush.Parse("#26364A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Child = new Grid { Children = { _tree, _raw } } };
        SetRow(left, 2); SetColumn(left, 0);
        Children.Add(left);
        var splitter = new GridSplitter { Width = 6, Background = Brushes.Transparent };
        SetRow(splitter, 2); SetColumn(splitter, 1);
        Children.Add(splitter);
        _results.ItemTemplate = new FuncDataTemplate<ResultRow>((row, _) => ResultView(row));
        var right = new Border { BorderBrush = Brush.Parse("#26364A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Child = new ScrollViewer { Content = _results } };
        SetRow(right, 2); SetColumn(right, 2);
        Children.Add(right);

        var fill = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 8, 0, 0), Children = { new TextBlock { Text = "Cells of the new columns:", VerticalAlignment = VerticalAlignment.Center }, _fillNone, _fillThis, _fillAll } };
        var bottom = new StackPanel { Children = { _summary, fill } };
        SetRow(bottom, 3); SetColumnSpan(bottom, 3);
        Children.Add(bottom);

        _step.SelectionChanged += (_, _) => { if (!_loading && _step.SelectedItem is string step) { Session?.SetStep(step); ShowRow(); } };
        _row.SelectionChanged += (_, _) => { if (!_loading && _row.SelectedIndex >= 0 && Session is { } s) { s.SelectRow(s.RowIds[_row.SelectedIndex]); ShowRow(refreshRows: false); } };
        _defaultVerb.SelectionChanged += (_, _) => { if (!_loading && _defaultVerb.SelectedItem is string verb) { Session?.SetDefaultVerb(verb); RefreshResults(); } };
        foreach (var radio in new[] { _fillNone, _fillThis, _fillAll })
            radio.IsCheckedChanged += (_, _) => { if (Session is { } s) s.Fill = _fillNone.IsChecked == true ? ResponseFill.None : _fillAll.IsChecked == true ? ResponseFill.AllRows : ResponseFill.ThisRow; };

        if (steps.Count > 0)
        {
            var chosen = initialStep is not null && steps.Contains(initialStep, StringComparer.OrdinalIgnoreCase) ? steps.First(s => s.Equals(initialStep, StringComparison.OrdinalIgnoreCase)) : steps[^1];
            _loading = true;
            _step.SelectedItem = chosen;
            _loading = false;
            Session = new ResponseColumnsSession(editor, chosen);
            ShowRow();
        }
        else _note.Text = "This testcase has no steps yet.";
    }

    private static Control Pad(Control child) => new Border { Margin = new Thickness(0, 0, 12, 4), Child = child };
    private static StackPanel Labeled(string label, Control box) => new() { Spacing = 2, Children = { new TextBlock { Text = label, Foreground = Brush.Parse("#8EA4BC"), FontSize = 11 }, box } };

    /// <summary>The headers the choices make, for the window's Add button.</summary>
    public int IncludedCount => Session?.Included.Count ?? 0;

    // ---- Showing the session ---------------------------------------------------------------------------------

    private void ShowRow(bool refreshRows = true)
    {
        if (Session is not { } s) return;
        _loading = true;
        try
        {
            if (refreshRows)
            {
                _row.ItemsSource = s.RowIds.Select(id => s.HasResponse(id) ? id + "  ✓" : id + "  –").ToList();
                _row.SelectedIndex = s.RowIds.ToList().FindIndex(r => r.Equals(s.CurrentRow, StringComparison.OrdinalIgnoreCase));
            }
            else _row.ItemsSource = s.RowIds.Select(id => s.HasResponse(id) ? id + "  ✓" : id + "  –").ToList();
            _row.SelectedIndex = s.RowIds.ToList().FindIndex(r => r.Equals(s.CurrentRow, StringComparison.OrdinalIgnoreCase));
        }
        finally { _loading = false; }
        _refreshers.Clear();
        _tree.ItemsSource = s.Tree is null ? null : new[] { s.Tree };
        if (_showRaw.IsChecked == true) ShowRawOrTree();
        _source.Text = s.Source.Length == 0 ? string.Empty : $"Response of row {s.CurrentRow}: {s.Source}";
        RefreshResults();
    }

    private void RefreshResults()
    {
        if (Session is not { } s) return;
        _results.ItemsSource = s.Results;
        foreach (var refresh in _refreshers.ToList()) refresh();
        var notes = new List<string>();
        if (s.Note.Length > 0) notes.Add(s.Note);
        if (s.Tree is not null && s.Results.Count == 0) notes.Add("Tick a node on the left to take it as a header; right-click an object or array to flatten it.");
        _note.Text = string.Join(" ", notes);
        var parts = new List<string> { $"{s.Included.Count} of {s.Results.Count} headers will be added" };
        if (s.Results.Count(r => r.Existing) is > 0 and var already) parts.Add($"{already} already in the sheet");
        if (s.SkippedDeep > 0) parts.Add($"{s.SkippedDeep} deeper {(s.SkippedDeep == 1 ? "node" : "nodes")} not expanded (raise the depth)");
        if (s.Truncated) parts.Add($"cut at {ResponseTree.MaxHeaders} headers");
        _summary.Text = string.Join("; ", parts) + ".";
    }

    private Control NodeView(ResponseNode node)
    {
        var tick = new CheckBox { Tag = "node-tick", MinWidth = 0, Margin = new Thickness(0, 0, 4, 0) };
        var name = new TextBlock { Text = node.Path.Length == 0 ? "(response)" : node.Name, FontWeight = node.IsLeaf ? FontWeight.Normal : FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var kind = new TextBlock { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            Text = node.Kind switch { ResponseNodeKind.Object => $"{{ }} {node.Count}", ResponseNodeKind.Array => $"[ ] {node.Count}" + (node.Cut ? $" (first {node.Children.Count} shown)" : string.Empty), _ => string.Empty } };
        var value = new TextBlock { Text = node.IsLeaf ? (node.Value.Length > 50 ? node.Value[..50] + "…" : node.Value) : string.Empty, Foreground = Brush.Parse("#7DD3A8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var badge = new TextBlock { Foreground = Brush.Parse("#FFB86B"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Tag = "node-badge" };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Children = { tick, name, kind, value, badge } };
        if (node.OddName) ToolTip.SetTip(panel, "This name contains a dot or a bracket, so the runner cannot tell its path from a nested one.");

        void Refresh()
        {
            var choice = Session is { } s && s.Choices.TryGetValue(node.Path, out var c) ? c : null;
            _loading = true;
            tick.IsChecked = choice is not null;
            _loading = false;
            badge.Text = choice is null ? string.Empty : choice.Mode == NodeMode.AsOne ? (node.IsLeaf ? "1 header" : "1 header (count)") : $"⤓ {(choice.Depth == 0 ? "all levels" : choice.Depth + (choice.Depth == 1 ? " level" : " levels"))}" + (choice.Elements > 1 ? $" · {choice.Elements} elements" : string.Empty);
        }
        _refreshers.Add(Refresh);
        Refresh();
        tick.IsCheckedChanged += (_, _) =>
        {
            if (_loading || Session is not { } s) return;
            s.Choose(node.Path, tick.IsChecked == true ? new NodeChoice(NodeMode.AsOne) : null);
            RefreshResults();
        };
        panel.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(panel).Properties.IsRightButtonPressed) return;
            BuildMenu(node).Open(panel);
            e.Handled = true;
        };
        return panel;
    }

    private ContextMenu BuildMenu(ResponseNode node)
    {
        var menu = new ContextMenu();
        var chosen = Session is { } s && s.Choices.TryGetValue(node.Path, out var existing) ? existing : null;
        void Set(NodeChoice? choice) { Session?.Choose(node.Path, choice); RefreshResults(); }
        MenuItem Item(string header, Action click, bool enabled = true) { var item = new MenuItem { Header = header, IsEnabled = enabled }; item.Click += (_, _) => click(); return item; }

        if (node.IsLeaf) menu.Items.Add(Item(chosen is null ? "Take as one header" : "Remove the header", () => Set(chosen is null ? new NodeChoice(NodeMode.AsOne) : null)));
        else
        {
            var asOne = chosen?.Mode == NodeMode.AsOne;
            if (!node.Xml)   // the runner counts the elements of a JSON array or object; an XML element has no count
            menu.Items.Add(Item(asOne ? "Remove the count header" : $"Take as one header (count of {node.Count} {(node.Kind == ResponseNodeKind.Array ? "elements" : "properties")})", () => Set(asOne ? null : new NodeChoice(NodeMode.AsOne))));
            var flatten = new MenuItem { Header = node.Kind == ResponseNodeKind.Array ? "Flatten this array (first element of every array)" : "Flatten this object (first element of every array)" };
            for (var i = 0; i < Depths.Length; i++) { var depth = DepthValues[i]; flatten.Items.Add(Item(Depths[i], () => Set(new NodeChoice(NodeMode.Flatten, depth)))); }
            menu.Items.Add(flatten);
            var more = new MenuItem { Header = "Flatten, taking more array elements" };
            for (var i = 0; i < ElementTexts.Length; i++) { var elements = ElementValues[i]; more.Items.Add(Item(ElementTexts[i] + " (all levels)", () => Set(new NodeChoice(NodeMode.Flatten, 0, elements)))); }
            menu.Items.Add(more);
            menu.Items.Add(Item("Remove the choice", () => Set(null), chosen is not null));
            if (Session is { } session) menu.Items.Add(Item("Untick everything below", () => { foreach (var key in session.Choices.Keys.Where(k => k.StartsWith(node.Path + ".", StringComparison.Ordinal) || k.StartsWith(node.Path + "[", StringComparison.Ordinal)).ToList()) session.Choose(key, null); RefreshResults(); }));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy the path", async () => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(node.Path); }, node.Path.Length > 0));
        return menu;
    }

    private Control ResultView(ResultRow row)
    {
        var include = new CheckBox { Tag = "result-include", IsEnabled = row.CanInclude, MinWidth = 0 };
        include.Bind(ToggleButton_IsChecked, new Binding(nameof(ResultRow.Included)) { Mode = BindingMode.TwoWay });
        var verb = new ComboBox { Tag = "result-verb", ItemsSource = DataEditorViewModel.AssertionVerbs, MinWidth = 96, FontSize = 12, IsEnabled = row.CanInclude };
        verb.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(ResultRow.Verb)) { Mode = BindingMode.TwoWay });
        var header = new TextBlock { Text = $"{row.Step}.{row.Header.Path}", VerticalAlignment = VerticalAlignment.Center, Tag = "result-header", FontWeight = FontWeight.SemiBold };
        var value = new TextBlock { Text = row.Header.Value.Length == 0 ? string.Empty : "= " + row.ValueText, Foreground = Brush.Parse("#7DD3A8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var status = new TextBlock { Text = row.Status, Foreground = Brush.Parse(row.Existing ? "#6C7A8A" : "#8EA4BC"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Tag = "result-status" };
        include.IsCheckedChanged += (_, _) => RefreshSummaryOnly();
        var wizard = NewColumnWizard.Button(anchor => NewColumnWizard.Open(_editor, anchor, row.Value, row.WizardTitle, () =>
        {
            value.Text = row.TypedValue is { } typed ? "= " + typed : row.Header.Value.Length == 0 ? string.Empty : "= " + row.ValueText;
            value.Foreground = Brush.Parse(row.TypedValue is null ? "#7DD3A8" : "#FFB86B");
        }));
        wizard.IsEnabled = row.CanInclude;
        value.Tag = "result-value";
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2), Children = { include, verb, wizard, header, value, status } };
    }

    private static readonly AvaloniaProperty<bool?> ToggleButton_IsChecked = ToggleButton.IsCheckedProperty;

    private void RefreshSummaryOnly()
    {
        if (Session is not { } s) return;
        var parts = new List<string> { $"{s.Included.Count} of {s.Results.Count} headers will be added" };
        if (s.Results.Count(r => r.Existing) is > 0 and var already) parts.Add($"{already} already in the sheet");
        if (s.SkippedDeep > 0) parts.Add($"{s.SkippedDeep} deeper {(s.SkippedDeep == 1 ? "node" : "nodes")} not expanded (raise the depth)");
        if (s.Truncated) parts.Add($"cut at {ResponseTree.MaxHeaders} headers");
        _summary.Text = string.Join("; ", parts) + ".";
    }

    // ---- Paste and file --------------------------------------------------------------------------------------

    private async Task PasteAsync()
    {
        if (Session is not { } s || s.CurrentRow.Length == 0 || TopLevel.GetTopLevel(this) is not Window owner) return;
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 300, Tag = "paste-box", FontFamily = new FontFamily("Consolas, Menlo, monospace") };
        var ok = new Button { Content = "Use this response", Classes = { "primary" }, Tag = "paste-ok" };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        var dialog = new Window
        {
            Title = $"Response for row {s.CurrentRow}", Width = 640, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Margin = new Thickness(14), Children = { new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { cancel, ok } }, box } },
        };
        DockPanel.SetDock(((DockPanel)dialog.Content).Children[0], Dock.Bottom);
        ok.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        if (await dialog.ShowDialog<bool>(owner) && (box.Text ?? string.Empty).Trim().Length > 0) { s.SetOverride(s.CurrentRow, box.Text!); ShowRow(); }
    }

    private async Task PickFileAsync()
    {
        if (Session is not { } s || s.CurrentRow.Length == 0 || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = $"Response for row {s.CurrentRow}", AllowMultiple = false, SuggestedStartLocation = await PickerFolders.StartAsync(storage, "response-body") });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        PickerFolders.Remember("response-body", path);
        try { s.SetOverride(s.CurrentRow, await File.ReadAllTextAsync(path)); ShowRow(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _note.Text = $"Could not read the file: {ex.Message}"; }
    }

    /// <summary>Loads a response for the row shown from a file (also used by the tests).</summary>
    internal void UseText(string text) { Session?.SetOverride(Session.CurrentRow, text); ShowRow(); }
}
