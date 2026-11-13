using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

// Creating data columns for a step: a request variable (Step.name) or an expected value ({verb}Step.path).
public sealed partial class DataEditorViewModel
{
    public const string RequestKind = "Request value";
    public const string ExpectedKind = "Expected value";

    public static IReadOnlyList<string> ColumnKinds { get; } = [RequestKind, ExpectedKind];

    public static IReadOnlyList<string> AssertionVerbs { get; } =
        ["eq", "ne", "lt", "lte", "gt", "gte", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "in", "size"];

    public ObservableCollection<string> NewColumnSteps { get; } = [];

    [ObservableProperty] private bool _addColumnVisible;
    [ObservableProperty] private string? _newColumnStep;
    [ObservableProperty] private string _newColumnVerb = "eq";
    [ObservableProperty] private string _newColumnName = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(NewColumnIsExpected), nameof(NewColumnHint))]
    private string _newColumnKind = RequestKind;

    public bool NewColumnIsExpected => NewColumnKind == ExpectedKind;
    public string NewColumnHint => NewColumnIsExpected ? "Response path, e.g. id or data.user.name" : "Variable name, e.g. title";

    public const string PlaceAtEnd = "At the end";
    public const string PlaceBeforeCurrent = "Before the selected column";

    /// <summary>Where the new column goes: after the last column of its sheet, or directly before the column whose cell you last clicked.</summary>
    [ObservableProperty] private string _newColumnPlacement = PlaceAtEnd;

    public IReadOnlyList<string> PlacementOptions { get; } = [PlaceAtEnd, PlaceBeforeCurrent];

    private GridColumn? CurrentColumn => CurrentCellIndex > 0 && CurrentCellIndex <= Columns.Count ? Columns[CurrentCellIndex - 1] : null;

    [RelayCommand]
    private void OpenAddColumn()
    {
        if (SelectedTestcase is not { } testcase) return;
        NewColumnSteps.Clear();
        foreach (var step in testcase.Steps) NewColumnSteps.Add(step);
        NewColumnStep = SelectedStep is { } selected && NewColumnSteps.Contains(selected.CurrentName) ? selected.CurrentName : NewColumnSteps.LastOrDefault();
        NewColumnName = string.Empty;
        AddColumnVisible = true;
    }

    [RelayCommand]
    private void CancelAddColumn() => AddColumnVisible = false;

    [RelayCommand]
    private void ConfirmAddColumn()
    {
        var step = NewColumnStep?.Trim() ?? string.Empty;
        var name = NewColumnName.Trim();
        if (step.Length == 0) { ShowStatus("Choose the step the column belongs to. Add the step on the Steps tab first.", isError: true); return; }
        if (name.StartsWith(step + ".", StringComparison.OrdinalIgnoreCase)) name = name[(step.Length + 1)..];
        if (name.Length == 0 || name.StartsWith('.') || name.EndsWith('.')) { ShowStatus("Enter a name for the column.", isError: true); return; }

        var expected = NewColumnIsExpected;
        var header = expected ? $"{{{NewColumnVerb}}}{step}.{name}" : $"{step}.{name}";
        var table = expected ? _document.Response : _document.Request;
        var clash = expected
            ? table.Headers.Any(h => SamePath(h, step, name))
            : table.FindColumn(header) >= 0;
        if (clash) { ShowStatus($"The {(expected ? "response" : "request")} sheet already has a column for {step}.{name}.", isError: true); return; }

        var before = NewColumnPlacement == PlaceBeforeCurrent && CurrentColumn is { } current && current.Group == (expected ? ColumnGroup.Response : ColumnGroup.Request) ? current : null;
        Record($"add column {header}");
        string where;
        if (before is not null)
        {
            table.InsertColumnBefore(before.SheetColumn, header);
            where = $"directly before {before.Title}. Saving inserts a real column there; columns to its right move right";
        }
        else
        {
            table.EnsureColumn(header);
            where = $"It is the last {(expected ? "expected" : "request")} column";
        }
        AddColumnVisible = false;
        MarkChanged();
        BuildRows();
        ShowStatus($"Added column {header} to the {(expected ? "response" : "request")} sheet, {where}; fill in the values per row.", isError: false);
    }

    /// <summary>An expected-value column is identified by step and path; the verb in braces is only how it compares.</summary>
    private static bool SamePath(string header, string step, string path)
        => ResponseStepPrefix(header) is { } prefix
           && string.Equals(prefix, step, StringComparison.OrdinalIgnoreCase)
           && string.Equals(StripVerb(header), $"{step}.{path}", StringComparison.OrdinalIgnoreCase);

    private static string StripVerb(string header)
    {
        var text = header.Trim();
        return text.StartsWith('{') && text.IndexOf('}') is var closing and > 0 ? text[(closing + 1)..].Trim() : text;
    }

    // ---- Rename / remove the selected column -------------------------------------------------

    [ObservableProperty] private bool _editColumnVisible;
    [ObservableProperty] private string _editColumnName = string.Empty;
    [ObservableProperty] private string _editColumnTitle = string.Empty;
    [ObservableProperty] private string _editColumnNote = string.Empty;

    [RelayCommand]
    private void OpenEditColumn()
    {
        if (CurrentColumn is not { } column || SelectedTestcase is null)
        {
            ShowStatus("Click a cell in the column you want to rename or remove first.", isError: true);
            return;
        }
        var sheet = TableOf(column);
        var header = sheet.Headers[column.SheetColumn];
        EditColumnTitle = $"{(column.Group == ColumnGroup.Response ? "response" : "request")} · {header}";
        EditColumnName = header;
        var refs = _document.FindColumnReferences(sheet.SheetName, header).Count;
        EditColumnNote = refs == 0
            ? "No <From_…> reference points at this column."
            : $"{refs} <From_…> {(refs == 1 ? "reference points" : "references point")} at it. Renaming updates them when you save; removing it is refused while they exist.";
        EditColumnVisible = true;
    }

    [RelayCommand]
    private void CloseEditColumn() => EditColumnVisible = false;

    private LoadTestingTool.Editing.SheetTable TableOf(GridColumn column) => column.Group == ColumnGroup.Response ? _document.Response : _document.Request;

    [RelayCommand]
    private void ConfirmRenameColumn()
    {
        if (CurrentColumn is not { } column) return;
        var table = TableOf(column);
        var old = table.Headers[column.SheetColumn];
        var name = EditColumnName.Trim();
        if (name.Length == 0) { ShowStatus("Enter the new column name.", isError: true); return; }
        if (name == old) { EditColumnVisible = false; return; }
        if (table.FindColumn(name) >= 0) { ShowStatus($"The {table.SheetName} sheet already has a column '{name}'.", isError: true); return; }
        Record($"rename column {old} to {name}");
        table.RenameColumn(column.SheetColumn, name);
        EditColumnVisible = false;
        MarkChanged();
        BuildRows();
        ShowStatus($"Renamed {old} to {name}. Templates still use _variable_ names — check the Preview if this was a request column.", isError: false);
    }

    [RelayCommand]
    private void ConfirmRemoveColumn()
    {
        if (CurrentColumn is not { } column) return;
        var table = TableOf(column);
        var header = table.Headers[column.SheetColumn];
        var refs = _document.FindColumnReferences(table.SheetName, header);
        if (refs.Count > 0)
        {
            ShowStatus($"Cannot remove {header}: {refs.Count} <From_…> {(refs.Count == 1 ? "reference" : "references")} point at it (for example {refs[0].Sheet}!{refs[0].Cell}). Change those first.", isError: true);
            return;
        }
        Record($"remove column {header}");
        table.RemoveColumn(column.SheetColumn);
        EditColumnVisible = false;
        MarkChanged();
        BuildRows();
        ShowStatus($"Removed column {header}. Saving empties it in the sheet; the columns after it stay where they are.", isError: false);
    }
}
