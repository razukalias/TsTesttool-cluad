using System.Text.Json;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Validation;

public sealed record SelectedTestcaseRun(TestcaseDefinition Testcase, string Environment);

public sealed class RunSelectionPlanner
{
    // Empty step selection means all enabled steps. Explicit JSON must cover every selected testcase.
    // Selected steps retain workbook order; dependencies are never silently added.
    public IReadOnlyList<SelectedTestcaseRun> Build(WorkbookModel workbook, IReadOnlyList<int> indexes,
        string? stepSelectionJson, string? environmentSelection, string? environmentSelectionJson = null,
        IReadOnlyList<string>? availableEnvironments = null)
    {
        if (indexes.Count == 0) throw new InvalidDataException("Select at least one testcase.");
        var cases = workbook.Testcases.Where(t => indexes.Contains(t.TestcaseIndex)).OrderBy(t => t.TestcaseIndex).ToList();
        if (indexes.Any(index => cases.All(t => t.TestcaseIndex != index)))
            throw new InvalidDataException("One or more selected testcase indexes do not exist in the workbook.");
        var filters = ParseSteps(stepSelectionJson);
        if (filters is not null)
        {
            if (filters.Keys.Any(index => !indexes.Contains(index)))
                throw new InvalidDataException("The step selection contains an unselected testcase.");
            foreach (var testcase in cases)
            {
                if (!filters.TryGetValue(testcase.TestcaseIndex, out var names) || names.Count == 0)
                    throw new InvalidDataException($"Select at least one step for testcase {testcase.TestcaseIndex}.");
                var available = testcase.Steps.Where(s => s.Enabled).Select(s => s.StepName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unknown = names.Where(name => !available.Contains(name)).ToList();
                if (unknown.Count > 0)
                    throw new InvalidDataException($"Unknown or disabled step(s) in testcase {testcase.TestcaseIndex}: {string.Join(", ", unknown)}.");
            }
        }
        var chosen = cases.Select(t => new TestcaseDefinition
        {
            TestcaseIndex = t.TestcaseIndex, Testcase = t.Testcase,
            Steps = t.Steps.Where(s => s.Enabled && (filters is null || filters[t.TestcaseIndex].Contains(s.StepName)))
                .OrderBy(s => s.Sequence).ToList()
        }).ToList();
        if (chosen.Any(t => t.Steps.Count == 0))
            throw new InvalidDataException("A selected testcase has no enabled steps. Select an executable testcase.");
        var definedEnvironments = (availableEnvironments ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string[] environments;
        if (!string.IsNullOrWhiteSpace(environmentSelectionJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(environmentSelectionJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
                    throw new InvalidDataException("--environments-json must be an array of strings; an empty string selects the default environment.");
                environments = doc.RootElement.EnumerateArray().Select(e => e.GetString()!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (JsonException ex) { throw new InvalidDataException("Invalid --environments-json.", ex); }
        }
        else environments = string.IsNullOrWhiteSpace(environmentSelection)
            ? (definedEnvironments.Count == 0 ? [""] : definedEnvironments.ToArray())
            : environmentSelection.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (environments.Length == 0) throw new InvalidDataException("Select at least one environment.");
        if (definedEnvironments.Count > 0 && environments.Any(e => e.Length > 0 && !definedEnvironments.Contains(e, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("The environment selection is not available in the central environment list.");
        var work = new List<SelectedTestcaseRun>();
        foreach (var environment in environments)
        foreach (var testcase in chosen)
        {
            work.Add(new SelectedTestcaseRun(new TestcaseDefinition
            { TestcaseIndex = testcase.TestcaseIndex, Testcase = testcase.Testcase, Steps = testcase.Steps }, environment));
        }
        if (work.Count == 0) throw new InvalidDataException("The selected testcases, steps and environments produce no executable work.");
        return work;
    }

    private static Dictionary<int, HashSet<string>>? ParseSteps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("--steps must be a JSON object: {\"testcaseIndex\":[\"stepName\"]}.");
            var result = new Dictionary<int, HashSet<string>>();
            foreach (var group in document.RootElement.EnumerateObject())
            {
                if (!int.TryParse(group.Name, out var index) || result.ContainsKey(index) || group.Value.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Invalid or duplicate testcase key in --steps.");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var element in group.Value.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
                        throw new InvalidDataException("Step names must be nonempty strings.");
                    names.Add(element.GetString()!);
                }
                result.Add(index, names);
            }
            if (result.Count == 0) throw new InvalidDataException("An explicit step selection cannot be empty.");
            return result;
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid --steps JSON. Expected {\"testcaseIndex\":[\"stepName\"]}.", ex); }
    }
}
