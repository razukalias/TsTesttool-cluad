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

public sealed partial class TestcaseItem : ObservableObject
{
    public TestcaseItem(TestcaseInfo info, int rowCount)
    {
        Index = info.Index;
        Name = info.Name;
        Steps = info.Steps;
        _rowCount = rowCount;
    }

    public int Index { get; }
    public string Name { get; }
    public IReadOnlyList<string> Steps { get; private set; }

    public void UpdateSteps(IReadOnlyList<string> steps)
    {
        Steps = steps;
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(Summary));
    }
    public string Title => $"{Index} · {Name}";
    public string Summary => $"{RowCount} {(RowCount == 1 ? "row" : "rows")} · {Steps.Count} {(Steps.Count == 1 ? "step" : "steps")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _rowCount;
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
public sealed class DataRowViewModel
{
    private readonly Action<DataRowViewModel, int, string> _edited;
    private bool _suppress;

    public DataRowViewModel(int testcaseIndex, TableRow request, TableRow? response, IReadOnlyList<GridColumn> columns, Action<DataRowViewModel, int, string> edited)
    {
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
    }

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
