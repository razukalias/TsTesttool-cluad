using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One data column a step's template uses that the request sheet does not have yet.</summary>
public sealed partial class TemplateColumnChoice(string step, string name) : ObservableObject
{
    public string Step { get; } = step;
    public string Name { get; } = name;
    public string Header => $"{Step}.{Name}";
    [ObservableProperty] private bool _isChosen = true;
}

public sealed partial class DataEditorViewModel
{
    public ObservableCollection<TemplateColumnChoice> TemplateColumns { get; } = [];

    /// <summary>Lists the <c>_variable_</c> placeholders of the testcase's templates that have no column yet (all steps of the instance in instance view).</summary>
    public void FindTemplateColumns()
    {
        TemplateColumns.Clear();
        var rows = _document.Config.Rows.Where(r => !r.IsBlank && (SelectedTestcase is not { } t || WorkbookDocument.EffectiveIndex(r) == t.Index));
        foreach (var row in rows)
        {
            var step = row.Get("stepname").Trim();
            var template = row.Get("templetsource").Trim();
            if (step.Length == 0 || template.Length == 0) continue;
            string text;
            try
            {
                var file = TemplateEditorWindow.ResolveInside(TemplatesFolder, template);
                if (!File.Exists(file)) continue;
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
            foreach (var name in TemplateVariables.Find(text))
                if (_document.Request.FindColumn($"{step}.{name}") < 0 && TemplateColumns.All(c => !c.Header.Equals($"{step}.{name}", StringComparison.OrdinalIgnoreCase)))
                    TemplateColumns.Add(new TemplateColumnChoice(step, name));
        }
    }

    /// <summary>Adds the ticked columns to the request sheet, named <c>Step.variable</c>.</summary>
    public int AddTemplateColumns()
    {
        var chosen = TemplateColumns.Where(c => c.IsChosen).ToList();
        if (chosen.Count == 0) return 0;
        Record($"add {chosen.Count} columns from templates");
        foreach (var column in chosen) _document.Request.EnsureColumn(column.Header);
        TemplateColumns.Clear();
        MarkChanged();
        BuildRows();
        ShowStatus($"Added {chosen.Count} {(chosen.Count == 1 ? "column" : "columns")} from the templates: {string.Join(", ", chosen.Select(c => c.Header))}.", isError: false);
        return chosen.Count;
    }

    /// <summary>Which rows a value is copied to.</summary>
    public enum CopyScope { TestcaseRows, ShownRows, AllRowsOfInstance }

    /// <summary>
    /// Writes one line's stored text into the same column of other rows (the row itself is kept). A value with a random number,
    /// GUID or date gives every row its own value at run time, which is what load-test data needs.
    /// </summary>
    public int CopyLineToRows(RecordLineViewModel line, CopyScope scope)
    {
        if (SelectedRow is not { } source) return 0;
        var column = Columns.FirstOrDefault(c => c.Title == line.FullTitle && c.Group == (line.IsExpected ? ColumnGroup.Response : ColumnGroup.Request));
        if (column is null) return 0;
        var text = source.Cells[column.CellIndex] ?? string.Empty;
        var targets = scope switch
        {
            CopyScope.ShownRows => VisibleRows.ToList(),
            CopyScope.AllRowsOfInstance => _allRows.ToList(),
            _ => _allRows.Where(r => r.TestcaseIndex == source.TestcaseIndex).ToList(),
        };
        targets.Remove(source);
        if (targets.Count == 0) { ShowStatus("There are no other rows to copy to.", isError: false); return 0; }
        var selected = source;
        foreach (var target in targets) target.Cells[column.CellIndex] = text;   // each write is an undo step like a typed edit
        SelectedRow = selected;
        ShowStatus($"Copied {line.Title} to {targets.Count} other {(targets.Count == 1 ? "row" : "rows")}.", isError: false);
        return targets.Count;
    }

    /// <summary>
    /// Problems with a line's references: a column that does not exist, a step that is not in the workbook, or a response
    /// value from a step that runs after this one.
    /// </summary>
    internal List<string> LineWarnings(IEnumerable<ValuePart> parts, string? ownStep)
    {
        var warnings = new List<string>();
        var order = _document.Config.Rows.Where(r => !r.IsBlank && (SelectedTestcase is not { } t || WorkbookDocument.EffectiveIndex(r) == t.Index))
            .Select(r => r.Get("stepname").Trim()).ToList();
        foreach (var part in parts.Where(p => p.Kind == ValuePartKind.Reference))
        {
            var match = System.Text.RegularExpressions.Regex.Match(part.Raw, @"^<From_(request|response|config)_(.+)_(r|\d+)>$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var sheet = match.Groups[1].Value.ToLowerInvariant();
            var header = match.Groups[2].Value;
            if (sheet == "response")
            {
                var step = header.Contains('.') ? header[..header.IndexOf('.')] : header;
                var at = order.FindIndex(n => n.Equals(step, StringComparison.OrdinalIgnoreCase));
                var own = ownStep is null ? -1 : order.FindIndex(n => n.Equals(ownStep, StringComparison.OrdinalIgnoreCase));
                if (at < 0) warnings.Add($"There is no step named {step} for {part.Label}.");
                else if (own >= 0 && at >= own) warnings.Add($"{step} runs after this step (or is this step), so its response is not available yet.");
            }
            else
            {
                var table = sheet == "request" ? _document.Request : _document.Config;
                if (table.FindColumn(header) < 0) warnings.Add($"The {sheet} sheet has no column {header}.");
            }
        }
        return warnings;
    }

    /// <summary>The warnings function for a column: the known-bug tag check, then the dontrun or reference checks.</summary>
    internal Func<IEnumerable<ValuePart>, string?, List<string>> WarningsFor(bool expected, bool dontRun)
    {
        var inner = dontRun ? (Func<IEnumerable<ValuePart>, string?, List<string>>)DontRunWarnings : LineWarnings;
        return (parts, step) => BugTagWarnings(ValueParts.Format(parts), expected).Concat(inner(parts, step)).ToList();
    }

    /// <summary>What looks wrong about a <c>&lt;bug: environments; description&gt;</c> tag: not written as a tag, no environment, or on a request cell where it cannot do anything.</summary>
    public static List<string> BugTagWarnings(string text, bool expected)
    {
        var warnings = new List<string>();
        if (text.IndexOf("<bug", StringComparison.OrdinalIgnoreCase) < 0) return warnings;
        var (_, tag) = LoadTestingTool.Execution.BugTag.Parse(text);
        if (tag is null) warnings.Add("This looks like a bug tag but is not written as one. Use <bug: dev,test; description> (or all instead of the environments).");
        else if (tag.Environments.Count == 0) warnings.Add("The bug tag lists no environment, so it does nothing. Write the environments (dev,test) or all before the semicolon.");
        else if (!expected) warnings.Add("A bug tag only works on an expected-value cell (it makes a failing assertion a known bug), so it does nothing here.");
        return warnings;
    }

    /// <summary>What looks wrong about a <c>dontrun</c> value: a name that is no step of the testcase (a typo skips nothing), or every step listed.</summary>
    internal List<string> DontRunWarnings(IEnumerable<ValuePart> parts, string? ownStep)
    {
        var warnings = new List<string>();
        var steps = ResponseSteps();
        if (steps.Count == 0) return warnings;
        var listed = RecordLineViewModel.ListedIn(ValueParts.Format(parts));
        var unknown = listed.Where(n => !steps.Contains(n, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0) warnings.Add($"{string.Join(", ", unknown)} {(unknown.Count == 1 ? "is not a step" : "are not steps")} of this testcase, so {(unknown.Count == 1 ? "it skips" : "they skip")} nothing. Steps: {string.Join(", ", steps)}.");
        if (steps.All(s => listed.Contains(s, StringComparer.OrdinalIgnoreCase))) warnings.Add("Every step is listed, so this row runs no steps at all.");
        return warnings;
    }
}
