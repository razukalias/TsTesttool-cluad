using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
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

public class LongValueTests
{
    [Fact]
    public void A_value_longer_than_an_Excel_cell_is_saved_as_a_file_and_the_cell_holds_the_token_and_path()
    {
        using var dir = new TempDir();
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        var big = new string('a', 40_000) + "END";
        var run = new RunResult { RunId = "r1", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "T", DataId = "d1", Iteration = 1, StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        testcase.Requests.Add(new RequestResult { Testcase = "T", StepName = "Read", HttpStatus = 200, ResponseBody = big, Variables = new Dictionary<string, string> { ["content"] = big } });
        run.Testcases.Add(testcase);

        var path = new HistoryWriter(config).Write(run);

        using var book = new XLWorkbook(path);
        var cell = book.Worksheet("ResolvedValues").Cell(2, 9).GetString();
        Assert.StartsWith("<from_filepath>", cell);
        var file = cell["<from_filepath>".Length..];
        Assert.Equal(big, File.ReadAllText(file));           // the full value is kept
        Assert.Contains(Path.Combine("_values", "r1"), file);
        Assert.True(FileTokens.TryParse(cell, out _, out var parsed) && File.Exists(parsed));   // and the token reads it back
    }

    [Fact]
    public void Validation_reports_a_cell_over_the_limit_with_the_file_fix()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = LoadTestingTool.Editing.WorkbookDocument.Load(path);
        document.Request.Rows.First(r => !r.IsBlank).SetAt(4, new string('x', 33_000));

        var problem = Assert.Single(document.Validate(), p => p.Message.Contains("32,767"));
        Assert.Equal(LoadTestingTool.Editing.ProblemSeverity.Error, problem.Severity);
        Assert.Contains("<from_filepath>", problem.Message);
    }
}

public class ValueFilePruneTests
{
    [Fact]
    public void Value_files_go_when_the_history_that_pointed_at_them_is_gone_and_stay_while_newer_history_remains()
    {
        using var dir = new TempDir();
        var history = dir.Combine("history");
        Directory.CreateDirectory(Path.Combine(history, "_values", "old")); File.WriteAllText(Path.Combine(history, "_values", "old", "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(history, "_values", "new")); File.WriteAllText(Path.Combine(history, "_values", "new", "b.txt"), "y");
        Directory.SetCreationTimeUtc(Path.Combine(history, "_values", "old"), DateTime.UtcNow.AddDays(-2));
        Directory.CreateDirectory(Path.Combine(history, "passed"));
        var kept = Path.Combine(history, "passed", "keep.xlsx"); File.WriteAllText(kept, "x");
        File.SetCreationTimeUtc(kept, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(1, HistoryValueFiles.Prune(history));
        Assert.False(Directory.Exists(Path.Combine(history, "_values", "old")));
        Assert.True(Directory.Exists(Path.Combine(history, "_values", "new")));

        File.Delete(kept);
        Assert.Equal(1, HistoryValueFiles.Prune(history));       // no history left: nothing points at the files
    }
}

public class FileCellHistoryTests
{
    [Fact]
    public void A_file_cell_is_shown_as_its_token_and_path_in_the_resolved_copies_never_its_content()
    {
        using var dir = new TempDir();
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        var run = new RunResult { RunId = "r1", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = "d1", Iteration = 1, StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        var redacted = new RedactedVariableSet(["payload"]);
        redacted.FileCells["payload"] = "<from_filepath>docs/big.json";
        var request = new RequestResult
        {
            RunId = "r1", TestcaseIndex = 1, Testcase = "Order", DataId = "d1", StepName = "Create", StepType = "http", Iteration = 1,
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["payload"] = "FILE-CONTENT-SECRET", ["title"] = "t" },
            RedactedVariables = redacted,
        };
        testcase.Requests.Add(request);
        run.Testcases.Add(testcase);

        using var book = new XLWorkbook(new HistoryWriter(config).Write(run));

        var values = book.Worksheet("ResolvedValues").RowsUsed().Skip(1).Select(r => (r.Cell(8).GetString(), r.Cell(9).GetString())).ToList();
        Assert.Contains(("payload", "<from_filepath>docs/big.json"), values);
        var requestSheet = book.Worksheet("Request");
        var headers = requestSheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal("<from_filepath>docs/big.json", requestSheet.Cell(2, headers.IndexOf("payload") + 1).GetString());
        Assert.DoesNotContain("FILE-CONTENT-SECRET", string.Join("|", book.Worksheets.SelectMany(w => w.CellsUsed()).Select(c => c.GetString())));
    }
}
