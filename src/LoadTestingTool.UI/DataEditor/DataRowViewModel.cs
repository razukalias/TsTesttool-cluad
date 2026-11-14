using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

public enum ColumnGroup { Id, Request, Response }

/// <param name="Title">Text shown in the column header.</param>
/// <param name="Group">Where the value lives: the request inputs or the expected response.</param>
/// <param name="CellIndex">Position in <see cref="DataRowViewModel.Cells"/> (0 is the data ID).</param>
/// <param name="SheetColumn">Position of the column in its <see cref="SheetTable"/>.</param>
public sealed record GridColumn(string Title, ColumnGroup Group, int CellIndex, int SheetColumn);

/// <summary>One step under a testcase in the tree.</summary>
public sealed partial class StepNode(TestcaseItem owner, string name, int position) : ObservableObject
{
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = name;
    public bool IsNotEditing => !IsEditing;
    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsNotEditing));

    public TestcaseItem Owner { get; } = owner;
    public string Name { get; } = name;
    public int Position { get; } = position;
    public string Title => $"{Position} · {Name}";
}

public sealed partial class TestcaseItem : ObservableObject
{
    public TestcaseItem(TestcaseInfo info, int rowCount)
    {
        Index = info.Index;
        Name = info.Name;
        Steps = info.Steps;
        _rowCount = rowCount;
        RebuildStepNodes();
    }

    /// <summary>The steps as children of the testcase in the tree, in run order.</summary>
    public ObservableCollection<StepNode> StepNodes { get; } = [];

    /// <summary>True while the name is being edited in the tree.</summary>
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = string.Empty;
    public bool IsNotEditing => !IsEditing;
    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsNotEditing));

    private void RebuildStepNodes()
    {
        StepNodes.Clear();
        for (var i = 0; i < Steps.Count; i++) StepNodes.Add(new StepNode(this, Steps[i], i + 1));
    }

    public int Index { get; }
    public string Name { get; }
    public IReadOnlyList<string> Steps { get; private set; }

    public void UpdateSteps(IReadOnlyList<string> steps)
    {
        Steps = steps;
        if (!StepNodes.Select(n => n.Name).SequenceEqual(steps)) RebuildStepNodes();
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(Summary));
    }
    public string Title => $"{Index} · {Name}";
    public string Summary => $"{RowCount} {(RowCount == 1 ? "row" : "rows")} · {Steps.Count} {(Steps.Count == 1 ? "step" : "steps")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _rowCount;

    /// <summary>Latest-run result for the testcase's rows, e.g. "3 failed · 12 passed"; empty when there are no results.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasResults))]
    private string _resultText = string.Empty;

    [ObservableProperty] private string _resultColor = "#8EA4BC";

    public bool HasResults => ResultText.Length > 0;

    /// <summary>Ticked for "Chosen testcases" in the run options.</summary>
    [ObservableProperty] private bool _isChosen;

    partial void OnIsChosenChanged(bool value) => ChosenChanged?.Invoke(this, EventArgs.Empty);
    internal event EventHandler? ChosenChanged;

    public void SetResults(int passed, int failed)
    {
        if (passed + failed == 0) { ResultText = string.Empty; return; }
        ResultText = failed > 0 ? $"{failed} failed · {passed} passed" : $"{passed} passed";
        ResultColor = failed > 0 ? "#FF7588" : "#39D39B";
    }
}

public sealed class ProblemViewModel
{
    public ProblemViewModel(WorkbookProblem problem)
    {
        Problem = problem;
        IsError = problem.Severity == ProblemSeverity.Error;
    }

    public WorkbookProblem Problem { get; }
    public bool IsError { get; }
    public string SeverityText => IsError ? "Error" : "Warning";
    public string BadgeBackground => IsError ? "#432333" : "#443A25";
    public string BadgeForeground => IsError ? "#FF7588" : "#F6C76B";
    public string Message => Problem.Message;
    public string Location => Problem.RowNumber is int row ? $"{Problem.Sheet} · row {row}" : Problem.Row is null ? Problem.Sheet : $"{Problem.Sheet} · new row";
}

/// <summary>
/// One line of the grid: a request row and its response row shown together. Cell 0 is the data ID, followed
/// by the request columns and then the expected-response columns. Editing a cell writes through to the workbook.
/// </summary>
public sealed partial class DataRowViewModel : ObservableObject, IFormattedRow
{
    private readonly SheetTable? _requestTable;
    private readonly SheetTable? _responseTable;
    private readonly IReadOnlyList<GridColumn> _columns;
    private readonly Action<DataRowViewModel, int, string> _edited;
    private bool _suppress;

    public DataRowViewModel(int testcaseIndex, TableRow request, TableRow? response, IReadOnlyList<GridColumn> columns, Action<DataRowViewModel, int, string> edited, SheetTable? requestTable = null, SheetTable? responseTable = null)
    {
        _columns = columns;
        _requestTable = requestTable;
        _responseTable = responseTable;
        TestcaseIndex = testcaseIndex;
        Request = request;
        Response = response;
        _edited = edited;
        var values = new List<string> { request.Get("dataid") };
        foreach (var column in columns)
            values.Add(column.Group == ColumnGroup.Request ? request.GetAt(column.SheetColumn) : response?.GetAt(column.SheetColumn) ?? string.Empty);
        CurrentDataId = values[0];
        Cells = new ObservableCollection<string>(values);
        Cells.CollectionChanged += OnCellsChanged;
        Formats = new ObservableCollection<CellStyleView>(Enumerable.Range(0, values.Count).Select(_ => new CellStyleView()));
        RefreshFormats();
    }

    public ObservableCollection<CellStyleView> Formats { get; }

    public (SheetTable Table, TableRow Row, int Column)? Locate(int cell)
    {
        if (cell == 0) return _requestTable is null || _requestTable.FindColumn("dataid") is not (>= 0 and var id) ? null : (_requestTable, Request, id);
        var column = _columns[cell - 1];
        if (column.Group == ColumnGroup.Request) return _requestTable is null ? null : (_requestTable, Request, column.SheetColumn);
        return _responseTable is null || Response is null ? null : (_responseTable, Response, column.SheetColumn);
    }

    /// <summary>Reads every cell's look from the workbook tables again.</summary>
    public void RefreshFormats()
    {
        for (var cell = 0; cell < Formats.Count; cell++)
            Formats[cell].Set(Locate(cell) is { } at ? at.Table.GetFormat(at.Row, at.Column) : CellFormat.None);
    }

    /// <summary>How this row fared in the latest run, or null when there are no results for it.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ResultText), nameof(ResultColor))]
    private RowOutcome? _outcome;

    public string ResultText => Outcome is null ? "—" : Outcome.Passed ? "PASS" : "FAIL";
    public string ResultColor => Outcome is null ? "#5E7590" : Outcome.Passed ? "#39D39B" : "#FF7588";

    public int TestcaseIndex { get; }
    public TableRow Request { get; }
    public TableRow? Response { get; internal set; }
    public ObservableCollection<string> Cells { get; }

    /// <summary>The data ID as last accepted by the workbook, so a rejected rename can be undone.</summary>
    public string CurrentDataId { get; internal set; }

    /// <summary>Puts a value in a cell without treating it as a user edit.</summary>
    internal void SetCellSilently(int index, string value)
    {
        _suppress = true;
        try { Cells[index] = value; }
        finally { _suppress = false; }
    }

    private void OnCellsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppress || e.Action != NotifyCollectionChangedAction.Replace) return;
        var index = e.NewStartingIndex;
        _edited(this, index, Cells[index] ?? string.Empty);
    }
}
