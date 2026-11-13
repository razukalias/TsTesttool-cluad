using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One "column operator value" condition; a row is kept when it satisfies every condition.</summary>
public sealed class ColumnFilter
{
    public ColumnFilter(string column, string op, string value, ICommand remove)
    {
        Column = column; Operator = op; Value = value; RemoveCommand = remove;
    }

    public string Column { get; }
    public string Operator { get; }
    public string Value { get; }
    public ICommand RemoveCommand { get; }
    public bool NeedsValue => Operator != DataEditorViewModel.OpIsEmpty && Operator != DataEditorViewModel.OpIsNotEmpty;
    public string Text => NeedsValue ? $"{Column} {Operator} “{Value}”" : $"{Column} {Operator}";
}

// "Only rows where this column ..." conditions, like an Excel filter on a column. They combine with AND
// and with the other filter boxes, and only hide rows; nothing is changed in the workbook.
public sealed partial class DataEditorViewModel
{
    public const string OpContains = "contains";
    public const string OpEquals = "equals";
    public const string OpNotEquals = "does not equal";
    public const string OpStartsWith = "starts with";
    public const string OpIsEmpty = "is empty";
    public const string OpIsNotEmpty = "is not empty";
    public const string DataIdColumn = "Data ID";

    public static IReadOnlyList<string> FilterOperators { get; } = [OpContains, OpEquals, OpNotEquals, OpStartsWith, OpIsEmpty, OpIsNotEmpty];

    public ObservableCollection<ColumnFilter> ColumnFilters { get; } = [];
    public ObservableCollection<string> FilterColumnOptions { get; } = [];

    [ObservableProperty] private bool _columnFiltersVisible;
    [ObservableProperty] private string? _newFilterColumn;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NewFilterNeedsValue))] private string _newFilterOperator = OpContains;
    [ObservableProperty] private string _newFilterValue = string.Empty;

    public bool NewFilterNeedsValue => NewFilterOperator != OpIsEmpty && NewFilterOperator != OpIsNotEmpty;
    public string ColumnFilterSummary => ColumnFilters.Count == 0 ? "Column filters" : $"Column filters ({ColumnFilters.Count})";

    private static string ColumnLabel(GridColumn column) => column.Group == ColumnGroup.Response ? column.Title + " [expected]" : column.Title;

    /// <summary>Re-lists the columns a filter can use after the grid's columns were rebuilt.</summary>
    private void RefreshFilterColumns()
    {
        FilterColumnOptions.Clear();
        FilterColumnOptions.Add(DataIdColumn);
        foreach (var column in Columns) FilterColumnOptions.Add(ColumnLabel(column));
        if (NewFilterColumn is null || !FilterColumnOptions.Contains(NewFilterColumn)) NewFilterColumn = FilterColumnOptions.FirstOrDefault(o => o != DataIdColumn) ?? DataIdColumn;
    }

    [RelayCommand]
    private void ToggleColumnFilters() => ColumnFiltersVisible = !ColumnFiltersVisible;

    [RelayCommand]
    private void AddColumnFilter()
    {
        if (NewFilterColumn is null) return;
        if (NewFilterNeedsValue && NewFilterValue.Length == 0) { ShowStatus("Enter the value to filter on.", isError: true); return; }
        ColumnFilters.Add(new ColumnFilter(NewFilterColumn, NewFilterOperator, NewFilterNeedsValue ? NewFilterValue : string.Empty, new RelayCommand<ColumnFilter>(RemoveColumnFilter)));
        NewFilterValue = string.Empty;
        FiltersChanged();
    }

    private void RemoveColumnFilter(ColumnFilter? filter)
    {
        if (filter is null) return;
        ColumnFilters.Remove(filter);
        FiltersChanged();
    }

    [RelayCommand]
    private void ClearColumnFilters()
    {
        if (ColumnFilters.Count == 0) return;
        ColumnFilters.Clear();
        FiltersChanged();
    }

    private void FiltersChanged()
    {
        OnPropertyChanged(nameof(ColumnFilterSummary));
        ApplyFilter();
    }

    /// <summary>Whether a row satisfies every column filter. A filter whose column is not shown right now is ignored.</summary>
    private bool PassesColumnFilters(DataRowViewModel row)
    {
        foreach (var filter in ColumnFilters)
        {
            var cell = filter.Column == DataIdColumn ? 0 : Columns.FirstOrDefault(c => ColumnLabel(c) == filter.Column)?.CellIndex ?? -1;
            if (cell < 0) continue;
            var value = row.Cells[cell];
            var ok = filter.Operator switch
            {
                OpContains => value.Contains(filter.Value, StringComparison.OrdinalIgnoreCase),
                OpEquals => string.Equals(value, filter.Value, StringComparison.OrdinalIgnoreCase),
                OpNotEquals => !string.Equals(value, filter.Value, StringComparison.OrdinalIgnoreCase),
                OpStartsWith => value.StartsWith(filter.Value, StringComparison.OrdinalIgnoreCase),
                OpIsEmpty => string.IsNullOrWhiteSpace(value),
                OpIsNotEmpty => !string.IsNullOrWhiteSpace(value),
                _ => true
            };
            if (!ok) return false;
        }
        return true;
    }
}
