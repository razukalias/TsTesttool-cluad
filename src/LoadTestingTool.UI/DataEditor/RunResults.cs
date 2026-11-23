using System.Text.Json;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One thing that went wrong for a data row in a run: a failed step, or a failed assertion.</summary>
public sealed record FailureDetail(string StepName, string? ResponsePath, string? Verb, string? Expected, string? Actual, string Message, string Environment, bool Bare = false, bool Secondary = false, string Category = "")
{
    public bool IsAssertion => ResponsePath is not null;

    /// <summary>
    /// The step itself broke (it could not connect, timed out, could not build its request, hit a script or SQL error …), as opposed to a
    /// wrong value (a failed assertion) or an unexpected HTTP status. Such a row is shown as ERROR, not FAIL.
    /// </summary>
    public bool IsError => !IsAssertion && IsErrorCategory(Category);

    /// <summary>Failure categories that mean "something went wrong that the test did not check for".</summary>
    public static bool IsErrorCategory(string category) => category is not ("" or "Assertion" or "HttpStatus" or "Cancelled");

    /// <summary>A failed assertion of a step that also failed for its own reason: the step error is what counts, the assertion stays marked in its cell.</summary>
    public bool IsSecondary => Secondary;
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
    public int Problems => FailedAssertions + FailedRequests - ExplainedRequests - SecondaryAssertions;

    /// <summary>Failed assertions of steps that failed for their own reason; the step error counts instead.</summary>
    public int SecondaryAssertions { get; internal set; }
    public long MaxDurationMs { get; internal set; }
    public List<FailureDetail> Failures { get; } = [];

    /// <summary>The same row seen through one environment of a multi-environment run (null when the row did not run there).</summary>
    public RowOutcome? In(string environment)
        => _byEnvironment.TryGetValue(environment, out var outcome) ? outcome : null;

    internal RowOutcome Child(string environment)
    {
        if (!_byEnvironment.TryGetValue(environment, out var outcome)) _byEnvironment[environment] = outcome = new RowOutcome();
        return outcome;
    }

    internal IEnumerable<RowOutcome> Children => _byEnvironment.Values;
    private readonly Dictionary<string, RowOutcome> _byEnvironment = new(StringComparer.OrdinalIgnoreCase);

    public bool Passed => FailedRequests == 0 && FailedAssertions == 0 && Requests > 0;

    /// <summary>Steps that broke on their own (see <see cref="FailureDetail.IsError"/>).</summary>
    public int Errors => Failures.Count(f => f.IsError);

    /// <summary>Environments this row ran in (only filled for multi-environment runs).</summary>
    public IReadOnlyCollection<string> Environments => _byEnvironment.Keys;

    /// <summary>"step.path" (lower case) of the assertions that passed, so a comparison between environments can tell a passing column from one that was not checked.</summary>
    public HashSet<string> PassedColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"step.path" of assertions the runner counted as known bugs.</summary>
    public HashSet<string> BugColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The latest run found in an instance's Results folder, summarised per data row.</summary>
public sealed class RunResults
{
    public string RunId { get; init; } = string.Empty;
    public DateTime WhenLocal { get; init; }
    public IReadOnlyDictionary<(int Testcase, string DataId), RowOutcome> Rows { get; init; } = new Dictionary<(int, string), RowOutcome>();

    /// <summary>Every environment the run covered (empty when the events carry no environment names).</summary>
    public IReadOnlyList<string> Environments { get; init; } = [];
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
        foreach (var outcome in rows.Values) { FoldExplainedRequests(outcome); foreach (var child in outcome.Children) FoldExplainedRequests(child); }
        var environments = rows.Values.SelectMany(r => r.Environments).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new RunResults { RunId = runId, WhenLocal = runFiles.Max(f => f.LastWriteTime), Rows = rows, Environments = environments };
    }

    /// <summary>
    /// One real error must not count twice. A step marked failed only because its assertions failed is not a second problem: the assertions count.
    /// A step that failed for its own reason (HTTP status, timeout, missing reference) counts as that one error, and its failed assertions stay marked in their cells but are not counted.
    /// Step and assertion are matched by name and environment ignoring case: the runner can write the step name differently in the two events.
    /// </summary>
    private static void FoldExplainedRequests(RowOutcome outcome)
    {
        static bool Same(FailureDetail a, FailureDetail b) => string.Equals(a.StepName, b.StepName, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Environment, b.Environment, StringComparison.OrdinalIgnoreCase);
        var explained = outcome.Failures.Where(f => !f.IsAssertion && f.Bare && outcome.Failures.Any(a => a.IsAssertion && Same(a, f))).ToList();
        foreach (var failure in explained) outcome.Failures.Remove(failure);
        outcome.ExplainedRequests = explained.Count;
        // steps with an error of their own: their assertions are secondary
        var own = outcome.Failures.Where(f => !f.IsAssertion).ToList();
        var secondary = 0;
        for (var i = 0; i < outcome.Failures.Count; i++)
        {
            var failure = outcome.Failures[i];
            if (!failure.IsAssertion || !own.Any(o => Same(o, failure))) continue;
            outcome.Failures[i] = failure with { Secondary = true };
            secondary++;
        }
        outcome.SecondaryAssertions = secondary;
    }

    /// <summary>Largest run the editor refreshes by itself; bigger ones (long load tests) are only read when asked.</summary>
    public const long AutoRefreshLimitBytes = 50L * 1024 * 1024;

    /// <summary>
    /// A cheap fingerprint of an instance's event files (count, newest write, total size), so the editor can tell
    /// whether a run has written anything new without reading it. Null when there are no event files.
    /// </summary>
    public static (string Signature, long TotalBytes)? Signature(string resultsFolder)
    {
        if (!Directory.Exists(resultsFolder)) return null;
        var files = Directory.EnumerateFiles(resultsFolder, "events_*.ndjson", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).ToList();
        if (files.Count == 0) return null;
        var bytes = files.Sum(f => f.Length);
        return ($"{files.Count}|{files.Max(f => f.LastWriteTimeUtc.Ticks)}|{bytes}", bytes);
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
                if (environment.Length > 0)   // also keep the row per environment, so the table can show one environment's status
                {
                    if (isRequest) AddRequest(outcome.Child(environment), root, environment);
                    else AddAssertion(outcome.Child(environment), root, environment);
                }
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
        // a step the runner marked failed only because its assertions failed carries the category "Assertion" and no error of its own
        var bare = Text(root, "errorMessage").Length == 0 && Text(root, "failureCategory") is "" or "Assertion";
        outcome.Failures.Add(new FailureDetail(Text(root, "stepName"), null, null, null, null, message.Length > 0 ? message : "The step failed.", environment, bare, Category: Text(root, "failureCategory")));
    }

    private static void AddAssertion(RowOutcome outcome, JsonElement root, string environment)
    {
        var result = Text(root, "result");
        if (!result.Equals("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            var column = $"{Text(root, "stepName")}.{Text(root, "responsePath")}";
            if (result.Equals("PASS", StringComparison.OrdinalIgnoreCase)) outcome.PassedColumns.Add(column);
            else if (result.Equals("KNOWN_BUG", StringComparison.OrdinalIgnoreCase)) outcome.BugColumns.Add(column);
            return;
        }
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
