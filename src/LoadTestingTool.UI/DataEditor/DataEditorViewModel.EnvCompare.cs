namespace LoadTestingTool.UI.DataEditor;

/// <summary>One column (an expected value, or a whole step) and how each environment fared on it.</summary>
/// <param name="Title">"Step.path" as the header spells it, or "Step (step)" for a step that failed on its own.</param>
/// <param name="CellIndex">Grid position of the column (0 is the data ID), or -1 when it is not shown.</param>
/// <param name="StatusByEnvironment">PASS, FAIL, ERROR, BUG, or — (not checked there).</param>
public sealed record ComparedColumn(string Title, int CellIndex, IReadOnlyDictionary<string, string> StatusByEnvironment)
{
    /// <summary>"failed in test, ok in dev, prod": the environments grouped by what happened.</summary>
    public string Summary
    {
        get
        {
            var groups = StatusByEnvironment.Where(p => p.Value != "—").GroupBy(p => p.Value).OrderBy(g => g.Key == "PASS" ? 1 : 0).ThenBy(g => g.Key, StringComparer.Ordinal);
            return string.Join(", ", groups.Select(g => $"{Word(g.Key)} in {string.Join(", ", g.Select(p => p.Key))}"));
        }
    }

    private static string Word(string status) => status switch { "PASS" => "ok", "FAIL" => "failed", "ERROR" => "error", "BUG" => "known bug", _ => status };
}

/// <summary>A row seen across the environments of the latest run: its status in each, and which columns agree.</summary>
public sealed class EnvComparison
{
    public required IReadOnlyList<EnvChip> Environments { get; init; }

    /// <summary>Columns whose outcome differs between the environments that checked them.</summary>
    public required IReadOnlyList<ComparedColumn> Differs { get; init; }

    /// <summary>Columns that went wrong in every environment that checked them: usually a wrong expected value, not an environment problem.</summary>
    public required IReadOnlyList<ComparedColumn> SameFailing { get; init; }

    /// <summary>Columns that passed everywhere.</summary>
    public required IReadOnlyList<ComparedColumn> PassedEverywhere { get; init; }

    /// <summary>The run logged no passing assertions, so only the failed columns can be listed.</summary>
    public bool PassesUnknown { get; init; }
}

// Comparing one row between the environments of a multi-environment run.
public sealed partial class DataEditorViewModel
{
    /// <summary>Works out, for one row, how every environment of the latest run fared on every column.</summary>
    public EnvComparison? CompareEnvironments(DataRowViewModel row)
    {
        if (_results is not { Environments.Count: > 1 } results || row.AllOutcome is not { } whole) return null;
        var environments = results.Environments;
        var perEnvironment = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);   // environment -> column key -> status
        var titles = new Dictionary<string, (string Title, int Cell)>(StringComparer.OrdinalIgnoreCase);
        var anyPassed = false;

        foreach (var environment in environments)
        {
            var states = perEnvironment[environment] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (whole.In(environment) is not { } outcome) continue;
            foreach (var column in outcome.PassedColumns) { states[column] = "PASS"; Remember(column, null); anyPassed = true; }
            foreach (var column in outcome.BugColumns) { states[column] = "BUG"; Remember(column, null); }
            foreach (var failure in outcome.Failures)
            {
                if (failure.IsAssertion)
                {
                    var key = $"{failure.StepName}.{failure.ResponsePath}";
                    states[key] = IsKnownBug(row, failure) ? "BUG" : "FAIL";
                    Remember(key, failure);
                }
                else
                {
                    var key = $"{failure.StepName} (step)";
                    states[key] = failure.IsError ? "ERROR" : "FAIL";
                    Remember(key, failure);
                }
            }
        }

        void Remember(string key, FailureDetail? failure)
        {
            if (titles.ContainsKey(key)) return;
            if (failure is { IsAssertion: false }) { titles[key] = (key, FindStepColumn(failure.StepName)); return; }
            var column = Columns.FirstOrDefault(c => c.Group == ColumnGroup.Response && string.Equals(StripVerb(c.Title), key, StringComparison.OrdinalIgnoreCase));
            titles[key] = (column is not null ? StripVerb(column.Title) : key, column?.CellIndex ?? -1);
        }

        var differs = new List<ComparedColumn>(); var same = new List<ComparedColumn>(); var passed = new List<ComparedColumn>();
        foreach (var (key, (title, cell)) in titles.OrderBy(t => t.Value.Cell < 0 ? int.MaxValue : t.Value.Cell).ThenBy(t => t.Value.Title, StringComparer.OrdinalIgnoreCase))
        {
            var status = environments.ToDictionary(e => e, e => perEnvironment[e].GetValueOrDefault(key, "—"), StringComparer.OrdinalIgnoreCase);
            var checkedIn = status.Values.Where(v => v != "—").Distinct().ToList();
            if (checkedIn.Count == 0) continue;
            var compared = new ComparedColumn(title, cell, status);
            if (checkedIn.Count > 1) differs.Add(compared);
            else if (checkedIn[0] == "PASS") passed.Add(compared);
            else same.Add(compared);
        }

        return new EnvComparison
        {
            Environments = environments.Select(e => { var s = row.StatusIn(e); return new EnvChip(e, s, $"{DataRowViewModel.SymbolOf(s)} {e}", DataRowViewModel.ColorOf(s)); }).ToList(),
            Differs = differs, SameFailing = same, PassedEverywhere = passed, PassesUnknown = !anyPassed
        };
    }

    /// <summary>Switches the table to one environment (what clicking a chip in the comparison does).</summary>
    public void ShowEnvironment(string environment)
    {
        if (EnvironmentOptions.Contains(environment, StringComparer.OrdinalIgnoreCase)) EnvironmentChoice = EnvironmentOptions.First(o => string.Equals(o, environment, StringComparison.OrdinalIgnoreCase));
    }
}
