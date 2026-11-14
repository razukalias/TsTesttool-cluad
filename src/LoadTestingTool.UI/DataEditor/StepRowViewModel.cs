using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// One step of a testcase: a row of the config sheet. Cell 0 is the step name, followed by the other config
/// columns in sheet order. Editing a cell writes through to the workbook.
/// </summary>
public sealed partial class StepRowViewModel : ObservableObject, IFormattedRow
{
    private readonly SheetTable? _table;
    private readonly IReadOnlyList<GridColumn> _columns;
    private readonly Action<StepRowViewModel, int, string> _edited;
    private bool _suppress;

    public StepRowViewModel(TableRow row, IReadOnlyList<GridColumn> columns, Action<StepRowViewModel, int, string> edited, SheetTable? table = null)
    {
        _table = table;
        _columns = columns;
        Row = row;
        _edited = edited;
        var values = new List<string> { row.Get("stepname") };
        foreach (var column in columns) values.Add(Normalize(column.Title, row.GetAt(column.SheetColumn)));
        CurrentName = values[0];
        Cells = new ObservableCollection<string>(values);
        Cells.CollectionChanged += OnCellsChanged;
        Formats = new ObservableCollection<CellStyleView>(Enumerable.Range(0, values.Count).Select(_ => new CellStyleView()));
        for (var cell = 0; cell < Formats.Count; cell++)
            Formats[cell].Set(Locate(cell) is { } at ? at.Table.GetFormat(at.Row, at.Column) : CellFormat.None);
    }

    public ObservableCollection<CellStyleView> Formats { get; }

    public (SheetTable Table, TableRow Row, int Column)? Locate(int cell)
    {
        if (_table is null) return null;
        var column = cell == 0 ? _table.FindColumn("stepname") : _columns[cell - 1].SheetColumn;
        return column < 0 ? null : (_table, Row, column);
    }

    public TableRow Row { get; }
    public ObservableCollection<string> Cells { get; }

    private string _currentName = string.Empty;

    /// <summary>The name as last accepted, so a rejected rename can be undone.</summary>
    public string CurrentName { get => _currentName; internal set => SetProperty(ref _currentName, value); }

    /// <summary>The editor that owns this step; lets the step's form reach template files and the data grid.</summary>
    public DataEditorViewModel? Editor { get; internal set; }

    /// <summary>Open in the testcase's step list.</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>1-based place in the testcase.</summary>
    [ObservableProperty] private int _position;

    /// <summary>The form's content; null while collapsed, so closed steps cost nothing.</summary>
    public object? ExpandedContent => IsExpanded ? this : null;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandedContent));

    /// <summary>The value of a config column by header ("" when the sheet has no such column).</summary>
    public string Value(string header)
    {
        var column = _columns.FirstOrDefault(c => c.Title.Equals(header, StringComparison.OrdinalIgnoreCase));
        return column is null ? string.Empty : Cells[column.CellIndex] ?? string.Empty;
    }

    public string TypeText => Value("steptype");
    public bool IsStepEnabled => Value("enabled").Trim().ToLowerInvariant() is not ("false" or "0" or "no" or "off");
    public string EnabledText => IsStepEnabled ? "enabled" : "disabled";
    public string EnabledColor => IsStepEnabled ? "#39D39B" : "#F6C76B";

    /// <summary>One line telling what the step does, for the collapsed list.</summary>
    public string SummaryText
    {
        get
        {
            var url = Value("targeturl");
            var verb = Value("verb");
            if (url.Length > 0) return verb.Length > 0 ? $"{verb} {url}" : url;
            var template = Value("templetsource");
            return template;
        }
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(TypeText));
        OnPropertyChanged(nameof(IsStepEnabled));
        OnPropertyChanged(nameof(EnabledText));
        OnPropertyChanged(nameof(EnabledColor));
        OnPropertyChanged(nameof(SummaryText));
    }

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
        NotifySummary();
        if (_suppress || e.Action != NotifyCollectionChangedAction.Replace) return;
        var index = e.NewStartingIndex;
        if (Cells[index] is null) return; // a drop-down clearing itself because the value is not one of its choices is not an edit
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
