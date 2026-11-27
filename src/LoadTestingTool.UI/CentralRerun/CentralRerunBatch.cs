using LoadTestingTool.UI.DataEditor;

namespace LoadTestingTool.UI.CentralRerun;

/// <summary>What the re-run center needs from the application that owns the instances.</summary>
public interface ICentralRerunHost
{
    IReadOnlyList<CentralInstance> Instances { get; }

    /// <summary>Starts a run of one instance; null when it started, otherwise why it did not.</summary>
    Task<string?> StartAsync(string instance, RunRequest request);

    /// <summary>Completes when the instance's run has ended.</summary>
    Task WaitUntilFinishedAsync(string instance, CancellationToken cancellation);

    /// <summary>Where the reports are written.</summary>
    string ReportsFolder { get; }

    void Open(string path);
}

/// <summary>One ticked pair after the batch ran.</summary>
public sealed record BatchRow(FailedPair Before, string After, IReadOnlyList<FailureDetail> AfterFailures, long DurationMs, string StepsText = "")
{
    public const string NowPassed = "Now passed";
    public const string StillFailed = "Still failed";
    public const string NotRun = "Not run";
    public bool IsStillFailed => After == StillFailed;
}

public sealed record BatchResult(string BatchId, DateTime Started, DateTime Finished, IReadOnlyList<BatchRow> Rows, IReadOnlyDictionary<string, string> Problems,
    IReadOnlyDictionary<string, IReadOnlyList<string>> StepsByTestcase);

/// <summary>Runs the ticked pairs of every instance (one runner per instance, all at the same time) and compares them with the status before.</summary>
public static class CentralRerunBatch
{
    public static Task<BatchResult> RunAsync(ICentralRerunHost host, IReadOnlyList<FailedPair> ticked, IReadOnlyDictionary<(string Instance, int Testcase), IReadOnlyList<string>> steps,
        Action<string>? progress = null, CancellationToken cancellation = default)
        => RunCoreAsync(host, ticked, (instance, group) =>
        {
            var chosen = steps.Where(s => s.Key.Instance.Equals(instance.Name, StringComparison.OrdinalIgnoreCase)).ToDictionary(s => s.Key.Testcase, s => s.Value);
            return CentralRerunPlanner.Plan([.. group], chosen, instance.Catalog);
        }, steps.ToDictionary(s => $"{s.Key.Instance} › {s.Key.Testcase}", s => s.Value), _ => string.Empty, progress, cancellation);

    /// <summary>Runs failed rows that each have their own steps (and old or fresh data per step). Rows with the same choice run together.</summary>
    public static Task<BatchResult> RunAsync(ICentralRerunHost host, IReadOnlyList<(FailedPair Pair, IReadOnlyList<RowStep> Steps)> rows,
        Action<string>? progress = null, CancellationToken cancellation = default)
    {
        var usable = rows.Where(r => r.Steps.Any(s => s.Run)).ToList();
        var byKey = usable.ToDictionary(r => r.Pair.Key, r => r.Steps);
        var stepsByTestcase = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var group in usable.GroupBy(r => $"{r.Pair.Instance} › {r.Pair.Testcase}"))
            stepsByTestcase[group.Key] = group.SelectMany(r => r.Steps.Where(s => s.Run).Select(s => s.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return RunCoreAsync(host, [.. usable.Select(r => r.Pair)], (instance, group) =>
            CentralRerunPlanner.PlanRows([.. group.Select(p => (p, byKey[p.Key]))], instance.Catalog),
            stepsByTestcase, pair => StepsText(byKey[pair.Key]), progress, cancellation);
    }

    /// <summary>"Create (old data) › Verify (fresh data)": the steps a row ran and with which data.</summary>
    public static string StepsText(IReadOnlyList<RowStep> steps) => string.Join(" › ", steps.Where(s => s.Run).Select(s => $"{s.Name} ({(s.OldData ? "old data" : "fresh data")})"));

    private static async Task<BatchResult> RunCoreAsync(ICentralRerunHost host, IReadOnlyList<FailedPair> ticked, Func<CentralInstance, IReadOnlyList<FailedPair>, IReadOnlyList<RunRequest>> plan,
        IReadOnlyDictionary<string, IReadOnlyList<string>> stepsByTestcase, Func<FailedPair, string> stepsText, Action<string>? progress, CancellationToken cancellation)
    {
        var started = DateTime.Now;
        var problems = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var groups = ticked.GroupBy(p => p.Instance, StringComparer.OrdinalIgnoreCase).ToList();
        await Task.WhenAll(groups.Select(async group =>
        {
            var instance = host.Instances.FirstOrDefault(i => i.Name.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (instance is null) { problems[group.Key] = "The instance is no longer there."; return; }
            var requests = plan(instance, [.. group]);
            var number = 0;
            foreach (var request in requests)
            {
                number++;
                progress?.Invoke($"{instance.Name}: run {number} of {requests.Count} started");
                var error = await host.StartAsync(instance.Name, request);
                if (error is not null) { problems[instance.Name] = error; return; }
                await host.WaitUntilFinishedAsync(instance.Name, cancellation);
                progress?.Invoke($"{instance.Name}: run {number} of {requests.Count} finished");
            }
        }));

        var rows = new List<BatchRow>();
        foreach (var group in groups)
        {
            var instance = host.Instances.FirstOrDefault(i => i.Name.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            RunResults? after = null;
            if (instance is not null) { try { after = RunResultsReader.ReadLatest(instance.ResultsFolder); } catch (IOException) { } }
            foreach (var pair in group) rows.Add(Compare(pair, after, instance, problems.ContainsKey(group.Key)) with { StepsText = stepsText(pair) });
        }
        return new BatchResult($"{started:yyyyMMdd_HHmmss}", started, DateTime.Now, rows, new Dictionary<string, string>(problems, StringComparer.OrdinalIgnoreCase), stepsByTestcase);
    }

    private static BatchRow Compare(FailedPair pair, RunResults? after, CentralInstance? instance, bool startFailed)
    {
        if (after is null || instance is null || startFailed) return new BatchRow(pair, BatchRow.NotRun, [], 0);
        var outcome = after.Find(pair.TestcaseIndex, pair.DataId);
        var row = pair.Environment.Length == 0 ? outcome : outcome?.In(pair.Environment);
        // The batch's runs are the newest ones: a result that still comes from an earlier run means this batch did not run the row.
        if (row is null || row.FromRunId is not null) return new BatchRow(pair, BatchRow.NotRun, [], 0);
        var failures = row.Failures.Where(f => !f.Secondary).ToList();
        return new BatchRow(pair, row.Passed ? BatchRow.NowPassed : BatchRow.StillFailed, row.Passed ? [] : failures, row.MaxDurationMs);
    }
}
