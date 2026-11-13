using ClosedXML.Excel;
using LoadTestingTool.Editing;
using LoadTestingTool.Excel;
using Xunit;

namespace WorkbookEditTests;

public class RowInsertTests
{
    private static string Build(TempDir dir) => Workbooks.CreateSmall(dir, (config, request, response) =>
    {
        var workbook = request.Workbook;
        var notes = workbook.AddWorksheet("notes");
        notes.Cell(1, 1).Value = "see <From_request_title_3> and <From_request_title_4> and <From_response_statuscode_r>";
        notes.Cell(2, 1).FormulaA1 = "request!E4"; // an ordinary Excel reference to D3's title
        request.Cell(2, 6).Value = "uses <From_title_4>";   // same-sheet short form, points at D3
        request.Cell(1, 6).Value = "extra";
    });

    private static string Text(string path, string sheet, string address)
    {
        using var workbook = new XLWorkbook(path);
        return workbook.Worksheet(sheet).Cell(address).GetString();
    }

    [Fact]
    public void Inserting_before_a_row_puts_both_rows_in_place_and_renumbers_every_reference()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);

        var (request, response) = doc.InsertDataRowBefore(1, "NEW", "D3");
        request.Set("title", "inserted title");
        var result = doc.Save();

        Assert.Equal(2, result.RowsInserted);
        Assert.Equal(3, result.ReferencesRenumbered); // config URL, notes (one of two) and the short form
        // request: D1, D2, NEW, D3 — response likewise
        Assert.Equal(["D1", "D2", "NEW", "D3"], Enumerable.Range(2, 4).Select(r => Text(path, "request", $"C{r}")));
        Assert.Equal(["D1", "D2", "NEW", "D3"], Enumerable.Range(2, 4).Select(r => Text(path, "response", $"C{r}")));
        Assert.Equal("inserted title", Text(path, "request", "E4"));
        Assert.Equal("title 3", Text(path, "request", "E5"));
        // references to rows at or below the insert moved with their data; the one above did not
        Assert.Equal("http://host/<From_request_title_5>", Text(path, "config", "F2"));
        Assert.Equal("see <From_request_title_3> and <From_request_title_5> and <From_response_statuscode_r>", Text(path, "notes", "A1"));
        Assert.Equal("uses <From_title_5>", Text(path, "request", "F2"));
        // Excel's own formula reference follows the row too
        using var workbook = new XLWorkbook(path);
        Assert.Equal("request!E5", workbook.Worksheet("notes").Cell("A2").FormulaA1);
        // and the runner still reads it, resolving the URL to D3's title
        var model = new WorkbookReader().Read(path);
        Assert.Equal("http://host/title 3", model.Testcases[0].Steps[0].TargetUrl);
        Assert.Equal(4, model.RequestData.Count);
    }

    [Fact]
    public void Several_inserts_in_one_save_keep_every_reference_on_its_data()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        doc.InsertDataRowBefore(1, "A", "D2");
        doc.InsertDataRowBefore(1, "B", "D2");   // two rows above the same neighbour keep their order
        doc.InsertDataRowBefore(1, "C", "D3");

        doc.Save();

        Assert.Equal(["D1", "A", "B", "D2", "C", "D3"], Enumerable.Range(2, 6).Select(r => Text(path, "request", $"C{r}")));
        Assert.Equal(["D1", "A", "B", "D2", "C", "D3"], Enumerable.Range(2, 6).Select(r => Text(path, "response", $"C{r}")));
        var model = new WorkbookReader().Read(path);
        Assert.Equal("http://host/title 3", model.Testcases[0].Steps[0].TargetUrl); // D3 is now row 7
        Assert.Contains("<From_request_title_7>", Text(path, "notes", "A1"));
    }

    [Fact]
    public void The_scan_lists_what_points_at_rows_before_anything_is_inserted()
    {
        using var dir = new TempDir();
        var doc = WorkbookDocument.Load(Build(dir));
        var refs = doc.FindRowReferences().Where(r => r.TargetSheet == "request").ToList();
        Assert.Equal(4, refs.Count);
        Assert.Contains(refs, r => r.Sheet == "config" && r.TargetRow == 4);
        Assert.Contains(refs, r => r.Sheet == "request" && r.TargetRow == 4); // the short form, same sheet
    }

    [Fact]
    public void Inserting_is_refused_when_a_row_would_change_its_testcase_index()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) => request.Cell(3, 1).Value = "");
        var doc = WorkbookDocument.Load(path);
        var ex = Assert.Throws<InvalidOperationException>(() => doc.InsertDataRowBefore(1, "X", "D3"));
        Assert.Contains("numeric testcaseindex", ex.Message);
    }

    [Fact]
    public void Inserting_below_the_last_row_is_a_plain_append_and_other_rows_do_not_move()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);
        doc.AddDataRow(1, "END");
        var result = doc.Save();
        Assert.Equal(0, result.RowsInserted);
        Assert.Equal("END", Text(path, "request", "C5"));
        Assert.Equal("http://host/<From_request_title_4>", Text(path, "config", "F2"));
    }
}

public class RowInsertOnRealWorkbooksTests
{
    [Theory]
    [InlineData(Samples.IntegrationScenarios)]
    [InlineData(Samples.FileHttpScenarios)]
    [InlineData(Samples.AllParameters)]
    public void Inserting_a_row_into_a_shipped_workbook_keeps_it_valid_and_every_other_row_unchanged(string relative)
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, relative);
        var before = new WorkbookReader().Read(path);
        var again = new WorkbookReader().Read(path); // values built from <randomnumber> etc. differ between reads; leave those out
        var doc = WorkbookDocument.Load(path);
        var first = doc.Request.Rows.First(r => !r.IsBlank);
        var testcase = int.Parse(first.Get("testcaseindex"));
        doc.InsertDataRowBefore(testcase, "INSERTED_ROW", first.Get("dataid"));

        var result = doc.Save();

        Assert.Equal(2, result.RowsInserted);
        var after = new WorkbookReader().Read(path);
        Assert.Equal(before.RequestData.Count + 1, after.RequestData.Count);
        foreach (var (key, row) in before.RequestData)
        {
            var stable = row.Variables.Where(v => again.RequestData[key].Variables[v.Key] == v.Value).ToDictionary(v => v.Key, v => v.Value);
            Assert.NotEmpty(stable);
            foreach (var (name, value) in stable) Assert.Equal(value, after.RequestData[key].Variables[name]); // every existing row resolves to the same values
        }
        Assert.Equal(before.Testcases.SelectMany(t => t.Steps).Select(s => s.TargetUrl), after.Testcases.SelectMany(t => t.Steps).Select(s => s.TargetUrl));
        Assert.Empty(WorkbookDocument.Load(path).Validate().Where(p => p.Severity == ProblemSeverity.Error));
    }
}

public class RowInsertRuntimeReferenceTests
{
    [Fact]
    public void Runtime_references_to_earlier_step_responses_are_never_renumbered()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) => request.Cell(2, 5).Value = "<From_response_CreatePost.id_2> and <From_request_title_4>");
        var doc = WorkbookDocument.Load(path);
        Assert.DoesNotContain(doc.FindRowReferences(), r => r.Token.Contains("CreatePost.id"));
        doc.InsertDataRowBefore(1, "NEW", "D1");
        doc.Save();

        using var workbook = new XLWorkbook(path);
        Assert.Equal("<From_response_CreatePost.id_2> and <From_request_title_5>", workbook.Worksheet("request").Cell("E3").GetString());
    }
}
