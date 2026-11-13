using ClosedXML.Excel;
using LoadTestingTool.Editing;
using LoadTestingTool.Excel;
using Xunit;

namespace WorkbookEditTests;

public class TestcaseOperationTests
{
    // Testcase 1 "Flow" (step CreatePost, rows D1..D3, request rows 2-4) and testcase 2 "Second" (step Other, row D1, request row 5).
    private static string Build(TempDir dir) => Workbooks.CreateSmall(dir, (config, request, response) =>
    {
        config.Cell(3, 1).Value = 2; config.Cell(3, 2).Value = "Second"; config.Cell(3, 3).Value = "Other"; config.Cell(3, 4).Value = "HTTP";
        config.Cell(3, 5).Value = "other.json"; config.Cell(3, 6).Value = "http://host/other"; config.Cell(3, 7).Value = "GET"; config.Cell(3, 8).Value = true;
        request.Cell(1, 6).Value = "CreatePost.title";
        request.Cell(1, 7).Value = "Other.name";
        for (var n = 2; n <= 4; n++) request.Cell(n, 6).Value = "p" + n;
        request.Cell(2, 4).Value = "CreatePost, Other";
        request.Cell(5, 1).Value = 2; request.Cell(5, 2).Value = "Second"; request.Cell(5, 3).Value = "D1"; request.Cell(5, 7).Value = "name5";
        response.Cell(1, 5).Value = "extractvariable";
        response.Cell(1, 6).Value = "{eq}Other.statusCode";
        response.Cell(1, 7).Value = "{exists}CreatePost.id";
        response.Cell(2, 5).Value = "CreatePost.id:pid";
        response.Cell(5, 1).Value = 2; response.Cell(5, 2).Value = "Second"; response.Cell(5, 3).Value = "D1"; response.Cell(5, 6).Value = "200";
    });

    private static IXLWorksheet Sheet(XLWorkbook w, string name) => w.Worksheet(name);

    [Fact]
    public void Renaming_a_testcase_updates_all_three_sheets()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);

        doc.RenameTestcase(1, "Main flow");
        doc.Save();

        using var workbook = new XLWorkbook(path);
        Assert.Equal("Main flow", Sheet(workbook, "config").Cell("B2").GetString());
        Assert.Equal("Main flow", Sheet(workbook, "request").Cell("B4").GetString());
        Assert.Equal("Main flow", Sheet(workbook, "response").Cell("B3").GetString());
        Assert.Equal("Second", Sheet(workbook, "request").Cell("B5").GetString());
    }

    [Fact]
    public void Moving_a_testcase_swaps_numbers_with_its_neighbour_so_the_run_order_changes()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);

        Assert.Throws<InvalidOperationException>(() => doc.MoveTestcase(1, -1));
        var moved = doc.MoveTestcase(2, -1);
        Assert.Equal(1, moved);
        Assert.Equal(["Second", "Flow"], doc.GetTestcases().Select(t => t.Name));
        doc.Save();

        var model = new WorkbookReader().Read(path);
        Assert.Equal(["Second", "Flow"], model.Testcases.Select(t => t.Testcase));
        Assert.Equal(["D1", "D2", "D3"], model.RequestData.Where(kv => kv.Key.Item1 == 2).Select(kv => kv.Key.Item2).OrderBy(x => x));
    }

    [Fact]
    public void Moving_a_step_swaps_it_with_its_neighbour_in_the_same_testcase()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (config, _, _) =>
        {
            config.Cell(3, 1).Value = 1; config.Cell(3, 2).Value = "Flow"; config.Cell(3, 3).Value = "SecondStep"; config.Cell(3, 4).Value = "HTTP";
            config.Cell(3, 5).Value = "b.json"; config.Cell(3, 6).Value = "http://host/b"; config.Cell(3, 7).Value = "GET"; config.Cell(3, 8).Value = true;
        });
        var doc = WorkbookDocument.Load(path);
        var steps = doc.Config.Rows.Where(r => !r.IsBlank).ToList();

        Assert.False(doc.MoveStep(steps[0], -1));
        Assert.True(doc.MoveStep(steps[1], -1));
        Assert.Equal(["SecondStep", "CreatePost"], doc.GetTestcases().Single().Steps);
        doc.Save();

        Assert.Equal(["SecondStep", "CreatePost"], new WorkbookReader().Read(path).Testcases.Single().Steps.Select(s => s.StepName));
    }

    [Fact]
    public void Copying_a_testcase_creates_renamed_steps_columns_and_data_and_the_result_is_valid()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);

        var result = doc.CopyTestcase(1, "Flow copy");

        Assert.Equal(3, result.NewIndex);
        Assert.Equal(1, result.Steps);
        Assert.Equal(3, result.DataRows);
        Assert.Equal(3, result.ColumnsAdded); // CreatePostCopy.title (request) plus statusCode and id (response)
        Assert.Equal(1, result.AbsoluteReferences); // the config url's <From_request_title_4>
        Assert.DoesNotContain(doc.Validate(), p => p.Severity == ProblemSeverity.Error);
        doc.Save();

        using var workbook = new XLWorkbook(path);
        var config = Sheet(workbook, "config");
        Assert.Equal("CreatePostCopy", config.Cell("C4").GetString());
        Assert.Equal(3, config.Cell("A4").GetValue<int>());
        Assert.Equal("Flow copy", config.Cell("B4").GetString());
        var request = Sheet(workbook, "request");
        var header = request.Row(1).CellsUsed().First(c => c.GetString() == "CreatePostCopy.title").Address.ColumnNumber;
        Assert.Equal("p2", request.Cell(6, header).GetString());
        Assert.Equal("", request.Cell(6, 6).GetString());              // original column is blank for the copy
        Assert.Equal("p2", request.Cell(2, 6).GetString());            // original data untouched
        Assert.Equal("CreatePostCopy, Other", request.Cell(6, 4).GetString());
        var response = Sheet(workbook, "response");
        Assert.Equal("CreatePostCopy.id:pid", response.Cell(6, 5).GetString());
        Assert.Equal("D1", request.Cell(6, 3).GetString());
        var model = new WorkbookReader().Read(path);
        Assert.Equal(3, model.Testcases.Count);
    }

    [Fact]
    public void Deleting_a_testcase_clears_its_rows_and_is_refused_while_another_cell_points_at_them()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var doc = WorkbookDocument.Load(path);

        var refused = Assert.Throws<InvalidOperationException>(() =>
        {
            using var workbook = new XLWorkbook(path);
            workbook.Worksheet("config").Cell("F2").Value = "http://host/<From_request_title_5>"; // request row 5 belongs to testcase 2
            workbook.Save();
            WorkbookDocument.Load(path).DeleteTestcase(2);
        });
        Assert.Contains("refers to its row 5", refused.Message);

        using (var workbook = new XLWorkbook(path)) { workbook.Worksheet("config").Cell("F2").Value = "http://host/ok"; workbook.Save(); }
        doc = WorkbookDocument.Load(path);
        var result = doc.DeleteTestcase(2);
        Assert.Equal(1, result.Steps);
        Assert.Equal(1, result.DataRows);
        doc.Save();

        var model = new WorkbookReader().Read(path);
        Assert.Single(model.Testcases);
        Assert.Equal(1, model.Testcases[0].TestcaseIndex);
    }

    [Fact]
    public void Deleting_a_testcase_is_refused_when_another_testcase_reads_its_response_at_run_time()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        using (var workbook = new XLWorkbook(path)) { workbook.Worksheet("request").Cell("F3").Value = "<From_response_Other.id_2>"; workbook.Save(); }
        var doc = WorkbookDocument.Load(path);

        var ex = Assert.Throws<InvalidOperationException>(() => doc.DeleteTestcase(2));
        Assert.Contains("at run time", ex.Message);
    }
}
