using System.Text;
using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ImportSourceTests
{
    [Theory]
    [InlineData("B3:F40", 3, 2, 40, 6)]
    [InlineData("$B$3:$F$40", 3, 2, 40, 6)]
    [InlineData("F40:B3", 3, 2, 40, 6)]       // corners in any order
    [InlineData("B:F", 0, 2, 0, 6)]
    [InlineData("3:40", 3, 0, 40, 0)]
    [InlineData("C5", 5, 3, 5, 3)]
    [InlineData("", 0, 0, 0, 0)]
    [InlineData("all", 0, 0, 0, 0)]
    public void Ranges_are_written_like_in_Excel(string text, int r1, int c1, int r2, int c2)
    {
        Assert.True(ImportRange.TryParse(text, out var range));
        Assert.Equal(new ImportRange(r1, c1, r2, c2), range);
    }

    [Theory]
    [InlineData("B3:F40:Z")]
    [InlineData("3B")]
    [InlineData("A0")]
    [InlineData("hello world")]
    public void A_range_that_is_not_one_is_refused(string text) => Assert.False(ImportRange.TryParse(text, out _));

    [Fact]
    public void Csv_delimiter_is_detected_and_can_be_forced()
    {
        Assert.Equal(';', ImportSource.FromCsvText("a.csv", "a;b;c\n1;2;3").Delimiter);
        Assert.Equal('\t', ImportSource.FromCsvText("a.csv", "a\tb\n1\t2").Delimiter);
        Assert.Equal('|', ImportSource.FromCsvText("a.csv", "a|b|c\n1|2|3").Delimiter);
        var forced = ImportSource.FromCsvText("a.csv", "a;b,c\n1;2,3", ',');
        Assert.Equal(["a;b", "c"], forced.Sheets[0].Cells[0]);
    }

    [Fact]
    public void Csv_encoding_is_detected_utf8_bom_utf16_and_falls_back_to_windows_1252()
    {
        Assert.Equal("Åsa", ImportSource.FromCsvBytes("a.csv", new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("n\nÅsa")).ToArray()).Sheets[0].Cells[1][0]);
        Assert.Equal("Åsa", ImportSource.FromCsvBytes("a.csv", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("n\nÅsa")).ToArray()).Sheets[0].Cells[1][0]);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var legacy = Encoding.GetEncoding(1252).GetBytes("n\nÅsa");     // not valid UTF-8
        var source = ImportSource.FromCsvBytes("a.csv", legacy);
        Assert.Equal("Åsa", source.Sheets[0].Cells[1][0]);
        Assert.Equal("Windows-1252", source.EncodingName);
    }

    private static string Workbook(TempDir dir)
    {
        var path = dir.Combine("source.xlsx");
        using var wb = new XLWorkbook();
        var one = wb.AddWorksheet("People");
        one.Cell(1, 1).Value = "note: exported"; 
        string[] header = ["Data ID", "name", "age", "joined", "active"];
        for (var i = 0; i < header.Length; i++) one.Cell(3, i + 1).Value = header[i];
        one.Cell(4, 1).Value = "P1"; one.Cell(4, 2).Value = "Ann"; one.Cell(4, 3).Value = 41; one.Cell(4, 4).Value = new DateTime(2024, 5, 6); one.Cell(4, 5).Value = true;
        one.Cell(5, 1).Value = "P2"; one.Cell(5, 2).Value = "Bo"; one.Cell(5, 3).Value = 2.5;
        one.Cell(7, 1).Value = "P3"; one.Cell(7, 2).Value = "Cy";
        var two = wb.AddWorksheet("Other");
        two.Cell(1, 1).Value = "x"; two.Cell(2, 1).Value = "y";
        wb.AddWorksheet("Empty");
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public void An_Excel_file_lists_its_sheets_and_reads_values_as_text()
    {
        using var dir = new TempDir();
        var source = ImportSource.Open(Workbook(dir));
        Assert.Equal(["People", "Other", "Empty"], source.Sheets.Select(s => s.Name).ToArray());
        var table = source.Slice(0, ImportRange.Whole, headerRow: 3);
        Assert.Equal(["Data ID", "name", "age", "joined", "active"], table.Headers);
        Assert.Equal(3, table.Rows.Count);                                   // the blank row 6 is not a row
        Assert.Equal(["P1", "Ann", "41", "2024-05-06", "TRUE"], table.Rows[0].Cells);
        Assert.Equal("2.5", table.Rows[1].Cells[2]);
        Assert.Equal([4, 5, 7], table.Rows.Select(r => r.Row).ToArray());   // row numbers of the file
    }

    [Fact]
    public void A_range_picks_a_block_and_the_header_row_may_be_none()
    {
        using var dir = new TempDir();
        var source = ImportSource.Open(Workbook(dir));
        var block = source.Slice(0, ImportRange.TryParse("B3:C5", out var r) ? r : default, headerRow: 3);
        Assert.Equal(["name", "age"], block.Headers);
        Assert.Equal([2, 3], block.ColumnNumbers);
        Assert.Equal(2, block.Rows.Count);
        var none = source.Slice(0, ImportRange.TryParse("A4:B5", out var r2) ? r2 : default, headerRow: 0);
        Assert.Equal(["Column A", "Column B"], none.Headers);
        Assert.Equal(2, none.Rows.Count);
        Assert.Equal(["P1", "Ann"], none.Rows[0].Cells);
    }

    [Fact]
    public void A_file_over_the_limit_is_cut_and_says_so()
    {
        var text = "a\n" + string.Join("\n", Enumerable.Range(1, ImportSource.MaxRows + 5));
        var source = ImportSource.FromCsvText("big.csv", text);
        Assert.Equal(ImportSource.MaxRows, source.Sheets[0].RowCount);
        Assert.Contains("only the first", source.Truncated);
    }
}
