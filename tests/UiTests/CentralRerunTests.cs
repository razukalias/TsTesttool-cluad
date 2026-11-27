using System.Text.Json;
using ClosedXML.Excel;
using LoadTestingTool.UI;
using LoadTestingTool.UI.CentralRerun;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class CentralRerunTests
{
    internal static string RunId(int n) => $"20261124_20{n:00}00_abcde{n:0000}";

    internal sealed record Ev(int Testcase, string TestcaseName, string Id, string Env, bool Failed, string Step = "Create", string Path = "col2", string Expected = "a", string Actual = "b");

    /// <summary>Writes one run the way the runner does: a file per environment.</summary>
    internal static void WriteRun(string results, int number, IEnumerable<Ev> events)
    {
        var runId = RunId(number);
        var folder = Path.Combine(results, "failed", "Run_" + number, "log");
        Directory.CreateDirectory(folder);
        foreach (var group in events.GroupBy(e => (e.Env, e.TestcaseName)))
        {
            var lines = new List<string>();
            foreach (var e in group)
            {
                lines.Add(JsonSerializer.Serialize(new { eventType = "request-completed", runId, testcaseIndex = e.Testcase, testcase = e.TestcaseName, dataId = e.Id, stepName = e.Step, result = e.Failed ? "FAIL" : "PASS", httpStatus = 200, durationMs = 10 + number, errorMessage = "", failureCategory = e.Failed ? "Assertion" : "", environment = e.Env }));
                lines.Add(JsonSerializer.Serialize(new { eventType = "assertion-completed", runId, testcaseIndex = e.Testcase, testcase = e.TestcaseName, dataId = e.Id, stepName = e.Step, responsePath = e.Path, assertionVerb = "eq", expectedValue = e.Expected, actualValue = e.Failed ? e.Actual : e.Expected, result = e.Failed ? "FAIL" : "PASS", failureMessage = e.Failed ? "Values differ" : "", environment = e.Env }));
            }
            var file = Path.Combine(folder, $"events_{runId}_{group.Key.Env}_{group.Key.TestcaseName}.ndjson");
            File.WriteAllLines(file, lines);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(number));
        }
    }

    internal static IReadOnlyList<TestcaseOption> Catalog(params (int Index, string Name)[] testcases)
        => testcases.Select(t => new TestcaseOption(t.Index, t.Name, [new TestStepOption("Create", "HTTP", true, []), new TestStepOption("Verify", "HTTP", true, [])])).ToList();

    private static CentralInstance Instance(string name, string results, params (int, string)[] testcases) => new(name, results, Catalog(testcases), (_, id) => id);

    [Fact]
    public void Failed_pairs_of_every_instance_come_from_the_merged_status_and_carry_the_reason()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "A", "Results"); var b = Path.Combine(dir.Path, "B", "Results");
        string[] envs = ["dev", "prod"];
        IEnumerable<Ev> All(string tc, int idx, Func<int, string, bool> fails) => Enumerable.Range(1, 4).SelectMany(n => envs.Select(e => new Ev(idx, tc, $"ROW{n:000}", e, fails(n, e))));
        WriteRun(a, 1, All("Orders", 1, (n, e) => n == 2 && e == "dev" || n == 3));
        WriteRun(b, 1, All("Users", 1, (n, e) => n == 1 && e == "prod"));
        // a re-run of ROW003 in prod on A passed: that pair is no longer failed, the other one still is (newest result wins)
        WriteRun(a, 2, [new Ev(1, "Orders", "ROW003", "prod", false)]);

        var pairsA = CentralRerunSource.Collect(Instance("A", a, (1, "Orders")));
        Assert.Equal(["ROW002|dev", "ROW003|dev"], pairsA.Select(p => $"{p.DataId}|{p.Environment}"));
        Assert.All(pairsA, p => { Assert.Equal("FAIL", p.Kind); Assert.Contains("col2 expected a, got b", p.Reason); Assert.Equal(["Create"], p.FailedSteps); });
        Assert.All(pairsA, p => Assert.Equal(RunId(1), p.FromRunId));   // both failures come from run 1: the re-run (run 2) only ran ROW003 in prod

        var pairsB = CentralRerunSource.Collect(Instance("B", b, (1, "Users")));
        Assert.Equal(["ROW001|prod"], pairsB.Select(p => $"{p.DataId}|{p.Environment}"));
    }

    [Fact]
    public void A_testcase_that_is_gone_from_the_workbook_is_left_out()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "A", "Results");
        WriteRun(a, 1, [new Ev(1, "Old", "ROW001", "dev", true), new Ev(2, "Kept", "ROW001", "dev", true)]);
        var pairs = CentralRerunSource.Collect(Instance("A", a, (1, "Kept")));   // "Kept" is testcase 1 now, "Old" is gone
        Assert.Single(pairs);
        Assert.Equal(("Kept", 1), (pairs[0].Testcase, pairs[0].TestcaseIndex));
    }

    private static FailedPair Pair(int tc, string id, string env, string instance = "A") => new(instance, tc, "T" + tc, id, env, "FAIL", "x", [], ["Create"], null);

    [Fact]
    public void One_request_runs_exactly_the_ticked_pairs_and_it_is_split_when_one_request_would_run_more()
    {
        var catalog = Catalog((1, "T1"), (2, "T2"));
        var none = new Dictionary<int, IReadOnlyList<string>>();

        // two testcases, the same rows and environments: one request is exact
        var same = new[] { Pair(1, "R1", "dev"), Pair(2, "R1", "dev"), Pair(1, "R2", "prod"), Pair(2, "R2", "prod") };
        var one = CentralRerunPlanner.Plan(same, none, catalog);
        Assert.Single(one);
        Assert.Equal(same.Select(p => (p.TestcaseIndex, p.Environment.ToUpperInvariant(), p.DataId)).ToHashSet(), CentralRerunPlanner.Expand(one[0]));

        // the same data id fails in dev in testcase 1 but in prod in testcase 2: one request would also run the other two combinations -> one run per testcase
        var mixed = new[] { Pair(1, "R1", "dev"), Pair(2, "R1", "prod") };
        var split = CentralRerunPlanner.Plan(mixed, none, catalog);
        Assert.Equal(2, split.Count);
        Assert.All(split, r => Assert.Single(r.Testcases));
        Assert.Equal(mixed.Select(p => (p.TestcaseIndex, p.Environment.ToUpperInvariant(), p.DataId)).ToHashSet(), split.SelectMany(r => CentralRerunPlanner.Expand(r)).ToHashSet());
    }

    [Fact]
    public void Whatever_is_ticked_the_planned_runs_run_exactly_that_and_nothing_else()
    {
        var catalog = Catalog((1, "T1"), (2, "T2"), (3, "T3"));
        var random = new Random(7);
        string[] envs = ["dev", "test", "prod", ""];
        for (var round = 0; round < 300; round++)
        {
            var pairs = new List<FailedPair>();
            foreach (var tc in new[] { 1, 2, 3 }) foreach (var env in envs) for (var n = 1; n <= 4; n++) if (random.Next(5) == 0) pairs.Add(Pair(tc, $"R{n}", env));
            if (pairs.Count == 0) continue;
            var requests = CentralRerunPlanner.Plan(pairs, new Dictionary<int, IReadOnlyList<string>>(), catalog);
            var expected = pairs.Select(p => (p.TestcaseIndex, p.Environment.ToUpperInvariant(), p.DataId.ToUpperInvariant())).ToHashSet();
            var run = requests.SelectMany(r => CentralRerunPlanner.Expand(r)).ToHashSet();
            Assert.True(expected.SetEquals(run), $"round {round}: ticked {expected.Count}, planned runs {run.Count}");
        }
    }

    [Fact]
    public void Steps_are_chosen_per_testcase_and_all_steps_means_no_step_filter()
    {
        var catalog = Catalog((1, "T1"), (2, "T2"));
        var pairs = new[] { Pair(1, "R1", "dev"), Pair(2, "R1", "dev") };
        Assert.Null(CentralRerunPlanner.Plan(pairs, new Dictionary<int, IReadOnlyList<string>>(), catalog)[0].Steps);   // nothing restricted
        var only = CentralRerunPlanner.Plan(pairs, new Dictionary<int, IReadOnlyList<string>> { [1] = ["Create"], [2] = ["Create", "Verify"] }, catalog)[0];
        Assert.Equal(["Create"], only.Steps![1]);
        Assert.Equal(["Create", "Verify"], only.Steps[2]);   // every testcase of the run is listed
    }

    private static IReadOnlyList<TestcaseOption> ThreeSteps(params int[] testcases)
        => testcases.Select(i => new TestcaseOption(i, "T" + i, [new TestStepOption("Step1", "HTTP", true, []), new TestStepOption("Step2", "HTTP", true, []), new TestStepOption("Step3", "HTTP", true, [])])).ToList();

    [Fact]
    public void A_row_by_default_skips_the_steps_before_the_failure_reruns_the_failed_step_with_old_data_and_the_ones_after_with_fresh_data()
    {
        string[] steps = ["Step1", "Step2", "Step3"];
        FailedPair Failed(params string[] failedSteps) => new("A", 1, "T1", "R1", "dev", "FAIL", "x", [], failedSteps, null);
        Assert.Equal([new RowStep("Step1", false, false), new RowStep("Step2", true, true), new RowStep("Step3", true, false)], RowStepDefaults.For(Failed("Step2"), steps));
        Assert.Equal([new RowStep("Step1", true, true), new RowStep("Step2", true, false), new RowStep("Step3", true, false)], RowStepDefaults.For(Failed("Step1"), steps));
        // two failed steps: both take old data, the steps from the first failure on run
        Assert.Equal([new RowStep("Step1", false, false), new RowStep("Step2", true, true), new RowStep("Step3", true, true)], RowStepDefaults.For(Failed("Step2", "Step3"), steps));
        // no failed step known (an error before any step): every step runs, with fresh data
        Assert.All(RowStepDefaults.For(Failed(), steps), s => { Assert.True(s.Run); Assert.False(s.OldData); });
    }

    [Fact]
    public void Rows_with_the_same_step_choice_run_together_and_the_old_data_steps_go_to_the_runner()
    {
        var catalog = ThreeSteps(1, 2);
        string[] steps = ["Step1", "Step2", "Step3"];
        FailedPair P(int tc, string id, string env, params string[] failed) => new("A", tc, "T" + tc, id, env, "FAIL", "x", [], failed, null);
        (FailedPair, IReadOnlyList<RowStep>) Row(FailedPair pair) => (pair, RowStepDefaults.For(pair, steps));

        // all rows fail at Step2: one request; Step1 does not run, Step2 reuses the old data (and so Step1's values), Step3 is fresh
        var same = new[] { Row(P(1, "R1", "dev", "Step2")), Row(P(1, "R2", "dev", "Step2")), Row(P(2, "R1", "dev", "Step2")), Row(P(2, "R2", "dev", "Step2")) };
        var one = Assert.Single(CentralRerunPlanner.PlanRows(same, catalog));
        Assert.Equal(["Step2", "Step3"], one.Steps![1]); Assert.Equal(["Step2", "Step3"], one.Steps[2]);
        Assert.Equal(["Step2"], one.RerunSteps![1]); Assert.Equal(["Step2"], one.RerunSteps[2]);

        // rows that fail at different steps are separate runs
        var mixed = new[] { Row(P(1, "R1", "dev", "Step1")), Row(P(1, "R2", "dev", "Step3")) };
        var split = CentralRerunPlanner.PlanRows(mixed, catalog);
        Assert.Equal(2, split.Count);
        Assert.Null(split.Single(r => r.RowsByTestcase![1].Contains("R1")).Steps);                       // Step1 fails: every step runs
        Assert.Equal(["Step3"], split.Single(r => r.RowsByTestcase![1].Contains("R2")).Steps![1]);       // Step3 fails: only Step3 runs
        Assert.Equal(["Step3"], split.Single(r => r.RowsByTestcase![1].Contains("R2")).RerunSteps![1]);

        // a row with no step ticked is not planned
        var none = new (FailedPair, IReadOnlyList<RowStep>)[] { (P(1, "R9", "dev"), steps.Select(n => new RowStep(n, false, false)).ToList()) };
        Assert.Empty(CentralRerunPlanner.PlanRows(none, catalog));
        // nothing old: no re-run steps at all
        var fresh = new (FailedPair, IReadOnlyList<RowStep>)[] { (P(1, "R1", "dev"), steps.Select(n => new RowStep(n, true, false)).ToList()) };
        var freshRequest = Assert.Single(CentralRerunPlanner.PlanRows(fresh, catalog));
        Assert.Null(freshRequest.RerunSteps); Assert.Null(freshRequest.Steps);
    }

    [Fact]
    public void Whatever_rows_and_steps_are_chosen_the_planned_runs_run_exactly_the_ticked_rows_with_their_own_steps()
    {
        var catalog = ThreeSteps(1, 2, 3);
        string[] steps = ["Step1", "Step2", "Step3"];
        var random = new Random(11);
        string[] envs = ["dev", "test", "prod", ""];
        for (var round = 0; round < 300; round++)
        {
            var rows = new List<(FailedPair, IReadOnlyList<RowStep>)>();
            foreach (var tc in new[] { 1, 2, 3 }) foreach (var env in envs) for (var n = 1; n <= 4; n++)
                if (random.Next(5) == 0)
                {
                    var pair = new FailedPair("A", tc, "T" + tc, $"R{n}", env, "FAIL", "x", [], [steps[random.Next(3)]], null);
                    var chosen = RowStepDefaults.For(pair, steps).Select(s => random.Next(6) == 0 ? s with { Run = !s.Run } : s).ToList();
                    rows.Add((pair, chosen));
                }
            var usable = rows.Where(r => r.Item2.Any(s => s.Run)).ToList();
            var requests = CentralRerunPlanner.PlanRows(rows, catalog);
            var expected = usable.Select(r => (r.Item1.TestcaseIndex, r.Item1.Environment.ToUpperInvariant(), r.Item1.DataId.ToUpperInvariant())).ToHashSet();
            var planned = requests.SelectMany(r => CentralRerunPlanner.Expand(r)).ToList();
            Assert.True(expected.SetEquals(planned) && planned.Count == expected.Count, $"round {round}: ticked {expected.Count}, planned runs {planned.Count}");
            // every planned run uses the steps its rows chose
            foreach (var request in requests)
                foreach (var (tc, env, id) in CentralRerunPlanner.Expand(request))
                {
                    var row = usable.Single(r => r.Item1.TestcaseIndex == tc && r.Item1.Environment.ToUpperInvariant() == env && r.Item1.DataId.ToUpperInvariant() == id);
                    var wantedRun = row.Item2.Where(s => s.Run).Select(s => s.Name).ToList();
                    var ranSteps = request.Steps is { } byTestcase ? byTestcase[tc] : steps.ToList();
                    Assert.Equal(wantedRun, ranSteps);
                    Assert.Equal(row.Item2.Where(s => s.Run && s.OldData).Select(s => s.Name).ToList(), request.RerunSteps is { } old && old.TryGetValue(tc, out var names) ? names : []);
                }
        }
    }

    internal sealed class FakeHost : ICentralRerunHost
    {
        public required IReadOnlyList<CentralInstance> Instances { get; init; }
        public string ReportsFolder { get; init; } = string.Empty;
        public Func<string, RunRequest, Task<string?>> OnStart { get; init; } = (_, _) => Task.FromResult<string?>(null);
        public List<(string Instance, RunRequest Request)> Started { get; } = [];
        public List<string> Opened { get; } = [];
        public Task<string?> StartAsync(string instance, RunRequest request) { lock (Started) Started.Add((instance, request)); return OnStart(instance, request); }
        public Task WaitUntilFinishedAsync(string instance, CancellationToken cancellation) => Task.Delay(5, cancellation);
        public void Open(string path) => Opened.Add(path);
    }

    [Fact]
    public async Task A_batch_runs_every_instance_and_tells_what_is_now_passed_still_failed_or_not_run()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "A", "Results"); var b = Path.Combine(dir.Path, "B", "Results");
        string[] envs = ["dev", "prod"];
        IEnumerable<Ev> All(string tc, Func<int, string, bool> fails) => Enumerable.Range(1, 4).SelectMany(n => envs.Select(e => new Ev(1, tc, $"ROW{n:000}", e, fails(n, e))));
        WriteRun(a, 1, All("Orders", (n, e) => n <= 2));       // ROW001, ROW002 fail in both environments
        WriteRun(b, 1, All("Users", (n, e) => n == 3 && e == "prod"));
        var instances = new[] { Instance("A", a, (1, "Orders")), Instance("B", b, (1, "Users")) };
        var before = instances.SelectMany(i => CentralRerunSource.Collect(i)).ToList();
        Assert.Equal(5, before.Count);

        var run = 2;
        var host = new FakeHost
        {
            Instances = instances,
            ReportsFolder = Path.Combine(dir.Path, "Reports"),
            OnStart = (instance, request) =>
            {
                // the "runner": writes only what was asked for; ROW001 in prod keeps failing, everything else passes now
                var (results, name) = instance == "A" ? (a, "Orders") : (b, "Users");
                var asked = CentralRerunPlanner.Expand(request).Select(t => new Ev(1, name, t.DataId, t.Environment.ToLowerInvariant(), instance == "A" && t.DataId == "ROW001" && t.Environment == "PROD", Actual: "boom")).ToList();
                WriteRun(results, Interlocked.Increment(ref run), asked);
                return Task.FromResult<string?>(null);
            }
        };
        // tick: everything of A except ROW002 in dev (left out), and B's pair
        var ticked = before.Where(p => !(p.Instance == "A" && p.DataId == "ROW002" && p.Environment == "dev")).ToList();
        var result = await CentralRerunBatch.RunAsync(host, ticked, new Dictionary<(string, int), IReadOnlyList<string>> { [("A", 1)] = ["Create"], [("B", 1)] = ["Create"] });

        Assert.Equal(2, host.Started.Count);                       // one runner per instance
        Assert.Equal(["A", "B"], host.Started.Select(s => s.Instance).Order());
        Assert.Equal(ticked.Count, result.Rows.Count);
        string After(string instance, string id, string env) => result.Rows.Single(r => r.Before.Instance == instance && r.Before.DataId == id && r.Before.Environment == env).After;
        Assert.Equal(BatchRow.StillFailed, After("A", "ROW001", "prod"));
        Assert.Equal(BatchRow.NowPassed, After("A", "ROW001", "dev"));
        Assert.Equal(BatchRow.NowPassed, After("A", "ROW002", "prod"));
        Assert.Equal(BatchRow.NowPassed, After("B", "ROW003", "prod"));
        var still = result.Rows.Single(r => r.IsStillFailed);
        Assert.Equal("boom", still.AfterFailures.Single().Actual);
        // the left-out pair is not in the batch and still failed in the newest merged status
        Assert.Contains(CentralRerunSource.Collect(instances[0]), p => p.DataId == "ROW002" && p.Environment == "dev");
    }

    [Fact]
    public async Task A_row_the_runner_did_not_run_is_reported_as_not_run_and_a_start_error_is_reported()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "A", "Results");
        WriteRun(a, 1, [new Ev(1, "Orders", "ROW001", "dev", true), new Ev(1, "Orders", "ROW002", "dev", true)]);
        var instances = new[] { Instance("A", a, (1, "Orders")) };
        var before = CentralRerunSource.Collect(instances[0]);

        var skipping = new FakeHost { Instances = instances, OnStart = (_, _) => { WriteRun(a, 2, [new Ev(1, "Orders", "ROW001", "dev", false)]); return Task.FromResult<string?>(null); } };
        var result = await CentralRerunBatch.RunAsync(skipping, before, new Dictionary<(string, int), IReadOnlyList<string>>());
        Assert.Equal(BatchRow.NowPassed, result.Rows.Single(r => r.Before.DataId == "ROW001").After);
        Assert.Equal(BatchRow.NotRun, result.Rows.Single(r => r.Before.DataId == "ROW002").After);   // the runner skipped it: its newest result is still the old one

        var refusing = new FakeHost { Instances = instances, OnStart = (_, _) => Task.FromResult<string?>("A is already running; stop it first.") };
        var refused = await CentralRerunBatch.RunAsync(refusing, before, new Dictionary<(string, int), IReadOnlyList<string>>());
        Assert.All(refused.Rows, r => Assert.Equal(BatchRow.NotRun, r.After));
        Assert.Contains("already running", refused.Problems["A"]);
    }

    [Fact]
    public void The_report_has_an_html_page_and_an_excel_workbook_with_the_reason_of_every_failing_cell()
    {
        using var dir = new TempDir();
        var failure = new FailureDetail("Create", "data.<name>", "eq", "a & b", "=cmd|' /C calc'!A0", "Values differ", "dev");
        var still = new BatchRow(new FailedPair("Demo <1>", 1, "Orders", "ROW001", "dev", "FAIL", "x", [failure], ["Create"], null), BatchRow.StillFailed, [failure], 321);
        var passed = new BatchRow(new FailedPair("Demo <1>", 1, "Orders", "ROW002", "", "FAIL", "x", [], ["Create"], null), BatchRow.NowPassed, [], 12);
        var notRun = new BatchRow(new FailedPair("Other", 2, "Users", "ROW009", "prod", "ERROR", "x", [], [], null), BatchRow.NotRun, [], 0);
        var batch = new BatchResult("20261126_220000", new DateTime(2026, 11, 26, 22, 0, 0), new DateTime(2026, 11, 26, 22, 1, 5), [still, passed, notRun],
            new Dictionary<string, string> { ["Other"] = "Other is already running." }, new Dictionary<string, IReadOnlyList<string>> { ["Demo <1> › 1"] = ["Create"] });

        var (html, excel) = CentralRerunReport.Write(Path.Combine(dir.Path, "Reports"), batch);
        Assert.True(File.Exists(html)); Assert.True(File.Exists(excel));
        var page = File.ReadAllText(html);
        Assert.Contains("Demo &lt;1&gt;", page);                    // names are escaped
        Assert.Contains("data.&lt;name&gt;", page);
        Assert.Contains("a &amp; b", page);
        Assert.Contains("Still failing", page);
        Assert.Contains("Other is already running.", page);
        Assert.DoesNotContain("<name>", page);

        using var book = new XLWorkbook(excel);
        Assert.Equal(["Summary", "Rows", "StillFailing"], book.Worksheets.Select(w => w.Name));
        var rows = book.Worksheet("Rows");
        Assert.Equal(4, rows.LastRowUsed()!.RowNumber());           // header + 3 rows
        Assert.Equal("Still failed", rows.Cell(2, 7).GetString());
        var cell = book.Worksheet("StillFailing").Cell(2, 9);
        Assert.Equal(XLDataType.Text, cell.DataType);               // a value that looks like a formula stays text
        Assert.StartsWith("=cmd", cell.GetString());
        Assert.False(cell.HasFormula);
        Assert.Equal("a & b", book.Worksheet("StillFailing").Cell(2, 8).GetString());

        var second = CentralRerunReport.Write(Path.Combine(dir.Path, "Reports"), batch);      // the same batch id again does not overwrite
        Assert.NotEqual(html, second.Html);
    }
}
