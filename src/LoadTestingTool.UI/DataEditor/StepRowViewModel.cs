using System.Collections.ObjectModel;
using System.Collections.Specialized;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// One step of a testcase: a row of the config sheet. Cell 0 is the step name, followed by the other config
/// columns in sheet order. Editing a cell writes through to the workbook.
/// </summary>
public sealed class StepRowViewModel
{
    private readonly Action<StepRowViewModel, int, string> _edited;
    private bool _suppress;

    public StepRowViewModel(TableRow row, IReadOnlyList<GridColumn> columns, Action<StepRowViewModel, int, string> edited)
    {
        Row = row;
        _edited = edited;
        var values = new List<string> { row.Get("stepname") };
        foreach (var column in columns) values.Add(Normalize(column.Title, row.GetAt(column.SheetColumn)));
        CurrentName = values[0];
        Cells = new ObservableCollection<string>(values);
        Cells.CollectionChanged += OnCellsChanged;
    }

    public TableRow Row { get; }
    public ObservableCollection<string> Cells { get; }

    /// <summary>The name as last accepted, so a rejected rename can be undone.</summary>
    public string CurrentName { get; internal set; }

    /// <summary>A step added in this session has no sheet row yet; only such steps can still be renamed.</summary>
    public bool IsNew => Row.SourceRow is null;

    internal void SetCellSilently(int index, string value)
    {
        _suppress = true;
        try { Cells[index] = value; }
        finally { _suppress = false; }
    }

    /// <summary>Shows "TRUE", "http" and the like as the spelling the drop-downs offer, without changing the workbook.</summary>
    private static string Normalize(string header, string value)
    {
        var options = StepOptions.For(header);
        return options?.FirstOrDefault(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase)) ?? value;
    }

    private void OnCellsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppress || e.Action != NotifyCollectionChangedAction.Replace) return;
        var index = e.NewStartingIndex;
        _edited(this, index, Cells[index] ?? string.Empty);
    }
}

/// <summary>The fixed choices for config columns that have them; those columns become drop-downs.</summary>
public static class StepOptions
{
    public static readonly IReadOnlyList<string> StepTypes = ["HTTP", "GraphQL", "CaseQL", "SQL", "File", "Script", "ActionManual"];
    public static readonly IReadOnlyList<string> Stages = ["Setup", "Test", "Teardown"];
    public static readonly IReadOnlyList<string> Verbs = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];
    public static readonly IReadOnlyList<string> Booleans = ["True", "False"];

    public static IReadOnlyList<string>? For(string header) => header.Trim().ToLowerInvariant() switch
    {
        "steptype" => StepTypes,
        "stage" => Stages,
        "verb" => Verbs,
        "enabled" or "stoponfailure" or "assert" or "assertonlyresponse" => Booleans,
        _ => null
    };
}
