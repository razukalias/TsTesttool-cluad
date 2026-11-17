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
    public const string PlaceAfterCurrent = "After the selected column";

    /// <summary>How many columns the next "Add column" creates; with more than one, 1, 2, 3… is added to the name.</summary>
    [ObservableProperty] private decimal? _newColumnCount = 1;

    /// <summary>Where the new column goes: after the last column of its sheet, or directly before the column whose cell you last clicked.</summary>
    [ObservableProperty] private string _newColumnPlacement = PlaceAtEnd;

    public IReadOnlyList<string> PlacementOptions { get; } = [PlaceAtEnd, PlaceBeforeCurrent, PlaceAfterCurrent];

    private GridColumn? CurrentColumn => CurrentCellIndex > 0 && CurrentCellIndex <= Columns.Count ? Columns[CurrentCellIndex - 1] : null;

    [RelayCommand]
    private void OpenAddColumn()
    {
        if (SelectedTestcase is not { } testcase) return;
        NewColumnSteps.Clear();
        foreach (var step in testcase.Steps) NewColumnSteps.Add(step);
        NewColumnStep = SelectedStep is { } selected && NewColumnSteps.Contains(selected.CurrentName) ? selected.CurrentName : NewColumnSteps.LastOrDefault();
        NewColumnName = string.Empty;
        NewColumnCount = 1;
        AddColumnVisible = true;
    }

    /// <summary>Opens the add-column panel for the column whose header was right-clicked: left or right of it, several at once.</summary>
    public void OpenInsertColumns(bool left, int count)
    {
        if (SelectedTestcase is null) return;
        var current = CurrentColumn;
        OpenAddColumn();
        NewColumnCount = count;
        if (current is null) return;
        NewColumnKind = current.Group == ColumnGroup.Response ? ExpectedKind : RequestKind;
        NewColumnPlacement = left ? PlaceBeforeCurrent : PlaceAfterCurrent;
        var title = StripVerb(current.Title);
        var dot = title.IndexOf('.');
        if (dot > 0 && NewColumnSteps.FirstOrDefault(x => x.Equals(title[..dot], StringComparison.OrdinalIgnoreCase)) is { } step) NewColumnStep = step;
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
        var count = (int)Math.Clamp(NewColumnCount ?? 1, 1, 200);
        var names = count == 1 ? [name] : Enumerable.Range(1, count).Select(i => name + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var headers = names.Select(n => expected ? $"{{{NewColumnVerb}}}{step}.{n}" : $"{step}.{n}").ToList();
        var table = expected ? _document.Response : _document.Request;
        for (var i = 0; i < headers.Count; i++)
        {
            var clash = expected ? table.Headers.Any(h => SamePath(h, step, names[i])) : table.FindColumn(headers[i]) >= 0;
            if (clash) { ShowStatus($"The {(expected ? "response" : "request")} sheet already has a column for {step}.{names[i]}.", isError: true); return; }
        }

        var near = NewColumnPlacement != PlaceAtEnd && CurrentColumn is { } current && current.Group == (expected ? ColumnGroup.Response : ColumnGroup.Request) ? current : null;
        Record(count == 1 ? $"add column {headers[0]}" : $"add {count} columns {headers[0]}…");
        string where;
        if (near is not null)
        {
            var position = NewColumnPlacement == PlaceAfterCurrent ? near.SheetColumn + 1 : near.SheetColumn;
            foreach (var header in headers) table.InsertColumnBefore(position++, header);
            where = $"directly {(NewColumnPlacement == PlaceAfterCurrent ? "after" : "before")} {near.Title}. Saving inserts real columns there; columns to the right move right";
        }
        else
        {
            foreach (var header in headers) table.EnsureColumn(header);
            where = $"At the end of the {(expected ? "expected" : "request")} columns";
        }
        AddColumnVisible = false;
        MarkChanged();
        BuildRows();
        ShowStatus($"Added {(count == 1 ? "column " + headers[0] : count + " columns " + headers[0] + "…")} to the {(expected ? "response" : "request")} sheet, {where}; fill in the values per row.", isError: false);
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
