using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace LoadTestingTool.UI.DataEditor;

// Right-click menus of the data grid: a row menu (add, insert, duplicate, copy, paste, colour, run, remove) and a column menu
// (insert, rename/remove, fill down, clear, colour, wrap). Clicking a column header picks the whole column.
public partial class DataEditorWindow
{
    private static readonly int[] CountChoices = [1, 2, 3, 5, 10, 20];

    // ---- Row menu ----------------------------------------------------------------------------

    /// <summary>Called for every cell click on the data grid: a right click on a row opens the row menu.</summary>
    private void OnDataGridCellPressed(DataGridCellPointerPressedEventArgs e)
    {
        var point = e.PointerPressedEventArgs.GetCurrentPoint(e.Cell);
        var right = point.Properties.IsRightButtonPressed;
        if (_shell.Current is not { } editor || this.FindControl<DataGrid>("Grid") is not { } grid) return;
        HandleCellSelectionPress(e, editor, grid, right);
        if (!right) { PickColumn(null); return; }
        if (e.Cell.DataContext is DataRowViewModel row && !grid.SelectedItems.Contains(row)) { grid.SelectedItem = row; editor.SelectedRow = row; }
        e.PointerPressedEventArgs.Handled = true;
        // On a request or expected cell the menu works on the selected cells; the tick, Data ID and Result cells open the row menu
        if (e.Column?.Tag is int cell && cell >= 1) BuildCellMenu(editor, grid, e.Cell).Open(e.Cell);
        else BuildRowMenu(editor, grid).Open(e.Cell);
    }

    public ContextMenu BuildRowMenu(DataEditorViewModel editor, DataGrid grid)
    {
        var selected = () => editor.VisibleRows.Where(grid.SelectedItems.OfType<DataRowViewModel>().ToHashSet().Contains).ToList();
        var haveRow = editor.SelectedRow is not null;
        var menu = new ContextMenu();
        menu.Items.Add(Item("▶  Run this row", () => editor.RunRowCommand.Execute(null), haveRow && editor.CanRun));
        menu.Items.Add(new Separator());
        menu.Items.Add(CountItem("Add rows at the end", n => editor.AddRows(n), editor.SelectedTestcase is not null));
        menu.Items.Add(CountItem("Insert above", n => editor.InsertRows(n, above: true), haveRow));
        menu.Items.Add(CountItem("Insert below", n => editor.InsertRows(n, above: false), haveRow));
        menu.Items.Add(CountItem("Duplicate (copies at the end)", n => editor.DuplicateRows(n), haveRow));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy row(s)", async () => { if (Clipboard is not null) await Clipboard.SetTextAsync(editor.CopyRowsText(selected())); }, haveRow));
        menu.Items.Add(Item("Paste row(s) below", async () =>
        {
            var text = Clipboard is null ? null : await Clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(text)) return;
            CommitPendingEdit();
            editor.PasteRowsBelow(ClipboardTable.Parse(text));
        }, editor.SelectedTestcase is not null));
        menu.Items.Add(new Separator());
        menu.Items.Add(ColourItem("Highlight row", haveRow,
            hex => editor.FormatCells(RowCells(editor, selected()), f => f with { Fill = hex }, "Highlighted row"),
            () => editor.FormatCells(RowCells(editor, selected()), f => f with { Fill = null }, "Cleared highlight")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remove row", () => editor.RemoveRowCommand.Execute(null), haveRow, danger: true));
        return menu;
    }

    private static List<(IFormattedRow Row, int Cell)> RowCells(DataEditorViewModel editor, IEnumerable<DataRowViewModel> rows)
        => rows.SelectMany(row => Enumerable.Range(0, editor.Columns.Count + 1).Select(c => ((IFormattedRow)row, c))).ToList();

    // ---- Column header: pick, rename and menu --------------------------------------------------

    private readonly List<FormattedTextColumn> _picked = [];
    private FormattedTextColumn? _pickAnchor;

    /// <summary>The cell indexes of the picked columns, left to right.</summary>
    private List<int> PickedCells() => _picked.Select(c => c.Tag is int cell ? cell : -1).Where(c => c > 0).OrderBy(c => c).ToList();

    /// <summary>
    /// A click on a column header picks the column (tinted). Ctrl+click adds or removes one, Shift+click picks everything from the last picked column.
    /// A right click opens the column menu for the picked columns; a double click renames the header in place.
    /// </summary>
    private void OnDataGridHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Avalonia.Visual source || source.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not { } header) return;
        if (e.Source is Visual clicked && clicked.FindAncestorOfType<Button>(includeSelf: true) is not null) return; // the ✦ button
        if (_shell.Current is not { } editor || this.FindControl<DataGrid>("Grid") is not { } grid) return;
        var column = grid.Columns.FirstOrDefault(c => ReferenceEquals(c.Header, header.Content));
        if (column?.Tag is not int cell || cell < 0) return;
        CommitPendingEdit();
        editor.CurrentCellIndex = cell;
        var right = e.GetCurrentPoint(header).Properties.IsRightButtonPressed;
        if (column is FormattedTextColumn formatted)
        {
            if (right) { if (!_picked.Contains(formatted)) PickColumn(formatted); }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _pickAnchor is not null) PickRange(grid, _pickAnchor, formatted);
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) TogglePick(formatted);
            else PickColumn(formatted);
        }
        else PickColumn(null);
        if (!right)
        {
            if (e.ClickCount == 2 && column is FormattedTextColumn && cell > 0) { e.Handled = true; ShowRenameHeader(editor, cell, header); }
            return;
        }
        e.Handled = true;
        BuildColumnMenu(editor, cell).Open(header);
    }

    private void PickColumn(FormattedTextColumn? column)
    {
        foreach (var old in _picked.Where(c => !ReferenceEquals(c, column)).ToList()) { old.Selected = false; _picked.Remove(old); }
        if (column is null) { _pickAnchor = null; return; }
        if (!_picked.Contains(column)) { _picked.Add(column); column.Selected = true; }
        _pickAnchor = column;
    }

    private void TogglePick(FormattedTextColumn column)
    {
        if (_picked.Remove(column)) { column.Selected = false; _pickAnchor = _picked.LastOrDefault(); }
        else { _picked.Add(column); column.Selected = true; _pickAnchor = column; }
    }

    private void PickRange(DataGrid grid, FormattedTextColumn from, FormattedTextColumn to)
    {
        var order = grid.Columns.OfType<FormattedTextColumn>().Where(c => c.Tag is int t && t > 0).ToList();
        var a = order.IndexOf(from); var b = order.IndexOf(to);
        if (a < 0 || b < 0) { PickColumn(to); return; }
        foreach (var column in _picked) column.Selected = false;
        _picked.Clear();
        foreach (var column in order.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)) { _picked.Add(column); column.Selected = true; }
    }

    /// <summary>A text box under the header: Enter renames, Escape cancels. Works on any column, no wizard button needed.</summary>
    internal void ShowRenameHeader(DataEditorViewModel editor, int cell, Control anchor)
    {
        if (cell < 1 || cell > editor.Columns.Count) return;
        var column = editor.Columns[cell - 1];
        var current = editor.HeaderText(cell);
        var box = new TextBox { Text = current, MinWidth = 260, Tag = "rename-header", Watermark = "Header name" };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = $"Rename header ({(column.Group == ColumnGroup.Response ? "response" : "request")} sheet)", FontSize = 11, Opacity = 0.75 }, box } }, Placement = PlacementMode.Bottom };
        void Commit()
        {
            var text = box.Text ?? string.Empty;
            flyout.Hide();
            if (text.Trim() != current) editor.RenameColumnTo(cell, text);
        }
        box.KeyDown += (_, k) =>
        {
            if (k.Key == Key.Enter) { k.Handled = true; Commit(); }
            else if (k.Key == Key.Escape) { k.Handled = true; flyout.Hide(); }
        };
        flyout.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        flyout.ShowAt(anchor);
    }

    /// <summary>The ✦ button of an expected column's header: lists the assertion verbs, the current one ticked.</summary>
    private void OpenVerbMenu(GridColumn column, Control anchor)
    {
        if (_shell.Current is not { } editor) return;
        var cell = editor.Columns.ToList().FindIndex(c => c.Group == column.Group && c.SheetColumn == column.SheetColumn) + 1;
        if (cell < 1) return;
        var current = editor.VerbOf(cell);
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = $"Assertion verb for {column.Title}", IsEnabled = false });
        menu.Items.Add(new Separator());
        foreach (var verb in DataEditorViewModel.AssertionVerbs)
        {
            var chosen = verb;
            var item = Item((verb.Equals(current, StringComparison.OrdinalIgnoreCase) ? "✓  " : "    ") + verb, () => { CommitPendingEdit(); editor.ChangeColumnVerb(cell, chosen); });
            menu.Items.Add(item);
        }
        menu.Open(anchor);
    }

    /// <summary>The rows a column action covers: the selected rows when two or more are selected, otherwise every row shown.</summary>
    private List<DataRowViewModel> ActionRows(DataEditorViewModel editor)
    {
        if (this.FindControl<DataGrid>("Grid") is { } grid)
        {
            var selected = grid.SelectedItems.OfType<DataRowViewModel>().ToHashSet();
            if (selected.Count >= 2) return editor.VisibleRows.Where(selected.Contains).ToList();
        }
        return editor.VisibleRows.ToList();
    }

    public ContextMenu BuildColumnMenu(DataEditorViewModel editor, int cell)
    {
        var data = cell >= 1 && cell <= editor.Columns.Count;
        var cells = PickedCells();
        if (!cells.Contains(cell)) cells = [cell];
        var many = cells.Count > 1;
        var rows = ActionRows(editor);
        var scope = rows.Count < editor.VisibleRows.Count ? $" in {rows.Count} selected rows" : string.Empty;
        var noun = many ? $"{cells.Count} columns" : "column";
        var menu = new ContextMenu();
        if (data)
        {
            menu.Items.Add(CountItem("Insert columns to the left", n => editor.OpenInsertColumns(left: true, n)));
            menu.Items.Add(CountItem("Insert columns to the right", n => editor.OpenInsertColumns(left: false, n)));
            menu.Items.Add(Item("Rename header (or double-click it)", () => { if (this.FindControl<DataGrid>("Grid") is { } g) ShowRenameHeader(editor, cell, HeaderOf(g, cell) ?? g); }, !many));
            menu.Items.Add(Item("Rename / remove column…", () => editor.OpenEditColumnCommand.Execute(null), !many));
            menu.Items.Add(Item(many ? $"Delete {cells.Count} columns" : "Delete column", () => { CommitPendingEdit(); editor.RemoveColumns(cells); }, danger: true));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(many ? $"Copy {cells.Count} columns{scope}" : $"Copy column{scope}", async () => { if (Clipboard is not null) await Clipboard.SetTextAsync(editor.CopyCellsText(cells, rows)); }));
            menu.Items.Add(Item(many ? $"Fill down {cells.Count} columns (first row's value){scope}" : "Fill down (first row's value)" + scope, () => { CommitPendingEdit(); editor.FillCellsDown(cells, rows); }));
            menu.Items.Add(Item(many ? $"Clear {cells.Count} columns{scope}" : "Clear column" + scope, () => { CommitPendingEdit(); editor.ClearCells(cells, rows); }, danger: true));
            menu.Items.Add(new Separator());
        }
        var block = cells.SelectMany(c => rows.Select(row => ((IFormattedRow)row, c))).ToList();
        menu.Items.Add(ColourItem(many ? $"Highlight {noun}" : "Highlight column", true,
            hex => editor.FormatCells(block, f => f with { Fill = hex }, "Highlighted " + noun),
            () => editor.FormatCells(block, f => f with { Fill = null }, "Cleared highlight")));
        menu.Items.Add(Item(many ? $"Wrap text in {noun}" : "Wrap text in column", () => editor.FormatCells(block, f => f with { Wrap = true }, "Wrapped text")));
        menu.Items.Add(Item(many ? $"No wrap in {noun}" : "No wrap in column", () => editor.FormatCells(block, f => f with { Wrap = false }, "Unwrapped text")));
        return menu;
    }

    private static Control? HeaderOf(DataGrid grid, int cell)
        => grid.GetVisualDescendants().OfType<DataGridColumnHeader>().FirstOrDefault(h => grid.Columns.FirstOrDefault(c => ReferenceEquals(c.Header, h.Content))?.Tag is int t && t == cell);

    private static List<(IFormattedRow Row, int Cell)> ColumnCells(DataEditorViewModel editor, int cell)
        => editor.VisibleRows.Select(row => ((IFormattedRow)row, cell)).ToList();

    // ---- Other sheets: row and column menus ------------------------------------------------------

    private ContextMenu BuildSheetRowMenu(DataEditorViewModel editor, DataGrid grid)
    {
        var selected = () => editor.SheetRows.Where(grid.SelectedItems.OfType<SheetRowViewModel>().ToHashSet().Contains).ToList();
        var haveRow = editor.SelectedSheetRow?.Row is not null;
        var menu = new ContextMenu();
        menu.Items.Add(CountItem("Add rows at the end", n => editor.AddSheetRows(n)));
        menu.Items.Add(CountItem("Insert above", n => editor.InsertSheetRows(n, above: true), haveRow));
        menu.Items.Add(CountItem("Insert below", n => editor.InsertSheetRows(n, above: false), haveRow));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy row(s)", async () => { if (Clipboard is not null) await Clipboard.SetTextAsync(editor.CopySheetRowsText(selected())); }, haveRow));
        menu.Items.Add(Item("Paste row(s) below", async () =>
        {
            var text = Clipboard is null ? null : await Clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(text)) return;
            CommitPendingEdit();
            editor.PasteSheetRowsBelow(ClipboardTable.Parse(text));
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Remove row", () => editor.RemoveSheetRowCommand.Execute(null), haveRow, danger: true));
        return menu;
    }

    private ContextMenu BuildSheetColumnMenu(DataEditorViewModel editor, int column)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CountItem("Insert columns to the left", n => editor.OpenInsertSheetColumns(left: true, n)));
        menu.Items.Add(CountItem("Insert columns to the right", n => editor.OpenInsertSheetColumns(left: false, n)));
        menu.Items.Add(Item("Rename column…", () => editor.OpenRenameSheetColumnCommand.Execute(null)));
        menu.Items.Add(Item("Remove column…", () => editor.OpenRemoveSheetColumnCommand.Execute(null), danger: true));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Fill down (first row's value)", () => { CommitPendingEdit(); editor.FillSheetColumnDown(column); }));
        menu.Items.Add(Item("Clear column", () => { CommitPendingEdit(); editor.ClearSheetColumn(column); }, danger: true));
        return menu;
    }

    // ---- Menu building blocks -----------------------------------------------------------------

    private static MenuItem Item(string header, Action click, bool enabled = true, bool danger = false)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (danger) item.Foreground = new SolidColorBrush(Color.Parse("#FF8998"));
        item.Click += (_, _) => click();
        return item;
    }

    private static MenuItem Item(string header, Func<Task> click, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += async (_, _) => await click();
        return item;
    }

    /// <summary>A sub-menu offering 1, 2, 3, 5, 10, 20 or "Other…" (asks for a number) for an action that takes a count.</summary>
    private MenuItem CountItem(string header, Action<int> run, bool enabled = true)
    {
        var parent = new MenuItem { Header = header, IsEnabled = enabled };
        foreach (var n in CountChoices)
        {
            var count = n;
            parent.Items.Add(Item(count == 1 ? "1" : count.ToString(), () => { CommitPendingEdit(); run(count); }));
        }
        parent.Items.Add(new Separator());
        parent.Items.Add(Item("Other…", async () =>
        {
            if (await CountPromptWindow.AskAsync(this, header + " — how many?") is not { } chosen) return;
            CommitPendingEdit();
            run(chosen);
        }));
        return parent;
    }

    private static MenuItem ColourItem(string header, bool enabled, Action<string> apply, Action clear)
    {
        var parent = new MenuItem { Header = header, IsEnabled = enabled };
        foreach (var (name, hex) in DataEditorViewModel.HighlightColors)
        {
            var colour = hex;
            var item = Item(name, () => apply(colour));
            item.Icon = new Border { Width = 14, Height = 14, CornerRadius = new Avalonia.CornerRadius(3), Background = new SolidColorBrush(Color.Parse(hex)), VerticalAlignment = VerticalAlignment.Center };
            parent.Items.Add(item);
        }
        parent.Items.Add(new Separator());
        parent.Items.Add(Item("Clear highlight", clear));
        return parent;
    }
}
