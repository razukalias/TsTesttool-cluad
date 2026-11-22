using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using LoadTestingTool.MockServer;
using Xunit;

namespace WorkbookEditTests;

/// <summary>A step marked async starts in the background and the next step carries on; the row ends when all of them ended.</summary>
public class AsyncStepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"async-{Guid.NewGuid():N}");
    private MockServerHost? _host;

    public AsyncStepTests() { Directory.CreateDirectory(Path.Combine(_root, "Templates")); File.WriteAllText(Path.Combine(_root, "Templates", "empty.json"), "{}"); }
    public void Dispose() { _host?.Dispose(); try { Directory.Delete(_root, true); } catch (IOException) { } }

    private sealed record S(int Case, string Name, string Path, bool Async = false, string Wait = "", string Config = "", string Expected = "", string Type = "HTTP", string Parent = "");

    private async Task<RunResult> RunAsync(params S[] steps)
    {
        var port = MockServerHost.FreePort();
        var server = new MockServerConfig { Name = "t", Port = port, Routes =
        [
            new MockRoute { Method = "GET", Path = "/slow", Response = new MockResponse { Body = "{\"id\":\"A1\"}", DelayMs = 1500 } },
            new MockRoute { Method = "GET", Path = "/fast", Response = new MockResponse { Body = "{\"ok\":true}" } },
            new MockRoute { Method = "GET", Path = "/slowfail", Response = new MockResponse { Status = 500, Body = "{}", DelayMs = 300 } },
            new MockRoute { Method = "GET", Path = "/echo/{id}", Response = new MockResponse { Body = "{\"echo\":\"{{path.id}}\"}" } },
        ] };
        _host = new MockServerHost(server); _host.Start();
        var path = Path.Combine(_root, "w.xlsx");
        using (var book = new XLWorkbook())
        {
            string[] header = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb", "contenttype", "expectedstatus", "stoponfailure", "enabled", "async", "wait", "stepconfig", "timeout", "stage", "parent"];
            var config = book.AddWorksheet("config");
            for (var c = 0; c < header.Length; c++) config.Cell(1, c + 1).Value = header[c];
            var row = 2;
            foreach (var s in steps)
            {
                object[] values = [s.Case, "case" + s.Case, s.Name, s.Type, "empty.json", $"http://127.0.0.1:{port}{s.Path}", "GET", "json", s.Expected, true, true, s.Async, s.Wait, s.Config, 30, "Test", s.Parent];
                for (var c = 0; c < values.Length; c++) config.Cell(row, c + 1).Value = XLCellValue.FromObject(values[c]);
                row++;
            }
            var request = book.AddWorksheet("request"); var response = book.AddWorksheet("response");
            foreach (var sheet in new[] { request, response }) { sheet.Cell(1, 1).Value = "testcaseindex"; sheet.Cell(1, 2).Value = "testcase"; sheet.Cell(1, 3).Value = "dataid"; }
            var r = 2;
            foreach (var index in steps.Select(x => x.Case).Distinct())
            {
                foreach (var sheet in new[] { request, response }) { sheet.Cell(r, 1).Value = index; sheet.Cell(r, 2).Value = "case" + index; sheet.Cell(r, 3).Value = "R1"; }
                r++;
            }
            book.SaveAs(path);
        }
        var model = new WorkbookReader().Read(path);
        var runner = new TestRunner(new HttpClient(), new AssertionEngine(), Path.Combine(_root, "Templates"), 30, features: new FeatureConfig { WorkspaceRoot = _root });
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        foreach (var testcase in model.Testcases) await runner.RunAsync(testcase, model, run, CancellationToken.None, executionMode: "sequential");
        return run;
    }

    private static RequestResult Step(RunResult run, int testcase, string name) => run.Testcases.First(t => t.TestcaseIndex == testcase).Requests.Single(r => r.StepName == name);

    [Fact]
    public async Task The_next_step_starts_while_the_async_step_still_runs_and_the_row_waits_for_it()
    {
        var run = await RunAsync(new S(1, "Slow", "/slow", Async: true), new S(1, "Next", "/fast"), new S(1, "Last", "/fast"));
        var slow = Step(run, 1, "Slow"); var next = Step(run, 1, "Next"); var last = Step(run, 1, "Last");
        Assert.All(new[] { slow, next, last }, r => Assert.Equal("PASS", r.Result));
        Assert.True(next.StartedAt < slow.EndedAt, "the next step must start before the async step is done");
        Assert.True(last.EndedAt < slow.EndedAt, "the steps after it finish first");
        Assert.True(run.Testcases[0].EndedAt >= slow.EndedAt, "the row ends only when the async step has ended");
    }

    [Fact]
    public async Task A_later_step_without_a_wait_does_not_see_the_value_of_an_async_step()
    {
        var run = await RunAsync(new S(2, "Slow", "/slow", Async: true, Config: "{\"outputs\":\"tok=id\"}"), new S(2, "Use", "/echo/_tok_"));
        var use = Step(run, 2, "Use");
        Assert.NotEqual("PASS", use.Result);
        Assert.Contains("tok", use.ErrorMessage + string.Join(" ", use.Assertions.Select(a => a.FailureMessage)));
    }

    [Fact]
    public async Task A_wait_on_the_later_step_gives_the_async_step_time_to_hand_over_its_value()
    {
        var run = await RunAsync(new S(3, "Slow", "/slow", Async: true, Config: "{\"outputs\":\"tok=id\"}"), new S(3, "Use", "/echo/_tok_", Wait: "2600"));
        Assert.Equal("PASS", Step(run, 3, "Use").Result);
        Assert.Contains("A1", Step(run, 3, "Use").TargetUrl);
    }

    [Fact]
    public async Task A_failing_async_step_with_stop_on_failure_stops_the_steps_that_have_not_started()
    {
        var run = await RunAsync(new S(4, "Bad", "/slowfail", Async: true, Expected: "200"), new S(4, "Middle", "/slow"), new S(4, "Never", "/fast"));
        var requests = run.Testcases.Single().Requests;
        Assert.Equal("FAIL", Step(run, 4, "Bad").Result);
        Assert.DoesNotContain(requests, r => r.StepName == "Never");   // started after the async step had failed
        Assert.Contains(requests, r => r.StepName == "Middle");        // was already running: not undone
    }

    [Fact]
    public async Task A_loop_marked_async_runs_with_its_steps_in_the_background()
    {
        var run = await RunAsync(new S(6, "Loop", "/fast", Async: true, Type: "For", Config: "{\"var\":\"i\",\"from\":1,\"to\":2}"), new S(6, "Inner", "/slow", Parent: "Loop"), new S(6, "After", "/fast"));
        var requests = run.Testcases.Single().Requests;
        Assert.Equal(2, requests.Count(r => r.StepName == "Inner"));          // both rounds ran before the row ended
        var after = requests.Single(r => r.StepName == "After");
        Assert.True(after.EndedAt < requests.Where(r => r.StepName == "Inner").Min(r => r.EndedAt), "the step after the loop did not wait for the first round");
        Assert.All(requests, r => Assert.Equal("PASS", r.Result));
    }

    [Fact]
    public async Task Without_async_the_steps_run_one_after_the_other_as_before()
    {
        var run = await RunAsync(new S(5, "Slow", "/slow"), new S(5, "Next", "/fast"));
        Assert.True(Step(run, 5, "Next").StartedAt >= Step(run, 5, "Slow").EndedAt);
    }
}
