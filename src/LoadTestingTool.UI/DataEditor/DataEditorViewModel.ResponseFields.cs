using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>A field of a response that can become an expected-value column.</summary>
public sealed partial class ResponseFieldChoice(string step, ResponseField field) : ObservableObject
{
    public string Step { get; } = step;
    public string Path { get; } = field.Path;
    public string Value { get; } = field.Value;
    public string Header => $"{Step}.{Path}";
    public string Display => Value.Length == 0 ? Path : $"{Path}  =  {(Value.Length > 40 ? Value[..40] + "…" : Value)}";
    [ObservableProperty] private bool _isChosen = true;
}

/// <summary>An HTTP response header that can become an expected-value column, <c>Step.responseHeader.Name</c>.</summary>
public sealed partial class ResponseHeaderChoice(string step, string name, string value) : ObservableObject
{
    public string Step { get; } = step;
    public string Name { get; } = name;
    public string Value { get; } = value;
    public string Path => LoadTestingTool.Execution.AssertionEngine.ResponseHeaderPrefix + Name;
    public string Header => $"{Step}.{Path}";
    public string Display => Value.Length == 0 ? Name : $"{Name}  =  {(Value.Length > 40 ? Value[..40] + "…" : Value)}";
    // volatile headers (Date, Set-Cookie, Server-Timing …) make poor expectations, so they start unticked
    [ObservableProperty] private bool _isChosen = !Volatile.Contains(name);
    private static readonly HashSet<string> Volatile = new(["Date", "Set-Cookie", "Age", "ETag", "Last-Modified", "Expires", "Content-Length", "Server-Timing", "X-Request-Id", "X-Correlation-Id", "Traceparent", "Request-Context"], StringComparer.OrdinalIgnoreCase);
}

/// <summary>Expected fields and the From menu are filled from what a step answered in earlier runs (or a sample file).</summary>
public sealed partial class DataEditorViewModel
{
    public ObservableCollection<ResponseFieldChoice> ResponseFields { get; } = [];

    /// <summary>Where the listed fields came from, or why there are none.</summary>
    [ObservableProperty] private string _responseFieldsNote = string.Empty;

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

    /// <summary>
    /// Lists the fields of a step's response that have no expected column yet. The source is a sample file when one is given,
    /// else the newest stored response of the step (the selected row's data ID first).
    /// </summary>
    public void FindResponseFields(string step, string? sampleFile = null)
    {
        ResponseFields.Clear();
        if (step.Length == 0) { ResponseFieldsNote = "Choose a step."; return; }
        string? body; string source;
        if (sampleFile is not null)
        {
            try { body = File.ReadAllText(sampleFile); source = $"sample file {Path.GetFileName(sampleFile)}"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ResponseFieldsNote = $"Could not read the file: {ex.Message}"; return; }
        }
        else
        {
            var stored = ResponseSamples.Latest(HistoryFolderPath, step, SelectedRow?.CurrentDataId);
            body = stored?.Body;
            source = stored is null ? string.Empty : $"the last run's response ({stored.Source}, data ID {stored.DataId})";
        }
        if (body is null) { ResponseFieldsNote = $"No stored response for {step}. Run the testcase with \"save response bodies to history\" on, or choose a sample file."; return; }
        var fields = ResponseSamples.Flatten(body);
        if (fields.Count == 0) { ResponseFieldsNote = $"The response from {source} is not JSON, or has no values."; return; }
        foreach (var field in fields)
            if (!_document.Response.Headers.Any(h => SamePath(h, step, field.Path)))
                ResponseFields.Add(new ResponseFieldChoice(step, field));
        ResponseFieldsNote = ResponseFields.Count == 0
            ? $"Every field of {source} already has an expected column."
            : $"{ResponseFields.Count} fields from {source}.";
    }

    /// <summary>Adds the ticked fields as <c>{eq}Step.path</c> columns; optionally fills the selected row's expected value from the response.</summary>
    public int AddResponseFields(bool fillSelectedRow)
    {
        var chosen = ResponseFields.Where(f => f.IsChosen).ToList();
        if (chosen.Count == 0) return 0;
        var dataId = SelectedRow?.CurrentDataId;
        var testcaseIndex = SelectedRow?.TestcaseIndex;
        Record($"add {chosen.Count} expected fields");
        foreach (var field in chosen) _document.Response.EnsureColumn($"{{eq}}{field.Header}");
        ResponseFields.Clear();
        MarkChanged();
        BuildRows();
        if (fillSelectedRow && dataId is not null)
        {
            SelectedRow = _allRows.FirstOrDefault(r => r.CurrentDataId == dataId && r.TestcaseIndex == testcaseIndex) ?? SelectedRow;
            if (SelectedRow is { } row)
                foreach (var field in chosen)
                    if (Columns.FirstOrDefault(c => c.Group == ColumnGroup.Response && SamePathHeader(c.Title, field.Header)) is { } column)
                        row.Cells[column.CellIndex] = field.Value;
        }
        ShowStatus($"Added {chosen.Count} expected {(chosen.Count == 1 ? "column" : "columns")}{(fillSelectedRow ? " and filled them for the selected row" : string.Empty)}: {string.Join(", ", chosen.Take(4).Select(c => c.Header))}{(chosen.Count > 4 ? ", …" : string.Empty)}.", isError: false);
        return chosen.Count;
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
        foreach (var header in chosen) _document.Response.EnsureColumn($"{{eq}}{header.Header}");
        ResponseHeaderChoices.Clear();
        MarkChanged();
        BuildRows();
        if (fillSelectedRow && dataId is not null)
        {
            SelectedRow = _allRows.FirstOrDefault(r => r.CurrentDataId == dataId && r.TestcaseIndex == testcaseIndex) ?? SelectedRow;
            if (SelectedRow is { } row)
                foreach (var header in chosen)
                    if (Columns.FirstOrDefault(c => c.Group == ColumnGroup.Response && SamePathHeader(c.Title, header.Header)) is { } column)
                        row.Cells[column.CellIndex] = header.Value;
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

    /// <summary>Forget stored responses so the next menu reads the newest history (after a run or when Record view opens).</summary>
    private void ForgetStoredResponses() => _storedResponses = null;
}
