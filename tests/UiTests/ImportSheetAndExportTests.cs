using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ImportSheetAndExportTests
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
            book.Worksheet("request").Cell(2, 8).Value = "<From_Notes_text_3>";
            book.Save();
        }
        return new DataEditorViewModel(path, "Customers") { ActiveSheet = "Notes" };
    }

    private static ImportPlan Plan(string csv, ImportMode mode, ImportMatch match = ImportMatch.ByHeader, bool createUnmatched = false, ImportPlacement placement = ImportPlacement.End)
    {
        var table = ImportSource.FromCsvText("t.csv", csv).Slice(0, ImportRange.Whole, 1);
        return new ImportPlan { Table = table, Mode = mode, Match = match, CreateUnmatched = createUnmatched, Placement = placement, TakeColumn = Enumerable.Repeat(true, table.ColumnCount).ToArray(), TakeRow = Enumerable.Repeat(true, table.Rows.Count).ToArray() };
    }

    private static string[][] Rows(DataEditorViewModel e) => e.SheetRows.Select(r => r.Cells.ToArray()).ToArray();

    [Fact]
    public void A_sheet_imports_rows_by_header_at_the_end_and_undo_reverts()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        Assert.True(editor.CanImport);
        var report = editor.ApplyImport(Plan("text,topic\nweekly,billing\nmonthly,tax", ImportMode.AppendRows));
        Assert.Equal(2, report.Added);
        Assert.Equal(["billing", "weekly"], Rows(editor)[2]);
        Assert.Equal(4, editor.SheetRows.Count);
        editor.UndoCommand.Execute(null);
        Assert.Equal(2, editor.SheetRows.Count);
    }

    [Fact]
    public void Inserting_rows_in_the_middle_of_a_sheet_says_how_many_references_are_renumbered()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.SelectedSheetRow = editor.SheetRows[0];
        var report = editor.ApplyImport(Plan("topic,text\nnew,row", ImportMode.InsertRowsBelow));
        Assert.Equal(["auth", "new", "limits"], Rows(editor).Select(r => r[0]).ToArray());
        Assert.Contains("renumbers 1", report.Describe());
    }

    [Fact]
    public void A_sheet_can_replace_its_rows_fill_by_position_and_get_new_columns()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var fill = editor.ApplyImport(Plan("text\nA\nB\nC", ImportMode.FillExistingRows));
        Assert.Equal(2, fill.Updated);
        Assert.Equal(1, fill.NotFound);
        Assert.Equal(["A", "B"], Rows(editor).Select(r => r[1]).ToArray());

        var cols = editor.ApplyImport(Plan("level,owner\n1,ann\n2,bo", ImportMode.NewColumns));
        Assert.Equal(["level", "owner"], cols.ColumnsCreated.ToArray());
        Assert.Equal(["topic", "text", "level", "owner"], editor.SheetHeaders.ToArray());
        Assert.Equal(["auth", "A", "1", "ann"], Rows(editor)[0]);

        var replaced = editor.ApplyImport(Plan("topic,text\nonly,one", ImportMode.ReplaceRows));
        Assert.Equal(2, replaced.Removed);
        Assert.Equal([["only", "one", "", ""]], Rows(editor));
    }

    [Fact]
    public void A_header_the_sheet_does_not_have_is_skipped_or_created_on_request()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        Assert.NotNull(editor.ApplyImport(Plan("zzz\n1", ImportMode.AppendRows)).Error);
        var made = editor.ApplyImport(Plan("topic,extra\nq,w", ImportMode.AppendRows, createUnmatched: true));
        Assert.Equal(["extra"], made.ColumnsCreated.ToArray());
        Assert.Equal("w", Rows(editor)[2][2]);
    }

    [Fact]
    public void Export_writes_the_headers_the_import_understands_and_a_round_trip_changes_nothing()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3), "Customers");
        var table = editor.ExportTable();
        Assert.Equal("Data ID", table[0][0]);
        Assert.Contains(table[0], h => h.EndsWith("statusCode [expected]"));
        Assert.Equal(4, table.Count);
        var csv = editor.BuildExportCsv();
        var back = ImportSource.FromCsvText("x.csv", csv).Slice(0, ImportRange.Whole, 1);
        var report = editor.ApplyImport(new ImportPlan { Table = back, Mode = ImportMode.FillExistingRows, TakeColumn = Enumerable.Repeat(true, back.ColumnCount).ToArray(), TakeRow = Enumerable.Repeat(true, back.Rows.Count).ToArray() });
        Assert.False(report.Changed);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Excel_export_keeps_numbers_numbers_and_leading_zeros_text_and_imports_back()
    {
        using var dir = new TempDir();
        var path = dir.Combine("out.xlsx");
        DataEditorViewModel.WriteXlsx(path, "Customers: all/some", [["Data ID", "n", "zip"], ["A", "42", "00123"], ["B", "2.5", ""]]);
        using (var wb = new XLWorkbook(path))
        {
            var sheet = wb.Worksheets.First();
            Assert.Equal("Customers_ all_some", sheet.Name);
            Assert.True(sheet.Cell(2, 2).DataType == XLDataType.Number);
            Assert.True(sheet.Cell(2, 3).DataType == XLDataType.Text);
        }
        var source = ImportSource.Open(path);
        Assert.Equal(["A", "42", "00123"], source.Sheets[0].Cells[1]);
        Assert.Equal("2.5", source.Sheets[0].Cells[2][1]);
    }

    [Fact]
    public void Export_of_selected_cells_takes_only_their_rows_and_columns_with_the_Data_ID()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 4), "Customers");
        var email = editor.Columns.First(c => c.Title == "CreateCustomer.email").CellIndex;
        editor.CellSelection.Select(editor.VisibleRows[1], email);
        editor.CellSelection.Toggle(editor.VisibleRows[3], email);
        var table = editor.ExportTable(selectionOnly: true);
        Assert.Equal(["Data ID", "CreateCustomer.email"], table[0]);
        Assert.Equal(["CUST002", "customer2@example.test"], table[1]);
        Assert.Equal(["CUST004", "customer4@example.test"], table[2]);
        Assert.Equal(3, table.Count);
    }
}
