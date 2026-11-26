using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using Xunit;
using Xunit.Abstractions;

namespace WorkbookEditTests;

public sealed class SqliteHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sqlhist-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _output;
    public SqliteHistoryTests(ITestOutputHelper output) { _output = output; Directory.CreateDirectory(_root); }
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static RunResult MakeRun(string runId, DateTimeOffset start, int rows, Func<int, bool> fails, string environment = "qa", string prefix = "D", int steps = 3)
    {
        var run = new RunResult { RunId = runId, StartedAt = start, EndedAt = start.AddMinutes(1) };
        for (var n = 1; n <= rows; n++)
        {
            var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = $"{prefix}{n:0000}", Iteration = 1, Environment = environment, StartedAt = start, EndedAt = start.AddSeconds(1) };
            for (var s = 1; s <= steps; s++) testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Step" + s, HttpStatus = fails(n) && s == 2 ? 500 : 200 });
            run.Testcases.Add(testcase);
        }
        return run;
    }

    private static RunValueMemory.Snapshot MakeState(RunResult run, string tag)
    {
        var state = new RunValueMemory.Snapshot();
        foreach (var t in run.Testcases)
        {
            var key = RunValueMemory.RowKey(t.TestcaseIndex, t.DataId, t.Iteration, t.Environment);
            state.Generated[$"{key}|guid|1"] = $"{tag}-guid-{t.DataId}";
            state.Steps[$"{key}|Step1|url"] = $"http://x/{tag}/{t.DataId}";
            state.Rows[key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["token"] = $"{tag}-token-{t.DataId}" };
        }
        return state;
    }

    [Fact]
    public void A_saved_run_lists_its_rows_failed_steps_and_gives_back_its_old_data()
    {
        var store = new SqliteHistoryStore(_root);
        var run = MakeRun("run1", DateTimeOffset.Now, 5, n => n is 2 or 4);
        store.SaveRun(run, MakeState(run, "A"), null);

        var (rows, count) = store.ScanPage(0, 10);
        Assert.Equal(1, count);
        Assert.Equal(5, rows.Count);
        Assert.Equal(["D0002", "D0004"], rows.Where(r => r.Failed).Select(r => r.DataId).ToArray());
        Assert.Equal(["Step2"], rows.Single(r => r.DataId == "D0002").FailedSteps!.ToArray());
        Assert.Empty(rows.Single(r => r.DataId == "D0001").FailedSteps!);

        var state = store.OpenRunState(null).Load()!;
        Assert.Equal("A-guid-D0003", state.Generated["1|D0003|1|qa|guid|1"]);
        Assert.Equal("http://x/A/D0003", state.Steps["1|D0003|1|qa|Step1|url"]);
        Assert.Equal("A-token-D0003", state.Rows["1|D0003|1|qa"]["token"]);
    }

    [Fact]
    public void The_newest_run_of_a_row_supplies_its_old_data_unless_one_run_is_asked_for()
    {
        var store = new SqliteHistoryStore(_root);
        var first = MakeRun("run1", DateTimeOffset.Now.AddHours(-2), 4, _ => false);
        var second = MakeRun("run2", DateTimeOffset.Now.AddHours(-1), 2, _ => false);   // a re-run of D0001 and D0002 only
        store.SaveRun(first, MakeState(first, "A"), null);
        store.SaveRun(second, MakeState(second, "B"), "run1");

        var newest = store.OpenRunState(null).Load()!;
        Assert.Equal("B-guid-D0001", newest.Generated["1|D0001|1|qa|guid|1"]);   // re-run: the newest
        Assert.Equal("A-guid-D0004", newest.Generated["1|D0004|1|qa|guid|1"]);   // not re-run: the older run still supplies it
        var only = store.OpenRunState("run1").Load()!;
        Assert.Equal("A-guid-D0001", only.Generated["1|D0001|1|qa|guid|1"]);
        Assert.Equal("run1", store.ScanPage(0, 10).Rows.First(r => r.RunId == "run2").RerunOf);
    }

    [Fact]
    public void Pages_hold_that_many_runs_newest_first_and_saving_a_run_twice_replaces_it()
    {
        var store = new SqliteHistoryStore(_root);
        for (var i = 1; i <= 5; i++) store.SaveRun(MakeRun("run" + i, DateTimeOffset.Now.AddMinutes(i), 2, _ => false), null, null);
        var page = store.ScanPage(0, 2);
        Assert.Equal(2, page.Files);
        Assert.Equal(["run5", "run4"], page.Rows.Select(r => r.RunId).Distinct().ToArray());
        Assert.Equal(["run3", "run2"], store.ScanPage(2, 2).Rows.Select(r => r.RunId).Distinct().ToArray());
        store.SaveRun(MakeRun("run5", DateTimeOffset.Now.AddMinutes(5), 3, _ => false), null, null);
        Assert.Equal(3, store.ScanPage(0, 1).Rows.Count);
    }

    /// <summary>A history workbook as the runner writes it: the hidden RunState sheet.</summary>
    private static void WriteWorkbook(string folder, RunResult run, RunValueMemory.Snapshot state, string? rerunOf = null)
    {
        Directory.CreateDirectory(folder);
        using var book = new XLWorkbook();
        book.AddWorksheet("Summary").Cell(1, 1).Value = "x";
        HistoryRunStateStore.WriteSheet(book, state, run, rerunOf);
        book.SaveAs(Path.Combine(folder, $"Execution_History_{run.RunId}.xlsx"));
    }

    [Fact]
    public void Old_history_workbooks_are_imported_by_date_and_the_database_then_agrees_with_them()
    {
        var folder = Path.Combine(_root, "files");
        var days = new[] { new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero) };
        for (var i = 0; i < days.Length; i++)
        {
            var run = MakeRun("old" + i, days[i], 6, n => n % 3 == 0);
            WriteWorkbook(folder, run, MakeState(run, "T" + i), i == 2 ? "old1" : null);
        }
        var store = new SqliteHistoryStore(Path.Combine(_root, "db"));
        var middle = store.ImportWorkbooks(folder, new DateTime(2026, 3, 5), new DateTime(2026, 3, 15));
        Assert.Equal((3, 1, 0), (middle.Files, middle.RunsAdded, middle.RunsSkipped));
        Assert.Equal(["old1"], store.ScanPage(0, 10).Rows.Select(r => r.RunId).Distinct().ToArray());

        var all = store.ImportWorkbooks(folder, null, null);
        Assert.Equal((2, 1), (all.RunsAdded, all.RunsSkipped));       // old1 was already there
        Assert.Empty(all.Problems);
        Assert.Empty(store.CompareWithWorkbooks(folder));
        Assert.Equal("old1", store.ScanPage(0, 10).Rows.First(r => r.RunId == "old2").RerunOf);
    }

    [Fact]
    public void Per_row_history_files_of_one_run_are_all_imported_not_only_the_first()
    {
        // The runner writes one workbook per data row (all with the same run id) and optionally an aggregate with every row.
        var folder = Path.Combine(_root, "rowfiles");
        var start = new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero);
        var run = new RunResult { RunId = "perrow", StartedAt = start, EndedAt = start.AddMinutes(1) };
        for (var n = 1; n <= 4; n++)
        {
            var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = $"D{n:0000}", Iteration = 1, Environment = "qa", StartedAt = start.AddSeconds((n - 1) * 5), EndedAt = start.AddSeconds((n - 1) * 5 + 1), TestcaseRowNumber = n + 1 };
            for (var step = 1; step <= 3; step++) testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Step" + step, HttpStatus = n == 3 && step == 2 ? 500 : 200 });
            run.Testcases.Add(testcase);
        }
        var config = new AppConfig { HistoryFolder = folder };
        var writer = new HistoryWriter(config) { RunState = MakeState(run, "P") };
        Assert.Equal(4, writer.WritePerTestcase(run).Count);
        writer.Write(run, Path.Combine(folder, "_aggregate", "failed"), "Execution_History_perrow.xlsx");   // the aggregate repeats every row

        var store = new SqliteHistoryStore(Path.Combine(_root, "db"));
        var result = store.ImportWorkbooks(folder, null, null);
        Assert.Empty(result.Problems);
        Assert.Equal((1, 0), (result.RunsAdded, result.RunsSkipped));          // one run, however many files it was in
        var rows = store.ScanPage(0, 10).Rows;
        Assert.Equal(["D0001", "D0002", "D0003", "D0004"], rows.Select(r => r.DataId).Order().ToArray());
        Assert.Equal(["D0003"], rows.Where(r => r.Failed).Select(r => r.DataId).ToArray());
        Assert.Equal(start, rows.First().StartedAt);                          // the run started with its first row
        Assert.Empty(store.CompareWithWorkbooks(folder));
        Assert.Equal("P-token-D0002", store.OpenRunState(null).Load()!.Rows["1|D0002|1|qa"]["token"]);

        var again = store.ImportWorkbooks(folder, null, null);                 // the same folder again adds nothing
        Assert.Equal((0, 1), (again.RunsAdded, again.RunsSkipped));
        Assert.Equal(4, store.ScanPage(0, 10).Rows.Count);
    }

    private static RunResult RunWithDetails(string runId, DateTimeOffset start)
    {
        var run = new RunResult { RunId = runId, StartedAt = start, EndedAt = start.AddMinutes(1) };
        for (var n = 1; n <= 3; n++)
        {
            var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = $"D{n:0000}", Iteration = 1, Environment = "qa", StartedAt = start.AddSeconds(n), EndedAt = start.AddSeconds(n + 1), TestcaseRowNumber = n + 1 };
            for (var step = 1; step <= 2; step++)
            {
                var request = new RequestResult
                {
                    Testcase = "Order", TestcaseIndex = 1, DataId = testcase.DataId, StepName = "Step" + step, StepType = "HTTP", Iteration = 1, Environment = "qa", StartedAt = start.AddSeconds(n), Verb = "POST", TargetUrl = "http://x/orders",
                    HttpStatus = n == 2 && step == 2 ? 500 : 200, DurationMs = 10 * n + step, RequestBody = "{\"id\":\"" + testcase.DataId + "\",\"token\":\"secret-abc\"}", ResponseBody = "{\"ok\":" + (n == 2 && step == 2 ? "false" : "true") + ",\"text\":\"" + new string('x', 3000) + "\"}",
                    RequestHeaders = "Authorization: Bearer secret-abc", ResponseHeaders = "Content-Type: application/json"
                };
                request.Assertions.Add(new AssertionResult { RequestId = request.RequestId, Testcase = "Order", TestcaseIndex = 1, DataId = testcase.DataId, StepName = request.StepName, ResponsePath = "ok", AssertionVerb = "eq", ExpectedValue = "true", ActualValue = n == 2 && step == 2 ? "false" : "true", Result = n == 2 && step == 2 ? "FAIL" : "PASS", FailureMessage = n == 2 && step == 2 ? "Values differ" : "" });
                testcase.Requests.Add(request);
            }
            run.Testcases.Add(testcase);
        }
        return run;
    }

    [Fact]
    public void Requests_bodies_and_assertions_are_stored_compressed_and_come_back_the_way_the_workbook_would_show_them()
    {
        var folder = Path.Combine(_root, "db1");
        var run = RunWithDetails("withdetail", new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        var config = new AppConfig { HistoryFolder = folder, HistoryStorage = HistoryStorage.Database, SaveRequestBodyToHistory = true, SaveResponseBodyToHistory = false };
        HistoryStores.SaveToDatabase(config, run, MakeState(run, "A"), null);

        var store = new SqliteHistoryStore(folder);
        var requests = store.LoadRequests("withdetail");
        Assert.Equal(6, requests.Count);
        Assert.Equal(["Step1", "Step2"], requests.Where(r => r.DataId == "D0002").Select(r => r.Step).ToArray());
        Assert.Contains("\"id\":\"D0002\"", requests.First(r => r.DataId == "D0002").RequestBody);
        Assert.All(requests, r => Assert.Equal("[omitted]", r.ResponseBody));                  // the "save response bodies" choice applies
        Assert.All(requests, r => Assert.Equal("POST", r.Verb));
        var lite = store.LoadRequests("withdetail", withBodies: false);
        Assert.All(lite, r => { Assert.Equal(string.Empty, r.RequestBody); Assert.Equal("http://x/orders", r.TargetUrl); });   // a list needs no bodies
        var assertions = store.LoadAssertions("withdetail");
        Assert.Equal(6, assertions.Count);
        var failed = assertions.Single(a => a.Result == "FAIL");
        Assert.Equal(("D0002", "Step2", "ok", "true", "false", "Values differ"), (failed.DataId, failed.Step, failed.Path, failed.Expected, failed.Actual, failed.Message));
        Assert.Equal(requests.First(r => r.DataId == "D0002" && r.Step == "Step2").Seq, failed.RequestSeq);

        var info = store.ListRuns(0, 10).Single();
        Assert.Equal((6, 5, 1, "failed"), (info.Requests, info.Passed, info.Failed, info.Result));
        Assert.Equal(32, info.P95Ms);                                                          // nearest rank of 6 durations (11, 12, 21, 22, 31, 32): the 95th percentile is the largest
        // saving the same run again replaces it, nothing is doubled
        HistoryStores.SaveToDatabase(config, run, MakeState(run, "A"), null);
        Assert.Equal(6, store.LoadRequests("withdetail").Count);
        Assert.Equal(6, store.LoadAssertions("withdetail").Count);
    }

    [Fact]
    public void A_body_is_kept_with_both_save_choices_on_and_many_large_bodies_take_much_less_room_than_their_text()
    {
        var folder = Path.Combine(_root, "db2");
        var start = DateTimeOffset.Now;
        var run = new RunResult { RunId = "sizes", StartedAt = start, EndedAt = start.AddMinutes(1) };
        var json = string.Concat(Enumerable.Range(0, 60).Select(i => $"{{\"id\":{i},\"name\":\"customer {i}\",\"status\":\"active\"}},"));
        for (var n = 1; n <= 150; n++)
        {
            var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = $"D{n:0000}", Iteration = 1, Environment = "qa", StartedAt = start, EndedAt = start.AddSeconds(1) };
            testcase.Requests.Add(new RequestResult { Testcase = "Order", TestcaseIndex = 1, DataId = testcase.DataId, StepName = "Get", Iteration = 1, Environment = "qa", StartedAt = start, HttpStatus = 200, ResponseBody = "[" + json + "{\"n\":" + n + "}]" });
            run.Testcases.Add(testcase);
        }
        var config = new AppConfig { HistoryFolder = folder, HistoryStorage = HistoryStorage.Database, SaveRequestBodyToHistory = true, SaveResponseBodyToHistory = true };
        HistoryStores.SaveToDatabase(config, run, null, null);
        var store = new SqliteHistoryStore(folder);
        Assert.Equal(run.Testcases[7].Requests[0].ResponseBody, store.LoadRequests("sizes").Single(r => r.DataId == "D0008").ResponseBody);   // round trip, unchanged
        var raw = run.Testcases.Sum(t => (long)t.Requests[0].ResponseBody.Length);
        var onDisk = Directory.GetFiles(folder, "history.sqlite*").Sum(f => new FileInfo(f).Length);
        _output.WriteLine($"{raw:N0} characters of responses -> {onDisk:N0} bytes on disk");
        Assert.True(onDisk < raw / 2, $"{onDisk} bytes on disk for {raw} characters of responses");
    }

    [Fact]
    public void Imported_workbooks_bring_their_requests_bodies_and_assertions_too()
    {
        var folder = Path.Combine(_root, "oldfiles");
        var run = RunWithDetails("oldrun", new DateTimeOffset(2026, 4, 3, 8, 0, 0, TimeSpan.Zero));
        var config = new AppConfig { HistoryFolder = folder, SaveRequestBodyToHistory = true, SaveResponseBodyToHistory = true };
        var writer = new HistoryWriter(config) { RunState = MakeState(run, "O") };
        writer.WritePerTestcase(run);                                                           // three per-row workbooks of the same run

        var store = new SqliteHistoryStore(Path.Combine(_root, "db3"));
        var result = store.ImportWorkbooks(folder, null, null);
        Assert.Empty(result.Problems);
        Assert.Equal(1, result.RunsAdded);
        var expected = new HistoryWriter(config).BuildDetails(run);
        var imported = store.LoadRequests("oldrun");
        Assert.Equal(expected.Requests.Count, imported.Count);
        foreach (var e in expected.Requests)
        {
            var i = imported.Single(x => x.RequestId == e.RequestId);
            Assert.Equal((e.DataId, e.Step, e.Result, e.HttpStatus, e.DurationMs, e.Verb, e.TargetUrl, e.RequestBody, e.ResponseBody, e.RequestHeaders, e.ResponseHeaders),
                         (i.DataId, i.Step, i.Result, i.HttpStatus, i.DurationMs, i.Verb, i.TargetUrl, i.RequestBody, i.ResponseBody, i.RequestHeaders, i.ResponseHeaders));
            Assert.Equal("qa", i.Environment);
        }
        var assertions = store.LoadAssertions("oldrun");
        Assert.Equal(expected.Assertions.Count, assertions.Count);
        Assert.Equal(1, assertions.Count(a => a.Result == "FAIL"));
        Assert.All(assertions, a => Assert.Contains(imported, r => r.Seq == a.RequestSeq && r.DataId == a.DataId && r.Step == a.Step));   // every assertion still points at its own request
    }

    [Fact]
    public void The_compare_check_reports_a_difference()
    {
        var folder = Path.Combine(_root, "files");
        var run = MakeRun("old0", DateTimeOffset.Now, 4, n => n == 2);
        WriteWorkbook(folder, run, MakeState(run, "T"));
        var store = new SqliteHistoryStore(Path.Combine(_root, "db"));
        store.ImportWorkbooks(folder, null, null);
        Assert.Empty(store.CompareWithWorkbooks(folder));
        var changed = MakeRun("old0", run.StartedAt, 4, n => n == 3);   // the database now holds another outcome for the same run
        store.SaveRun(changed, MakeState(changed, "X"), null);
        Assert.NotEmpty(store.CompareWithWorkbooks(folder));
    }

    [Fact]
    public async Task Measure_saving_big_runs_one_instance_and_six_instances_at_once()
    {
        const int rows = 5000;
        var run = MakeRun("big", DateTimeOffset.Now, rows, n => n % 10 == 0, steps: 5);
        var state = MakeState(run, "A");

        var watch = Stopwatch.StartNew();
        new SqliteHistoryStore(Path.Combine(_root, "one")).SaveRun(run, state, null);
        var oneDatabase = watch.Elapsed;

        watch.Restart();
        using (var book = new XLWorkbook()) { HistoryRunStateStore.WriteSheet(book, state, run); book.SaveAs(Path.Combine(_root, "one.xlsx")); }
        var oneWorkbook = watch.Elapsed;

        watch.Restart();
        await Task.WhenAll(Enumerable.Range(1, 6).Select(i => Task.Run(() => new SqliteHistoryStore(Path.Combine(_root, "inst" + i)).SaveRun(MakeRun("r" + i, DateTimeOffset.Now, rows, n => n % 10 == 0, steps: 5), state, null))));
        var sixDatabases = watch.Elapsed;

        watch.Restart();
        await Task.WhenAll(Enumerable.Range(1, 6).Select(i => Task.Run(() => new SqliteHistoryStore(Path.Combine(_root, "same")).SaveRun(MakeRun("s" + i, DateTimeOffset.Now, rows, n => n % 10 == 0, steps: 5), state, null))));
        var sixIntoOne = watch.Elapsed;

        watch.Restart();
        var rowsRead = new SqliteHistoryStore(Path.Combine(_root, "same")).ScanPage(0, 100).Rows.Count;
        var read = watch.Elapsed;
        watch.Restart();
        var oldData = new SqliteHistoryStore(Path.Combine(_root, "same")).OpenRunState(null).Load()!;
        var readState = watch.Elapsed;

        _output.WriteLine($"{rows} rows x 5 steps, with re-run memory: database {oneDatabase.TotalMilliseconds:0} ms, RunState workbook sheet {oneWorkbook.TotalMilliseconds:0} ms");
        _output.WriteLine($"6 instances saving at once, 6 files: {sixDatabases.TotalMilliseconds:0} ms; 6 runs into ONE file at once: {sixIntoOne.TotalMilliseconds:0} ms");
        _output.WriteLine($"read run list ({rowsRead} rows): {read.TotalMilliseconds:0} ms; read the old data of {rows} rows: {readState.TotalMilliseconds:0} ms");
        Assert.Equal(rows, oldData.Rows.Count);
        Assert.True(sixIntoOne < TimeSpan.FromSeconds(60), $"six runs into one file took {sixIntoOne}");
    }
}

public sealed class HistoryStorageSettingTests
{
    [Fact]
    public void The_storage_is_read_from_the_settings_file_for_the_whole_app_and_for_one_instance()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"sqlset-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "appsettings.json"), "{ \"HistoryStorage\": \"Database\", \"Instances\": { \"Demo\": { \"HistoryStorage\": \"Both\" }, \"Other\": { } } }");
            var config = new AppConfig();
            new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json")).Build().Bind(config);
            Assert.Equal(HistoryStorage.Database, config.HistoryStorage);
            Assert.Equal(HistoryStorage.Both, config.Instances["Demo"].HistoryStorage);
            Assert.Null(config.Instances["Other"].HistoryStorage);                      // not set: the app-wide choice applies
            Assert.Equal(HistoryStorage.Files, new AppConfig().HistoryStorage);         // the default changes nothing for existing instances
        }
        finally { Directory.Delete(folder, true); }
    }
}
