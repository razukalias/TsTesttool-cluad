namespace LoadTestingTool.UI;

public enum StepMatch { Match, ResultDiffers, ErrorDiffers }

/// <summary>How one step ended in one environment: Passed, Failed or Skipped, and for a failure its error text.</summary>
public sealed record EnvironmentOutcome(string Environment, string Result, string Error);

/// <summary>One step (testcase, data row, step name) as it ended in every environment that ran it.</summary>
public sealed record StepComparison(string Testcase, string DataId, string StepName, IReadOnlyList<EnvironmentOutcome> Outcomes, StepMatch Kind);

public sealed record ErrorOccurrence(string Testcase, string DataId, string StepName, string Environment);

/// <summary>Every failure with the same error, with where it happened.</summary>
public sealed record ErrorGroup(string Key, string Sample, IReadOnlyList<ErrorOccurrence> Occurrences)
{
    public IReadOnlyList<string> Environments => Occurrences.Select(o => o.Environment).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
    public int Steps => Occurrences.Select(o => (o.Testcase, o.DataId, o.StepName)).Distinct().Count();
}

/// <summary>One asserted field whose actual value is not the same in every environment (even if the assertions all passed).</summary>
public sealed record ValueDifference(string Testcase, string DataId, string StepName, string Path, IReadOnlyList<(string Environment, string Actual)> Values);

public enum RunChange { Regressed, Fixed, ErrorChanged, New, Gone }

/// <summary>What changed for one step in one environment between an earlier run and the current one.</summary>
public sealed record StepChange(string Testcase, string DataId, string StepName, string Environment, RunChange Change, string Before, string After);

/// <summary>Compares the steps of an earlier run with the current run, step by step and environment by environment.</summary>
public static class RunComparer
{
    private static string Name(string environment) => string.IsNullOrWhiteSpace(environment) ? "default" : environment.Trim();

    private static Dictionary<(string, string, string, string), EnvironmentOutcome> Collect(IEnumerable<DashboardRequest> requests, Func<DashboardRequest, string?>? assertionText)
    {
        var result = new Dictionary<(string, string, string, string), EnvironmentOutcome>();
        foreach (var group in requests.GroupBy(r => (r.Testcase.ToLowerInvariant(), r.DataId.ToLowerInvariant(), r.StepName.ToLowerInvariant(), Name(r.Environment).ToLowerInvariant())))
        {
            var failed = group.FirstOrDefault(r => !r.Result.Equals("PASS", StringComparison.OrdinalIgnoreCase) && !r.Result.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase));
            if (failed is not null)
            {
                var error = !string.IsNullOrWhiteSpace(failed.Error) ? failed.Error : assertionText?.Invoke(failed) is { Length: > 0 } text ? text
                    : !string.IsNullOrWhiteSpace(failed.FailureCategory) ? failed.FailureCategory : "Failed (no message)";
                result[group.Key] = new EnvironmentOutcome(Name(failed.Environment), "Failed", error.Trim());
            }
            else result[group.Key] = new EnvironmentOutcome(Name(group.First().Environment), group.All(r => r.Result.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase)) ? "Skipped" : "Passed", string.Empty);
        }
        return result;
    }

    public static IReadOnlyList<StepChange> Compare(IEnumerable<DashboardRequest> earlier, IEnumerable<DashboardRequest> current,
        Func<DashboardRequest, string?>? earlierText = null, Func<DashboardRequest, string?>? currentText = null)
    {
        var before = Collect(earlier, earlierText);
        var now = Collect(current, currentText);
        var names = current.Concat(earlier).GroupBy(r => (r.Testcase.ToLowerInvariant(), r.DataId.ToLowerInvariant(), r.StepName.ToLowerInvariant())).ToDictionary(g => g.Key, g => g.First());
        var changes = new List<StepChange>();
        foreach (var key in now.Keys.Concat(before.Keys).Distinct())
        {
            var sample = names[(key.Item1, key.Item2, key.Item3)];
            StepChange Make(RunChange change, string was, string isNow) => new(sample.Testcase, sample.DataId, sample.StepName, (now.TryGetValue(key, out var n) ? n : before[key]).Environment, change, was, isNow);
            string Describe(EnvironmentOutcome o) => o.Result == "Failed" ? $"Failed — {o.Error}" : o.Result;
            if (!before.TryGetValue(key, out var was)) { changes.Add(Make(RunChange.New, "not run", Describe(now[key]))); continue; }
            if (!now.TryGetValue(key, out var isNow)) { changes.Add(Make(RunChange.Gone, Describe(was), "not run")); continue; }
            if (was.Result == isNow.Result)
            {
                if (was.Result == "Failed" && EnvironmentReport.GroupKey(was.Error).ToLowerInvariant() != EnvironmentReport.GroupKey(isNow.Error).ToLowerInvariant()) changes.Add(Make(RunChange.ErrorChanged, Describe(was), Describe(isNow)));
                continue;
            }
            if (isNow.Result == "Failed") changes.Add(Make(RunChange.Regressed, Describe(was), Describe(isNow)));
            else if (was.Result == "Failed") changes.Add(Make(RunChange.Fixed, Describe(was), Describe(isNow)));
        }
        return changes.OrderBy(c => c.Change).ThenBy(c => c.Testcase).ThenBy(c => c.DataId).ThenBy(c => c.StepName).ThenBy(c => c.Environment).ToList();
    }
}

/// <summary>
/// The steps of one run set side by side across its environments. A step matches when every environment that ran it ended the same way (passed, failed with the
/// same error, or skipped); it differs when one passed and another failed, or when they failed with different errors. Failures are also grouped by error.
/// </summary>
public sealed class EnvironmentReport
{
    public IReadOnlyList<string> Environments { get; init; } = [];
    public IReadOnlyList<StepComparison> Compared { get; init; } = [];
    public IReadOnlyList<ErrorGroup> Groups { get; init; } = [];
    public IReadOnlyList<ValueDifference> ValueDifferences { get; init; } = [];
    public int Matching => Compared.Count(c => c.Kind == StepMatch.Match);
    public int PassedEverywhere => Compared.Count(c => c.Kind == StepMatch.Match && c.Outcomes.All(o => o.Result == "Passed"));
    public int FailedEverywhere => Compared.Count(c => c.Kind == StepMatch.Match && c.Outcomes.All(o => o.Result == "Failed"));
    public IReadOnlyList<StepComparison> Differing => Compared.Where(c => c.Kind != StepMatch.Match).ToList();

    private static string Name(string environment) => string.IsNullOrWhiteSpace(environment) ? "default" : environment.Trim();
    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>The key errors are grouped by: the first line, with the values of an assertion ("path eq: expected 1, got 2") left out so the same assertion failing with different values is one group.</summary>
    internal static string GroupKey(string error)
    {
        var line = Collapse(error.Split('\n')[0]);
        var cut = line.IndexOf(": expected", StringComparison.OrdinalIgnoreCase);
        if (cut > 0) line = line[..cut];
        return line.Length > 160 ? line[..160] : line;
    }

    /// <param name="requests">The finished steps of the run.</param>
    /// <param name="assertionText">For a failed step without an error message of its own: what its failed assertion said (or null).</param>
    public static EnvironmentReport Build(IEnumerable<DashboardRequest> requests, Func<DashboardRequest, string?>? assertionText = null, IEnumerable<AssertionInfo>? assertions = null)
    {
        var rows = requests.ToList();
        var environments = rows.Select(r => Name(r.Environment)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();

        EnvironmentOutcome Outcome(IGrouping<string, DashboardRequest> inEnvironment)
        {
            var failed = inEnvironment.FirstOrDefault(r => !r.Result.Equals("PASS", StringComparison.OrdinalIgnoreCase) && !r.Result.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase));
            if (failed is not null)
            {
                var error = !string.IsNullOrWhiteSpace(failed.Error) ? failed.Error : assertionText?.Invoke(failed) is { Length: > 0 } text ? text
                    : !string.IsNullOrWhiteSpace(failed.FailureCategory) ? failed.FailureCategory : "Failed (no message)";
                return new EnvironmentOutcome(inEnvironment.Key, "Failed", error.Trim());
            }
            return new EnvironmentOutcome(inEnvironment.Key, inEnvironment.All(r => r.Result.Equals("SKIPPED", StringComparison.OrdinalIgnoreCase)) ? "Skipped" : "Passed", string.Empty);
        }

        var compared = new List<StepComparison>();
        var occurrences = new Dictionary<string, (string Sample, List<ErrorOccurrence> Items)>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in rows.GroupBy(r => (r.Testcase, r.DataId, r.StepName)).OrderBy(g => g.Min(r => r.TestcaseIndex)).ThenBy(g => g.Min(r => r.ExcelRow)).ThenBy(g => g.Min(r => r.StepOrder)))
        {
            var outcomes = step.GroupBy(r => Name(r.Environment), StringComparer.OrdinalIgnoreCase).Select(Outcome).OrderBy(o => o.Environment, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var failed in outcomes.Where(o => o.Result == "Failed"))
            {
                var key = GroupKey(failed.Error);
                if (!occurrences.TryGetValue(key, out var group)) occurrences[key] = group = (failed.Error, []);
                group.Items.Add(new ErrorOccurrence(step.Key.Testcase, step.Key.DataId, step.Key.StepName, failed.Environment));
            }
            if (outcomes.Count < 2) continue;   // only one environment ran it: nothing to compare
            var kind = outcomes.Select(o => o.Result).Distinct().Count() > 1 ? StepMatch.ResultDiffers
                : outcomes.All(o => o.Result == "Failed") && outcomes.Select(o => GroupKey(o.Error).ToLowerInvariant()).Distinct().Count() > 1 ? StepMatch.ErrorDiffers
                : StepMatch.Match;
            compared.Add(new StepComparison(step.Key.Testcase, step.Key.DataId, step.Key.StepName, outcomes, kind));
        }
        var groups = occurrences.Select(p => new ErrorGroup(p.Key, p.Value.Sample, p.Value.Items)).OrderByDescending(g => g.Occurrences.Count).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var differences = new List<ValueDifference>();
        foreach (var field in (assertions ?? []).GroupBy(a => (a.Testcase, a.DataId, a.StepName, a.ResponsePath)))
        {
            var values = field.GroupBy(a => Name(a.Environment), StringComparer.OrdinalIgnoreCase).Select(g => (Environment: g.Key, Actual: g.First().ActualValue)).OrderBy(v => v.Environment, StringComparer.OrdinalIgnoreCase).ToList();
            if (values.Count > 1 && values.Select(v => v.Actual).Distinct().Count() > 1)
                differences.Add(new ValueDifference(field.Key.Testcase, field.Key.DataId, field.Key.StepName, field.Key.ResponsePath, values));
        }
        return new EnvironmentReport { Environments = environments, Compared = compared, Groups = groups, ValueDifferences = differences };
    }
}
