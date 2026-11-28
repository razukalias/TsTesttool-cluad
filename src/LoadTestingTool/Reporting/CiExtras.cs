using System.Text;
using System.Text.Json;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Reporting;

/// <summary>What changed since the previous run: steps that fail now but did not before, steps that were fixed, and steps that got much slower.</summary>
public sealed class RunComparison
{
    public string PreviousRunId { get; init; } = string.Empty;
    public List<string> NewlyFailing { get; } = [];
    public List<string> Fixed { get; } = [];
    public int NewSteps { get; set; }
    public List<string> Slower { get; } = [];

    /// <summary>One entry per testcase, environment, data row and step (loop rounds apart): the result and the slowest time.</summary>
    public static Dictionary<string, object> Snapshot(RunResult run)
    {
        var map = new Dictionary<string, (string Result, long Ms)>(StringComparer.Ordinal);
        foreach (var t in run.Testcases)
            foreach (var r in t.Requests)
            {
                var key = Key(t, r);
                var result = r.Result == "FAIL" ? "FAIL" : r.Result;
                map[key] = map.TryGetValue(key, out var old) ? (old.Result == "FAIL" || result == "FAIL" ? "FAIL" : result, Math.Max(old.Ms, r.DurationMs)) : (result, r.DurationMs);
            }
        return map.ToDictionary(x => x.Key, x => (object)new { result = x.Value.Result, ms = x.Value.Ms });
    }

    public static string Key(TestcaseResult t, RequestResult r)
        => $"{t.Testcase} [{(string.IsNullOrWhiteSpace(t.Environment) ? "default" : t.Environment)}] {t.DataId} / {r.StepName}{(r.LoopRound.Length > 0 ? $" ({r.LoopRound})" : string.Empty)}";

    /// <summary>The newest summary.json of another run in the results folder, or the file named by --compare-with.</summary>
    public static string? FindPrevious(string resultsFolder, string runId, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return File.Exists(explicitPath) ? explicitPath : null;
        if (!Directory.Exists(resultsFolder)) return null;
        var current = "Run_" + InternalLogger.SafeFolderName(runId);
        return Directory.GetDirectories(resultsFolder, "Run_*")
            .Where(d => !Path.GetFileName(d).Equals(current, StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.Combine(d, "summary.json")).Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    public static RunComparison? Compare(RunResult run, string? previousSummaryPath)
    {
        if (previousSummaryPath is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(previousSummaryPath));
            if (!doc.RootElement.TryGetProperty("stepResults", out var before) || before.ValueKind != JsonValueKind.Object) return null;
            var comparison = new RunComparison { PreviousRunId = doc.RootElement.TryGetProperty("runId", out var id) ? id.GetString() ?? "" : "" };
            var now = Snapshot(run);
            foreach (var (key, value) in now)
            {
                var json = JsonSerializer.SerializeToElement(value);
                var result = json.GetProperty("result").GetString(); var ms = json.GetProperty("ms").GetInt64();
                if (!before.TryGetProperty(key, out var old)) { comparison.NewSteps++; if (result == "FAIL") comparison.NewlyFailing.Add(key); continue; }
                var oldResult = old.GetProperty("result").GetString(); var oldMs = old.GetProperty("ms").GetInt64();
                if (result == "FAIL" && oldResult != "FAIL") comparison.NewlyFailing.Add(key);
                else if (result != "FAIL" && oldResult == "FAIL") comparison.Fixed.Add(key);
                if (result != "FAIL" && ms > 500 && ms > oldMs * 2 && oldMs > 0) comparison.Slower.Add($"{key}: {oldMs} ms → {ms} ms");
            }
            return comparison;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}

/// <summary>A readable report of the run and the message to a webhook.</summary>
public static class CiText
{
    public static string Markdown(RunResult run, int exitCode, RunComparison? comparison, IReadOnlyList<string>? flaky, int retries)
    {
        var steps = run.Testcases.SelectMany(t => t.Requests).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"# Test run {run.RunId}: {CiReports.ResultWord(exitCode).ToUpperInvariant()}");
        sb.AppendLine();
        sb.AppendLine($"* Rows: {run.Testcases.Count(t => t.Result == "PASSED")} passed, {run.Testcases.Count(t => t.Result == "FAILED")} failed, {run.Testcases.Count(t => t.Result == "SKIPPED")} skipped");
        sb.AppendLine($"* Steps: {steps.Count(r => r.Result == "PASS")} passed, {steps.Count(r => r.Result == "FAIL")} failed ({steps.Count(r => r.ErrorMessage.Length > 0)} with an error)");
        sb.AppendLine($"* Duration: {(run.EndedAt - run.StartedAt).TotalSeconds:0.#} s · exit code {exitCode}");
        if (retries > 0) sb.AppendLine($"* Retries allowed per failed row: {retries}");
        if (flaky is { Count: > 0 }) { sb.AppendLine(); sb.AppendLine("## Flaky (failed first, passed on a retry)"); foreach (var f in flaky) sb.AppendLine($"* {f}"); }
        if (comparison is not null)
        {
            sb.AppendLine(); sb.AppendLine($"## Compared with run {comparison.PreviousRunId}");
            sb.AppendLine($"* Newly failing: {comparison.NewlyFailing.Count} · fixed: {comparison.Fixed.Count} · new steps: {comparison.NewSteps} · slower: {comparison.Slower.Count}");
            foreach (var x in comparison.NewlyFailing.Take(30)) sb.AppendLine($"  * NEW FAILURE {x}");
            foreach (var x in comparison.Fixed.Take(30)) sb.AppendLine($"  * fixed {x}");
            foreach (var x in comparison.Slower.Take(30)) sb.AppendLine($"  * slower {x}");
        }
        var failed = run.Testcases.SelectMany(t => t.Requests.Where(r => r.Result == "FAIL").Select(r => (t, r))).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine("## Failed steps");
            foreach (var (t, r) in failed.Take(100))
            {
                sb.AppendLine($"### {RunComparison.Key(t, r)}");
                if (r.ErrorMessage.Length > 0) sb.AppendLine($"Error: {r.ErrorMessage.Replace("\r", "").Split('\n')[0]}");
                foreach (var a in r.Assertions.Where(a => a.Result == "FAIL")) sb.AppendLine($"* `{a.ResponsePath}` expected `{a.ExpectedValue}` but got `{a.ActualValue}`");
                if (r.ErrorMessage.Length == 0 && r.Assertions.All(a => a.Result != "FAIL")) sb.AppendLine($"HTTP status {r.HttpStatus} was not the expected one.");
            }
            if (failed.Count > 100) sb.AppendLine($"… and {failed.Count - 100} more failed steps (see junit.xml).");
        }
        return LoadTestingTool.Execution.Secrets.Mask(sb.ToString());
    }

    public static void Write(string path, string markdown)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, markdown, new UTF8Encoding(false));
    }

    /// <summary>Posts the result to a webhook. format "json" sends the summary, "text" sends {"text": "..."} that Slack and Teams show as a message. Returns an error text or null.</summary>
    public static async Task<string?> NotifyAsync(string url, string format, string summaryJson, string markdown, HttpMessageHandler? handler = null)
    {
        try
        {
            using var client = handler is null ? new HttpClient() : new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);
            var body = format.Equals("text", StringComparison.OrdinalIgnoreCase) ? JsonSerializer.Serialize(new { text = markdown.Length > 3500 ? markdown[..3500] + "…" : markdown }) : summaryJson;
            using var response = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            return response.IsSuccessStatusCode ? null : $"the webhook answered {(int)response.StatusCode}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException) { return ex.Message; }
    }
}
