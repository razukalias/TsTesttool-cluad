using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LoadTestingTool.Editing;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Record view: one data row, one line per column, the value as coloured chips with a [+] menu after the last one.</summary>
public sealed class RecordView : UserControl
{
    private readonly DataEditorViewModel _editor;
    private readonly StackPanel _body = new() { Spacing = 6, Margin = new Thickness(14) };

    public RecordView(DataEditorViewModel editor)
    {
        _editor = editor;
        Focusable = true;
        var scroll = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var bar = new ScrollViewer
        {
            Content = _bar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(14, 10, 14, 0), Tag = "row-bar",
        };
        DockPanel.SetDock(bar, Dock.Top);
        Content = new DockPanel { Children = { bar, scroll } };
        _barHost = bar;
        editor.RequestLines.CollectionChanged += (_, _) => Rebuild();
        editor.ExpectedLines.CollectionChanged += (_, _) => Rebuild();
        editor.VisibleRows.CollectionChanged += (_, _) => UpdateBar();
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DataEditorViewModel.RecordTitle)) { Rebuild(); UpdateBar(); }
        };
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            // a click on plain space gives the view the keyboard, so Left and Right switch rows; a click in a box or on a button keeps its own focus
            if (e.Source is Avalonia.Visual v && (Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<TextBox>(v, true) is not null || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Button>(v, true) is not null)) return;
            Focus();
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        UpdateBar();
        Rebuild();
    }

    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly ScrollViewer _barHost;

    /// <summary>The row bar: one button per row shown in Table view (position and Data ID); the selected row is highlighted and a row that failed is red. Buttons are reused so the focus stays where it is.</summary>
    private void UpdateBar()
    {
        var rows = _editor.VisibleRows;
        _barHost.IsVisible = rows.Count > 0;
        if (_bar.Children.Count != rows.Count)
        {
            _bar.Children.Clear();
            for (var i = 0; i < rows.Count; i++)
            {
                var button = new Button { Tag = "row-switch", MinHeight = 28, Padding = new Thickness(10, 2) };
                var index = i;
                button.Click += (_, _) => { if (index < _editor.VisibleRows.Count) _editor.SelectedRow = _editor.VisibleRows[index]; };
                _bar.Children.Add(button);
            }
        }
        for (var i = 0; i < rows.Count; i++)
        {
            var button = (Button)_bar.Children[i];
            var row = rows[i];
            var active = ReferenceEquals(row, _editor.SelectedRow);
            var label = new TextBlock { Text = $"{i + 1} · {row.CurrentDataId}" };
            if (row.Outcome is { Passed: false } && !active) label.Foreground = Brush.Parse("#FF7588");   // a null Foreground would draw nothing, so it is only set for a failed row
            button.Content = label;
            if (active) { if (!button.Classes.Contains("primary")) button.Classes.Add("primary"); }
            else button.Classes.Remove("primary");
            ToolTip.SetTip(button, row.Outcome is { Passed: false } ? "This row failed in the latest run" : "Show this row (or use the Left and Right arrow keys)");
            if (active) Avalonia.Threading.Dispatcher.UIThread.Post(() => button.BringIntoView());
        }
    }

    /// <summary>Left and Right move to the previous and next row, unless the keys are being used to edit text.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Left or Key.Right)) return;
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;
        var rows = _editor.VisibleRows;
        var at = _editor.SelectedRow is null ? -1 : rows.IndexOf(_editor.SelectedRow);
        var next = Math.Clamp(at + (e.Key == Key.Right ? 1 : -1), 0, rows.Count - 1);
        if (rows.Count == 0 || next == at) { e.Handled = rows.Count > 0; return; }
        _editor.SelectedRow = rows[next];
        e.Handled = true;
    }

    private bool _pending;

    private void Rebuild()
    {
        if (_pending) return;   // a rebuild fills the collections one line at a time; draw once afterwards
        _pending = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _pending = false; Draw(); });
    }

    public void Draw()
    {
        _body.Children.Clear();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (_editor.SelectedRow is not null) head.Children.Add(AddColumnsButton());
        head.Children.Add(new TextBlock { Text = _editor.RecordTitle, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _body.Children.Add(head);
        if (_editor.SelectedRow is null) return;
        if (_editor.SelectedTestcase?.ControlSummary is { Length: > 0 } controls)
            _body.Children.Add(new TextBlock { Text = controls, Tag = "control-summary", Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        Section("REQUEST", _editor.RequestLines);
        Section("EXPECTED RESPONSE", _editor.ExpectedLines);
    }

    /// <summary>"Add columns…": one form with the Request (template placeholders), Response (fields of the last response) and Response header tabs.</summary>
    private Button AddColumnsButton()
    {
        var button = new Button { Content = "Add columns…", Tag = "add-columns-open", HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 32, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(button, Dock.Right);
        ToolTip.SetTip(button, "Create request columns from the template's _variable_ placeholders, and expected columns from the fields and HTTP headers of a step's last response");
        button.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window owner) await new AddColumnsWindow(_editor).ShowDialog(owner);
        };
        return button;
    }

    private void Section(string title, IEnumerable<RecordLineViewModel> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        _body.Children.Add(new TextBlock { Text = title, Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 10, 0, 2) });
        foreach (var line in list) _body.Children.Add(new RecordLineView(line, _editor));
    }
}

/// <summary>One column of the row: header on the left, the value parts (with condition and [+]) on the right, raw text and an example below.</summary>
public sealed class RecordLineView : Border
{
    private static readonly IReadOnlyDictionary<ValuePartKind, (string Back, string Fore)> Colors = new Dictionary<ValuePartKind, (string, string)>
    {
        [ValuePartKind.Text] = ("#1C2E47", "#D6E2F2"), [ValuePartKind.RandomNumber] = ("#3A2A55", "#D9C4FF"), [ValuePartKind.Guid] = ("#4A3A1F", "#FFD58A"),
        [ValuePartKind.DateTime] = ("#1F4A47", "#8FE8DD"), [ValuePartKind.Reference] = ("#16335A", "#8CC1FF"), [ValuePartKind.File] = ("#46283A", "#FFA6D0"), [ValuePartKind.Script] = ("#2A3F1F", "#B9E58A"),
    };

    private readonly RecordLineViewModel _line;
    private readonly DataEditorViewModel _editor;
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _alternatives = new() { Spacing = 4, Tag = "alternatives" };
    private readonly TextBlock _raw = new() { FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 11, Foreground = Brush.Parse("#6F87A3"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _warning = new() { FontSize = 11, Foreground = Brush.Parse("#F6A623"), TextWrapping = TextWrapping.Wrap, Tag = "line-warning" };
    private readonly TextBlock _help = new() { FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), TextWrapping = TextWrapping.Wrap, Tag = "line-help", IsVisible = false };
    private readonly TextBlock _example = new() { FontSize = 11, Foreground = Brush.Parse("#6F87A3"), TextWrapping = TextWrapping.Wrap };

    public RecordLineView(RecordLineViewModel line, DataEditorViewModel editor)
    {
        _line = line;
        _editor = editor;
        Background = Brush.Parse("#13233A");
        BorderBrush = Brush.Parse("#22364F");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(12, 8);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*") };
        grid.Children.Add(new TextBlock { Text = line.Title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var right = new StackPanel { Spacing = 3 };
        Grid.SetColumn(right, 1);
        right.Children.Add(_chips);
        right.Children.Add(_alternatives);
        if (line.IsDontRun || line.HasCondition) right.Children.Add(_help);
        right.Children.Add(_raw);
        right.Children.Add(_example);
        right.Children.Add(_warning);
        grid.Children.Add(right);
        Child = grid;
        line.Parts.CollectionChanged += (_, _) => Render();
        line.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        _chips.Children.Clear();
        if (_line.HasCondition)
        {
            var box = new ComboBox { ItemsSource = RecordLineViewModel.ConditionChoices, SelectedItem = _line.ConditionWords, MinWidth = 150, Margin = new Thickness(0, 0, 8, 4), Tag = "condition" };
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is string words && words != _line.ConditionWords) _line.ConditionWords = words; };
            _chips.Children.Add(box);
            if (_line.ExtraVerbWords.Length > 0) _chips.Children.Add(new TextBlock { Text = _line.ExtraVerbWords + " (set in the column header)", Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4), Tag = "header-or-note" });
        }
        if (_line.ShowsValue)
        {
            for (var i = 0; i < _line.Parts.Count; i++) _chips.Children.Add(Chip(i));
            _chips.Children.Add(AddButton());
            if (_line.CanCombine)
            {
                _chips.Children.Add(AlternativeButton(true));
                _chips.Children.Add(AlternativeButton(false));
            }
            if (_line.IsDontRun) _chips.Children.Add(StepsButton());
            _chips.Children.Add(CopyButton());
        }
        RenderAlternatives();
        var generated = _line.Parts.Any(p => p.Kind != ValuePartKind.Text);
        _raw.Text = _line.Raw.Length == 0 ? "(empty)" : "stored as: " + _line.Raw;
        _raw.IsVisible = generated || _line.Raw.Length == 0 || _line.HasCondition && _line.Raw.StartsWith('{');   // plain text needs no second copy
        _help.Text = _line.IsDontRun ? RecordLineViewModel.DontRunHelp : RecordLineViewModel.ConditionHelp;
        _help.IsVisible = _line.IsDontRun || _line.CanCombine && _line.ShowsValue;
        _example.Text = generated ? "e.g. " + _line.Example : string.Empty;
        _example.IsVisible = generated;
        _warning.Text = _line.Warnings.Count == 0 ? string.Empty : "⚠ " + string.Join(" ", _line.Warnings);
        _warning.IsVisible = _line.Warnings.Count > 0;
    }

    private Control AlternativeButton(bool isOr)
    {
        var button = new Button { Content = isOr ? "+ OR" : "+ AND", Tag = isOr ? "alt-add-or" : "alt-add-and", Padding = new Thickness(10, 3), Margin = new Thickness(0, 0, 6, 4), MinHeight = 28, CornerRadius = new CornerRadius(14) };
        ToolTip.SetTip(button, isOr ? "Another check of this field: the cell passes when this OR the new check passes" : "Another check of this field: the cell passes when this AND the new check pass (AND binds tighter than OR)");
        button.Click += (_, _) => _line.AddAlternative(isOr);
        return button;
    }

    /// <summary>The OR / AND checks below the value: the kind (click to switch), the verb, the value and a ✕.</summary>
    private void RenderAlternatives()
    {
        _alternatives.Children.Clear();
        var verbs = ValueParts.Conditions.Where(c => AssertionAlternatives.CanCombine(c.Verb)).ToList();
        for (var i = 0; i < _line.Alternatives.Count; i++)
        {
            var index = i;
            var item = _line.Alternatives[i];
            var kind = new Button { Content = item.IsOr ? "OR" : "AND", Tag = "alt-kind", MinWidth = 52, Padding = new Thickness(8, 3), Background = Brush.Parse(item.IsOr ? "#3B2F6B" : "#1E4D5F") };
            ToolTip.SetTip(kind, "Click to switch between OR and AND");
            kind.Click += (_, _) => _line.ChangeAlternative(index, item with { IsOr = !item.IsOr });
            var verb = new ComboBox { ItemsSource = verbs.Select(v => v.Words).ToList(), SelectedItem = verbs.FirstOrDefault(v => v.Verb == item.Verb).Words ?? verbs.First().Words, MinWidth = 150, Tag = "alt-verb" };
            verb.SelectionChanged += (_, _) => { if (verb.SelectedItem is string words && verbs.First(v => v.Words == words).Verb is var chosen && chosen != item.Verb) _line.ChangeAlternative(index, item with { Verb = chosen }); };
            var value = new TextBox { Text = item.Value, MinWidth = 160, Watermark = "value", Tag = "alt-value", IsVisible = ValueParts.NeedsValue(item.Verb) };
            void Commit() { var text = (value.Text ?? string.Empty).Trim(); if (text != item.Value) _line.ChangeAlternative(index, item with { Value = text }); }
            value.LostFocus += (_, _) => Commit();
            value.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            var remove = new Button { Content = "✕", Tag = "alt-remove", Padding = new Thickness(8, 3) };
            ToolTip.SetTip(remove, "Remove this check");
            remove.Click += (_, _) => _line.RemoveAlternative(index);
            _alternatives.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Tag = "alt-row", Children = { kind, verb, value, remove } });
        }
    }

    private Control Chip(int index)
    {
        var part = _line.Parts[index];
        var (back, fore) = Colors[part.Kind];
        var button = new Button
        {
            Content = new TextBlock { Text = part.Kind == ValuePartKind.Text ? "“" + part.Label + "”" : part.Label, Foreground = Brush.Parse(fore), MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis },
            Background = Brush.Parse(back), Padding = new Thickness(10, 3), Margin = new Thickness(0, 0, 6, 4), MinHeight = 28, CornerRadius = new CornerRadius(14), Tag = "chip",
        };
        ToolTip.SetTip(button, part.Kind + ": click to change");
        button.Flyout = ChipFlyout(index, part);
        return button;
    }

    private Flyout ChipFlyout(int index, ValuePart part)
    {
        var box = new TextBox { Text = part.Raw, MinWidth = 320, AcceptsReturn = part.Kind == ValuePartKind.Text, Tag = "chip-edit" };
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        void Apply()
        {
            flyout.Hide();
            var text = box.Text ?? string.Empty;
            if (text == part.Raw) return;
            if (text.Length == 0) { _line.Remove(index); return; }
            var parsed = ValueParts.Parse(text);   // a changed chip may turn into several parts
            _line.Remove(index);
            for (var k = 0; k < parsed.Count; k++) InsertAt(index + k, parsed[k]);
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && !box.AcceptsReturn) { Apply(); e.Handled = true; } };
        var left = new Button { Content = "◀", Tag = "chip-left", IsEnabled = index > 0 };
        var right = new Button { Content = "▶", Tag = "chip-right", IsEnabled = index < _line.Parts.Count - 1 };
        var remove = new Button { Content = "✕ Remove", Tag = "chip-remove" };
        var ok = new Button { Content = "OK", Classes = { "primary" }, Tag = "chip-ok" };
        var editScript = new Button { Content = "⚙ Edit script…", Tag = "chip-edit-script", IsVisible = part.Kind == ValuePartKind.Script };
        editScript.Click += (_, _) =>
        {
            flyout.Hide();
            _ = AddScriptAsync(changed => { var i = index; _line.Remove(i); InsertAt(i, changed); }, part.Raw);
        };
        left.Click += (_, _) => { flyout.Hide(); _line.Move(index, -1); };
        right.Click += (_, _) => { flyout.Hide(); _line.Move(index, 1); };
        remove.Click += (_, _) => { flyout.Hide(); _line.Remove(index); };
        ok.Click += (_, _) => Apply();
        flyout.Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = part.Kind == ValuePartKind.Text ? "Text" : "As stored in the workbook", Foreground = Brush.Parse("#8EA4BC"), FontSize = 11 }, box, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { left, right, editScript, remove, ok } } } };
        flyout.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        return flyout;
    }

    private void InsertAt(int index, ValuePart part)
    {
        _line.Add(part);
        _line.Move(_line.Parts.Count - 1, index - (_line.Parts.Count - 1));
    }

    /// <summary>For the <c>dontrun</c> column: tick the steps this row skips instead of typing their names.</summary>
    private Control StepsButton()
    {
        var button = new Button { Content = "Choose steps ▾", Tag = "dontrun-steps", MinHeight = 28, Margin = new Thickness(8, 0, 0, 4), Padding = new Thickness(10, 0) };
        ToolTip.SetTip(button, "Tick the steps of this testcase that this row skips. Names already listed that are not steps stay as they are (and are warned about)");
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var list = new StackPanel { Spacing = 4 };
        var note = new TextBlock { Text = "This testcase has no steps yet.", Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, IsVisible = false };
        flyout.Content = new StackPanel { Spacing = 8, MinWidth = 240, Children = { new TextBlock { Text = "Skip these steps for this row", Foreground = Brush.Parse("#8EA4BC"), FontSize = 11 }, list, note } };
        flyout.Opened += (_, _) =>
        {
            var steps = _editor.ResponseSteps();
            var listed = _line.ListedSteps;
            list.Children.Clear();
            note.IsVisible = steps.Count == 0;
            foreach (var step in steps)
            {
                var box = new CheckBox { Content = step, Tag = "dontrun-step", IsChecked = listed.Contains(step, StringComparer.OrdinalIgnoreCase) };
                box.IsCheckedChanged += (_, _) =>
                {
                    // the ticked steps in testcase order, then anything listed that is no step (kept so a typo stays visible)
                    var ticked = list.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content!).ToList();
                    var others = _line.ListedSteps.Where(n => !steps.Contains(n, StringComparer.OrdinalIgnoreCase));
                    _line.SetListedSteps(ticked.Concat(others));
                };
                list.Children.Add(box);
            }
        };
        button.Flyout = flyout;
        return button;
    }

    private Control CopyButton()
    {
        var copy = new Button { Content = "Copy to rows ▾", Tag = "copy-to-rows", MinHeight = 28, Margin = new Thickness(8, 0, 0, 4), Padding = new Thickness(10, 0) };
        ToolTip.SetTip(copy, "Write this value into the same column of other rows. A random number, GUID or date still gives every row its own value when the test runs");
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        Button Item(string text, DataEditorViewModel.CopyScope scope, string tag)
        {
            var item = new Button { Content = text, Tag = tag, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            item.Click += (_, _) => { flyout.Hide(); _editor.CopyLineToRows(_line, scope); };
            return item;
        }
        flyout.Content = new StackPanel { Spacing = 4, MinWidth = 260, Children =
        {
            Item("All other rows of this testcase", DataEditorViewModel.CopyScope.TestcaseRows, "copy-testcase"),
            Item("All rows shown in Table view", DataEditorViewModel.CopyScope.ShownRows, "copy-shown"),
            Item("All rows of the instance", DataEditorViewModel.CopyScope.AllRowsOfInstance, "copy-instance"),
        } };
        copy.Flyout = flyout;
        return copy;
    }

    /// <summary>The Script dialog: pick a template and its parameters; the part is added to the line (or replaces the one being edited).</summary>
    private async Task AddScriptAsync(Action<ValuePart> add, string? existingToken)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (await ScriptPartWindow.AskAsync(owner, _editor, existingToken) is { } part) add(part);
    }

    /// <summary>Writes a new template first, then opens the Script dialog with it selected.</summary>
    private async Task NewTemplateThenAddAsync(Action<ValuePart> add)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var root = Path.GetDirectoryName(Path.GetFullPath(_editor.WorkbookPath)) ?? ".";
        var saved = await ScriptTemplateWindow.OpenAsync(owner, root, "Templates", null, _editor.MenuSources().RequestHeaders);
        if (saved is null) return;
        if (await ScriptPartWindow.AskAsync(owner, _editor, LoadTestingTool.Editing.ScriptTemplates.Token(saved, [])) is { } part) add(part);
    }

    /// <summary>
    /// Opens the file picker in the instance's Files folder and hands over a path that survives moving the application: relative to Files, to the instance, or to it with ../ for files
    /// elsewhere in the application. A file outside the application is copied into Files, so the cell never holds a path that only works on this computer.
    /// </summary>
    private async Task PickFileAsync(bool base64, Action<ValuePart> add)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var instance = Path.GetDirectoryName(Path.GetFullPath(_editor.WorkbookPath)) ?? ".";
            var files = Path.Combine(instance, LoadTestingTool.Execution.FileTokens.FilesFolder);
            Directory.CreateDirectory(files);
            var start = await top.StorageProvider.TryGetFolderFromPathAsync(files);
            var picked = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { Title = base64 ? "Choose a file (sent as Base64)" : "Choose a file (sent as text)", SuggestedStartLocation = start, AllowMultiple = false });
            if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } file) return;
            var (path, note) = PickedFile.Prepare(instance, file);
            add(ValuePart.File(path, base64));
            if (note is not null) _editor.Notify(note, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _editor.Notify($"Could not use that file: {ex.Message}", true); }
    }

    private Control AddButton()
    {
        var plus = new Button { Content = "＋", Tag = "add-part", MinHeight = 28, MinWidth = 32, Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 0) };
        ToolTip.SetTip(plus, "Add a part: text, a value from another cell, a random number, a GUID, a date or a file");
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var hooks = new RecordMenu.ScriptHooks(
            add => { flyout.Hide(); _ = AddScriptAsync(add, null); },
            add => { flyout.Hide(); _ = NewTemplateThenAddAsync(add); });
        var fileHooks = new RecordMenu.FileHooks((base64, add) => { flyout.Hide(); _ = PickFileAsync(base64, add); });
        var menu = new DrillDownMenu(RecordMenu.Root(_editor.MenuSources(), hooks, fileHooks), part => { flyout.Hide(); _line.Add(part); });
        flyout.Content = menu;
        flyout.Opened += (_, _) => menu.Reset();
        plus.Flyout = flyout;
        return plus;
    }
}

/// <summary>The [+] menu: one list at a time with a breadcrumb to go up and a filter box on every level.</summary>
public sealed class DrillDownMenu : UserControl
{
    private readonly IReadOnlyList<MenuNode> _root;
    private readonly Action<ValuePart> _picked;
    private readonly List<MenuNode> _path = [];
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly TextBox _filter = new() { Watermark = "Type to filter…", Tag = "menu-filter" };
    private readonly ListBox _list = new() { MaxHeight = 280, Tag = "menu-list" };
    private readonly TextBox _input = new() { Tag = "menu-input" };
    private readonly TextBlock _prompt = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11 };
    private readonly StackPanel _promptPanel = new() { Spacing = 6, IsVisible = false };
    private MenuNode? _asking;
    private IReadOnlyList<MenuNode> _level;

    public DrillDownMenu(IReadOnlyList<MenuNode> root, Action<ValuePart> picked)
    {
        _root = root;
        _level = root;
        _picked = picked;
        _list.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(MenuNode.Display));
        _list.DoubleTapped += (_, _) => Choose();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Choose(); e.Handled = true; } };
        _filter.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(_ => Fill()));
        _filter.KeyDown += OnFilterKey;
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        var ok = new Button { Content = "Add", Classes = { "primary" }, Tag = "menu-input-ok" };
        ok.Click += (_, _) => Accept();
        _promptPanel.Children.Add(_prompt);
        _promptPanel.Children.Add(_input);
        _promptPanel.Children.Add(ok);
        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(_crumbs);
        panel.Children.Add(_filter);
        panel.Children.Add(_list);
        panel.Children.Add(_promptPanel);
        Content = panel;
        Reset();
    }

    /// <summary>The names of the lists from the first to the current, for the breadcrumb.</summary>
    public IReadOnlyList<string> Path => _path.Select(n => n.Title).ToList();
    public IReadOnlyList<MenuNode> Shown => (_list.ItemsSource as IEnumerable<MenuNode>)?.ToList() ?? [];

    public void Reset()
    {
        _path.Clear();
        _asking = null;
        _level = _root;
        _filter.Text = string.Empty;
        Fill();
        _filter.Focus();
    }

    private void Fill()
    {
        _asking = null;
        _promptPanel.IsVisible = false;
        _list.IsVisible = true; _filter.IsVisible = true;
        var shown = RecordMenu.Filter(_level, _filter.Text ?? string.Empty);
        _list.ItemsSource = shown;
        if (shown.Count > 0) _list.SelectedIndex = 0;
        _crumbs.Children.Clear();
        var home = new Button { Content = _path.Count == 0 ? "Add part" : "‹ Add part", Padding = new Thickness(4, 0), Background = Brushes.Transparent, Tag = "crumb" };
        home.Click += (_, _) => GoTo(0);
        _crumbs.Children.Add(home);
        for (var i = 0; i < _path.Count; i++)
        {
            var depth = i + 1;
            _crumbs.Children.Add(new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush.Parse("#8EA4BC") });
            var crumb = new Button { Content = _path[i].Title, Padding = new Thickness(4, 0), Background = Brushes.Transparent, Tag = "crumb" };
            crumb.Click += (_, _) => GoTo(depth);
            _crumbs.Children.Add(crumb);
        }
    }

    private void GoTo(int depth)
    {
        while (_path.Count > depth) _path.RemoveAt(_path.Count - 1);
        _level = _path.Count == 0 ? _root : _path[^1].Children!();
        _filter.Text = string.Empty;
        Fill();
        _filter.Focus();
    }

    private void OnFilterKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1); e.Handled = true; break;
            case Key.Up: _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = true; break;
            case Key.Enter: Choose(); e.Handled = true; break;
            case Key.Back when string.IsNullOrEmpty(_filter.Text) && _path.Count > 0: GoTo(_path.Count - 1); e.Handled = true; break;
        }
    }

    /// <summary>Picks the highlighted item: opens a folder, asks for text, or adds the part.</summary>
    public void Choose()
    {
        if (_list.SelectedItem is not MenuNode node) return;
        if (node.IsFolder) { _path.Add(node); _level = node.Children!(); _filter.Text = string.Empty; Fill(); _filter.Focus(); return; }
        if (node.Prompt is not null)
        {
            _asking = node;
            _prompt.Text = node.Prompt;
            _input.Text = string.Empty;
            _list.IsVisible = false; _filter.IsVisible = false; _promptPanel.IsVisible = true;
            _input.Focus();
            return;
        }
        if (node.Open is { } open) { open(_picked); return; }
        if (node.Result is { } part) _picked(part);
    }

    private void Accept()
    {
        if (_asking?.FromInput?.Invoke(_input.Text ?? string.Empty) is { } part) _picked(part);
    }
}
