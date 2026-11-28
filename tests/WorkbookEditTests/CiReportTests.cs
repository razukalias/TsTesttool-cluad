using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using Xunit;

namespace WorkbookEditTests;

public class CiReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ci_" + Guid.NewGuid().ToString("N"));
    public CiReportTests() { Directory.CreateDirectory(_dir); Secrets.ResetForTests(); }
    public void Dispose() { Secrets.ResetForTests(); try { Directory.Delete(_dir, true); } catch { } }

    private static RunResult Run(bool failing = false, bool error = false, bool bug = false)
    {
        var run = new RunResult { RunId = "r1", StartedAt = DateTimeOffset.Now.AddSeconds(-5), EndedAt = DateTimeOffset.Now };
        var row = new TestcaseResult { RunId = "r1", TestcaseIndex = 1, Testcase = "Orders", DataId = "d1", StartedAt = run.StartedAt, Environment = "qa" };
        var ok = new RequestResult { StepName = "Create", HttpStatus = 200, ExpectedStatusMatched = true, DurationMs = 1500 };
        row.Requests.Add(ok);
        if (failing)
        {
            var step = new RequestResult { StepName = "Check", HttpStatus = 200, ExpectedStatusMatched = true };
            step.Assertions.Add(new AssertionResult { ResponsePath = "$.total", ExpectedValue = "5", ActualValue = "6", Result = "FAIL" });
            row.Requests.Add(step);
        }
        if (error) row.Requests.Add(new RequestResult { StepName = "Boom", ErrorMessage = "Connection refused <x>\nsecond line" });
        if (bug)
        {
            var step = new RequestResult { StepName = "Known", HttpStatus = 200, ExpectedStatusMatched = true };
            step.Assertions.Add(new AssertionResult { ResponsePath = "$.name", Result = "KNOWN_BUG", KnownBug = true, BugDescription = "BUG-1" });
            row.Requests.Add(step);
        }
        run.Testcases.Add(row);
        return run;
    }

    [Fact]
    public void Exit_codes_follow_the_fail_on_setting()
    {
        Assert.Equal(0, CiReports.ExitCode(Run()));
        Assert.Equal(2, CiReports.ExitCode(Run(failing: true)));
        Assert.Equal(2, CiReports.ExitCode(Run(error: true)));
        Assert.Equal(0, CiReports.ExitCode(Run(bug: true), "any"));          // a known bug passes
        Assert.Equal(2, CiReports.ExitCode(Run(bug: true), "bugs"));         // unless asked otherwise
        Assert.Equal(0, CiReports.ExitCode(Run(failing: true), "errors"));   // only runtime errors count
        Assert.Equal(2, CiReports.ExitCode(Run(error: true), "errors"));
        Assert.Equal(4, CiReports.ExitCode(Run(), timedOut: true));
        var cancelled = Run(); cancelled.Cancelled = true;
        Assert.Equal(3, CiReports.ExitCode(cancelled));
    }

    [Fact]
    public void JUnit_has_one_test_case_per_step_with_the_failure_text()
    {
        var path = Path.Combine(_dir, "junit.xml");
        CiReports.WriteJUnit(path, Run(failing: true, error: true));
        var xml = System.Xml.Linq.XDocument.Load(path);
        Assert.Equal(3, xml.Descendants("testcase").Count());
        Assert.Equal("Orders [qa]", xml.Descendants("testsuite").Single().Attribute("name")!.Value);
        Assert.Contains("expected '5' but got '6'", xml.Descendants("failure").Single().Value);
        Assert.Equal("Connection refused <x>", xml.Descendants("error").Single().Attribute("message")!.Value);
        Assert.Equal("1", xml.Root!.Attribute("failures")!.Value);
        Assert.Equal("1", xml.Root.Attribute("errors")!.Value);
    }

    [Fact]
    public void Summary_json_has_counts_result_and_exit_code()
    {
        var path = Path.Combine(_dir, "summary.json");
        CiReports.WriteSummary(path, Run(failing: true), 2, "any");
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("failed", json.RootElement.GetProperty("result").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("steps").GetProperty("failed").GetInt32());
        Assert.Equal("Check", json.RootElement.GetProperty("failures")[0].GetProperty("step").GetString());
    }

    [Fact]
    public void A_placeholder_takes_the_set_value_then_the_environment_then_the_fallback()
    {
        Secrets.SetOverrides(new Dictionary<string, string> { ["TOKEN"] = "abc123xyz" });
        Assert.Equal("Bearer abc123xyz", Secrets.Expand("Bearer ${TOKEN}"));
        Assert.Equal("fallback", Secrets.Expand("${CI_TEST_UNSET_NAME:-fallback}"));
        var ex = Assert.Throws<InvalidDataException>(() => Secrets.Expand("${CI_TEST_UNSET_NAME}", "cell B2"));
        Assert.Contains("--set CI_TEST_UNSET_NAME=", ex.Message);
        Assert.Contains("cell B2", ex.Message);
    }

    [Fact]
    public void Values_that_were_filled_in_are_masked_in_logs_and_reports()
    {
        Secrets.SetOverrides(new Dictionary<string, string> { ["TOKEN"] = "abc123xyz" });
        Secrets.Expand("${TOKEN}");
        Assert.Equal("header: ***", Secrets.Mask("header: abc123xyz"));
        Assert.Equal("header: ***", InternalLogger.MaskSensitiveData("header: abc123xyz"));
        var run = Run(error: true); run.Testcases[0].Requests[1].ErrorMessage = "401 for abc123xyz";
        var path = Path.Combine(_dir, "j.xml");
        CiReports.WriteJUnit(path, run);
        Assert.DoesNotContain("abc123xyz", File.ReadAllText(path));
    }

    [Fact]
    public void Validate_only_reports_problems_and_returns_1_for_errors()
    {
        var workbook = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Instances", "ControlFlowScenarios", "ControlFlowScenarios.xlsx");
        if (!File.Exists(workbook)) return;
        var copy = Path.Combine(_dir, "wb.xlsx"); File.Copy(workbook, copy);
        var output = new StringWriter();
        var code = CiReports.ValidateOnly(copy, Path.Combine(_dir, "v.xml"), output);
        Assert.Contains("Validation:", output.ToString());
        Assert.True(File.Exists(Path.Combine(_dir, "v.xml")));
        Assert.Equal(code == 0, !output.ToString().Contains("[Error]"));
    }
}

public class CiMaskingTests
{
    [Fact]
    public void A_secret_in_a_header_and_a_response_is_masked_in_the_published_result()
    {
        Secrets.ResetForTests();
        Secrets.SetOverrides(new Dictionary<string, string> { ["TOKEN"] = "tok-98765-secret" });
        var header = Secrets.Expand("Bearer ${TOKEN}");
        var result = new RequestResult { RequestHeaders = "Authorization: " + header, ResponseBody = "{\"echo\":\"" + header + "\"}", ErrorMessage = "failed with " + header };
        var method = typeof(TestRunner).GetMethod("MaskSecrets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, [result]);
        Assert.DoesNotContain("tok-98765-secret", result.RequestHeaders + result.ResponseBody + result.ErrorMessage);
        Assert.Contains("***", result.RequestHeaders);
        Secrets.ResetForTests();
    }
}

public class CiPathTests
{
    [Fact]
    public void Settings_folders_without_an_instance_are_relative_to_the_settings_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cip_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), "{\"ResultsFolder\":\"Out/Results\",\"HistoryFolder\":\"H\"}");
        var load = typeof(LoadTestingTool.Domain.AppConfig).Assembly.GetType("LoadTestingTool.Program")!.GetMethod("LoadConfig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var config = (LoadTestingTool.Domain.AppConfig)load.Invoke(null, [Path.Combine(dir, "appsettings.json"), null])!;
        Assert.Equal(Path.GetFullPath("Out/Results", dir), config.ResultsFolder);
        Assert.Equal(Path.GetFullPath("H", dir), config.HistoryFolder);
        Directory.Delete(dir, true);
    }
}

public class CiCompareTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cic_" + Guid.NewGuid().ToString("N"));
    public CiCompareTests() { Directory.CreateDirectory(_dir); Secrets.ResetForTests(); }
    public void Dispose() { Secrets.ResetForTests(); try { Directory.Delete(_dir, true); } catch { } }

    private static RunResult Run(string runId, params (string Step, bool Fail, long Ms)[] steps)
    {
        var run = new RunResult { RunId = runId, StartedAt = DateTimeOffset.Now.AddSeconds(-2), EndedAt = DateTimeOffset.Now };
        var row = new TestcaseResult { RunId = runId, TestcaseIndex = 1, Testcase = "Orders", DataId = "d1", Environment = "qa" };
        foreach (var (step, fail, ms) in steps)
            row.Requests.Add(new RequestResult { StepName = step, DurationMs = ms, HttpStatus = 200, ExpectedStatusMatched = true, ErrorMessage = fail ? "boom" : "" });
        run.Testcases.Add(row);
        return run;
    }

    [Fact]
    public void A_run_is_compared_with_the_previous_summary()
    {
        var before = Run("old", ("A", false, 100), ("B", true, 100), ("C", false, 100));
        var oldPath = Path.Combine(_dir, "Run_old", "summary.json");
        CiReports.WriteSummary(oldPath, before, 2, "any");
        var now = Run("new", ("A", true, 100), ("B", false, 100), ("C", false, 1200), ("D", true, 10));
        var found = RunComparison.FindPrevious(_dir, "new", null);
        Assert.Equal(oldPath, found);
        var comparison = RunComparison.Compare(now, found)!;
        Assert.Equal(["Orders [qa] d1 / A", "Orders [qa] d1 / D"], comparison.NewlyFailing);
        Assert.Equal(["Orders [qa] d1 / B"], comparison.Fixed);
        Assert.Equal(1, comparison.NewSteps);
        Assert.Single(comparison.Slower);
        Assert.Equal(2, CiReports.ExitCode(now, "new", comparison: comparison));
        Assert.Equal(0, CiReports.ExitCode(Run("same", ("B", true, 100)), "new", comparison: RunComparison.Compare(Run("same", ("B", true, 100)), found)));   // B failed before too
    }

    [Fact]
    public void Without_a_previous_run_new_means_any_failure()
    {
        Assert.Null(RunComparison.Compare(Run("x", ("A", true, 1)), null));
        Assert.Equal(2, CiReports.ExitCode(Run("x", ("A", true, 1)), "new"));
    }

    [Fact]
    public void The_text_report_lists_failures_flaky_rows_and_the_comparison_without_secrets()
    {
        Secrets.SetOverrides(new Dictionary<string, string> { ["T"] = "tok-123456" }); Secrets.Expand("${T}");
        var run = Run("r", ("A", true, 5)); run.Testcases[0].Requests[0].ErrorMessage = "401 for tok-123456";
        var text = CiText.Markdown(run, 2, new RunComparison { PreviousRunId = "old" }, ["Orders [qa] d9 (passed on retry 1)"], 2);
        Assert.Contains("FAILED", text); Assert.Contains("Flaky", text); Assert.Contains("Compared with run old", text); Assert.Contains("401 for ***", text);
        Assert.DoesNotContain("tok-123456", text);
    }

    [Fact]
    public async Task The_webhook_gets_the_summary_or_a_text_message()
    {
        var handler = new Capture();
        Assert.Null(await CiText.NotifyAsync("http://hook.test/x", "json", "{\"result\":\"failed\"}", "# md", handler));
        Assert.Equal("{\"result\":\"failed\"}", handler.Body);
        Assert.Null(await CiText.NotifyAsync("http://hook.test/x", "text", "{}", "# md", handler));
        Assert.Equal("{\"text\":\"# md\"}", handler.Body);
        handler.Status = System.Net.HttpStatusCode.BadRequest;
        Assert.Contains("400", await CiText.NotifyAsync("http://hook.test/x", "json", "{}", "m", handler));
    }

    private sealed class Capture : HttpMessageHandler
    {
        public string Body = ""; public System.Net.HttpStatusCode Status = System.Net.HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Body = await request.Content!.ReadAsStringAsync(cancellationToken); return new HttpResponseMessage(Status); }
    }
}
