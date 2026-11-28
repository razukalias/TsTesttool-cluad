using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

// The File step's Read for Excel, CSV, Word and PDF files and its "Return as" choices.
public class FileContentTests
{
    private static FileContent.Options Opt(string returnAs = "", string sheet = "", string range = "", bool header = true, int max = 10000) => new(returnAs, sheet, header, range, "", max);

    private static string Excel(TempDir dir)
    {
        Directory.CreateDirectory(dir.Path);
        var path = Path.Combine(dir.Path, "people.xlsx");
        using var book = new ClosedXML.Excel.XLWorkbook();
        book.AddWorksheet("Other").Cell(1, 1).Value = "x";
        var sheet = book.AddWorksheet("People");
        sheet.Cell(1, 1).Value = "name"; sheet.Cell(1, 2).Value = "age";
        sheet.Cell(2, 1).Value = "Ann"; sheet.Cell(2, 2).Value = 31; sheet.Cell(3, 1).Value = "Bo, Jr"; sheet.Cell(3, 2).Value = 40; sheet.Cell(4, 1).Value = "Cy"; sheet.Cell(4, 2).Value = 22;
        book.SaveAs(path);
        return path;
    }

    private static string Word(TempDir dir)
    {
        Directory.CreateDirectory(dir.Path);
        var path = Path.Combine(dir.Path, "report.docx");
        using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            Paragraph P(string text, string? style = null) { var p = new Paragraph(new Run(new Text(text))); if (style is not null) p.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = style }); return p; }
            body.Append(P("Summary", "Heading1"), P("Hello world."));
            var table = new Table();
            foreach (var row in new[] { new[] { "id", "city" }, new[] { "1", "Oslo" }, new[] { "2", "Rome" } })
            {
                var r = new TableRow();
                foreach (var cell in row) r.Append(new TableCell(P(cell)));
                table.Append(r);
            }
            body.Append(table, P("The end"));
            main.Document = new Document(body);
        }
        return path;
    }

    [Fact]
    public void Excel_returns_rows_as_json_by_default_with_sheet_range_and_header_choices()
    {
        var path = Excel(new TempDir());
        var (value, text) = FileContent.Read(path, Opt(sheet: "People"));
        var rows = (List<Dictionary<string, object?>>)value;
        Assert.Equal(3, rows.Count); Assert.Equal("Ann", rows[0]["name"]); Assert.Equal("31", rows[0]["age"]);
        Assert.Equal("Ann", JsonDocument.Parse(text).RootElement.GetProperty("rows")[0].GetProperty("name").GetString());
        var ranged = (List<Dictionary<string, object?>>)FileContent.Read(path, Opt(sheet: "People", range: "A1:B3")).Value;
        Assert.Equal(2, ranged.Count);
        var noHeader = (List<Dictionary<string, object?>>)FileContent.Read(path, Opt(sheet: "People", header: false)).Value;
        Assert.Equal(4, noHeader.Count); Assert.Equal("name", noHeader[0]["col1"]);
    }

    [Fact]
    public void Excel_as_csv_quotes_values_and_a_too_big_sheet_is_refused_in_plain_words()
    {
        var path = Excel(new TempDir());
        var csv = (string)FileContent.Read(path, Opt("csv", "People")).Value;
        Assert.Equal("name,age\nAnn,31\n\"Bo, Jr\",40\nCy,22\n", csv.Replace("\r\n", "\n"));
        var tooBig = Assert.Throws<InvalidDataException>(() => FileContent.Read(path, Opt(sheet: "People", max: 2)));
        Assert.Contains("more than the limit of 2", tooBig.Message);
        Assert.Contains("There is no sheet 'Nope'", Assert.Throws<InvalidDataException>(() => FileContent.Read(path, Opt(sheet: "Nope"))).Message);
        Assert.Contains("does not fit excel", Assert.Throws<InvalidDataException>(() => FileContent.Read(path, Opt("paragraphs"))).Message);
    }

    [Fact]
    public void Csv_stays_plain_text_unless_a_return_as_is_chosen()
    {
        var dir = new TempDir(); Directory.CreateDirectory(dir.Path);
        var path = Path.Combine(dir.Path, "a.csv"); File.WriteAllText(path, "id;name\n1;x\n2;y\n");
        Assert.False(FileContent.Handles(path, ""));
        Assert.True(FileContent.Handles(path, "rows"));
        var rows = (List<Dictionary<string, object?>>)FileContent.Read(path, Opt("rows")).Value;
        Assert.Equal("y", rows[1]["name"]);
    }

    [Fact]
    public void Word_returns_text_paragraphs_tables_or_markdown()
    {
        var path = Word(new TempDir());
        Assert.Equal("Summary\nHello world.\nid\ncity\n1\nOslo\n2\nRome\nThe end", ((string)FileContent.Read(path, Opt()).Value).Replace("\r\n", "\n"));
        var paragraphs = (List<Dictionary<string, object?>>)FileContent.Read(path, Opt("paragraphs")).Value;
        Assert.Equal(3, paragraphs.Count); Assert.Equal("Heading1", paragraphs[0]["style"]); Assert.Equal("Hello world.", paragraphs[1]["text"]);
        var tables = (List<List<Dictionary<string, object?>>>)FileContent.Read(path, Opt("tables")).Value;
        Assert.Single(tables); Assert.Equal("Rome", tables[0][1]["city"]);
        var markdown = (string)FileContent.Read(path, Opt("markdown")).Value;
        Assert.Contains("# Summary", markdown); Assert.Contains("| id | city |", markdown); Assert.Contains("| 1 | Oslo |", markdown);
    }

    [Fact]
    public void Pdf_returns_text_or_pages_and_a_broken_file_is_said_in_plain_words()
    {
        var dir = new TempDir(); Directory.CreateDirectory(dir.Path);
        var path = Path.Combine(dir.Path, "a.pdf");
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("First page", 12, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Second page", 12, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
        File.WriteAllBytes(path, builder.Build());
        Assert.Contains("First page", (string)FileContent.Read(path, Opt()).Value);
        var pages = (List<Dictionary<string, object?>>)FileContent.Read(path, Opt("pages")).Value;
        Assert.Equal(2, pages.Count); Assert.Equal("Second page", pages[1]["text"]);
        Assert.Equal(2, FileMetadata.Read(path)["pages"]);
        var bad = Path.Combine(dir.Path, "bad.pdf"); File.WriteAllText(bad, "not a pdf");
        Assert.Contains("could not be read as pdf", Assert.Throws<InvalidDataException>(() => FileContent.Read(bad, Opt())).Message);
    }

    private const string RowsJson = "{\"rows\":[{\"id\":1,\"name\":\"Ann\"},{\"id\":2,\"name\":\"Bo, Jr\"}]}";

    [Fact]
    public void Written_excel_csv_word_and_pdf_read_back_the_same()
    {
        var dir = new TempDir(); Directory.CreateDirectory(dir.Path);
        string P(string n) => Path.Combine(dir.Path, n);

        Assert.True(FileWriter.Handles(P("a.xlsx"), RowsJson));
        Assert.Contains("2 rows", FileWriter.Write(P("a.xlsx"), RowsJson, "People"));
        var rows = (List<Dictionary<string, object?>>)FileContent.Read(P("a.xlsx"), Opt(sheet: "People")).Value;
        Assert.Equal("Bo, Jr", rows[1]["name"]); Assert.Equal("2", rows[1]["id"]);
        Assert.Equal(2, ((List<string>)FileMetadata.Read(P("a.xlsx"))["sheets"]!).Count + 1);

        FileWriter.Write(P("fromcsv.xlsx"), "x;y\n1;2\n", "");
        Assert.Equal("2", ((List<Dictionary<string, object?>>)FileContent.Read(P("fromcsv.xlsx"), Opt()).Value)[0]["y"]);

        Assert.True(FileWriter.Handles(P("b.csv"), RowsJson)); Assert.False(FileWriter.Handles(P("b.csv"), "plain,text"));
        FileWriter.Write(P("b.csv"), RowsJson, "");
        Assert.Equal("id,name\n1,Ann\n2,\"Bo, Jr\"\n", File.ReadAllText(P("b.csv")).Replace("\r\n", "\n"));

        FileWriter.Write(P("c.docx"), "# Title\nSome text\n- one", null!);
        var paragraphs = (List<Dictionary<string, object?>>)FileContent.Read(P("c.docx"), Opt("paragraphs")).Value;
        Assert.Equal("Heading1", paragraphs[0]["style"]); Assert.Equal("• one", paragraphs[2]["text"]);
        FileWriter.Write(P("d.docx"), RowsJson, "");
        var tables = (List<List<Dictionary<string, object?>>>)FileContent.Read(P("d.docx"), Opt("tables")).Value;
        Assert.Equal("Ann", tables[0][0]["name"]);

        FileWriter.Write(P("e.pdf"), string.Join("\n", Enumerable.Range(1, 120).Select(i => "Line " + i)), "");
        var pages = (List<Dictionary<string, object?>>)FileContent.Read(P("e.pdf"), Opt("pages")).Value;
        Assert.True(pages.Count >= 2); Assert.Contains("Line 1", (string)pages[0]["text"]!); Assert.Contains("Line 120", (string)pages[^1]["text"]!);
        FileWriter.Write(P("f.pdf"), RowsJson, "");
        Assert.Contains("Bo, Jr", (string)FileContent.Read(P("f.pdf"), Opt()).Value);
    }

    [Fact]
    public async Task The_file_step_write_builds_the_file_inside_the_workspace_and_plain_text_files_are_untouched()
    {
        var dir = new TempDir(); Directory.CreateDirectory(dir.Path);
        async Task<LoadTestingTool.Domain.StepResult> Run(string path, string content)
        {
            var config = JsonSerializer.Serialize(new { Action = "Write", Path = path, Content = content, Sheet = "S" });
            var step = new LoadTestingTool.Domain.RequestStep { StepName = "W", StepType = LoadTestingTool.Domain.StepType.File, StepConfig = config };
            return await new FileStepExecutor().ExecuteAsync(new LoadTestingTool.Domain.StepExecutionRequest { Step = step, Context = new LoadTestingTool.Domain.ExecutionContext(), WorkspaceRoot = dir.Path, CancellationToken = CancellationToken.None });
        }
        var excel = await Run("out/r.xlsx", RowsJson);
        Assert.True(excel.Succeeded, excel.ErrorMessage); Assert.Contains("xlsx written", excel.ResponseText);
        Assert.Equal("Ann", ((List<Dictionary<string, object?>>)FileContent.Read(Path.Combine(dir.Path, "out", "r.xlsx"), Opt(sheet: "S")).Value)[0]["name"]);
        var text = await Run("notes.txt", "hello");
        Assert.True(text.Succeeded); Assert.Equal("hello", File.ReadAllText(Path.Combine(dir.Path, "notes.txt")));
        var json = await Run("d.json", RowsJson);
        Assert.Equal(RowsJson, File.ReadAllText(Path.Combine(dir.Path, "d.json")));
        var outside = await Run("../escape.xlsx", RowsJson);
        Assert.False(outside.Succeeded);
    }
}
