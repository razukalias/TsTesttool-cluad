using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Reporting;
using Xunit;

namespace WorkbookEditTests;

public class ResolvedHistoryTests
{
    private static string Write(TempDir dir, Action<AppConfig>? configure = null)
    {
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        configure?.Invoke(config);
        var run = new RunResult { RunId = "r1", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = "d1", Iteration = 2, Environment = "qa", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["title"] = "order-123456", ["token"] = "SECRET", ["env"] = "qa", ["prev"] = "from-earlier" };
        var request = new RequestResult
        {
            RunId = "r1", TestcaseIndex = 1, Testcase = "Order", DataId = "d1", StepName = "Create", StepType = "http", Iteration = 2, Environment = "qa",
            TargetUrl = "http://x/y", Verb = "POST", Variables = vars, RedactedVariables = new HashSet<string>(["token"], StringComparer.OrdinalIgnoreCase)
        };
        request.Correlations.Add(new CorrelationRecord { DataId = "d1", SourceStepName = "Login", Variable = "prev", ExtractedValue = "from-earlier" });
        request.Assertions.Add(new AssertionResult { RunId = "r1", TestcaseIndex = 1, Testcase = "Order", DataId = "d1", StepName = "Create", ResponsePath = "$.title", AssertionVerb = "eq", ExpectedValue = "order-123456", ActualValue = "order-123456", Result = "PASS" });
        testcase.Requests.Add(request);
        run.Testcases.Add(testcase);
        return new HistoryWriter(config).Write(run);
    }

    [Fact]
    public void History_holds_resolved_copies_of_config_request_and_response_with_run_iteration_and_environment()
    {
        using var dir = new TempDir();
        using var book = new XLWorkbook(Write(dir));

        var request = book.Worksheet("Request");
        Assert.Equal("r1", request.Cell(2, 1).GetString());
        Assert.Equal("2", request.Cell(2, 2).GetString());
        Assert.Equal("qa", request.Cell(2, 3).GetString());
        var headers = request.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal("order-123456", request.Cell(2, headers.IndexOf("title") + 1).GetString());
        Assert.DoesNotContain("env", headers);                  // the environment has its own column
        Assert.DoesNotContain("prev", headers);                 // values from earlier responses are not request cells
        Assert.Equal("Create", book.Worksheet("Config").Cell(2, 6).GetString());
        Assert.Equal("order-123456", book.Worksheet("Response").Cell(2, 10).GetString());
    }

    [Fact]
    public void Sensitive_values_are_masked_and_ResolvedValues_lists_each_request_cell_once_in_long_form()
    {
        using var dir = new TempDir();
        using var book = new XLWorkbook(Write(dir));

        var values = book.Worksheet("ResolvedValues");
        var rows = values.RowsUsed().Skip(1).Select(r => (Cell: r.Cell(8).GetString(), Value: r.Cell(9).GetString(), Iteration: r.Cell(5).GetString())).ToList();
        Assert.Contains(("title", "order-123456", "2"), rows);
        Assert.DoesNotContain(rows, r => r.Value.Contains("SECRET"));
        Assert.Contains(rows, r => r.Cell == "token" && r.Value.Contains("[redacted:token]"));
        Assert.DoesNotContain(rows, r => r.Cell is "env" or "prev");
    }

    [Fact]
    public void Each_copy_can_be_switched_off_but_ResolvedValues_is_always_written()
    {
        using var dir = new TempDir();
        using var book = new XLWorkbook(Write(dir, c => { c.HistorySheets["Config"] = false; c.HistorySheets["Request"] = false; c.HistorySheets["Response"] = false; c.HistorySheets["ResolvedValues"] = false; }));

        Assert.False(book.Worksheets.Contains("Config"));
        Assert.False(book.Worksheets.Contains("Request"));
        Assert.False(book.Worksheets.Contains("Response"));
        Assert.True(book.Worksheets.Contains("ResolvedValues"));
    }
}
