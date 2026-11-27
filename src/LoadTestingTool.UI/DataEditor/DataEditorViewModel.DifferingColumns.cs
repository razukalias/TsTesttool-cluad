using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

// "Differing columns (selected rows)": with two or more rows selected in the table, only the columns whose value is not the same in all of them stay.
public sealed partial class DataEditorViewModel
{
    /// <summary>"Differing columns": hide the columns where every selected row has the same value (needs two or more selected rows).</summary>
    [ObservableProperty] private bool _showDifferingColumns;

    private IReadOnlyList<DataRowViewModel> _compareRows = [];

    /// <summary>The grid cells (column numbers) whose values are not the same in all selected rows.</summary>
    public HashSet<int> DifferingColumnCells { get; private set; } = [];

    /// <summary>True when the option is on and two or more rows are selected: only then are columns hidden.</summary>
    public bool DifferingColumnsActive => ShowDifferingColumns && _compareRows.Count >= 2;

    /// <summary>What the option does right now, shown next to the Show menu while it is on.</summary>
    public string DifferingColumnsText
        => !ShowDifferingColumns ? string.Empty
         : _compareRows.Count < 2 ? "Differing columns: select two or more rows to compare"
         : DifferingColumnCells.Count == 0 ? $"Differing columns: the {_compareRows.Count} selected rows have the same value in every column"
         : $"Differing columns: {DifferingColumnCells.Count} of {Columns.Count} columns differ in the {_compareRows.Count} selected rows";

    partial void OnShowDifferingColumnsChanged(bool value) { OnPropertyChanged(nameof(ShowOptionsTitle)); RefreshDifferingColumns(); }

    /// <summary>The grid tells which rows are selected; the columns that differ between them are worked out again.</summary>
    public void SetCompareRows(IReadOnlyList<DataRowViewModel> rows)
    {
        if (rows.Count == _compareRows.Count && rows.SequenceEqual(_compareRows)) return;
        _compareRows = rows;
        RefreshDifferingColumns();
    }

    /// <summary>Works out the differing columns from the selected rows and tells the grid. Texts are compared as they are in the table (trimmed, case matters).</summary>
    public void RefreshDifferingColumns()
    {
        var cells = new HashSet<int>();
        if (ShowDifferingColumns && _compareRows.Count >= 2)
            foreach (var column in Columns)
            {
                var cell = column.CellIndex;
                if (cell < 1) continue;
                var first = ValueOf(_compareRows[0], cell);
                if (_compareRows.Skip(1).Any(row => !string.Equals(ValueOf(row, cell), first, StringComparison.Ordinal))) cells.Add(cell);
            }
        DifferingColumnCells = cells;
        OnPropertyChanged(nameof(DifferingColumnsText));
        OnPropertyChanged(nameof(DifferingColumnsActive));
        RaiseFailedColumnsChanged();
    }

    private static string ValueOf(DataRowViewModel row, int cell) => cell < row.Cells.Count ? (row.Cells[cell] ?? string.Empty).Trim() : string.Empty;
}
