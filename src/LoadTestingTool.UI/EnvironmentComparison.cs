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

/// <summary>
/// The steps of one run set side by side across its environments. A step matches when every environment that ran it ended the same way (passed, failed with the
/// same error, or skipped); it differs when one passed and another failed, or when they failed with different errors. Failures are also grouped by error.
/// </summary>
public sealed class EnvironmentReport
{
    public IReadOnlyList<string> Environments { get; init; } = [];
    public IReadOnlyList<StepComparison> Compared { get; init; } = [];
    public IReadOnlyList<ErrorGroup> Groups { get; init; } = [];
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
    public static EnvironmentReport Build(IEnumerable<DashboardRequest> requests, Func<DashboardRequest, string?>? assertionText = null)
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
        return new EnvironmentReport { Environments = environments, Compared = compared, Groups = groups };
    }
}
