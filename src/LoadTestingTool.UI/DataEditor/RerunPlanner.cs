using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Turns the choices of the "Re-run from history" dialog into a run request.</summary>
public static class RerunPlanner
{
    /// <summary>The data rows of a testcase in one earlier run: the failed ones by default.</summary>
    public static IReadOnlyList<RunHistoryRow> RowsOf(IEnumerable<RunHistoryRow> history, string runId, int testcaseIndex)
        => history.Where(r => r.RunId == runId && r.TestcaseIndex == testcaseIndex).ToList();

    /// <param name="testcaseIndex">The testcase being re-run.</param>
    /// <param name="runId">The earlier run whose data (and link) is used.</param>
    /// <param name="rows">The rows chosen. The runner filters by data id, so every iteration and environment of a chosen data row runs.</param>
    /// <param name="allSteps">Every step of the testcase, in run order.</param>
    /// <param name="steps">The chosen steps and, for each, whether it uses the old data (true) or fresh data (false).</param>
    /// <param name="allEnvironments">Every environment the instance has.</param>
    /// <param name="environments">The chosen environments.</param>
    public static RunRequest Build(int testcaseIndex, string runId, IReadOnlyCollection<RunHistoryRow> rows, IReadOnlyList<string> allSteps,
        IReadOnlyDictionary<string, bool> steps, IReadOnlyList<string> allEnvironments, IReadOnlyCollection<string> environments)
    {
        var dataIds = rows.Select(r => r.DataId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var chosenSteps = allSteps.Where(steps.ContainsKey).ToList();
        IReadOnlyDictionary<int, IReadOnlyList<string>>? stepFilter = chosenSteps.Count == allSteps.Count || chosenSteps.Count == 0
            ? null : new Dictionary<int, IReadOnlyList<string>> { [testcaseIndex] = chosenSteps };
        var old = chosenSteps.Where(s => steps[s]).ToList();
        var allOld = chosenSteps.Count > 0 && old.Count == chosenSteps.Count;
        var envs = environments.Count == 0 || environments.Count >= allEnvironments.Count ? null : (IReadOnlyList<string>)environments.ToList();
        return new RunRequest([testcaseIndex], dataIds, Steps: stepFilter,
            Rerun: allOld && stepFilter is null,
            RerunSteps: old.Count > 0 && !(allOld && stepFilter is null) ? new Dictionary<int, IReadOnlyList<string>> { [testcaseIndex] = old } : null,
            RerunOf: runId, Environments: envs);
    }
}
