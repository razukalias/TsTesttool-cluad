using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Reporting;
using Xunit;

namespace WorkbookEditTests;

public class HistoryProfileTests
{
    [Fact]
    public void Each_profile_is_detected_as_itself_and_any_single_change_makes_it_Custom()
    {
        foreach (var name in HistoryProfiles.Names)
            Assert.Equal(name, HistoryProfiles.Detect(HistoryProfiles.For(name)));

        var functional = HistoryProfiles.For(HistoryProfiles.Functional);
        Assert.Equal(HistoryProfiles.Custom, HistoryProfiles.Detect(functional with { SaveResponseBodies = false }));
        var sheets = new Dictionary<string, bool>(functional.Sheets, StringComparer.OrdinalIgnoreCase) { ["Metrics"] = false };
        Assert.Equal(HistoryProfiles.Custom, HistoryProfiles.Detect(functional with { Sheets = sheets }));
    }

    [Fact]
    public void The_Load_test_profile_keeps_the_summary_sheets_and_turns_row_history_off_and_aggregate_on()
    {
        var load = HistoryProfiles.For(HistoryProfiles.LoadTest);

        Assert.True(load.InternalLog); Assert.True(load.InternalLogErrorsOnly);
        Assert.True(load.AggregateHistory); Assert.False(load.RowHistory);
        Assert.Equal(HistoryProfiles.DetailFailedAndSample, load.Detail);
        Assert.Equal(HistoryProfiles.DefaultSampleEvery, load.SampleEvery);
        Assert.All(new[] { "RunSummary", "Metrics", "TestcaseSummary", "RequestHistory" }, n => Assert.True(load.Sheets[n]));
        Assert.All(new[] { "Variables", "Config", "Request", "Response", "DataEngineRequestRows" }, n => Assert.False(load.Sheets[n]));
        Assert.True(HistoryProfiles.RunIsLarge(10, 100, 5));
        Assert.False(HistoryProfiles.RunIsLarge(1, 1, 20));
    }

    private static (AppConfig Config, RunResult Run) Scenario(string folder, string detail, int every)
    {
        var config = new AppConfig { HistoryFolder = folder, HistoryDetail = detail, HistorySampleEvery = every };
        var run = new RunResult { RunId = "r1", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "T", DataId = "d1", Iteration = 1, StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        for (var i = 1; i <= 10; i++)
        {
            var request = new RequestResult { RequestId = $"q{i}", Testcase = "T", TestcaseIndex = 1, DataId = "d1", StepName = $"S{i}", HttpStatus = i == 7 ? 500 : 200, RequestBody = $"req{i}", ResponseBody = $"resp{i}", Variables = new Dictionary<string, string> { ["v"] = $"val{i}" } };
            request.Assertions.Add(new AssertionResult { RequestId = $"q{i}", Testcase = "T", StepName = $"S{i}", ResponsePath = "$.a", AssertionVerb = "eq", Result = i == 7 ? "FAIL" : "PASS" });
            testcase.Requests.Add(request);
        }
        run.Testcases.Add(testcase);
        return (config, run);
    }

    [Fact]
    public void Load_test_detail_keeps_failed_requests_in_full_and_only_a_sample_of_passing_ones_but_counts_all()
    {
        using var dir = new TempDir();
        var (config, run) = Scenario(dir.Combine("h"), HistoryProfiles.DetailFailedAndSample, every: 5);

        using var book = new XLWorkbook(new HistoryWriter(config).Write(run));

        var requests = book.Worksheet("RequestHistory").RowsUsed().Skip(1).ToList();
        Assert.Equal(10, requests.Count);                                              // every request is still counted
        var headers = book.Worksheet("RequestHistory").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        var body = headers.IndexOf("responsebody") + 1;
        var withBody = requests.Where(r => r.Cell(body).GetString() != "[omitted]").Select(r => r.Cell(2).GetString()).ToList();
        Assert.Equal(new[] { "q1", "q6", "q7" }, withBody.Order().ToArray());                    // passing #1 and #6 (every 5th, from the first) plus the failed one
        Assert.Contains(book.Worksheet("AssertionHistory").RowsUsed().Skip(1), r => r.Cell(2).GetString() == "q7");
        Assert.DoesNotContain(book.Worksheet("AssertionHistory").RowsUsed().Skip(1), r => r.Cell(2).GetString() == "q3");
        Assert.True(book.Worksheet("Metrics").RowsUsed().Count() > 0);
    }

    [Fact]
    public void Full_detail_keeps_everything()
    {
        using var dir = new TempDir();
        var (config, run) = Scenario(dir.Combine("h"), HistoryProfiles.DetailAll, every: 5);

        using var book = new XLWorkbook(new HistoryWriter(config).Write(run));

        Assert.Equal(10, book.Worksheet("AssertionHistory").RowsUsed().Skip(1).Count());
        var headers = book.Worksheet("RequestHistory").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.All(book.Worksheet("RequestHistory").RowsUsed().Skip(1), r => Assert.NotEqual("[omitted]", r.Cell(headers.IndexOf("responsebody") + 1).GetString()));
    }

    [Fact]
    public void An_errors_only_internal_log_drops_info_lines_and_keeps_warnings_and_errors()
    {
        using var dir = new TempDir();
        using (var log = new InternalLogger(dir.Path, "i", "r", enabled: true) { ErrorsOnly = true })
        {
            log.Info("chatty"); log.Section("a section"); log.Warn("careful"); log.Error("broken");
        }
        var text = File.ReadAllText(Directory.GetFiles(dir.Path, "*.log").Single());
        Assert.DoesNotContain("chatty", text); Assert.DoesNotContain("a section", text);
        Assert.Contains("careful", text); Assert.Contains("broken", text);
    }
}
