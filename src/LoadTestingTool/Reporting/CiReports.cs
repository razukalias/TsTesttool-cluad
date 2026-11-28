using System.Text;
using System.Text.Json;
using System.Xml;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Reporting;

/// <summary>What a CI system reads after a run: JUnit XML (test results page), summary.json (numbers and the exit code) and the exit code itself.</summary>
public static class CiReports
{
    /// <summary>0 passed · 1 workbook or settings unusable · 2 failed · 3 cancelled · 4 timed out. <paramref name="failOn"/>: any (default: a failed test or error), errors (only runtime errors) or bugs (a known bug counts as a failure too).</summary>
    public static int ExitCode(RunResult run, string failOn = "any", bool timedOut = false, RunComparison? comparison = null)
    {
        if (timedOut) return 4;
        if (run.Cancelled) return 3;
        var steps = run.Testcases.SelectMany(t => t.Requests).ToList();
        var failed = (failOn?.Trim().ToLowerInvariant()) switch
        {
            "errors" => steps.Any(r => r.ErrorMessage.Length > 0),
            "new" => comparison is null ? run.Testcases.Any(t => t.Result == "FAILED") : comparison.NewlyFailing.Count > 0,   // only what did not fail in the previous run
            "bugs" => run.Testcases.Any(t => t.Result == "FAILED") || steps.SelectMany(r => r.Assertions).Any(a => a.KnownBug),
            _ => run.Testcases.Any(t => t.Result == "FAILED"),
        };
        return failed ? 2 : 0;
    }

    public static string ResultWord(int exitCode) => exitCode switch { 0 => "passed", 2 => "failed", 3 => "cancelled", 4 => "timeout", _ => "error" };

    /// <summary>One test suite per testcase and environment, one test case per step (loop rounds get their own), with the failure text of the step.</summary>
    public static void WriteJUnit(string path, RunResult run)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var groups = run.Testcases.GroupBy(t => (t.Testcase, Environment: string.IsNullOrWhiteSpace(t.Environment) ? "default" : t.Environment)).ToList();
        var all = run.Testcases.SelectMany(t => t.Requests).ToList();
        using var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) });
        writer.WriteStartDocument();
        writer.WriteStartElement("testsuites");
        writer.WriteAttributeString("name", Clean(run.RunId));
        writer.WriteAttributeString("tests", all.Count.ToString());
        writer.WriteAttributeString("failures", all.Count(r => r.Result == "FAIL" && r.ErrorMessage.Length == 0).ToString());
        writer.WriteAttributeString("errors", all.Count(r => r.ErrorMessage.Length > 0).ToString());
        writer.WriteAttributeString("skipped", all.Count(r => r.Result == "SKIPPED").ToString());
        writer.WriteAttributeString("time", Seconds(all.Sum(r => r.DurationMs)));
        foreach (var group in groups)
        {
            var steps = group.SelectMany(t => t.Requests.Select(r => (Row: t, Step: r))).ToList();
            writer.WriteStartElement("testsuite");
            writer.WriteAttributeString("name", Clean($"{group.Key.Testcase} [{group.Key.Environment}]"));
            writer.WriteAttributeString("tests", steps.Count.ToString());
            writer.WriteAttributeString("failures", steps.Count(s => s.Step.Result == "FAIL" && s.Step.ErrorMessage.Length == 0).ToString());
            writer.WriteAttributeString("errors", steps.Count(s => s.Step.ErrorMessage.Length > 0).ToString());
            writer.WriteAttributeString("skipped", steps.Count(s => s.Step.Result == "SKIPPED").ToString());
            writer.WriteAttributeString("time", Seconds(steps.Sum(s => s.Step.DurationMs)));
            writer.WriteAttributeString("timestamp", group.Min(t => t.StartedAt).ToString("s"));
            foreach (var (row, step) in steps)
            {
                writer.WriteStartElement("testcase");
                writer.WriteAttributeString("classname", Clean($"{group.Key.Testcase}.{row.DataId}"));
                writer.WriteAttributeString("name", Clean(step.LoopRound.Length > 0 ? $"{step.StepName} ({step.LoopRound})" : step.StepName));
                writer.WriteAttributeString("time", Seconds(step.DurationMs));
                if (step.ErrorMessage.Length > 0)
                {
                    writer.WriteStartElement("error"); writer.WriteAttributeString("message", Clean(First(step.ErrorMessage))); writer.WriteAttributeString("type", step.FailureCategory.Length > 0 ? step.FailureCategory : "Error");
                    writer.WriteString(Clean(step.ErrorMessage)); writer.WriteEndElement();
                }
                else if (step.Result == "FAIL")
                {
                    var failing = step.Assertions.Where(a => a.Result == "FAIL").ToList();
                    var text = failing.Count > 0 ? string.Join("\n", failing.Select(a => $"{a.ResponsePath}: expected '{a.ExpectedValue}' but got '{a.ActualValue}'{(a.FailureMessage.Length > 0 ? " – " + a.FailureMessage : string.Empty)}")) : $"HTTP status {step.HttpStatus} was not the expected one.";
                    writer.WriteStartElement("failure"); writer.WriteAttributeString("message", Clean(First(text))); writer.WriteAttributeString("type", "AssertionFailed");
                    writer.WriteString(Clean(text)); writer.WriteEndElement();
                }
                else if (step.Result == "SKIPPED") writer.WriteElementString("skipped", string.Empty);
                var bugs = step.Assertions.Where(a => a.KnownBug).ToList();
                if (bugs.Count > 0) writer.WriteElementString("system-out", Clean(string.Join("\n", bugs.Select(a => $"KNOWN BUG {a.ResponsePath}: {a.BugDescription}"))));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>The numbers of a run and its exit code, as JSON a pipeline step can read.</summary>
    public static void WriteSummary(string path, RunResult run, int exitCode, string failOn, RunComparison? comparison = null, IReadOnlyList<string>? flaky = null, int retries = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var steps = run.Testcases.SelectMany(t => t.Requests).ToList();
        var failedSteps = run.Testcases.SelectMany(t => t.Requests.Where(r => r.Result == "FAIL").Select(r => new { testcase = t.Testcase, dataId = t.DataId, environment = t.Environment, step = r.StepName, message = First(r.ErrorMessage.Length > 0 ? r.ErrorMessage : string.Join("; ", r.Assertions.Where(a => a.Result == "FAIL").Select(a => $"{a.ResponsePath}: expected '{a.ExpectedValue}' but got '{a.ActualValue}'"))) })).Take(100).ToList();
        var summary = new
        {
            runId = run.RunId, result = ResultWord(exitCode), exitCode, failOn = string.IsNullOrWhiteSpace(failOn) ? "any" : failOn,
            startedAt = run.StartedAt, endedAt = run.EndedAt, durationSeconds = Math.Round((run.EndedAt - run.StartedAt).TotalSeconds, 1),
            environments = run.Testcases.Select(t => string.IsNullOrWhiteSpace(t.Environment) ? "default" : t.Environment).Distinct().ToList(),
            rows = new { total = run.Testcases.Count, passed = run.Testcases.Count(t => t.Result == "PASSED"), failed = run.Testcases.Count(t => t.Result == "FAILED"), skipped = run.Testcases.Count(t => t.Result == "SKIPPED") },
            steps = new { total = steps.Count, passed = steps.Count(r => r.Result == "PASS"), failed = steps.Count(r => r.Result == "FAIL"), errors = steps.Count(r => r.ErrorMessage.Length > 0) },
            assertions = new { failed = steps.SelectMany(r => r.Assertions).Count(a => a.Result == "FAIL"), knownBugs = steps.SelectMany(r => r.Assertions).Count(a => a.KnownBug) },
            retries, flaky = flaky ?? [],
            comparison = comparison is null ? null : new { previousRunId = comparison.PreviousRunId, newlyFailing = comparison.NewlyFailing, fixedSteps = comparison.Fixed, newSteps = comparison.NewSteps, slower = comparison.Slower },
            stepResults = RunComparison.Snapshot(run),
            failures = failedSteps,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    /// <summary>Workbook problems as a JUnit file (one test case per problem, or one passing case), for <c>--validate-only</c>.</summary>
    public static void WriteValidationJUnit(string path, string workbook, IReadOnlyList<(string Severity, string Where, string Message)> problems)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var errors = problems.Where(p => p.Severity == "Error").ToList();
        using var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) });
        writer.WriteStartDocument();
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", "Workbook validation: " + Clean(Path.GetFileName(workbook)));
        writer.WriteAttributeString("tests", Math.Max(1, errors.Count).ToString()); writer.WriteAttributeString("failures", errors.Count.ToString());
        if (errors.Count == 0) { writer.WriteStartElement("testcase"); writer.WriteAttributeString("classname", "Workbook"); writer.WriteAttributeString("name", "The workbook has no errors"); writer.WriteEndElement(); }
        foreach (var problem in errors)
        {
            writer.WriteStartElement("testcase"); writer.WriteAttributeString("classname", "Workbook"); writer.WriteAttributeString("name", Clean(problem.Where));
            writer.WriteStartElement("failure"); writer.WriteAttributeString("message", Clean(problem.Message)); writer.WriteString(Clean(problem.Message)); writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>--validate-only: reads the workbook like the editor does, prints every problem and stops. Exit code 0 without errors, 1 with errors.</summary>
    public static int ValidateOnly(string workbookPath, string junitPath, TextWriter? output = null)
    {
        output ??= Console.Out;
        var document = LoadTestingTool.Editing.WorkbookDocument.Load(workbookPath);
        var problems = LoadTestingTool.Editing.WorkbookValidator.Validate(document);
        static string Where(LoadTestingTool.Editing.WorkbookProblem p) => $"{p.Sheet}{(p.RowNumber is { } r ? $" row {r}" : string.Empty)}";
        var isError = (LoadTestingTool.Editing.WorkbookProblem p) => p.Severity == LoadTestingTool.Editing.ProblemSeverity.Error;
        foreach (var p in problems) output.WriteLine($"[{(isError(p) ? "Error" : "Warning")}] {Where(p)}: {p.Message}");
        var errors = problems.Count(isError);
        output.WriteLine($"Validation: {errors} errors, {problems.Count - errors} warnings in {Path.GetFileName(workbookPath)}.");
        try { WriteValidationJUnit(junitPath, workbookPath, problems.Select(p => (isError(p) ? "Error" : "Warning", Where(p), p.Message)).ToList()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.Error.WriteLine($"[Warning] The validation report could not be written: {ex.Message}"); }
        return errors > 0 ? 1 : 0;
    }

    private static string Seconds(long milliseconds) => (milliseconds / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string First(string text) => (text ?? string.Empty).Replace("\r", "").Split('\n')[0] is { Length: > 300 } line ? line[..300] : (text ?? string.Empty).Replace("\r", "").Split('\n')[0];
    /// <summary>Text for XML: characters XML 1.0 cannot hold are dropped, and values that were filled in from ${…} are masked.</summary>
    private static string Clean(string? text) => new string(LoadTestingTool.Execution.Secrets.Mask(text).Where(c => c is '\t' or '\n' or '\r' || (c >= ' ' && c != '￾' && c != '￿' && !char.IsSurrogate(c))).ToArray());
}
