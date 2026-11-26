using System.Globalization;
using System.Text.Json;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Reporting;

/// <summary>
/// Picks up what a runner that was killed (power loss, crash) left behind. A run is saved to the history database only when it ends, so a killed run has no history there;
/// what survives are its live event files (written line by line while it ran) and its log files, which still carry the name <c>…_running_…</c>.
/// <see cref="CatchUp"/> renames the logs of dead runs, rebuilds such a run in the database from its events (no bodies or headers: the events do not carry them) and stores the logs.
/// </summary>
public static class RunRecovery
{
    /// <summary>A run that has not written anything for this long is taken for dead. A run that is silent for longer than this (for example a very long manual wait) would be wrongly rebuilt.</summary>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(2);

    public const string RecoveredNote = "Rebuilt from the live event files after the run ended without saving its history (the runner was probably killed). Bodies, headers and generated values are not available.";

    public sealed record CatchUpResult(int LogsRenamed, int RunsRecovered, int LogsStored)
    {
        public bool Nothing => LogsRenamed == 0 && RunsRecovered == 0 && LogsStored == 0;
    }

    /// <summary>Does everything for an instance whose history is in the database. Safe to call often and from several processes: what is done is not done again.</summary>
    public static CatchUpResult CatchUp(AppConfig config, string resultsFolder, IReadOnlyCollection<string> environments, TimeSpan? staleAfter = null, DateTimeOffset? now = null)
    {
        if (config.HistoryStorage == HistoryStorage.Files) return new(0, 0, 0);
        var stale = staleAfter ?? DefaultStaleAfter;
        var clock = now ?? DateTimeOffset.Now;
        var renamed = RenameStaleLogs(config.LogsFolder, stale, clock);
        var store = new SqliteHistoryStore(config.HistoryFolder);
        var recovered = 0;
        foreach (var abandoned in FindAbandoned(config, resultsFolder, store, stale, clock))
        {
            try { if (Recover(config, store, abandoned)) recovered++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or Microsoft.Data.Sqlite.SqliteException)
            { Console.Error.WriteLine($"[Warning] Run {abandoned.RunId} could not be rebuilt from its event files: {ex.Message}"); }
        }
        var logs = 0;
        if (config.HistoryLogsInDatabase && store.Exists)
            try { logs = store.ImportLogFiles(config.LogsFolder, environments).Added; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { Console.Error.WriteLine($"[Warning] Logs could not be stored in the history database: {ex.Message}"); }
        return new(renamed, recovered, logs);
    }

    /// <summary>A log that is still called <c>running</c> but has not been written to for <paramref name="stale"/> belongs to a dead process: it is renamed to <c>failed</c> (its process log too).</summary>
    public static int RenameStaleLogs(string logsFolder, TimeSpan stale, DateTimeOffset now)
    {
        if (!Directory.Exists(logsFolder)) return 0;
        var renamed = 0;
        foreach (var path in Directory.GetFiles(logsFolder, "*_running_*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (now - File.GetLastWriteTimeUtc(path) < stale) continue;
                var target = path.Replace("_running_", "_failed_", StringComparison.Ordinal);
                if (File.Exists(target)) continue;
                File.Move(path, target);
                renamed++;
            }
            catch (IOException) { /* another process was quicker, or the file is in use: it is not dead */ }
            catch (UnauthorizedAccessException) { }
        }
        return renamed;
    }

    public sealed record Abandoned(string RunId, IReadOnlyList<string> EventFiles, DateTimeOffset First, DateTimeOffset Last);

    /// <summary>Runs with event files that have been silent for a while, that the database does not have and that wrote no history workbook either.</summary>
    public static IReadOnlyList<Abandoned> FindAbandoned(AppConfig config, string resultsFolder, SqliteHistoryStore store, TimeSpan stale, DateTimeOffset now)
    {
        var roots = new[] { resultsFolder, Path.Combine(config.LogsFolder, ".runtime") }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        // history workbooks do not carry the run id in their name; one written between the run's start and shortly after its last event means the run got to write its history
        var workbookTimes = config.HistoryStorage == HistoryStorage.Both && Directory.Exists(config.HistoryFolder)
            ? Directory.GetFiles(config.HistoryFolder, "*.xlsx", SearchOption.AllDirectories).Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal)).Select(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero)).ToList() : [];
        var found = new List<Abandoned>();
        foreach (var root in roots)
            foreach (var folder in Directory.GetDirectories(root, "Run_*", SearchOption.TopDirectoryOnly))
            {
                var runId = Path.GetFileName(folder)["Run_".Length..];
                var files = Directory.GetFiles(folder, "events_*.ndjson", SearchOption.TopDirectoryOnly);
                if (files.Length == 0 || found.Any(f => f.RunId == runId)) continue;
                var last = files.Max(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero));
                if (now - last < stale) continue;
                var first = new DateTimeOffset(files.Min(f => File.GetCreationTimeUtc(f)), TimeSpan.Zero);
                if (store.ContainsRun(runId) || workbookTimes.Any(t => t >= first && t <= last.AddMinutes(30))) continue;
                found.Add(new Abandoned(runId, files, first, last));
            }
        return found;
    }

    /// <summary>Rebuilds the run from its events and stores it as cancelled with a note. False when the events hold no request.</summary>
    public static bool Recover(AppConfig config, SqliteHistoryStore store, Abandoned abandoned)
    {
        var run = new RunResult { RunId = abandoned.RunId, StartedAt = StartOf(abandoned), Cancelled = true, EndedAt = abandoned.Last };
        var requests = new Dictionary<string, RequestResult>(StringComparer.Ordinal);
        var order = new List<RequestResult>();
        var assertions = new List<JsonElement>();
        foreach (var file in abandoned.EventFiles.OrderBy(f => f, StringComparer.Ordinal))
        {
            string[] lines;
            try { using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var reader = new StreamReader(stream); lines = reader.ReadToEnd().Split('\n'); }
            catch (IOException) { continue; }
            foreach (var line in lines)
            {
                if (line.Length == 0 || line[0] != '{') continue;
                JsonElement e;
                try { e = JsonDocument.Parse(line).RootElement.Clone(); } catch (JsonException) { continue; }   // the last line of a killed process may be cut off
                var type = Text(e, "eventType");
                if (type == "request-completed")
                {
                    var request = new RequestResult
                    {
                        RunId = abandoned.RunId, RequestId = Text(e, "requestId"), TestcaseIndex = Number(e, "testcaseIndex"), Testcase = Text(e, "testcase"), DataId = Text(e, "dataId"),
                        StepName = Text(e, "stepName"), StepType = Text(e, "stepType"), Thread = Number(e, "thread"), Iteration = Math.Max(1, Number(e, "iteration")),
                        StartedAt = run.StartedAt, TemplateSource = Text(e, "template"), Environment = Text(e, "environment"),
                        ExcelRowNumber = Number(e, "excelRow"), StepOrder = Number(e, "stepOrder"), TargetUrl = Text(e, "targetUrl"), HttpStatus = Number(e, "httpStatus"),
                        DurationMs = Number(e, "durationMs"), ErrorMessage = Text(e, "errorMessage"), FailureCategory = Text(e, "failureCategory"), LoopRound = Text(e, "loopRound"),
                        ExpectedStatusMatched = Text(e, "result") == "PASS",     // the recorded verdict stands, also for a step that expected an error status
                    };
                    if (request.RequestId.Length == 0 || requests.ContainsKey(request.RequestId)) continue;
                    requests[request.RequestId] = request; order.Add(request);
                }
                else if (type == "assertion-completed") assertions.Add(e);
            }
        }
        if (order.Count == 0) return false;
        foreach (var e in assertions)
            if (requests.TryGetValue(Text(e, "requestId"), out var request))
                request.Assertions.Add(new AssertionResult
                {
                    RunId = abandoned.RunId, RequestId = request.RequestId, TestcaseIndex = Number(e, "testcaseIndex"), Testcase = Text(e, "testcase"), DataId = Text(e, "dataId"), StepName = Text(e, "stepName"),
                    ResponsePath = Text(e, "responsePath"), AssertionVerb = Text(e, "assertionVerb"), AssertionType = Text(e, "assertionType"), AssertionMeaning = Text(e, "assertionMeaning"),
                    ExpectedValue = Text(e, "expectedValue"), ActualValue = Text(e, "actualValue"), Result = Text(e, "result"), FailureMessage = Text(e, "failureMessage"),
                    KnownBug = e.TryGetProperty("knownBug", out var bug) && bug.ValueKind == JsonValueKind.True, Environment = request.Environment,
                });
        foreach (var group in order.GroupBy(r => (r.TestcaseIndex, r.DataId, r.Iteration, r.Environment)))
        {
            var first = group.First();
            var row = new TestcaseResult { RunId = abandoned.RunId, TestcaseIndex = first.TestcaseIndex, Testcase = first.Testcase, DataId = first.DataId, Iteration = first.Iteration, Environment = first.Environment, StartedAt = run.StartedAt, ExcelRowNumber = first.ExcelRowNumber, EndedAt = abandoned.Last };
            row.Requests.AddRange(group.OrderBy(r => r.StepOrder));
            run.Testcases.Add(row);
        }
        store.SaveRun(run, null, null, new HistoryWriter(config).BuildDetails(run));
        store.SetNote(abandoned.RunId, RecoveredNote);
        return true;
    }

    /// <summary>A run id starts with its start time (yyyyMMdd_HHmmss); otherwise the first event file's creation time.</summary>
    private static DateTimeOffset StartOf(Abandoned a)
        => a.RunId.Length >= 15 && DateTime.TryParseExact(a.RunId[..15], "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? new DateTimeOffset(parsed) : a.First;

    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Null ? "" : v.ToString() : "";
    private static int Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
}
