using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class MultiRowActionTests
{
    private static DataEditorViewModel Open(TempDir dir, int rows = 5) => new(Samples.CreateLarge(dir, rows), "Customers");
    private static List<string> Ids(DataEditorViewModel e) => e.VisibleRows.Select(r => r.Cells[0]).ToList();

    [Fact]
    public void Removing_several_rows_removes_each_request_and_expected_row_and_is_one_undo_step()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var picked = new[] { editor.VisibleRows[0], editor.VisibleRows[2], editor.VisibleRows[3] };
        editor.SelectedRow = picked[0];

        editor.RemoveRows(picked);

        Assert.Equal(["CUST002", "CUST005"], Ids(editor));
        Assert.Equal(2, editor.SelectedTestcase!.RowCount);
        Assert.DoesNotContain(editor.Document.Response.Rows.Where(r => !r.IsBlank), r => r.Get("dataid") is "CUST001" or "CUST003" or "CUST004");
        editor.UndoCommand.Execute(null);
        Assert.Equal(5, editor.VisibleRows.Count);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Duplicating_the_selected_rows_adds_one_copy_of_each_at_the_end_with_their_values()
    {
        using var dir = new TempDir();
        var editor = Open(dir, 3);
        var picked = new[] { editor.VisibleRows[0], editor.VisibleRows[2] };
        var cell = editor.Columns.Count > 0 ? 1 : 0;
        var values = picked.Select(r => r.Cells[cell]).ToList();

        editor.DuplicateRows(picked);

        Assert.Equal(5, editor.VisibleRows.Count);
        Assert.Equal(values, editor.VisibleRows.Skip(3).Select(r => r.Cells[cell]).ToList());
        editor.UndoCommand.Execute(null);
        Assert.Equal(3, editor.VisibleRows.Count);
    }

    [Fact]
    public void Ticking_and_unticking_apply_to_every_selected_row()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var picked = new[] { editor.VisibleRows[1], editor.VisibleRows[3] };

        editor.TickRows(picked, true);
        Assert.Equal(["CUST002", "CUST004"], editor.VisibleRows.Where(r => r.IsTicked).Select(r => r.Cells[0]).ToList());
        editor.TickRows([picked[0]], false);
        Assert.Equal(["CUST004"], editor.VisibleRows.Where(r => r.IsTicked).Select(r => r.Cells[0]).ToList());
    }

    [Fact]
    public void Clearing_request_values_keeps_the_data_id_and_the_expected_values_and_undoes_in_one_step()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var picked = new[] { editor.VisibleRows[0], editor.VisibleRows[1] };
        var requestCell = editor.Columns.Select((c, i) => (c, i)).First(x => x.c.Group != ColumnGroup.Response && picked[0].Cells[x.i + 1].Length > 0).i + 1;
        var expectedCell = editor.Columns.Select((c, i) => (c, i)).First(x => x.c.Group == ColumnGroup.Response).i + 1;
        Assert.NotEmpty(picked[0].Cells[requestCell]);
        var expectedBefore = picked[0].Cells[expectedCell];

        editor.ClearRowValues(picked, expected: false);

        Assert.All(picked, r => Assert.Equal(string.Empty, r.Cells[requestCell]));
        Assert.All(picked, r => Assert.StartsWith("CUST", r.Cells[0]));
        Assert.Equal(expectedBefore, picked[0].Cells[expectedCell]);
        editor.UndoCommand.Execute(null);
        Assert.NotEmpty(editor.VisibleRows[0].Cells[requestCell]);   // undo rebuilds the rows
    }

    [Fact]
    public void Removing_rows_that_another_cell_reads_by_number_lists_those_cells_first()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, 3);
        var editor = new DataEditorViewModel(path, "Customers");
        var first = editor.VisibleRows[0];
        var sourceRow = first.Request.SourceRow!.Value;
        var header = editor.Document.Request.Headers.First(h => h.Length > 0 && !h.Contains('_') && !h.Contains('.') && !h.Equals("testcaseindex", StringComparison.OrdinalIgnoreCase) && !h.Equals("testcase", StringComparison.OrdinalIgnoreCase));
        var reader = editor.VisibleRows[2];
        var cell = editor.Columns.Select((c, i) => (c, i)).First(x => x.c.Group != ColumnGroup.Response).i + 1;
        reader.Cells[cell] = $"<From_{editor.Document.Request.SheetName}_{header}_{sourceRow}>";
        editor.SaveCommand.Execute(null);                                   // references are read from the saved file
        var again = new DataEditorViewModel(path, "Customers");

        Assert.NotEmpty(again.RemovalWarnings([again.VisibleRows[0]]));
        Assert.Empty(again.RemovalWarnings([again.VisibleRows[0], again.VisibleRows[2]]));   // the reader goes too
        Assert.Empty(again.RemovalWarnings([again.VisibleRows[1]]));
    }
}
