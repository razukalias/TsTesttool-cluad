namespace LoadTestingTool.UI;

/// <summary>One row of the Bugs tab's statistics: an instance, one of its testcases, or one of their steps, with its known-bug count.</summary>
internal sealed record BugStatNode(string Kind, string Name, string? Testcase, string? Step, int Count, IReadOnlyList<BugStatNode> Children);

/// <summary>Known bugs counted per instance, testcase and step (instance = testcases added up, testcase = steps added up).</summary>
internal static class BugStatistics
{
    /// <summary>Builds the instance, testcase and step rows from the assertions of a run. Only failing tagged cells (KNOWN_BUG) count; "may be fixed" entries are not bugs.</summary>
    public static BugStatNode Build(string instanceName, IEnumerable<AssertionInfo> assertions)
    {
        var testcases = assertions
            .GroupBy(a => a.Testcase)
            .OrderBy(g => g.Min(a => a.TestcaseIndex)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var steps = group.GroupBy(a => a.StepName)
                    .Select(s => new BugStatNode("Step", s.Key, group.Key, s.Key, s.Count(a => a.Result == "KNOWN_BUG"), []))
                    .ToList();
                return new BugStatNode("Testcase", group.Key, group.Key, null, steps.Sum(s => s.Count), steps);
            })
            .ToList();
        return new BugStatNode("Instance", instanceName, null, null, testcases.Sum(t => t.Count), testcases);
    }
}
