using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Row and column operations of the other sheets, including the <From_Sheet_header_N> references that must follow the rows they point at.
public class SheetRowMenuTests
{
    private static DataEditorViewModel Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var book = new XLWorkbook(path))
        {
            var notes = book.AddWorksheet("Notes");
            notes.Cell(1, 1).Value = "topic"; notes.Cell(1, 2).Value = "text";
            notes.Cell(2, 1).Value = "auth"; notes.Cell(2, 2).Value = "hourly";
            notes.Cell(3, 1).Value = "limits"; notes.Cell(3, 2).Value = "daily";
            notes.Cell(4, 1).Value = "retry"; notes.Cell(4, 2).Value = "three times";
            book.Worksheet("request").Cell(2, 8).Value = "<From_Notes_text_3>";   // "daily"
            book.Save();
        }
        var editor = new DataEditorViewModel(path, "Customers") { ActiveSheet = "Notes" };
        return editor;
    }

    private static string Cell(string path, string sheet, int row, int column)
    {
        using var book = new XLWorkbook(path);
        return book.Worksheet(sheet).Cell(row, column).GetString();
    }

    [Fact]
    public void Inserting_above_a_row_in_the_middle_keeps_references_pointing_at_the_same_text_and_undo_reverts()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.SelectedSheetRow = editor.SheetRows[1];             // "limits", sheet row 3, referenced from the request sheet

        editor.InsertSheetRows(2, above: true);

        Assert.Equal(["auth", "", "", "limits", "retry"], editor.SheetRows.Select(r => r.Cells[0]).ToArray());
        Assert.Contains("renumbers 1 <From_…_N> reference", editor.StatusMessage);

        editor.SheetRows[1].Cells[0] = "new1"; editor.SheetRows[1].Cells[1] = "x";
        editor.SheetRows[2].Cells[0] = "new2"; editor.SheetRows[2].Cells[1] = "y";
        editor.SaveCommand.Execute(null);

        Assert.Equal("limits", Cell(editor.WorkbookPath, "Notes", 5, 1));
        Assert.Equal("daily", Cell(editor.WorkbookPath, "Notes", 5, 2));
        Assert.Equal("<From_Notes_text_5>", Cell(editor.WorkbookPath, "request", 2, 8));   // still "daily", now at row 5
        Assert.Equal(["auth", "new1", "new2", "limits", "retry"], editor.SheetRows.Select(r => r.Cells[0]).ToArray());   // reloaded

        editor.UndoCommand.Execute(null);                          // nothing to undo after a save: the history starts clean
    }

    [Fact]
    public void Inserting_below_the_last_row_just_adds_at_the_end_and_undo_removes_the_new_rows()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.SelectedSheetRow = editor.SheetRows[2];

        editor.InsertSheetRows(1, above: false);
        Assert.Equal(4, editor.SheetRows.Count);
        Assert.Contains("No <From_…_N> references need renumbering", editor.StatusMessage);

        editor.UndoCommand.Execute(null);
        Assert.Equal(3, editor.SheetRows.Count);
    }

    [Fact]
    public void Pasted_rows_go_below_the_selected_row_and_references_below_follow()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.SelectedSheetRow = editor.SheetRows[0];             // paste below "auth": "limits" moves from row 3 to 4

        editor.PasteSheetRowsBelow([["pasted", "p1"], ["pasted2", "p2"]]);
        Assert.Equal(["auth", "pasted", "pasted2", "limits", "retry"], editor.SheetRows.Select(r => r.Cells[0]).ToArray());
        editor.SaveCommand.Execute(null);

        Assert.Equal("p1", Cell(editor.WorkbookPath, "Notes", 3, 2));
        Assert.Equal("<From_Notes_text_5>", Cell(editor.WorkbookPath, "request", 2, 8));
        Assert.Equal("daily", Cell(editor.WorkbookPath, "Notes", 5, 2));
    }

    [Fact]
    public void Copied_rows_are_tab_separated_text()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        Assert.Equal("auth\thourly\r\nlimits\tdaily", editor.CopySheetRowsText([editor.SheetRows[0], editor.SheetRows[1]]));
    }

    [Fact]
    public void Fill_down_and_clear_work_on_the_clicked_column_and_undo_each()
    {
        using var dir = new TempDir();
        var editor = Open(dir);

        editor.FillSheetColumnDown(1);
        Assert.All(editor.SheetRows, r => Assert.Equal("hourly", r.Cells[1]));
        editor.UndoCommand.Execute(null);
        Assert.Equal(["hourly", "daily", "three times"], editor.SheetRows.Select(r => r.Cells[1]).ToArray());

        editor.ClearSheetColumn(1);
        Assert.All(editor.SheetRows, r => Assert.Equal("", r.Cells[1]));
        Assert.Contains("1 <From_…> reference reads this column", editor.StatusMessage);
        editor.UndoCommand.Execute(null);
        Assert.Equal("daily", editor.SheetRows[1].Cells[1]);
    }

    [Fact]
    public void The_column_menu_opens_the_add_columns_panel_with_side_and_count_chosen()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.SheetCurrentColumn = 1;

        editor.OpenInsertSheetColumns(left: true, 3);

        Assert.True(editor.SheetPanelVisible);
        Assert.Equal(DataEditorViewModel.PlaceColumnBefore, editor.SheetPanelPlacement);
        Assert.Equal(3m, editor.SheetPanelCount);
    }
}
