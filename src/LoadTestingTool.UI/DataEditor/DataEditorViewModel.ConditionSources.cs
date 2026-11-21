using LoadTestingTool.Editing;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

// What a condition can look at besides the response of an earlier step: the data of the current row, the variables of the loops the step sits in.
public sealed partial class DataEditorViewModel
{
    /// <summary>The request data columns the condition of a step can read as <c>&lt;From_request_Column_r&gt;</c> (the current data row's cells), without the bookkeeping columns.</summary>
    public IReadOnlyList<string> RequestDataTargets()
        => _document.Request.Headers.Where(h => h.Length > 0 && !ReservedColumns.Contains(h) && !h.Equals("dontrun", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// The variables a step can use as <c>_name_</c> because of the loops it is in (its own loop counts: a For's stop-when sees its counter):
    /// For gives its variable (i), ForEach its item and index, While <c>round</c>; the run's environment is always there as <c>env</c>.
    /// </summary>
    public IReadOnlyList<string> LoopVariablesFor(string? stepName)
    {
        var names = new List<string>();
        var rows = _document.Config.Rows.Where(r => !r.IsBlank && (SelectedTestcase is not { } t || WorkbookDocument.EffectiveIndex(r) == t.Index)).ToList();
        var current = rows.FirstOrDefault(r => string.Equals(r.Get("stepname").Trim(), stepName?.Trim(), StringComparison.OrdinalIgnoreCase));
        var seen = new HashSet<LoadTestingTool.Editing.TableRow>();
        while (current is not null && seen.Add(current))
        {
            var config = ControlConfig.Parse(current.Get("stepconfig"));
            switch (current.Get("steptype").Trim().ToLowerInvariant())
            {
                case "for": names.Add(config.GetValueOrDefault("var", "i")); break;
                case "foreach": names.Add(config.GetValueOrDefault("var", "item")); names.Add(config.GetValueOrDefault("index", "index")); break;
                case "while": names.Add("round"); break;
            }
            var parent = current.Get("parent").Trim();
            if (parent.EndsWith(":else", StringComparison.OrdinalIgnoreCase)) parent = parent[..^5].Trim();
            current = parent.Length == 0 ? null : rows.FirstOrDefault(r => string.Equals(r.Get("stepname").Trim(), parent, StringComparison.OrdinalIgnoreCase));
        }
        names.Add("env");
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
