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

        Record($"add column {header}");
        table.EnsureColumn(header);
        AddColumnVisible = false;
        MarkChanged();
        BuildRows();
        ShowStatus($"Added column {header} to the {(expected ? "response" : "request")} sheet. It is the last {(expected ? "expected" : "request")} column; fill in the values per row.", isError: false);
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
}
