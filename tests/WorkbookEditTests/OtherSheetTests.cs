using ClosedXML.Excel;
using LoadTestingTool.Editing;
using Xunit;

namespace WorkbookEditTests;

public class OtherSheetTests
{
    private static string Build(TempDir dir, bool referenced = false) => Workbooks.CreateSmall(dir, (config, request, response) =>
    {
        var notes = request.Workbook.AddWorksheet("notes");
        notes.Cell(1, 1).Value = "key"; notes.Cell(1, 2).Value = "value";
        notes.Cell(2, 1).Value = "a"; notes.Cell(2, 2).Value = "1";
        notes.Cell(3, 1).Value = "b"; notes.Cell(3, 2).Value = "2";
        var readme = request.Workbook.AddWorksheet("README");
        readme.Cell(1, 1).Value = "Read me first";
        if (referenced) request.Cell(2, 6).Value = "<From_notes_value_2>";
    });

    private static IXLWorksheet Sheet(XLWorkbook w, string name) => w.Worksheet(name);

    [Fact]
    public void Other_sheets_load_as_editable_tables_and_untouched_ones_stay_byte_for_byte()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var before = Workbooks.Snapshot(path);
        var doc = WorkbookDocument.Load(path);
        Assert.Equal(["notes", "README"], doc.OtherSheets);
        Assert.False(doc.IsDirty);
        Assert.Equal(["key", "value"], doc.FindOtherSheet("notes")!.Headers);
        Assert.Equal(2, doc.FindOtherSheet("notes")!.Rows.Count);
        Assert.Empty(Workbooks.Diff(before, Workbooks.Snapshot(path)));
    }

    [Fact]
    public void Editing_cells_rows_and_columns_of_another_sheet_is_saved_and_nothing_else_changes()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var before = Workbooks.Snapshot(path);
        var doc = WorkbookDocument.Load(path);
        var notes = doc.FindOtherSheet("notes")!;
        notes.Rows[0].SetAt(1, "changed");
        notes.AddRow().SetAt(0, "c");
        notes.InsertColumnBefore(1, "mid");
        Assert.True(doc.IsDirty);
        doc.Save();

        using var workbook = new XLWorkbook(path);
        var sheet = Sheet(workbook, "notes");
        Assert.Equal("key", sheet.Cell(1, 1).GetString());
        Assert.Equal("mid", sheet.Cell(1, 2).GetString());
        Assert.Equal("value", sheet.Cell(1, 3).GetString());
        Assert.Equal("changed", sheet.Cell(2, 3).GetString());
        Assert.Equal("c", sheet.Cell(4, 1).GetString());
        var changed = Workbooks.Diff(before, Workbooks.Snapshot(path));
        Assert.All(changed, c => Assert.StartsWith("notes!", c));
    }

    [Fact]
    public void A_sheet_can_be_added_with_columns_and_rows_and_removed_again()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        var table = doc.AddSheet("Env data");
        table.InsertColumnBefore(0, "name");
        table.InsertColumnBefore(1, "url");
        var row = table.AddRow(); row.SetAt(0, "qa"); row.SetAt(1, "http://qa");
        doc.RemoveSheet("README");
        doc.Save();

        using var workbook = new XLWorkbook(path);
        Assert.False(workbook.TryGetWorksheet("README", out _));
        var sheet = Sheet(workbook, "Env data");
        Assert.Equal("name", sheet.Cell(1, 1).GetString());
        Assert.Equal("url", sheet.Cell(1, 2).GetString());
        Assert.Equal("http://qa", sheet.Cell(2, 2).GetString());
        Assert.Equal(["notes", "Env data"], WorkbookDocument.Load(path).OtherSheets);
    }

    [Fact]
    public void Rename_keeps_the_content_and_swapping_two_names_works()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        doc.RenameSheet("notes", "tmp");
        doc.RenameSheet("README", "notes");
        doc.RenameSheet("tmp", "README");
        doc.Save();

        using var workbook = new XLWorkbook(path);
        Assert.Equal("Read me first", Sheet(workbook, "notes").Cell(1, 1).GetString());
        Assert.Equal("a", Sheet(workbook, "README").Cell(2, 1).GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("a:b")]
    [InlineData("config")]
    [InlineData("Request")]
    [InlineData("NOTES")]
    [InlineData("an extremely long sheet name that is over thirty one")]
    public void Bad_or_taken_names_are_refused(string name)
    {
        using var dir = new TempDir();
        var doc = WorkbookDocument.Load(Build(dir));
        Assert.Throws<InvalidOperationException>(() => doc.AddSheet(name));
    }

    [Fact]
    public void A_sheet_that_other_cells_refer_to_cannot_be_renamed_or_removed()
    {
        using var dir = new TempDir();
        var doc = WorkbookDocument.Load(Build(dir, referenced: true));
        Assert.Contains("refer", Assert.Throws<InvalidOperationException>(() => doc.RemoveSheet("notes")).Message);
        Assert.Throws<InvalidOperationException>(() => doc.RenameSheet("notes", "other"));
        Assert.Throws<InvalidOperationException>(() => doc.RemoveSheet("config"));
        doc.RemoveSheet("README");   // nothing refers to it
    }

    [Fact]
    public void Undo_restores_removed_renamed_and_added_sheets()
    {
        using var dir = new TempDir();
        var doc = WorkbookDocument.Load(Build(dir));
        var start = doc.Snapshot();
        doc.AddSheet("extra");
        doc.RenameSheet("notes", "renamed");
        doc.RemoveSheet("README");
        doc.FindOtherSheet("renamed")!.Rows[0].SetAt(0, "zzz");
        Assert.Equal(["renamed", "extra"], doc.OtherSheets);

        doc.Restore(start);
        Assert.Equal(["notes", "README"], doc.OtherSheets);
        Assert.Equal("a", doc.FindOtherSheet("notes")!.Rows[0].GetAt(0));
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Renaming_a_column_updates_references_to_it_on_save()
    {
        using var dir = new TempDir();
        var path = Build(dir, referenced: true);
        var doc = WorkbookDocument.Load(path);
        doc.FindOtherSheet("notes")!.RenameColumn(1, "amount");
        doc.Save();
        using var workbook = new XLWorkbook(path);
        Assert.Equal("<From_notes_amount_2>", Sheet(workbook, "request").Cell(2, 6).GetString());
    }
}
