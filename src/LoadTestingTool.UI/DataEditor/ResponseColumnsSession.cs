using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI.DataEditor;

public enum ResponseFill { None, ThisRow, AllRows }

/// <summary>One header the window will create (or finds already in the sheet), with the verb it compares with and the value in the response being shown.</summary>
public sealed partial class ResultRow(GeneratedHeader header, string step, string verb, bool existing) : ObservableObject
{
    public GeneratedHeader Header { get; } = header;
    public string Step { get; } = step;
    public bool Existing { get; } = existing;
    public bool CanInclude => !Existing;
    [ObservableProperty] private string _verb = verb;
    [ObservableProperty] private bool _included = !existing;
    public string HeaderText => $"{{{Verb}}}{Step}.{Header.Path}";
    public string Status => Existing ? "already in the sheet" : Header.Kind == GeneratedKind.Count ? "new · count" : "new";
    public string ValueText => Header.Value.Length > 60 ? Header.Value[..60] + "…" : Header.Value;
    partial void OnVerbChanged(string value) => OnPropertyChanged(nameof(HeaderText));
}

/// <summary>
/// What the response part of the "Template and response" window is doing, without any controls: which step and row's response is shown, what the user ticked or
/// flattened in it, the headers that produces with their verbs, and how the cells are filled. <see cref="Apply"/> writes the columns as part of one undo step.
/// </summary>
public sealed class ResponseColumnsSession
{
    private Dictionary<string, ResponseSample> _stored = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _verbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResponseNode?> _trees = new(StringComparer.OrdinalIgnoreCase);

    public DataEditorViewModel Editor { get; }
    public string Step { get; private set; } = string.Empty;
    public IReadOnlyList<string> RowIds { get; }
    public string CurrentRow { get; private set; } = string.Empty;
    public ResponseNode? Tree { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public Dictionary<string, NodeChoice> Choices { get; } = new(StringComparer.Ordinal);
    public string DefaultVerb { get; private set; } = "eq";
    public ResponseFill Fill { get; set; } = ResponseFill.ThisRow;
    public List<ResultRow> Results { get; private set; } = [];
    public bool Truncated { get; private set; }
    public int SkippedDeep { get; private set; }

    public ResponseColumnsSession(DataEditorViewModel editor, string step)
    {
        Editor = editor;
        RowIds = editor.ResponseRowIds();
        CurrentRow = editor.SelectedRow?.CurrentDataId is { } id && RowIds.Contains(id, StringComparer.OrdinalIgnoreCase) ? id : RowIds.FirstOrDefault() ?? string.Empty;
        SetStep(step);
    }

    /// <summary>Changes the step: its stored responses are read again (one scan of the history for all rows) and the choices start over.</summary>
    public void SetStep(string step)
    {
        Step = step;
        Choices.Clear(); _verbs.Clear(); _excluded.Clear(); _overrides.Clear(); _trees.Clear();
        _stored = step.Length == 0 ? new(StringComparer.OrdinalIgnoreCase) : ResponseSamples.LatestPerDataId(Editor.HistoryFolderPath, step);
        Show();
    }

    public bool HasResponse(string dataId) => _overrides.ContainsKey(dataId) || _stored.ContainsKey(dataId);

    public string? BodyFor(string dataId) => _overrides.TryGetValue(dataId, out var text) ? text : _stored.TryGetValue(dataId, out var sample) ? sample.Body : null;

    private ResponseNode? TreeFor(string dataId)
    {
        if (_trees.TryGetValue(dataId, out var cached)) return cached;
        var body = BodyFor(dataId);
        var tree = body is null ? null : ResponseTree.Parse(body, out _);
        _trees[dataId] = tree;
        return tree;
    }

    public void SelectRow(string dataId)
    {
        if (RowIds.Contains(dataId, StringComparer.OrdinalIgnoreCase)) CurrentRow = RowIds.First(r => r.Equals(dataId, StringComparison.OrdinalIgnoreCase));
        Show();
    }

    public void Move(int delta)
    {
        if (RowIds.Count == 0) return;
        var index = Math.Clamp(RowIds.ToList().FindIndex(r => r.Equals(CurrentRow, StringComparison.OrdinalIgnoreCase)) + delta, 0, RowIds.Count - 1);
        SelectRow(RowIds[index]);
    }

    /// <summary>Uses pasted text (or a file's text) as this row's response, instead of the stored one.</summary>
    public void SetOverride(string dataId, string text)
    {
        _overrides[dataId] = text;
        _trees.Remove(dataId);
        if (dataId.Equals(CurrentRow, StringComparison.OrdinalIgnoreCase)) Show();
    }

    private void Show()
    {
        Tree = CurrentRow.Length == 0 ? null : TreeFor(CurrentRow);
        if (CurrentRow.Length == 0) { Note = "This testcase has no data rows."; Source = string.Empty; }
        else if (BodyFor(CurrentRow) is null)
        {
            Note = Step.Length == 0 ? "Choose a step." : $"No stored response for {Step}, row {CurrentRow}. Run the testcase with \"save response bodies to history\" on, or paste a response or pick a file for this row.";
            Source = string.Empty;
        }
        else
        {
            ResponseTree.Parse(BodyFor(CurrentRow)!, out var parseNote);
            Note = parseNote ?? string.Empty;
            Source = _overrides.ContainsKey(CurrentRow) ? "pasted or file response" : $"last run ({_stored[CurrentRow].Source})";
        }
        Recompute();
    }

    // ---- Choices and the headers they produce -------------------------------------------------------------------

    public void Choose(string path, NodeChoice? choice)
    {
        if (choice is null) Choices.Remove(path); else Choices[path] = choice;
        Recompute();
    }

    public bool IsChosen(string path) => Choices.ContainsKey(path);

    public void Recompute()
    {
        var previous = Results.ToDictionary(r => r.Header.Path, StringComparer.OrdinalIgnoreCase);
        if (Tree is null) { Results = []; Truncated = false; SkippedDeep = 0; return; }
        var generated = ResponseTree.Generate(Tree, Choices, out var truncated, out var skipped);
        Truncated = truncated; SkippedDeep = skipped;
        Results = generated.Select(h =>
        {
            var existing = Editor.ResponseColumnExists(Step, h.Path);
            var verb = _verbs.TryGetValue(h.Path, out var own) ? own : DefaultVerb;
            var row = new ResultRow(h, Step, verb, existing) { Included = !existing && !_excluded.Contains(h.Path) };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ResultRow.Verb)) _verbs[h.Path] = row.Verb;
                if (e.PropertyName == nameof(ResultRow.Included)) { if (row.Included) _excluded.Remove(h.Path); else _excluded.Add(h.Path); }
            };
            return row;
        }).ToList();
    }

    /// <summary>The verb for headers whose verb was not set by hand.</summary>
    public void SetDefaultVerb(string verb)
    {
        DefaultVerb = verb;
        var byHand = _verbs.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Results.Where(r => !byHand.Contains(r.Header.Path))) { row.Verb = verb; _verbs.Remove(row.Header.Path); }   // setting Verb marks it "by hand"; these follow the default
    }

    public IReadOnlyList<ResultRow> Included => Results.Where(r => r.Included && !r.Existing).ToList();

    /// <summary>Writes the included headers into the response sheet and fills the cells as chosen. Returns how many columns were added.</summary>
    public int Apply()
    {
        var columns = Included.Select(r => (r.Verb, r.Header.Path)).ToList();
        if (columns.Count == 0) return 0;
        return Editor.AddResponseBodyColumns(Step, columns, Fill, CurrentRow, TreeFor);
    }
}
