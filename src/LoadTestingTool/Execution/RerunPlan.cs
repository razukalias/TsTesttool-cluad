using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

/// <summary>Works out which steps reuse their old values when only some steps are ticked for a re-run.</summary>
public static class RerunPlan
{
    /// <summary>
    /// The ticked steps plus every step before the lowest ticked one in the same testcase: the steps that ran before it must see the same values as in
    /// the run being repeated (a step 2 expected value that refers to a step 1 random number has to match what step 1 sent then).
    /// </summary>
    public static HashSet<string> StepsToReuse(WorkbookModel workbook, IReadOnlyDictionary<int, IReadOnlyList<string>> ticked)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (index, tickedSteps) in ticked)
        {
            foreach (var step in tickedSteps) names.Add(step);
            var steps = workbook.Testcases.FirstOrDefault(t => t.TestcaseIndex == index)?.Steps.Select(s => s.StepName).ToList();
            if (steps is null) continue;
            var lowest = steps.FindIndex(s => tickedSteps.Contains(s, StringComparer.OrdinalIgnoreCase));
            for (var i = 0; i < lowest; i++) names.Add(steps[i]);
        }
        return names;
    }

    /// <summary>The testcase index and step names in --rerun-steps, a JSON object like {"1":["CreateCustomer"]}; empty when absent or not valid.</summary>
    public static Dictionary<int, IReadOnlyList<string>> Parse(string? json)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
                if (int.TryParse(property.Name, out var index))
                    result[index] = property.Value.EnumerateArray().Select(n => n.GetString() ?? string.Empty).Where(n => n.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { /* ignored: no step reuses */ }
        return result;
    }
}
