using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class SheetEditingTests
{
    private static DataEditorViewModel Open(TempDir dir, bool referenced = false)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var book = new XLWorkbook(path))
        {
            var notes = book.AddWorksheet("Notes");
            notes.Cell(1, 1).Value = "topic"; notes.Cell(1, 2).Value = "text";
            notes.Cell(2, 1).Value = "auth"; notes.Cell(2, 2).Value = "token expires hourly";
            if (referenced) book.Worksheet("request").Cell(2, 8).Value = "<From_Notes_text_2>";
            book.Save();
        }
        return new DataEditorViewModel(path, "Customers");
    }

    private static string Cell(string path, string sheet, int row, int column)
    {
        using var book = new XLWorkbook(path);
        return book.Worksheet(sheet).Cell(row, column).GetString();
    }

    [Fact]
    public void Editing_a_cell_of_another_sheet_marks_the_workbook_changed_and_saves_it()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.ActiveSheet = "Notes";

        editor.SheetRows[0].Cells[1] = "token expires daily";

        Assert.True(editor.IsDirty);
        editor.SaveCommand.Execute(null);
        Assert.False(editor.IsDirty);
        Assert.Equal("token expires daily", Cell(editor.WorkbookPath, "Notes", 2, 2));
    }

    [Fact]
    public void A_new_sheet_gets_columns_and_rows_through_the_panel_and_is_written_on_save()
    {
        using var dir = new TempDir();
        var editor = Open(dir);

        editor.OpenAddSheetCommand.Execute(null);
        Assert.True(editor.SheetPanelVisible);
        editor.SheetPanelName = "Env data";
        editor.ConfirmSheetActionCommand.Execute(null);
        Assert.Equal("Env data", editor.ActiveSheet);
        Assert.Contains("Env data", editor.OpenSheets);

        editor.OpenAddSheetColumnsCommand.Execute(null);
        editor.SheetPanelName = "name"; editor.ConfirmSheetActionCommand.Execute(null);
        editor.OpenAddSheetColumnsCommand.Execute(null);
        editor.SheetPanelName = "url"; editor.ConfirmSheetActionCommand.Execute(null);
        editor.OpenAddSheetRowsCommand.Execute(null);
        editor.SheetPanelCount = 2; editor.ConfirmSheetActionCommand.Execute(null);
        Assert.Equal(2, editor.SheetRows.Count);
        editor.SheetRows[1].Cells[0] = "qa"; editor.SheetRows[1].Cells[1] = "http://qa";

        editor.SaveCommand.Execute(null);
        Assert.Equal("name", Cell(editor.WorkbookPath, "Env data", 1, 1));
        Assert.Equal("http://qa", Cell(editor.WorkbookPath, "Env data", 2, 2));   // the empty first row is not written
    }

    [Fact]
    public void Several_columns_can_be_added_after_the_clicked_one_and_numbered()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.ActiveSheet = "Notes";
        editor.SheetCurrentColumn = 0;

        editor.OpenAddSheetColumnsCommand.Execute(null);
        Assert.Equal(DataEditorViewModel.PlaceColumnAfter, editor.SheetPanelPlacement);
        editor.SheetPanelName = "extra"; editor.SheetPanelCount = 3;
        editor.ConfirmSheetActionCommand.Execute(null);

        Assert.Equal(["topic", "extra1", "extra2", "extra3", "text"], editor.SheetHeaders);
        editor.SaveCommand.Execute(null);
        Assert.Equal("text", Cell(editor.WorkbookPath, "Notes", 1, 5));
        Assert.Equal("token expires hourly", Cell(editor.WorkbookPath, "Notes", 2, 5));
    }

    [Fact]
    public void Rename_and_remove_work_and_undo_brings_the_sheet_back()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.ActiveSheet = "Notes";

        editor.OpenRenameSheetCommand.Execute(null);
        editor.SheetPanelName = "Docs"; editor.ConfirmSheetActionCommand.Execute(null);
        Assert.Equal("Docs", editor.ActiveSheet);

        editor.OpenRemoveSheetCommand.Execute(null);
        editor.ConfirmSheetActionCommand.Execute(null);
        Assert.DoesNotContain("Docs", editor.OpenSheets);
        Assert.Equal(DataEditorViewModel.DataSheet, editor.ActiveSheet);

        editor.UndoCommand.Execute(null);
        Assert.Contains("Docs", editor.OpenSheets);
        editor.UndoCommand.Execute(null);
        Assert.Contains("Notes", editor.OpenSheets);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void A_sheet_that_a_reference_points_at_cannot_be_removed_and_the_reason_is_shown()
    {
        using var dir = new TempDir();
        var editor = Open(dir, referenced: true);
        editor.ActiveSheet = "Notes";

        editor.OpenRemoveSheetCommand.Execute(null);
        editor.ConfirmSheetActionCommand.Execute(null);

        Assert.True(editor.StatusIsError);
        Assert.Contains("refer", editor.StatusMessage);
        Assert.Contains("Notes", editor.OpenSheets);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void The_sheets_the_runner_reads_cannot_be_renamed_or_removed_here()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.ActiveSheet = "config";

        editor.OpenRemoveSheetCommand.Execute(null);

        Assert.False(editor.SheetPanelVisible);
        Assert.True(editor.StatusIsError);
    }

    [Fact]
    public void Removing_a_row_and_a_column_of_another_sheet_is_saved()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.ActiveSheet = "Notes";
        editor.SelectedSheetRow = editor.SheetRows[0];
        editor.RemoveSheetRowCommand.Execute(null);
        Assert.Empty(editor.SheetRows);

        editor.SheetCurrentColumn = 1;
        editor.OpenRemoveSheetColumnCommand.Execute(null);
        editor.ConfirmSheetActionCommand.Execute(null);
        Assert.Equal(["topic"], editor.SheetHeaders);

        editor.SaveCommand.Execute(null);
        Assert.Equal(string.Empty, Cell(editor.WorkbookPath, "Notes", 2, 1));
        Assert.Equal(string.Empty, Cell(editor.WorkbookPath, "Notes", 1, 2));
    }
}
