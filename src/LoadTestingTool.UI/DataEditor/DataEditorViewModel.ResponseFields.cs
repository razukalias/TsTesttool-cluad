using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>An HTTP response header that can become an expected-value column, <c>Step.responseHeader.Name</c>.</summary>
public sealed partial class ResponseHeaderChoice(string step, string name, string value) : ObservableObject
{
    public string Step { get; } = step;
    public string Name { get; } = name;
    public string Value { get; } = value;
    public string Path => LoadTestingTool.Execution.AssertionEngine.ResponseHeaderPrefix + Name;
    public string Header => $"{Step}.{Path}";

    /// <summary>The value the new column gets for the selected row: the header's value, or what the ✦ wizard built.</summary>
    public NewColumnWizard.Cell Cell { get; } = new(value);
    public string WizardTitle => $"{{{Verb}}}{Header}";
    public string Display => Value.Length == 0 ? Name : $"{Name}  =  {(Value.Length > 40 ? Value[..40] + "…" : Value)}";
    // volatile headers (Date, Set-Cookie, Server-Timing …) make poor expectations, so they start unticked
    [ObservableProperty] private bool _isChosen = !Volatile.Contains(name);
    /// <summary>How the header is compared (the <c>{verb}</c> of the column).</summary>
    [ObservableProperty] private string _verb = "eq";
    private static readonly HashSet<string> Volatile = new(["Date", "Set-Cookie", "Age", "ETag", "Last-Modified", "Expires", "Content-Length", "Server-Timing", "X-Request-Id", "X-Correlation-Id", "Traceparent", "Request-Context"], StringComparer.OrdinalIgnoreCase);
}

/// <summary>Expected fields and the From menu are filled from what a step answered in earlier runs (or a sample file).</summary>
public sealed partial class DataEditorViewModel
{
    private Dictionary<string, List<ResponseField>>? _storedResponses;

    /// <summary>The history folder: the dashboard's configured one when the editor is hosted there, else <c>History</c> beside the workbook.</summary>
    public string HistoryFolderPath
        => RerunContextProvider?.Invoke()?.HistoryFolder is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(WorkbookPath)) ?? ".", "History");

    /// <summary>The steps of the selected testcase (every step in instance view) in run order.</summary>
    public IReadOnlyList<string> ResponseSteps()
        => _document.Config.Rows.Where(r => !r.IsBlank && (SelectedTestcase is not { } t || WorkbookDocument.EffectiveIndex(r) == t.Index))
            .Select(r => r.Get("stepname").Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The Data IDs of the rows of the selected testcase (all rows in instance view), in order: the rows whose responses the window can show.</summary>
    public IReadOnlyList<string> ResponseRowIds() => _allRows.Select(r => r.CurrentDataId).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The response sheet already has an expected column for this step and path (whatever its verb).</summary>
    public bool ResponseColumnExists(string step, string path) => _document.Response.Headers.Any(h => SamePath(h, step, path));

    /// <summary>
    /// Adds <c>{verb}Step.path</c> expected columns to the response sheet and fills their cells from the responses: none, the row shown (<paramref name="currentDataId"/>),
    /// or every row from its own response (<paramref name="treeFor"/> gives a row's response). A row whose response lacks the path keeps an empty cell.
    /// </summary>
    public int AddResponseBodyColumns(string step, IReadOnlyList<(string Verb, string Path)> columns, ResponseFill fill, string? currentDataId, Func<string, LoadTestingTool.Reporting.ResponseNode?> treeFor, IReadOnlyDictionary<string, string>? typed = null)
    {
        if (columns.Count == 0) return 0;
        var keepId = SelectedRow?.CurrentDataId;
        var keepTestcase = SelectedRow?.TestcaseIndex;
        Record($"add {columns.Count} expected {(columns.Count == 1 ? "column" : "columns")} from the response");
        foreach (var (verb, path) in columns) _document.Response.EnsureColumn($"{{{verb}}}{step}.{path}");
        MarkChanged();
        BuildRows();
        int filled = 0, rowsWithout = 0;
        if (fill != ResponseFill.None)
        {
            var cells = columns.Select(c => (c.Path, Column: Columns.FirstOrDefault(col => col.Group == ColumnGroup.Response && SamePathHeader(col.Title, $"{step}.{c.Path}")))).Where(c => c.Column is not null).ToList();
            foreach (var row in _allRows.Where(r => fill == ResponseFill.AllRows || r.CurrentDataId.Equals(currentDataId, StringComparison.OrdinalIgnoreCase)))
            {
                var tree = treeFor(row.CurrentDataId);
                if (tree is null) { rowsWithout++; continue; }
                foreach (var (path, column) in cells)
                    if (LoadTestingTool.Reporting.ResponseTree.ValueAt(tree, path) is { } value && ApplyCell(row, column!.CellIndex, value) == CellResult.Changed) filled++;
            }
        }
        if (typed is { Count: > 0 })
        {
            // values built in the ✦ wizard: written to the row shown, whatever the fill choice is, and instead of the response value
            foreach (var row in _allRows.Where(r => r.CurrentDataId.Equals(currentDataId, StringComparison.OrdinalIgnoreCase)))
                foreach (var (path, text) in typed)
                    if (Columns.FirstOrDefault(col => col.Group == ColumnGroup.Response && SamePathHeader(col.Title, $"{step}.{path}")) is { } column && ApplyCell(row, column.CellIndex, text) == CellResult.Changed) filled++;
        }
        if (keepId is not null) SelectedRow = _allRows.FirstOrDefault(r => r.CurrentDataId == keepId && r.TestcaseIndex == keepTestcase) ?? SelectedRow;
        var note = fill == ResponseFill.None && typed is not { Count: > 0 } ? string.Empty : $" and filled {filled} {(filled == 1 ? "cell" : "cells")}{(rowsWithout > 0 ? $" ({rowsWithout} {(rowsWithout == 1 ? "row has" : "rows have")} no response)" : string.Empty)}";
        ShowStatus($"Added {columns.Count} expected {(columns.Count == 1 ? "column" : "columns")}{note}: {string.Join(", ", columns.Take(4).Select(c => $"{step}.{c.Path}"))}{(columns.Count > 4 ? ", …" : string.Empty)}.", isError: false);
        return columns.Count;
    }

    /// <summary>Adds the expected-value columns <c>Step.output</c> (and <c>Step.index</c> for a loop) to the response sheet, the ones that are not there yet.</summary>
    public int AddOutputColumns(string step, bool index)
    {
        var wanted = (index ? new[] { "output", "index" } : ["output"]).Where(path => !ResponseColumnExists(step, path)).Select(path => ("eq", path)).ToList();
        if (wanted.Count == 0) { ShowStatus($"{step} already has its output columns in the response sheet.", isError: false); return 0; }
        return AddResponseBodyColumns(step, wanted, ResponseFill.None, null, _ => null);
    }

    public ObservableCollection<ResponseHeaderChoice> ResponseHeaderChoices { get; } = [];
    [ObservableProperty] private string _responseHeadersNote = string.Empty;

    /// <summary>Lists the HTTP response headers of a step's newest stored run that have no expected column yet.</summary>
    public void FindResponseHeaders(string step, string? sampleFile = null)
    {
        ResponseHeaderChoices.Clear();
        if (step.Length == 0) { ResponseHeadersNote = "Choose a step."; return; }
        (List<ResponseSamples.HeaderSample> Headers, string Source)? stored;
        if (sampleFile is not null)
        {
            try { stored = (ResponseSamples.ParseHeaderLines(File.ReadAllText(sampleFile)), $"sample file {Path.GetFileName(sampleFile)}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ResponseHeadersNote = $"Could not read the file: {ex.Message}"; return; }
            if (stored.Value.Headers.Count == 0) { ResponseHeadersNote = "The file has no header lines (Name: value)."; return; }
        }
        else stored = ResponseSamples.LatestHeaders(HistoryFolderPath, step, SelectedRow?.CurrentDataId);
        if (stored is null) { ResponseHeadersNote = $"No stored response headers for {step}. Run the testcase with the request history on (it keeps the headers), then open this again."; return; }
        foreach (var header in stored.Value.Headers)
        {
            var path = LoadTestingTool.Execution.AssertionEngine.ResponseHeaderPrefix + header.Name;
            if (!_document.Response.Headers.Any(h => SamePath(h, step, path)) && ResponseHeaderChoices.All(c => !c.Name.Equals(header.Name, StringComparison.OrdinalIgnoreCase)))
                ResponseHeaderChoices.Add(new ResponseHeaderChoice(step, header.Name, header.Value));
        }
        ResponseHeadersNote = ResponseHeaderChoices.Count == 0 ? $"Every header of {stored.Value.Source} already has an expected column." : $"{ResponseHeaderChoices.Count} headers from {stored.Value.Source}. Changing values such as Date or Set-Cookie start unticked.";
    }

    /// <summary>Adds the ticked headers as <c>{eq}Step.responseHeader.Name</c> columns; optionally fills the selected row's expected value.</summary>
    public int AddResponseHeaders(bool fillSelectedRow)
    {
        var chosen = ResponseHeaderChoices.Where(h => h.IsChosen).ToList();
        if (chosen.Count == 0) return 0;
        var dataId = SelectedRow?.CurrentDataId;
        var testcaseIndex = SelectedRow?.TestcaseIndex;
        Record($"add {chosen.Count} expected response headers");
        foreach (var header in chosen) _document.Response.EnsureColumn($"{{{header.Verb}}}{header.Header}");
        ResponseHeaderChoices.Clear();
        MarkChanged();
        BuildRows();
        if ((fillSelectedRow || chosen.Any(h => h.Cell.Edited)) && dataId is not null)
        {
            SelectedRow = _allRows.FirstOrDefault(r => r.CurrentDataId == dataId && r.TestcaseIndex == testcaseIndex) ?? SelectedRow;
            if (SelectedRow is { } row)
                foreach (var header in chosen.Where(h => fillSelectedRow || h.Cell.Edited))
                    if (Columns.FirstOrDefault(c => c.Group == ColumnGroup.Response && SamePathHeader(c.Title, header.Header)) is { } column)
                        row.Cells[column.CellIndex] = header.Cell.Cells[0];   // the wizard's text when it was used, else the header's value
        }
        ShowStatus($"Added {chosen.Count} expected response {(chosen.Count == 1 ? "header" : "headers")}{(fillSelectedRow ? " and filled them for the selected row" : string.Empty)}: {string.Join(", ", chosen.Take(4).Select(c => c.Header))}{(chosen.Count > 4 ? ", …" : string.Empty)}.", isError: false);
        return chosen.Count;
    }

    private static bool SamePathHeader(string header, string stepDotPath) => string.Equals(StripVerb(header), stepDotPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Response fields for the From menu: the expected columns the workbook has, plus the fields of each step's stored response.</summary>
    private List<string> ResponseFieldNames()
    {
        var names = _document.Response.Headers.Select(h => StripVerb(h)).Where(f => f.Contains('.')).ToList();
        if (_storedResponses is null)
        {
            _storedResponses = new(StringComparer.OrdinalIgnoreCase);
            foreach (var step in ResponseSteps())
                if (ResponseSamples.Latest(HistoryFolderPath, step) is { } sample) _storedResponses[step] = ResponseSamples.Flatten(sample.Body);
        }
        foreach (var (step, fields) in _storedResponses) names.AddRange(fields.Select(f => $"{step}.{f.Path}"));
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The values another step of the testcase can give a condition or a loop: its status code, the fields of its expected response columns
    /// and of its last stored response, and its response headers, as <c>Step.path</c> (the text goes into <c>&lt;From_response_Step.path_r&gt;</c>).
    /// </summary>
    public IReadOnlyList<string> ReferenceTargets(string? exceptStep)
    {
        var targets = new List<string>();
        var steps = ResponseSteps().Where(s => !s.Equals(exceptStep, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var step in steps) targets.Add($"{step}.statusCode");
        foreach (var name in ResponseFieldNames())
            if (steps.Any(s => name.StartsWith(s + ".", StringComparison.OrdinalIgnoreCase))) targets.Add(name);
        foreach (var step in steps)
            if (ResponseSamples.LatestHeaders(HistoryFolderPath, step) is { } stored)
                targets.AddRange(stored.Headers.Select(h => $"{step}.{LoadTestingTool.Execution.AssertionEngine.ResponseHeaderPrefix}{h.Name}"));
        // fields a step saves as output variables are offered even before the step has ever run
        foreach (var row in _document.Config.Rows.Where(r => !r.IsBlank && (SelectedTestcase is not { } t || WorkbookDocument.EffectiveIndex(r) == t.Index)))
        {
            var step = row.Get("stepname").Trim();
            if (!steps.Contains(step, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var (name, source) in LoadTestingTool.Execution.StepOutputs.Parse(row.Get("stepconfig")))
                if (source.Length > 0) targets.Add(LoadTestingTool.Execution.StepOutputs.KeyFor(step, source));
        }
        return targets.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Forget stored responses so the next menu reads the newest history (after a run or when Record view opens).</summary>
    private void ForgetStoredResponses() => _storedResponses = null;
}
