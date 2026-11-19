using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One expected-value cell that failed in the latest run, with what the run got instead.</summary>
public sealed record FailedCell(DataRowViewModel Row, int CellIndex, string Header, string DataId, string Expected, string Actual, string Verb, string Environment, bool CanAssign);

// Failed cells: a failed assertion points at one expected-value cell of a row. The table marks it, and the actual value can be assigned to it.
public sealed partial class DataEditorViewModel
{
    /// <summary>The failed cells of the rows shown, filled when "Failed cells only" is on and after a run or an assignment.</summary>
    public BulkObservableCollection<FailedCell> FailedCells { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowFailedCellsList), nameof(ShowTableGrid))]
    private bool _showFailedCells;

    public bool ShowFailedCellsList => ShowFailedCells && ShowDataGrid;
    public bool ShowTableGrid => ShowDataGrid && !ShowFailedCells;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(ShowDataGrid)) { base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowTableGrid))); base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(ShowFailedCellsList))); }
    }

    partial void OnShowFailedCellsChanged(bool value) { if (value) RefreshFailedCells(); }

    /// <summary>"Failed columns only": the table shows only the expected-value columns that have a failed cell in the rows shown (plus the Data ID and result).</summary>
    [ObservableProperty] private bool _showFailedColumns;

    /// <summary>The grid cells of the columns that hold a failed assertion in the rows shown; refreshed while "Failed columns only" is on.</summary>
    public HashSet<int> FailedColumnCells { get; private set; } = [];

    /// <summary>Raised when <see cref="FailedColumnCells"/> or the option changed, so the grid can hide or show columns.</summary>
    public event EventHandler? FailedColumnsChanged;

    partial void OnShowFailedColumnsChanged(bool value) => RefreshFailedColumns();

    public void RefreshFailedColumns()
    {
        var cells = new HashSet<int>();
        if (ShowFailedColumns)
            foreach (var row in VisibleRows)
            {
                if (row.Outcome is not { } outcome) continue;
                foreach (var failure in FailuresOf(outcome).Where(f => f.IsAssertion))
                    if (CellOfFailure(failure) is >= 1 and var cell && !Assigned(row, cell, failure) && !IsKnownBug(row, failure)) cells.Add(cell);
            }
        FailedColumnCells = cells;
        FailedColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    public string HeaderWithoutVerb(string title) => StripVerb(title);

    // "Step.path" (without the verb) -> grid cell of that expected column; rebuilt when the columns change. Looked up for every cell that is drawn.
    private IReadOnlyList<GridColumn>? _failureCellsFor;
    private Dictionary<string, int> _failureCells = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _responseColumnOf = new(StringComparer.OrdinalIgnoreCase);   // "Step.path" -> column of the response sheet, whatever step is shown

    private int CellOfFailure(FailureDetail failure)
    {
        if (!ReferenceEquals(_failureCellsFor, Columns))
        {
            _responseColumnOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var headers = _document.Response.Headers;
            for (var i = 0; i < headers.Count; i++) _responseColumnOf.TryAdd(StripVerb(headers[i]), i);
            _failureCells = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in Columns.Where(c => c.Group == ColumnGroup.Response)) _failureCells.TryAdd(StripVerb(column.Title), column.CellIndex);
            _failureCellsFor = Columns;
        }
        return _failureCells.TryGetValue($"{failure.StepName}.{failure.ResponsePath}", out var cell) ? cell : -1;
    }

    /// <summary>
    /// The failure's expected cell carries a <c>&lt;bug: …&gt;</c> tag for the failure's environment: the runner counts it as a known bug, not a failure.
    /// Read from the workbook table, so it also works while another step's columns are shown, and the moment a tag is written (no re-run needed).
    /// </summary>
    internal bool IsKnownBug(DataRowViewModel row, FailureDetail failure)
    {
        if (!failure.IsAssertion || row.Response is not { } response) return false;
        CellOfFailure(failure);   // makes sure the column maps are current
        if (!_responseColumnOf.TryGetValue($"{failure.StepName}.{failure.ResponsePath}", out var column)) return false;
        var text = response.GetAt(column);
        if (text.IndexOf("<bug", StringComparison.OrdinalIgnoreCase) < 0) return false;
        return LoadTestingTool.Execution.BugTag.Parse(text).Tag is { } tag && tag.AppliesTo(failure.Environment);
    }

    private static bool Assigned(DataRowViewModel row, int cell, FailureDetail failure)
        => cell >= 0 && cell < row.Cells.Count && ValueParts.SplitCell(row.Cells[cell] ?? string.Empty).Expected.Trim() == (failure.Actual ?? string.Empty).Trim();

    /// <summary>The failed assertion behind this cell in the latest run, or null when the cell did not fail, or already holds the value the run got.</summary>
    public FailureDetail? FailureAt(DataRowViewModel row, int cellIndex)
    {
        // While the columns are rebuilt (another step chosen) a cell can still hold a row from the other column set: no mark, no crash
        if (row.Outcome is not { } outcome || cellIndex < 1 || cellIndex >= row.Cells.Count) return null;
        foreach (var failure in FailuresOf(outcome))
            if (failure.IsAssertion && CellOfFailure(failure) == cellIndex && !Assigned(row, cellIndex, failure) && !IsKnownBug(row, failure)) return failure;
        return null;
    }

    /// <summary>The actual value can be written unless there is none (a missing field) or the cell holds a reference or generated value that would be lost.</summary>
    public bool CanAssign(DataRowViewModel row, int cellIndex, FailureDetail failure)
    {
        if (failure.Actual is null or "<missing>" || cellIndex < 0 || cellIndex >= row.Cells.Count) return false;
        var expected = ValueParts.SplitCell(row.Cells[cellIndex] ?? string.Empty).Expected;
        return ValueParts.Parse(expected).All(p => p.Kind == ValuePartKind.Text);
    }

    /// <summary>Writes the actual value into the cell, keeping a verb written in the cell. An ordinary edit: it can be undone.</summary>
    public bool AssignActual(DataRowViewModel row, int cellIndex, FailureDetail failure)
    {
        if (cellIndex < 0 || cellIndex >= row.Cells.Count || !CanAssign(row, cellIndex, failure)) return false;
        var (verb, _) = ValueParts.SplitCell(row.Cells[cellIndex] ?? string.Empty);
        row.Cells[cellIndex] = (verb is null ? string.Empty : "{" + verb + "}") + failure.Actual;
        RefreshFailedCells();
        return true;
    }

    public void RefreshFailedCells()
    {
        var titles = Columns.ToDictionary(c => c.CellIndex, c => c.Title);
        var found = new List<FailedCell>();
        foreach (var row in VisibleRows)
        {
            if (row.Outcome is not { } outcome) continue;
            foreach (var failure in FailuresOf(outcome).Where(f => f.IsAssertion))
            {
                var cell = CellOfFailure(failure);
                if (cell < 1 || Assigned(row, cell, failure) || IsKnownBug(row, failure)) continue;
                found.Add(new FailedCell(row, cell, titles[cell], row.CurrentDataId, ValueParts.SplitCell(row.Cells[cell] ?? string.Empty).Expected, failure.Actual ?? string.Empty,
                    failure.Verb ?? string.Empty, failure.Environment, CanAssign(row, cell, failure)));
            }
        }
        FailedCells.ReplaceAll(found);   // one notification: the list is drawn once, however many cells failed
    }
}
