using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LoadTestingTool.UI.DataEditor;

// Excel-style cell selection in the table: mouse (click, Ctrl, Shift, drag), keys (arrows, Shift+arrows, Ctrl+A/C, Delete, Esc), and the cell menu.
public sealed partial class DataEditorWindow
{
    private bool _selectingByDrag;

    private void WireCellSelection(DataGrid grid, DataEditorViewModel editor)
    {
        editor.CellSelection.CellOrder = () => VisibleCellOrder(grid);
        editor.CellSelection.Changed -= OnCellSelectionChanged;
        editor.CellSelection.Changed += OnCellSelectionChanged;
        grid.RemoveHandler(PointerMovedEvent, OnGridPointerMoved);
        grid.AddHandler(PointerMovedEvent, OnGridPointerMoved, RoutingStrategies.Tunnel);
        grid.RemoveHandler(PointerReleasedEvent, OnGridPointerReleased);
        grid.AddHandler(PointerReleasedEvent, OnGridPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        grid.SelectionChanged -= OnGridSelectionClosesWizard;
        grid.SelectionChanged += OnGridSelectionClosesWizard;
        grid.RemoveHandler(ContextRequestedEvent, OnGridContextRequested);
        grid.AddHandler(ContextRequestedEvent, OnGridContextRequested, RoutingStrategies.Tunnel);
        grid.RemoveHandler(KeyDownEvent, OnGridCellKeyDown);
        grid.AddHandler(KeyDownEvent, OnGridCellKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The cell indexes of the data columns that are shown, left to right.</summary>
    private static IReadOnlyList<int> VisibleCellOrder(DataGrid grid)
        => grid.Columns.OfType<FormattedTextColumn>().Where(c => c.IsVisible && c.Tag is int cell && cell >= 1).OrderBy(c => c.DisplayIndex).Select(c => (int)c.Tag!).ToList();

    private static void OnGridSelectionClosesWizard(object? sender, SelectionChangedEventArgs e) => _openWizard?.Hide();

    private void OnCellSelectionChanged(object? sender, EventArgs e)
    {
        if (this.FindControl<DataGrid>("Grid") is not { } grid) return;
        _openWizard?.Hide();
        foreach (var column in grid.Columns.OfType<FormattedTextColumn>()) column.RefreshSelection();
    }

    /// <summary>A click on a request or expected cell: select it (Ctrl adds or removes, Shift extends, a plain click starts a drag). Other columns clear the selection.</summary>
    private void HandleCellSelectionPress(DataGridCellPointerPressedEventArgs e, DataEditorViewModel editor, DataGrid grid, bool right)
    {
        var selection = editor.CellSelection;
        if (e.Cell.DataContext is not DataRowViewModel row || e.Column?.Tag is not int cell || cell < 1) { selection.Clear(); return; }
        var keys = e.PointerPressedEventArgs.KeyModifiers;
        if (right) { if (!selection.Contains(row, cell)) selection.Select(row, cell); return; }
        if (keys.HasFlag(KeyModifiers.Control))
        {
            if (!selection.Toggle(row, cell)) editor.Hint("Request cells and expected cells cannot be selected together.");
            MakeCurrent(grid, row, e.Column);
            e.PointerPressedEventArgs.Handled = true;
        }
        else if (keys.HasFlag(KeyModifiers.Shift))
        {
            selection.ExtendTo(row, cell);
            MakeCurrent(grid, row, e.Column);
            e.PointerPressedEventArgs.Handled = true;
        }
        else
        {
            selection.Select(row, cell);
            _selectingByDrag = true;
        }
    }

    private static void MakeCurrent(DataGrid grid, DataRowViewModel row, DataGridColumn column)
    {
        grid.SelectedItem = row;
        grid.CurrentColumn = column;
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_selectingByDrag || _shell?.Current is not { } editor || sender is not DataGrid grid) return;
        if (!e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed) { _selectingByDrag = false; return; }
        if (grid.InputHitTest(e.GetPosition(grid)) is not Avalonia.Visual hit) return;
        var cell = hit.GetSelfAndVisualAncestors().OfType<DataGridCell>().FirstOrDefault();
        if (cell?.DataContext is not DataRowViewModel row || cell.Tag is not int index || index < 1) return;
        var selection = editor.CellSelection;
        if (selection.Active is { } active && ReferenceEquals(active.Row, row) && active.Cell == index && selection.Count <= 1) return;   // still inside the first cell: text selection
        selection.ExtendTo(row, index);
    }

    /// <summary>A cell in edit mode has a text box with its own Cut/Copy/Paste menu; it would open on top of ours (two menus at once, and a click away from them hung the app). Ours has Cut/Copy/Paste too, so the text box menu is suppressed.</summary>
    private void OnGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is Avalonia.Visual v && v.GetSelfAndVisualAncestors().OfType<TextBox>().Any(t => t.Tag is null or not "set-value")) e.Handled = true;
    }

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e) => _selectingByDrag = false;

    private void OnGridCellKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell?.Current is not { } editor || sender is not DataGrid grid || editor.ShowSteps) return;
        if (FocusManager?.GetFocusedElement() is TextBox) return;   // typing in a cell: the keys belong to the text box
        var selection = editor.CellSelection;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        (int Rows, int Columns)? step = e.Key switch { Key.Up => (-1, 0), Key.Down => (1, 0), Key.Left => (0, -1), Key.Right => (0, 1), _ => null };
        if (step is { } move)
        {
            if (shift && !control && selection.Active is not null)
            {
                selection.Move(move.Rows, move.Columns, extend: true);
                ScrollToActive(grid, selection, makeCurrent: false);
                e.Handled = true;
            }
            else Dispatcher.UIThread.Post(() => SelectCurrentCell(grid, editor));   // the grid moves its own current cell; the selection follows it
        }
        else if (control && e.Key == Key.A && selection.Active is not null) { selection.SelectAll(); e.Handled = true; }
        else if (control && e.Key == Key.C && selection.Count > 0)
        {
            if (Clipboard is not null) _ = Clipboard.SetTextAsync(editor.CopySelectionText());
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && selection.Count > 0) { CommitPendingEdit(); editor.SetSelectedCells(string.Empty); e.Handled = true; }
        else if (e.Key == Key.Escape && selection.Count > 0) { selection.Clear(); e.Handled = true; }
    }

    /// <summary>After a plain arrow key the grid has a new current cell: it becomes the selection.</summary>
    private static void SelectCurrentCell(DataGrid grid, DataEditorViewModel editor)
    {
        if (grid.SelectedItem is DataRowViewModel row && grid.CurrentColumn?.Tag is int cell && cell >= 1) editor.CellSelection.Select(row, cell);
    }

    private static void ScrollToActive(DataGrid grid, CellSelection selection, bool makeCurrent)
    {
        if (selection.Active is not { } active) return;
        var column = grid.Columns.OfType<FormattedTextColumn>().FirstOrDefault(c => c.Tag is int t && t == active.Cell);
        if (column is null) return;
        if (makeCurrent) MakeCurrent(grid, active.Row, column);
        grid.ScrollIntoView(active.Row, column);
    }

    // ---- Cell menu ---------------------------------------------------------------------------

    /// <summary>The menu of a right click on a request or expected cell: it works on all selected cells; the row actions are one entry below.</summary>
    public ContextMenu BuildCellMenu(DataEditorViewModel editor, DataGrid grid, Control anchor)
    {
        var selection = editor.CellSelection;
        var cells = selection.Cells;
        var count = cells.Count;
        var noun = count == 1 ? "cell" : $"{count} cells";
        var expected = selection.Kind == CellKind.Expected;
        var menu = new ContextMenu();
        if (count > 1)
        {
            // Actions on several cells at once; for one cell the ✦ button, the red mark and typing already do these
            menu.Items.Add(Item($"✦  Wizard for {noun}…", () => OpenSelectionWizard(editor, anchor)));
            var bug = new MenuItem { Header = $"⚑  Tag {noun} as known bug", IsEnabled = expected, Tag = "menu-bug" };
            if (!expected) ToolTip.SetTip(bug, "A bug tag only works on expected-value (response) cells");
            else
            {
                bug.Items.Add(Item("In all environments", () => editor.TagCellsAsBug(cells, "all")));
                foreach (var environment in editor.BugEnvironmentChoices)
                    bug.Items.Add(Item($"In {environment} only", () => editor.TagCellsAsBug(cells, environment)));
                bug.Items.Add(new Separator());
                bug.Items.Add(Item("Remove the bug tag", () => editor.RemoveBugTags(cells)));
            }
            menu.Items.Add(bug);
            menu.Items.Add(Item($"Set value in {noun}…", () => ShowSetValue(editor, anchor)));
            var assignable = cells.Any(c => editor.IsExpectedCell(c.Cell) && editor.FailureAt(c.Row, c.Cell) is not null);
            menu.Items.Add(Item("Assign actual value (failed cells)", () => editor.AssignActualToSelection(), assignable));
            menu.Items.Add(new Separator());
        }
        var edit = new MenuItem { Header = "Edit" };
        edit.Items.Add(Item("Copy", async () => { if (Clipboard is not null) await Clipboard.SetTextAsync(editor.CopySelectionText()); }, count > 0));
        edit.Items.Add(Item("Paste", async () =>
        {
            var text = Clipboard is null ? null : await Clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(text)) return;
            CommitPendingEdit();
            editor.PasteIntoSelection(ClipboardTable.Parse(text));
        }, count > 0));
        edit.Items.Add(Item("Clear", () => { CommitPendingEdit(); editor.SetSelectedCells(string.Empty); }, count > 0));
        menu.Items.Add(edit);
        menu.Items.Add(new Separator());
        // The row actions sit in the main menu
        var rowMenu = BuildRowMenu(editor, grid);
        foreach (var item in rowMenu.Items.Cast<object>().ToList()) { rowMenu.Items.Remove(item); menu.Items.Add(item); }
        return menu;
    }

    /// <summary>The ✦ wizard for the active cell; what it writes goes to every selected cell.</summary>
    private void OpenSelectionWizard(DataEditorViewModel editor, Control anchor)
    {
        var selection = editor.CellSelection;
        if (selection.Active is not { } active) return;
        var column = editor.Columns[active.Cell - 1];
        var expected = column.Group == ColumnGroup.Response;
        var dontRun = !expected && column.Title.Equals("dontrun", StringComparison.OrdinalIgnoreCase);
        var targets = selection.Cells.Where(c => !(ReferenceEquals(c.Row, active.Row) && c.Cell == active.Cell)).Select(c => ((ICellRow)c.Row, c.Cell)).ToList();
        ShowLineWizard(editor, anchor, active.Row, [], active.Cell, column.Title, expected, editor.WarningsFor(expected, dontRun), "cell", extra: null, targets);
    }

    /// <summary>A small popup to type one value that is written into every selected cell.</summary>
    private void ShowSetValue(DataEditorViewModel editor, Control anchor)
    {
        var count = editor.CellSelection.Count;
        var box = new TextBox { Watermark = $"Value for {(count == 1 ? "the cell" : $"{count} cells")}", MinWidth = 280, Tag = "set-value" };
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Apply() { flyout.Hide(); CommitPendingEdit(); editor.SetSelectedCells(box.Text ?? string.Empty); }
        var ok = new Button { Content = "Set", Tag = "set-value-ok" };
        ok.Click += (_, _) => Apply();
        box.KeyDown += (_, k) => { if (k.Key == Key.Enter) { Apply(); k.Handled = true; } };
        flyout.Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { box, ok } };
        flyout.ShowAt(anchor);
        Dispatcher.UIThread.Post(() => box.Focus());
    }
}
