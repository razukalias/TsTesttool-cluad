using Avalonia.Controls;
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

    private FormattedTextColumn? _pickedColumn;

    // ---- Row menu ----------------------------------------------------------------------------

    /// <summary>Called for every cell click on the data grid: a right click on a row opens the row menu.</summary>
    private void OnDataGridCellPressed(DataGridCellPointerPressedEventArgs e)
    {
        var point = e.PointerPressedEventArgs.GetCurrentPoint(e.Cell);
        if (!point.Properties.IsRightButtonPressed) { PickColumn(null); return; }
        if (_shell.Current is not { } editor || this.FindControl<DataGrid>("Grid") is not { } grid) return;
        if (e.Cell.DataContext is DataRowViewModel row && !grid.SelectedItems.Contains(row)) { grid.SelectedItem = row; editor.SelectedRow = row; }
        e.PointerPressedEventArgs.Handled = true;
        BuildRowMenu(editor, grid).Open(e.Cell);
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

    // ---- Column header: pick and menu ---------------------------------------------------------

    /// <summary>A click on a column header picks the column (tinted); a right click also opens the column menu.</summary>
    private void OnDataGridHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Avalonia.Visual source || source.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not { } header) return;
        if (_shell.Current is not { } editor || this.FindControl<DataGrid>("Grid") is not { } grid) return;
        var column = grid.Columns.FirstOrDefault(c => ReferenceEquals(c.Header, header.Content));
        if (column?.Tag is not int cell || cell < 0) return;
        CommitPendingEdit();
        editor.CurrentCellIndex = cell;
        PickColumn(column as FormattedTextColumn);
        if (!e.GetCurrentPoint(header).Properties.IsRightButtonPressed) return;
        e.Handled = true;
        BuildColumnMenu(editor, cell).Open(header);
    }

    private void PickColumn(FormattedTextColumn? column)
    {
        if (ReferenceEquals(_pickedColumn, column)) return;
        if (_pickedColumn is not null) _pickedColumn.Selected = false;
        _pickedColumn = column;
        if (column is not null) column.Selected = true;
    }

    public ContextMenu BuildColumnMenu(DataEditorViewModel editor, int cell)
    {
        var data = cell >= 1 && cell <= editor.Columns.Count;
        var menu = new ContextMenu();
        if (data)
        {
            menu.Items.Add(CountItem("Insert columns to the left", n => editor.OpenInsertColumns(left: true, n)));
            menu.Items.Add(CountItem("Insert columns to the right", n => editor.OpenInsertColumns(left: false, n)));
            menu.Items.Add(Item("Rename / remove column…", () => editor.OpenEditColumnCommand.Execute(null)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Fill down (first row's value)", () => { CommitPendingEdit(); editor.FillColumnDown(cell); }));
            menu.Items.Add(Item("Clear column", () => { CommitPendingEdit(); editor.ClearColumn(cell); }, danger: true));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(ColourItem("Highlight column", true,
            hex => editor.FormatCells(ColumnCells(editor, cell), f => f with { Fill = hex }, "Highlighted column"),
            () => editor.FormatCells(ColumnCells(editor, cell), f => f with { Fill = null }, "Cleared highlight")));
        menu.Items.Add(Item("Wrap text in column", () => editor.FormatCells(ColumnCells(editor, cell), f => f with { Wrap = true }, "Wrapped text")));
        menu.Items.Add(Item("No wrap in column", () => editor.FormatCells(ColumnCells(editor, cell), f => f with { Wrap = false }, "Unwrapped text")));
        return menu;
    }

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
