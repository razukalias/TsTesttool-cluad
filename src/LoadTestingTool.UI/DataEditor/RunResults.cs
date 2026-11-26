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

/// <summary>What a passed assertion (or an extra response field) saw in one environment: the value, or that the field was not in the response at all.</summary>
public sealed record PassMark(string Environment, string Value, bool Absent);

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

    /// <summary>The run this result comes from when it is older than the run shown (a later run did not run this row or environment again); null for the run shown.</summary>
    public string? FromRunId { get; internal set; }

    /// <summary>The name of the testcase the events gave for this row (empty when they carry none); used to match rows of older runs when testcases were reordered.</summary>
    public string Testcase { get; internal set; } = string.Empty;

    /// <summary>"24 Nov 20:10" for a run ID of the form yyyyMMdd_HHmmss_xxxxxxxxxx; the ID itself when it does not read that way.</summary>
    public static string RunTime(string runId)
        => runId.Length >= 15 && DateTime.TryParseExact(runId[..15], "yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when)
            ? when.ToString("d MMM HH:mm", System.Globalization.CultureInfo.CurrentCulture) : runId;

    internal bool HasEnvironment(string environment) => _byEnvironment.ContainsKey(environment);
    internal IEnumerable<KeyValuePair<string, RowOutcome>> EnvironmentPairs => _byEnvironment;

    /// <summary>Adds what an older run saw in one environment of this row (the newer run did not run it there).</summary>
    internal void AdoptEnvironment(string environment, RowOutcome older, string fromRunId)
    {
        _byEnvironment[environment] = older;
        older.FromRunId = fromRunId;
        Requests += older.Requests;
        FailedRequests += older.FailedRequests;
        FailedAssertions += older.FailedAssertions;
        MaxDurationMs = Math.Max(MaxDurationMs, older.MaxDurationMs);
        foreach (var failure in older.Failures) if (Failures.Count < 40) Failures.Add(failure);
        PassedColumns.UnionWith(older.PassedColumns);
        BugColumns.UnionWith(older.BugColumns);
        Merge(PassMarks, older.PassMarks);
        Merge(Extras, older.Extras);
    }
    private readonly Dictionary<string, RowOutcome> _byEnvironment = new(StringComparer.OrdinalIgnoreCase);

    public bool Passed => FailedRequests == 0 && FailedAssertions == 0 && Requests > 0;

    /// <summary>Steps that broke on their own (see <see cref="FailureDetail.IsError"/>).</summary>
    public int Errors => Failures.Count(f => f.IsError);

    /// <summary>Environments this row ran in (only filled for multi-environment runs).</summary>
    public IReadOnlyCollection<string> Environments => _byEnvironment.Keys;

    /// <summary>"step.path" (lower case) of the assertions that passed, so a comparison between environments can tell a passing column from one that was not checked.</summary>
    public HashSet<string> PassedColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"step.path" -> what the passed assertions of that column saw (one entry per environment and value): the response value, or "not in the response".</summary>
    public Dictionary<string, List<PassMark>> PassMarks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"step.path" -> the value of a response field that has a real value but no column in the workbook (the runner reported it as an unexpected path).</summary>
    public Dictionary<string, List<PassMark>> Extras { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal static void AddMark(Dictionary<string, List<PassMark>> marks, string key, PassMark mark)
    {
        if (!marks.TryGetValue(key, out var list)) marks[key] = list = [];
        if (list.Count < 12 && !list.Contains(mark)) list.Add(mark);   // iterations repeat the same value: one entry is enough
    }

    private static void Merge(Dictionary<string, List<PassMark>> into, Dictionary<string, List<PassMark>> from)
    {
        foreach (var (key, list) in from) foreach (var mark in list) AddMark(into, key, mark);
    }

    /// <summary>"step.path" of assertions the runner counted as known bugs.</summary>
    public HashSet<string> BugColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The latest run found in an instance's Results folder, summarised per data row.</summary>
public sealed class RunResults
{
    public string RunId { get; init; } = string.Empty;
    public DateTime WhenLocal { get; init; }

    /// <summary>The newest run on disk; differs from <see cref="RunId"/> when an earlier run is shown.</summary>
    public string LatestRunId { get; init; } = string.Empty;

    /// <summary>True when the shown run is not the newest one (the shared run choice points at an earlier run).</summary>
    public bool IsEarlierRun => LatestRunId.Length > 0 && !string.Equals(RunId, LatestRunId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every run found in the Results folder, newest first.</summary>
    public IReadOnlyList<RunChoice> Runs { get; init; } = [];
    public IReadOnlyDictionary<(int Testcase, string DataId), RowOutcome> Rows { get; init; } = new Dictionary<(int, string), RowOutcome>();

    /// <summary>Every environment the run covered (empty when the events carry no environment names).</summary>
    public IReadOnlyList<string> Environments { get; init; } = [];
    public int PassedRows => Rows.Values.Count(r => r.Passed);
    /// <summary>Rows whose result comes from an older run because the latest run did not run them (a rerun of some rows).</summary>
    public int RowsFromEarlierRuns => Rows.Values.Count(r => r.FromRunId is not null);
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

    /// <summary>How many older runs are looked through for the rows the newer runs did not run.</summary>
    private const int MaxEarlierRuns = 100;

    /// <summary>Summarises the most recently written run, or returns null when there are no results.</summary>
    public static RunResults? ReadLatest(string resultsFolder, string? asOfRunId = null)
    {
        if (!Directory.Exists(resultsFolder)) return null;
        var files = Directory.EnumerateFiles(resultsFolder, "events_*.ndjson", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Contains("aggregate", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .ToList();
        if (files.Count == 0) return null;

        // Files of one run share the run ID in their name; take the run whose newest file is most recent.
        var runs = files.GroupBy(f => RunIdOf(f.Name)).Select(g => (RunId: g.Key, Files: g.ToList(), Newest: g.Max(f => f.LastWriteTimeUtc), Bytes: g.Sum(f => f.Length)))
            .OrderByDescending(r => r.Newest).ToList();
        var newestRunId = runs[0].RunId;
        var choices = runs.Select(r => new RunChoice(r.RunId, RunLabel.WhenOf(r.RunId, r.Newest.ToLocalTime()))).ToList();
        // The shared run choice: an earlier run is shown as if it were the newest one; the runs after it are ignored.
        if (!string.IsNullOrWhiteSpace(asOfRunId))
        {
            var at = runs.FindIndex(r => r.RunId.Equals(asOfRunId, StringComparison.OrdinalIgnoreCase));
            if (at > 0) runs = runs.Skip(at).ToList();
        }
        var latest = runs[0];
        var runId = latest.RunId;
        var runFiles = latest.Files;

        var rows = new Dictionary<(int, string), RowOutcome>();
        foreach (var file in runFiles) ReadFile(file.FullName, rows);

        // A rerun (or a run of a few rows or one environment) holds only what it ran. What it did not run keeps the result of the run before:
        // look back through the older runs, newest first, and add the rows and environments the newer ones lack (the newest result of a row wins),
        // until a run that covered everything. Reruns of different rows (A, then B) are all folded in, whatever their size.
        var older = 0;
        foreach (var previous in runs.Skip(1))
        {
            if (++older > MaxEarlierRuns) break;
            var oldRows = new Dictionary<(int, string), RowOutcome>();
            foreach (var file in previous.Files) ReadFile(file.FullName, oldRows);
            var complete = rows.Keys.All(oldRows.ContainsKey) && rows.All(r => r.Value.Environments.All(e => oldRows[r.Key].HasEnvironment(e)));
            // Testcases are matched by their number, but when the events carry the testcase name and the number now means another testcase (or the
            // testcase moved), the old rows follow the name: they go to the number the newest run gave that testcase, or are left out when it is gone.
            var names = rows.Where(r => r.Value.Testcase.Length > 0).GroupBy(r => r.Key.Item1).ToDictionary(g => g.Key, g => g.First().Value.Testcase);
            var indexOf = names.GroupBy(n => n.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);
            foreach (var (oldKey, oldOutcome) in oldRows)
            {
                var key = oldKey;
                if (oldOutcome.Testcase.Length > 0)
                {
                    if (indexOf.TryGetValue(oldOutcome.Testcase, out var moved)) key = (moved, oldKey.Item2);
                    else if (names.ContainsKey(oldKey.Item1)) continue;   // that number is another testcase now
                }
                if (!rows.TryGetValue(key, out var current)) { oldOutcome.FromRunId = previous.RunId; foreach (var child in oldOutcome.Children) child.FromRunId = previous.RunId; rows[key] = oldOutcome; continue; }
                foreach (var (environment, child) in oldOutcome.EnvironmentPairs.ToList())
                    if (!current.HasEnvironment(environment) && current.Environments.Count > 0) current.AdoptEnvironment(environment, child, previous.RunId);
            }
            if (complete) break;
        }

        foreach (var outcome in rows.Values) { FoldExplainedRequests(outcome); foreach (var child in outcome.Children) FoldExplainedRequests(child); }
        var environments = rows.Values.SelectMany(r => r.Environments).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new RunResults { RunId = runId, LatestRunId = newestRunId, Runs = choices, WhenLocal = runFiles.Max(f => f.LastWriteTime), Rows = rows, Environments = environments };
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
                if (outcome.Testcase.Length == 0) outcome.Testcase = Text(root, "testcase");
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
            if (result.Equals("PASS", StringComparison.OrdinalIgnoreCase))
            {
                outcome.PassedColumns.Add(column);
                var actual = Text(root, "actualValue");
                RowOutcome.AddMark(outcome.PassMarks, column, new PassMark(environment, actual, actual == "<missing>"));
            }
            else if (result.Equals("KNOWN_BUG", StringComparison.OrdinalIgnoreCase)) outcome.BugColumns.Add(column);
            return;
        }
        outcome.FailedAssertions++;
        // a response field with a real value that the workbook has no column for: it becomes a read-only column in the table
        if (Text(root, "assertionVerb") == "contract" && Text(root, "failureMessage").StartsWith("Unexpected response path", StringComparison.Ordinal) && Text(root, "actualValue") is { } extra && !IsBlankValue(extra))
            RowOutcome.AddMark(outcome.Extras, $"{Text(root, "stepName")}.{Text(root, "responsePath")}", new PassMark(environment, extra, false));
        if (outcome.Failures.Count >= MaxFailuresPerRow) return;
        outcome.Failures.Add(new FailureDetail(Text(root, "stepName"), Text(root, "responsePath"), Text(root, "assertionVerb"),
            Text(root, "expectedValue"), Text(root, "actualValue"), Text(root, "failureMessage"), environment));
    }

    private static bool IsBlankValue(string value) => string.IsNullOrWhiteSpace(value) || value is "null" or "[]" or "{}" or "<missing>";

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? string.Empty : value.ToString() : string.Empty;

    private static int Int(JsonElement root, string name) => int.TryParse(Text(root, name), out var value) ? value : 0;

    private static long Long(JsonElement root, string name) => long.TryParse(Text(root, name), out var value) ? value : 0;
}
