using LoadTestingTool.UI.DataEditor;

namespace LoadTestingTool.UI.CentralRerun;

/// <summary>One instance as the re-run center sees it: where its results are and what its workbook holds.</summary>
/// <param name="DataIdCase">Gives a data id in the spelling of the workbook (results keep ids upper-cased); null keeps the id as it is.</param>
public sealed record CentralInstance(string Name, string ResultsFolder, IReadOnlyList<TestcaseOption> Catalog, Func<int, string, string>? DataIdCase = null);

/// <summary>A failed data row in one environment, with what went wrong.</summary>
public sealed record FailedPair(string Instance, int TestcaseIndex, string Testcase, string DataId, string Environment, string Kind, string Reason,
    IReadOnlyList<FailureDetail> Failures, IReadOnlyList<string> FailedSteps, string? FromRunId)
{
    public string Key => $"{Instance}\u001F{TestcaseIndex}\u001F{DataId.ToUpperInvariant()}\u001F{Environment.ToUpperInvariant()}";
    public string EnvironmentText => Environment.Length == 0 ? "default" : Environment;
}

/// <summary>One step of a failed row in the re-run: whether it runs and, when it does, whether it uses the old data of the run it follows (false = fresh data).</summary>
public sealed record RowStep(string Name, bool Run, bool OldData);

/// <summary>The default step choice of a failed row: the steps before the failure do not run (their old values are put back for the later steps), the step
/// that failed runs again with its old data, every step after it runs with fresh data. A row without a known failed step runs every step with fresh data.</summary>
public static class RowStepDefaults
{
    public static List<RowStep> For(FailedPair pair, IReadOnlyList<string> allSteps)
    {
        var failed = pair.FailedSteps.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var first = allSteps.ToList().FindIndex(failed.Contains);
        if (first < 0) return [.. allSteps.Select(name => new RowStep(name, true, false))];
        return [.. allSteps.Select((name, i) => new RowStep(name, i >= first, failed.Contains(name)))];
    }
}

/// <summary>Finds the failed row x environment pairs of an instance from the same merged status the table shows (newest result per row and environment).</summary>
public static class CentralRerunSource
{
    public static List<FailedPair> Collect(CentralInstance instance, RunResults? results = null)
    {
        results ??= RunResultsReader.ReadLatest(instance.ResultsFolder);
        var pairs = new List<FailedPair>();
        if (results is null) return pairs;
        foreach (var ((index, dataId), outcome) in results.Rows)
        {
            // A row belongs to the testcase the events name (a reordered workbook moves the number), a testcase that is gone is left out.
            TestcaseOption? testcase;
            if (outcome.Testcase.Length > 0) testcase = instance.Catalog.FirstOrDefault(c => c.Name.Equals(outcome.Testcase, StringComparison.OrdinalIgnoreCase));
            else testcase = instance.Catalog.FirstOrDefault(c => c.Index == index);
            if (testcase is null) continue;
            var id = instance.DataIdCase?.Invoke(testcase.Index, dataId) ?? dataId;
            var perEnvironment = outcome.EnvironmentPairs.ToList();
            if (perEnvironment.Count == 0) Add(outcome, string.Empty);
            else foreach (var (environment, child) in perEnvironment) Add(child, environment);

            void Add(RowOutcome row, string environment)
            {
                if (row.Passed || row.Requests == 0) return;
                var failures = row.Failures.Where(f => !f.Secondary).ToList();
                var steps = failures.Select(f => f.StepName).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var isError = failures.Count > 0 && failures.All(f => f.IsError);
                pairs.Add(new FailedPair(instance.Name, testcase.Index, testcase.Name, id, environment, isError ? "ERROR" : "FAIL", ReasonOf(failures), failures, steps, row.FromRunId));
            }
        }
        return [.. pairs.OrderBy(p => p.TestcaseIndex).ThenBy(p => p.DataId, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Environment, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>One line: the first problem, "+N more" for the rest.</summary>
    public static string ReasonOf(IReadOnlyList<FailureDetail> failures)
    {
        if (failures.Count == 0) return "failed";
        var first = failures[0];
        var text = first.IsAssertion
            ? $"{first.StepName}: {first.ResponsePath} expected {first.Expected ?? "(empty)"}, got {first.Actual ?? "(none)"}"
            : $"{first.StepName}: {(first.Message.Length > 0 ? first.Message : first.Category)}";
        return failures.Count > 1 ? $"{text}  (+{failures.Count - 1} more)" : text;
    }
}

/// <summary>Turns the ticked pairs of one instance into the run requests that run exactly those pairs.</summary>
public static class CentralRerunPlanner
{
    /// <param name="pairs">The ticked pairs of ONE instance.</param>
    /// <param name="chosenSteps">The steps to run per testcase index; a testcase that is missing runs every enabled step.</param>
    /// <param name="catalog">The instance's testcases and steps.</param>
    /// <returns>One request, or one per testcase when a single request would also run rows nobody ticked (the runner takes the rows of an
    /// environment as one list for all testcases of the run).</returns>
    public static IReadOnlyList<RunRequest> Plan(IReadOnlyList<FailedPair> pairs, IReadOnlyDictionary<int, IReadOnlyList<string>> chosenSteps, IReadOnlyList<TestcaseOption> catalog)
    {
        if (pairs.Count == 0) return [];
        var all = Request(pairs, chosenSteps, catalog);
        if (Expand(all).SetEquals(pairs.Select(KeyOf))) return [all];
        return [.. pairs.GroupBy(p => p.TestcaseIndex).OrderBy(g => g.Key).Select(g => Request([.. g], chosenSteps, catalog))];
    }

    /// <summary>
    /// The runs for failed rows with their own step choice. Rows of one testcase with the same steps and the same old-data choice make one run; one request carries
    /// several testcases only when every testcase has a single choice and the ticked rows are exactly the combinations that request would run.
    /// The steps ticked as old data go to the runner as the re-run steps: they and every step before them reuse the values of the run they follow, so a skipped
    /// step's values are still there for the later ones. Rows without any step ticked are not planned.
    /// </summary>
    public static IReadOnlyList<RunRequest> PlanRows(IReadOnlyList<(FailedPair Pair, IReadOnlyList<RowStep> Steps)> rows, IReadOnlyList<TestcaseOption> catalog)
    {
        var usable = rows.Where(r => r.Steps.Any(s => s.Run)).ToList();
        if (usable.Count == 0) return [];
        var configs = usable.GroupBy(r => (r.Pair.TestcaseIndex, Key(r.Steps)))
            .OrderBy(g => g.Key.TestcaseIndex).ThenBy(g => g.Key.Item2, StringComparer.Ordinal)
            .Select(g => (Index: g.Key.TestcaseIndex, Pairs: g.Select(x => x.Pair).ToList(), Steps: g.First().Steps)).ToList();
        if (configs.Select(c => c.Index).Distinct().Count() == configs.Count)
        {
            var all = BuildRows(configs, catalog);
            if (Expand(all).SetEquals(usable.Select(r => KeyOf(r.Pair)))) return [all];
        }
        return [.. configs.Select(c => BuildRows([c], catalog))];
    }

    private static string Key(IReadOnlyList<RowStep> steps) => string.Join("|", steps.Where(s => s.Run).Select(s => s.Name.ToUpperInvariant() + (s.OldData ? "*" : string.Empty)));

    private static RunRequest BuildRows(List<(int Index, List<FailedPair> Pairs, IReadOnlyList<RowStep> Steps)> configs, IReadOnlyList<TestcaseOption> catalog)
    {
        var pairs = configs.SelectMany(c => c.Pairs).ToList();
        var testcases = configs.Select(c => c.Index).Distinct().Order().ToList();
        var byTestcase = pairs.GroupBy(p => p.TestcaseIndex).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.DataId).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var byEnvironment = pairs.GroupBy(p => p.Environment, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.DataId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var steps = new Dictionary<int, IReadOnlyList<string>>();
        var oldData = new Dictionary<int, IReadOnlyList<string>>();
        var restricted = false;
        foreach (var config in configs)
        {
            var every = catalog.FirstOrDefault(c => c.Index == config.Index)?.Steps.Where(s => s.Enabled).Select(s => s.Name).ToList() ?? [];
            var run = every.Where(name => config.Steps.Any(s => s.Run && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
            steps[config.Index] = run;
            if (run.Count != every.Count) restricted = true;
            var old = run.Where(name => config.Steps.Any(s => s.OldData && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
            if (old.Count > 0) oldData[config.Index] = old;
        }
        return new RunRequest(testcases, [], Steps: restricted ? steps : null, RerunSteps: oldData.Count > 0 ? oldData : null,
            Environments: byEnvironment.Keys.ToList(), RowsByTestcase: byTestcase, RowsByEnvironment: byEnvironment);
    }

    private static (int, string, string) KeyOf(FailedPair p) => (p.TestcaseIndex, p.Environment.ToUpperInvariant(), p.DataId.ToUpperInvariant());

    private static RunRequest Request(IReadOnlyList<FailedPair> pairs, IReadOnlyDictionary<int, IReadOnlyList<string>> chosenSteps, IReadOnlyList<TestcaseOption> catalog)
    {
        var testcases = pairs.Select(p => p.TestcaseIndex).Distinct().Order().ToList();
        var byTestcase = pairs.GroupBy(p => p.TestcaseIndex).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.DataId).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var byEnvironment = pairs.GroupBy(p => p.Environment, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.DataId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        var steps = new Dictionary<int, IReadOnlyList<string>>();
        var restricted = false;
        foreach (var index in testcases)
        {
            var every = catalog.FirstOrDefault(c => c.Index == index)?.Steps.Where(s => s.Enabled).Select(s => s.Name).ToList() ?? [];
            var chosen = chosenSteps.TryGetValue(index, out var list) ? every.Where(s => list.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList() : every;
            steps[index] = chosen;
            if (chosen.Count != every.Count) restricted = true;
        }
        return new RunRequest(testcases, [], Steps: restricted ? steps : null, Environments: byEnvironment.Keys.ToList(), RowsByTestcase: byTestcase, RowsByEnvironment: byEnvironment);
    }

    /// <summary>The testcase x environment x data id combinations the runner would run for a request (what it does with --testcases and --rows-by-environment).</summary>
    public static HashSet<(int Testcase, string Environment, string DataId)> Expand(RunRequest request)
    {
        var result = new HashSet<(int, string, string)>();
        foreach (var testcase in request.Testcases)
            foreach (var (environment, ids) in request.RowsByEnvironment ?? new Dictionary<string, IReadOnlyList<string>>())
                foreach (var id in ids) result.Add((testcase, environment.ToUpperInvariant(), id.ToUpperInvariant()));
        return result;
    }
}
