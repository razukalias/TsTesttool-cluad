using ClosedXML.Excel;
using LoadTestingTool.Editing;
using LoadTestingTool.Excel;
using Xunit;

namespace WorkbookEditTests;

public class ColumnEditTests
{
    private static string Build(TempDir dir) => Workbooks.CreateSmall(dir, (config, request, response) =>
    {
        var notes = request.Workbook.AddWorksheet("notes");
        notes.Cell(1, 1).FormulaA1 = "request!E3";                       // D2's title by Excel reference
        notes.Cell(2, 1).Value = "x <From_request_title_4> y";           // D3's title by <From_…_N>
        request.Cell(1, 6).Value = "other";
        request.Cell(2, 6).Value = "o1";
    });

    private static string Text(string path, string sheet, string address)
    {
        using var workbook = new XLWorkbook(path);
        return workbook.Worksheet(sheet).Cell(address).GetString();
    }

    [Fact]
    public void An_inserted_column_lands_in_the_middle_and_everything_to_its_right_moves_with_its_data()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        var title = doc.Request.FindColumn("title");

        doc.Request.InsertColumnBefore(title, "Step.mid");
        doc.Request.Rows[1].Set("Step.mid", "mid 2");
        var result = doc.Save();

        Assert.True(result.RequiresReload);
        Assert.Equal("Step.mid", Text(path, "request", "E1"));
        Assert.Equal("title", Text(path, "request", "F1"));
        Assert.Equal("other", Text(path, "request", "G1"));
        Assert.Equal("mid 2", Text(path, "request", "E3"));
        Assert.Equal("title 2", Text(path, "request", "F3"));
        Assert.Equal("o1", Text(path, "request", "G2"));
        using var workbook = new XLWorkbook(path);
        Assert.Equal("request!F3", workbook.Worksheet("notes").Cell("A1").FormulaA1);        // Excel reference followed the column
        Assert.Equal("x <From_request_title_4> y", workbook.Worksheet("notes").Cell("A2").GetString()); // header-based reference untouched
        var model = new WorkbookReader().Read(path);
        Assert.Equal("title 2", model.RequestData[(1, "D2")].Variables["title"]);
        Assert.Equal("mid 2", model.RequestData[(1, "D2")].Variables["Step.mid"]);
    }

    [Fact]
    public void Renaming_a_column_renames_the_references_to_it_and_still_resolves_the_same_value()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);

        doc.Request.RenameColumn(doc.Request.FindColumn("title"), "heading");
        var result = doc.Save();

        Assert.True(result.RequiresReload);
        Assert.Equal("heading", Text(path, "request", "E1"));
        Assert.Equal("http://host/<From_request_heading_4>", Text(path, "config", "F2"));
        Assert.Equal("x <From_request_heading_4> y", Text(path, "notes", "A2"));
        Assert.Equal("http://host/title 3", new WorkbookReader().Read(path).Testcases[0].Steps[0].TargetUrl);
    }

    [Fact]
    public void Removing_a_column_empties_it_without_moving_the_columns_after_it()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        Assert.Equal(2, doc.FindColumnReferences("request", "title").Count); // config URL and notes cell point at it
        Assert.Empty(doc.FindColumnReferences("request", "other"));

        doc.Request.RemoveColumn(doc.Request.FindColumn("other"));
        doc.Save();

        Assert.Equal("", Text(path, "request", "F1"));
        Assert.Equal("", Text(path, "request", "F2"));
        Assert.Equal("title", Text(path, "request", "E1"));
        Assert.Equal("title 1", Text(path, "request", "E2"));
        Assert.DoesNotContain("other", WorkbookDocument.Load(path).Request.Headers);
    }

    [Fact]
    public void Undo_snapshots_cover_column_inserts_removals_and_renames()
    {
        using var dir = new TempDir();
        var doc = WorkbookDocument.Load(Build(dir));
        var before = doc.Snapshot();

        doc.Request.InsertColumnBefore(0, "first");
        doc.Request.RemoveColumn(doc.Request.FindColumn("other"));
        doc.Request.RenameColumn(doc.Request.FindColumn("title"), "heading");
        Assert.True(doc.IsDirty);
        doc.Restore(before);

        Assert.Equal(["testcaseindex", "testcase", "dataid", "dontrun", "title", "other"], doc.Request.Headers);
        Assert.Equal("o1", doc.Request.Rows[0].Get("other"));
        Assert.False(doc.IsDirty);
    }
}
