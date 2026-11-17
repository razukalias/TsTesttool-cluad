using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class GridMenuTests
{
    private static DataEditorViewModel Open(TempDir dir, int rows = 3) => new(Samples.CreateLarge(dir, rows), "Customers");

    private static List<string> Ids(DataEditorViewModel e) => e.VisibleRows.Select(r => r.Cells[0]).ToList();

    [Fact]
    public void Adding_several_rows_is_one_undo_step()
    {
        using var dir = new TempDir();
        var editor = Open(dir);

        editor.AddRows(3);

        Assert.Equal(6, editor.VisibleRows.Count);
        editor.UndoCommand.Execute(null);
        Assert.Equal(3, editor.VisibleRows.Count);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Several_rows_are_inserted_above_and_below_the_selected_row_in_order()
    {
        using var dir = new TempDir();
        var editor = Open(dir);

        editor.SelectedRow = editor.VisibleRows[1];                  // CUST002
        editor.InsertRows(2, above: true);
        Assert.Equal(["CUST001", "CUST004", "CUST005", "CUST002", "CUST003"], Ids(editor));

        editor.SelectedRow = editor.VisibleRows.First(r => r.Cells[0] == "CUST002");
        editor.InsertRows(1, above: false);
        Assert.Equal(["CUST001", "CUST004", "CUST005", "CUST002", "CUST006", "CUST003"], Ids(editor));

        editor.UndoCommand.Execute(null);
        editor.UndoCommand.Execute(null);
        Assert.Equal(["CUST001", "CUST002", "CUST003"], Ids(editor));
    }

    [Fact]
    public void Duplicating_a_row_several_times_copies_its_values()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.SelectedRow = editor.VisibleRows[0];

        editor.DuplicateRows(2);

        Assert.Equal(5, editor.VisibleRows.Count);
        Assert.Equal(editor.VisibleRows[0].Cells[1], editor.VisibleRows[4].Cells[1]);
        Assert.NotEqual(editor.VisibleRows[0].Cells[0], editor.VisibleRows[4].Cells[0]);
    }

    [Fact]
    public void Copied_rows_paste_below_as_new_rows_with_fresh_ids()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var text = editor.CopyRowsText([editor.VisibleRows[0], editor.VisibleRows[1]]);
        editor.SelectedRow = editor.VisibleRows[2];

        editor.PasteRowsBelow(ClipboardTable.Parse(text));

        Assert.Equal(5, editor.VisibleRows.Count);
        Assert.Equal(editor.VisibleRows[0].Cells[1], editor.VisibleRows[3].Cells[1]);
        Assert.Equal(editor.VisibleRows[1].Cells[1], editor.VisibleRows[4].Cells[1]);
        Assert.Equal(5, Ids(editor).Distinct().Count());
        editor.UndoCommand.Execute(null);
        Assert.Equal(3, editor.VisibleRows.Count);
    }

    [Fact]
    public void Clearing_and_filling_a_column_work_on_every_row_shown()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var cell = editor.Columns.ToList().FindIndex(c => c.Title.EndsWith("country", StringComparison.OrdinalIgnoreCase)) + 1;
        Assert.True(cell > 0);

        editor.FillColumnDown(cell);
        Assert.All(editor.VisibleRows, r => Assert.Equal(editor.VisibleRows[0].Cells[cell], r.Cells[cell]));

        editor.ClearColumn(cell);
        Assert.All(editor.VisibleRows, r => Assert.Equal(string.Empty, r.Cells[cell]));
        editor.UndoCommand.Execute(null);
        Assert.NotEqual(string.Empty, editor.VisibleRows[0].Cells[cell]);
    }

    [Fact]
    public void Several_columns_can_be_inserted_to_the_left_of_a_data_column_through_the_panel()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var before = editor.Columns.Count;
        editor.CurrentCellIndex = 2;                                   // the second data column, a request column

        editor.OpenInsertColumns(left: true, count: 3);
        Assert.True(editor.AddColumnVisible);
        Assert.Equal(DataEditorViewModel.PlaceBeforeCurrent, editor.NewColumnPlacement);
        editor.NewColumnName = "extra";
        editor.ConfirmAddColumnCommand.Execute(null);

        Assert.Equal(before + 3, editor.Columns.Count);
        var titles = editor.Columns.Select(c => c.Title).ToList();
        var first = titles.FindIndex(t => t.EndsWith("extra1"));
        Assert.True(first >= 0);
        Assert.Equal(["extra1", "extra2", "extra3"], titles.Skip(first).Take(3).Select(t => t[(t.LastIndexOf('.') + 1)..]).ToList());
    }

    [AvaloniaFact]
    public void The_row_and_column_menus_offer_what_was_asked_for()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<DataGrid>("Grid")!;

        var rowMenu = window.BuildRowMenu(window.Shell.Current!, grid);
        var rowItems = rowMenu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        foreach (var wanted in new[] { "Run this row", "Add rows at the end", "Insert above", "Insert below", "Duplicate", "Copy row(s)", "Paste row(s) below", "Highlight row", "Remove row" })
            Assert.Contains(rowItems, h => h!.Contains(wanted));
        var counts = rowMenu.Items.OfType<MenuItem>().First(i => (i.Header as string)!.StartsWith("Insert above")).Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Contains("10", counts);
        Assert.Contains("Other…", counts);

        var columnMenu = window.BuildColumnMenu(window.Shell.Current!, 2);
        var columnItems = columnMenu.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        foreach (var wanted in new[] { "Insert columns to the left", "Insert columns to the right", "Rename / remove column", "Fill down", "Clear column", "Highlight column", "Wrap text" })
            Assert.Contains(columnItems, h => h!.Contains(wanted));

        window.Close();
    }

    [AvaloniaFact]
    public void The_toolbar_no_longer_carries_the_buttons_that_moved_or_duplicate_the_tree()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Button>().Select(b => b.Content as string).Where(t => t is not null).ToList();
        foreach (var gone in new[] { "New testcase…", "Add row", "Insert above", "Insert below", "Duplicate", "Run row", "▶ Run…", "Edit cell…" })
            Assert.DoesNotContain(gone, texts);
        window.Close();
    }
}
