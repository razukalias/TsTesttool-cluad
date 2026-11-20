using LoadTestingTool.Domain;
using LoadTestingTool.Editing;
using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ResponseHeaderTests
{
    private static AssertionDefinition Def(string path, string expected, string verb = "eq") => new()
    {
        TestcaseIndex = 1, Testcase = "t", DataId = "d", StepName = "s", ResponsePath = path, ExpectedValue = expected, AssertionVerb = verb, ExcelRowNumber = 2, ExpectedValueColumn = 5,
    };

    private static readonly Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json; charset=utf-8", ["X-Trace"] = "abc" };

    [Fact]
    public void A_response_header_can_be_asserted_and_is_not_an_unexpected_payload_field()
    {
        var results = new AssertionEngine().Evaluate("""{"name":"Ann"}""", "json",
            [Def("name", "Ann"), Def("responseHeader.Content-Type", "application/json; charset=utf-8")], "run", "req", 200, false, false, Headers);
        Assert.All(results, r => Assert.Equal("PASS", r.Result));
        Assert.Equal(2, results.Count);   // X-Trace has no expectation and is not reported as unexpected
    }

    [Fact]
    public void A_wrong_header_value_and_a_missing_header_fail()
    {
        var results = new AssertionEngine().Evaluate("""{"name":"Ann"}""", "json",
            [Def("name", "Ann"), Def("responseHeader.content-type", "text/html"), Def("responseHeader.Location", "/x")], "run", "req", 200, false, false, Headers);
        Assert.Equal(2, results.Count(r => r.Result == "FAIL"));
    }

    [Fact]
    public void Stored_headers_are_split_per_line_and_old_files_with_semicolons_keep_the_value_together()
    {
        var modern = ResponseSamples.ParseHeaders("Content-Type=text/html; charset=utf-8\nX-Trace=abc");
        Assert.Equal([("Content-Type", "text/html; charset=utf-8"), ("X-Trace", "abc")], modern.Select(h => (h.Name, h.Value)));

        var legacy = ResponseSamples.ParseHeaders("Content-Type=text/html; charset=utf-8; X-Trace=abc");
        Assert.Equal([("Content-Type", "text/html; charset=utf-8"), ("X-Trace", "abc")], legacy.Select(h => (h.Name, h.Value)));
    }

    private static DataEditorViewModel Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 2);
        Directory.CreateDirectory(dir.Combine("History"));
        using var book = new ClosedXML.Excel.XLWorkbook();
        var sheet = book.AddWorksheet("RequestHistory");
        string[] columns = ["runid", "dataid", "stepname", "responseheaders", "responsebody"];
        for (var i = 0; i < columns.Length; i++) sheet.Cell(1, i + 1).Value = columns[i];
        sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer";
        sheet.Cell(2, 4).Value = "Content-Type=application/json; charset=utf-8\nDate=Fri, 20 Nov 2026 10:00:00 GMT\nX-Trace=abc"; sheet.Cell(2, 5).Value = "{}";
        book.SaveAs(System.IO.Path.Combine(dir.Combine("History"), "run1.xlsx"));
        var editor = new DataEditorViewModel(path, "Customers");
        editor.OpenRecordTabCommand.Execute(null);
        return editor;
    }

    [Fact]
    public void Headers_of_the_last_run_are_listed_with_changing_ones_unticked_and_become_expected_columns()
    {
        using var dir = new TempDir();
        var editor = Open(dir);

        editor.FindResponseHeaders("CreateCustomer");
        Assert.Equal(["Content-Type", "Date", "X-Trace"], editor.ResponseHeaderChoices.Select(h => h.Name));
        Assert.False(editor.ResponseHeaderChoices.Single(h => h.Name == "Date").IsChosen);

        Assert.Equal(2, editor.AddResponseHeaders(fillSelectedRow: true));
        var line = editor.ExpectedLines.Single(l => l.Title == "CreateCustomer.responseHeader.Content-Type");
        Assert.Equal("eq", line.Verb);
        Assert.Equal("application/json; charset=utf-8", ValueParts.Format(line.Parts));
        Assert.DoesNotContain(editor.ExpectedLines, l => l.Title.EndsWith("responseHeader.Date"));
        editor.FindResponseHeaders("CreateCustomer");
        Assert.Equal(["Date"], editor.ResponseHeaderChoices.Select(h => h.Name));
    }

    [Fact]
    public void Without_a_stored_run_the_header_list_says_what_to_do()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2), "Customers");
        editor.FindResponseHeaders("CreateCustomer");
        Assert.Empty(editor.ResponseHeaderChoices);
        Assert.Contains("No stored response headers", editor.ResponseHeadersNote);
    }

    [Fact]
    public void Template_columns_can_be_limited_to_one_step()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        Directory.CreateDirectory(dir.Combine("Templates"));
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Templates"), "create.json"), "{\"extra\":\"_newField_\"}");
        var editor = new DataEditorViewModel(path, "Customers");
        editor.FindTemplateColumns("NoSuchStep");
        Assert.Empty(editor.TemplateColumns);
        editor.FindTemplateColumns("CreateCustomer");
        Assert.Equal(["CreateCustomer.newField"], editor.TemplateColumns.Select(c => c.Header));
    }

    [Fact]
    public void A_sample_file_of_header_lines_stands_in_for_a_stored_run_and_status_lines_are_ignored()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2), "Customers");
        var sample = dir.Combine("headers.txt");
        File.WriteAllText(sample, "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nX-Trace: abc\r\n\r\n");
        editor.FindResponseHeaders("CreateCustomer", sample);
        Assert.Equal([("Content-Type", "application/json"), ("X-Trace", "abc")], editor.ResponseHeaderChoices.Select(h => (h.Name, h.Value)));
        Assert.Contains("headers.txt", editor.ResponseHeadersNote);
    }

    [Fact]
    public void Adding_from_all_three_tabs_is_one_undo_step()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        Directory.CreateDirectory(dir.Combine("Templates"));
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Templates"), "create.json"), "{\"x\":\"_newField_\"}");
        editor.FindTemplateColumns();
        editor.FindResponseHeaders("CreateCustomer");
        Assert.NotEmpty(editor.TemplateColumns);

        var added = editor.AddTickedColumns(fillResponseValues: true, fillHeaderValues: true);

        Assert.True(added >= 3);
        Assert.Contains(editor.RequestLines, l => l.Title == "CreateCustomer.newField");
        Assert.Contains(editor.ExpectedLines, l => l.Title == "CreateCustomer.responseHeader.X-Trace");
        editor.UndoCommand.Execute(null);
        Assert.DoesNotContain(editor.RequestLines, l => l.Title == "CreateCustomer.newField");
        Assert.DoesNotContain(editor.ExpectedLines, l => l.Title.Contains("responseHeader"));
        Assert.False(editor.CanUndo);   // exactly one step
    }
}
