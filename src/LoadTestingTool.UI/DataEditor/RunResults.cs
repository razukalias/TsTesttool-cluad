using System.Text.Json;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One thing that went wrong for a data row in a run: a failed step, or a failed assertion.</summary>
public sealed record FailureDetail(string StepName, string? ResponsePath, string? Verb, string? Expected, string? Actual, string Message, string Environment, bool Bare = false)
{
    public bool IsAssertion => ResponsePath is not null;
}

/// <summary>How one data row (a request/response row pair) fared in the latest run, across steps, iterations, threads and environments.</summary>
public sealed class RowOutcome
{
    public int Requests { get; internal set; }
    public int FailedRequests { get; internal set; }
    public int FailedAssertions { get; internal set; }

    /// <summary>Failed steps that carry no error of their own because a failed assertion already explains them.</summary>
    public int ExplainedRequests { get; internal set; }

    /// <summary>Distinct things to look at: failed assertions plus steps that failed for their own reason.</summary>
    public int Problems => FailedAssertions + FailedRequests - ExplainedRequests;
    public long MaxDurationMs { get; internal set; }
    public List<FailureDetail> Failures { get; } = [];
    public bool Passed => FailedRequests == 0 && FailedAssertions == 0 && Requests > 0;
}

/// <summary>The latest run found in an instance's Results folder, summarised per data row.</summary>
public sealed class RunResults
{
    public string RunId { get; init; } = string.Empty;
    public DateTime WhenLocal { get; init; }
    public IReadOnlyDictionary<(int Testcase, string DataId), RowOutcome> Rows { get; init; } = new Dictionary<(int, string), RowOutcome>();
    public int PassedRows => Rows.Values.Count(r => r.Passed);
    public int FailedRows => Rows.Values.Count(r => !r.Passed);

    public RowOutcome? Find(int testcase, string dataId) => Rows.TryGetValue((testcase, dataId.Trim().ToUpperInvariant()), out var outcome) ? outcome : null;
}

/// <summary>
/// Reads the runner's event files (<c>Results/&lt;passed|failed|cancelled&gt;/&lt;run&gt;/log/events_&lt;runId&gt;*.ndjson</c>).
/// Tolerant by design: the files can be large, can still be written to, and can contain lines it does not understand.
/// </summary>
public static class RunResultsReader
{
    private const int MaxFailuresPerRow = 40;

    /// <summary>Summarises the most recently written run, or returns null when there are no results.</summary>
    public static RunResults? ReadLatest(string resultsFolder)
    {
        if (!Directory.Exists(resultsFolder)) return null;
        var files = Directory.EnumerateFiles(resultsFolder, "events_*.ndjson", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Contains("aggregate", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .ToList();
        if (files.Count == 0) return null;

        // Files of one run share the run ID in their name; take the run whose newest file is most recent.
        var latest = files.OrderByDescending(f => f.LastWriteTimeUtc).First();
        var runId = RunIdOf(latest.Name);
        var runFiles = files.Where(f => RunIdOf(f.Name) == runId).ToList();

        var rows = new Dictionary<(int, string), RowOutcome>();
        foreach (var file in runFiles) ReadFile(file.FullName, rows);
        foreach (var outcome in rows.Values) FoldExplainedRequests(outcome);
        return new RunResults { RunId = runId, WhenLocal = runFiles.Max(f => f.LastWriteTime), Rows = rows };
    }

    /// <summary>A step marked failed only because one of its assertions failed is not a second problem; keep the assertion.</summary>
    private static void FoldExplainedRequests(RowOutcome outcome)
    {
        var explained = outcome.Failures.Where(f => f.Bare && outcome.Failures.Any(a => a.IsAssertion && a.StepName == f.StepName && a.Environment == f.Environment)).ToList();
        foreach (var failure in explained) outcome.Failures.Remove(failure);
        outcome.ExplainedRequests = explained.Count;
    }

    private static string RunIdOf(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName)["events_".Length..];
        // Per-environment files append "_<environment>" to the run ID, which is "yyyyMMdd_HHmmss_xxxxxxxxxx" (25 characters).
        return name.Length > 25 ? name[..25] : name;
    }

    private static void ReadFile(string path, Dictionary<(int, string), RowOutcome> rows)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var isRequest = line.Contains("\"request-completed\"", StringComparison.Ordinal);
            var isAssertion = !isRequest && line.Contains("\"assertion-completed\"", StringComparison.Ordinal);
            if (!isRequest && !isAssertion) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var key = (Int(root, "testcaseIndex"), Text(root, "dataId").Trim().ToUpperInvariant());
                if (!rows.TryGetValue(key, out var outcome)) rows[key] = outcome = new RowOutcome();
                var environment = Text(root, "environment");
                if (isRequest) AddRequest(outcome, root, environment);
                else AddAssertion(outcome, root, environment);
            }
            catch (JsonException)
            {
                // a line still being written, or not ours: skip it
            }
        }
    }

    private static void AddRequest(RowOutcome outcome, JsonElement root, string environment)
    {
        outcome.Requests++;
        outcome.MaxDurationMs = Math.Max(outcome.MaxDurationMs, Long(root, "durationMs"));
        if (Text(root, "result").Equals("PASS", StringComparison.OrdinalIgnoreCase)) return;
        outcome.FailedRequests++;
        if (outcome.Failures.Count >= MaxFailuresPerRow) return;
        var status = Int(root, "httpStatus");
        var parts = new[] { status > 0 ? $"HTTP {status}" : "", Text(root, "failureCategory"), Text(root, "errorMessage") }.Where(p => p.Length > 0);
        var message = string.Join(" — ", parts);
        var bare = Text(root, "errorMessage").Length == 0 && Text(root, "failureCategory").Length == 0;
        outcome.Failures.Add(new FailureDetail(Text(root, "stepName"), null, null, null, null, message.Length > 0 ? message : "The step failed.", environment, bare));
    }

    private static void AddAssertion(RowOutcome outcome, JsonElement root, string environment)
    {
        if (!Text(root, "result").Equals("FAIL", StringComparison.OrdinalIgnoreCase)) return;
        outcome.FailedAssertions++;
        if (outcome.Failures.Count >= MaxFailuresPerRow) return;
        outcome.Failures.Add(new FailureDetail(Text(root, "stepName"), Text(root, "responsePath"), Text(root, "assertionVerb"),
            Text(root, "expectedValue"), Text(root, "actualValue"), Text(root, "failureMessage"), environment));
    }

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? string.Empty : value.ToString() : string.Empty;

    private static int Int(JsonElement root, string name) => int.TryParse(Text(root, name), out var value) ? value : 0;

    private static long Long(JsonElement root, string name) => long.TryParse(Text(root, name), out var value) ? value : 0;
}
