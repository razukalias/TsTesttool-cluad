using ClosedXML.Excel;
using LoadTestingTool.Editing;
using Xunit;

namespace WorkbookEditTests;

public class CellFormatTests
{
    private static string Build(TempDir dir) => Workbooks.CreateSmall(dir, (config, request, response) =>
    {
        request.Cell("E4").Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEB9C"); // D3's title already highlighted in the file
    });

    private static IXLCell Cell(string path, string sheet, string address, out XLWorkbook workbook)
    {
        workbook = new XLWorkbook(path);
        return workbook.Worksheet(sheet).Cell(address);
    }

    [Fact]
    public void Existing_fills_are_read_and_highlight_and_wrap_are_saved_and_cleared_again()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        var title = doc.Request.FindColumn("title");
        Assert.Equal("#FFEB9C", doc.Request.GetFormat(doc.Request.Rows[2], title).Fill);
        Assert.False(doc.IsDirty);

        doc.Request.SetFormat(doc.Request.Rows[0], title, new CellFormat("#C6EFCE", true));
        Assert.True(doc.IsDirty);
        doc.Save();

        var cell = Cell(path, "request", "E2", out var workbook);
        using (workbook)
        {
            Assert.Equal("FFC6EFCE", cell.Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8"));
            Assert.True(cell.Style.Alignment.WrapText);
        }
        var reloaded = WorkbookDocument.Load(path);
        Assert.Equal(new CellFormat("#C6EFCE", true), reloaded.Request.GetFormat(reloaded.Request.Rows[0], title));
        Assert.Equal("#FFEB9C", reloaded.Request.GetFormat(reloaded.Request.Rows[2], title).Fill); // untouched cell keeps its fill

        reloaded.Request.SetFormat(reloaded.Request.Rows[0], title, CellFormat.None);
        reloaded.Save();
        var cleared = Cell(path, "request", "E2", out var wb2);
        using (wb2)
        {
            Assert.Equal(XLFillPatternValues.None, cleared.Style.Fill.PatternType);
            Assert.False(cleared.Style.Alignment.WrapText);
        }
    }

    [Fact]
    public void Formats_follow_their_cells_when_a_row_is_inserted_and_the_new_row_starts_plain()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        var title = doc.Request.FindColumn("title");
        doc.Request.SetFormat(doc.Request.Rows[1], title, new CellFormat("#C6EFCE", false)); // D2, sheet row 3

        doc.InsertDataRowBefore(1, "D1b", "D2");
        doc.Save();

        using var workbook = new XLWorkbook(path);
        var request = workbook.Worksheet("request");
        Assert.Equal("D1b", request.Cell("C3").GetString());
        Assert.Equal(XLFillPatternValues.None, request.Cell("E3").Style.Fill.PatternType);   // the inserted row is plain
        Assert.Equal("D2", request.Cell("C4").GetString());
        Assert.Equal("FFC6EFCE", request.Cell("E4").Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8")); // D2's highlight moved down with it
        Assert.Equal("FFFFEB9C", request.Cell("E5").Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8")); // so did D3's original fill
    }

    [Fact]
    public void Formats_follow_their_columns_and_restore_with_undo_snapshots()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        var title = doc.Request.FindColumn("title");
        var snapshot = doc.Snapshot();

        doc.Request.SetFormat(doc.Request.Rows[0], title, new CellFormat("#C6EFCE", false));
        doc.Restore(snapshot);
        Assert.Equal(CellFormat.None, doc.Request.GetFormat(doc.Request.Rows[0], title));
        Assert.False(doc.IsDirty);

        doc.Request.SetFormat(doc.Request.Rows[0], title, new CellFormat("#C6EFCE", false));
        doc.Request.InsertColumnBefore(title, "Step.mid");
        doc.Save();

        using var workbook = new XLWorkbook(path);
        var request = workbook.Worksheet("request");
        Assert.Equal("title", request.Cell("F1").GetString());
        Assert.Equal("FFC6EFCE", request.Cell("F2").Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8"));
    }
}
