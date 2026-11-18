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
    public ObservableCollection<FailedCell> FailedCells { get; } = [];

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

    public string HeaderWithoutVerb(string title) => StripVerb(title);

    private int CellOfFailure(FailureDetail failure)
    {
        var wanted = $"{failure.StepName}.{failure.ResponsePath}";
        return Columns.FirstOrDefault(c => c.Group == ColumnGroup.Response && string.Equals(StripVerb(c.Title), wanted, StringComparison.OrdinalIgnoreCase))?.CellIndex ?? -1;
    }

    private static bool Assigned(DataRowViewModel row, int cell, FailureDetail failure)
        => ValueParts.SplitCell(row.Cells[cell] ?? string.Empty).Expected.Trim() == (failure.Actual ?? string.Empty).Trim();

    /// <summary>The failed assertion behind this cell in the latest run, or null when the cell did not fail, or already holds the value the run got.</summary>
    public FailureDetail? FailureAt(DataRowViewModel row, int cellIndex)
    {
        if (row.Outcome is not { } outcome) return null;
        foreach (var failure in outcome.Failures)
            if (failure.IsAssertion && CellOfFailure(failure) == cellIndex && !Assigned(row, cellIndex, failure)) return failure;
        return null;
    }

    /// <summary>The actual value can be written unless there is none (a missing field) or the cell holds a reference or generated value that would be lost.</summary>
    public bool CanAssign(DataRowViewModel row, int cellIndex, FailureDetail failure)
    {
        if (failure.Actual is null or "<missing>") return false;
        var expected = ValueParts.SplitCell(row.Cells[cellIndex] ?? string.Empty).Expected;
        return ValueParts.Parse(expected).All(p => p.Kind == ValuePartKind.Text);
    }

    /// <summary>Writes the actual value into the cell, keeping a verb written in the cell. An ordinary edit: it can be undone.</summary>
    public bool AssignActual(DataRowViewModel row, int cellIndex, FailureDetail failure)
    {
        if (!CanAssign(row, cellIndex, failure)) return false;
        var (verb, _) = ValueParts.SplitCell(row.Cells[cellIndex] ?? string.Empty);
        row.Cells[cellIndex] = (verb is null ? string.Empty : "{" + verb + "}") + failure.Actual;
        RefreshFailedCells();
        return true;
    }

    public void RefreshFailedCells()
    {
        FailedCells.Clear();
        foreach (var row in VisibleRows)
        {
            if (row.Outcome is not { } outcome) continue;
            foreach (var failure in outcome.Failures.Where(f => f.IsAssertion))
            {
                var cell = CellOfFailure(failure);
                if (cell < 1 || Assigned(row, cell, failure)) continue;
                var header = Columns.First(c => c.CellIndex == cell).Title;
                FailedCells.Add(new FailedCell(row, cell, header, row.CurrentDataId, ValueParts.SplitCell(row.Cells[cell] ?? string.Empty).Expected, failure.Actual ?? string.Empty,
                    failure.Verb ?? string.Empty, failure.Environment, CanAssign(row, cell, failure)));
            }
        }
    }
}
