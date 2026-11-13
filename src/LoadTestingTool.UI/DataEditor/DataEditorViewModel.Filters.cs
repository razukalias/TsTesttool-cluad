using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

// The three filter boxes of the original workbook editor, plus the existing "row contains" text box.
//   Headers:     header1 & header2   keeps columns whose header contains any term
//   Rows:        1,2,5-9             keeps those rows (1 = first row of the testcase)
//   Cell values: value1 & value2     keeps columns where some shown row has a cell containing any term
public sealed partial class DataEditorViewModel
{
    private bool _clearingFilters;

    [ObservableProperty] private string _headerFilter = string.Empty;
    [ObservableProperty] private string _rowNumberFilter = string.Empty;
    [ObservableProperty] private string _valueFilter = string.Empty;

    partial void OnHeaderFilterChanged(string value) { if (!_loading) BuildRows(); }
    partial void OnValueFilterChanged(string value) { if (!_loading) BuildRows(); }

    partial void OnRowNumberFilterChanged(string value)
    {
        if (_loading || _clearingFilters) return;
        if (ValueFilter.Trim().Length > 0) BuildRows(); else ApplyFilter();
    }

    /// <summary>Clears the row-level filters so rows just added are visible. Column filters stay.</summary>
    private void ClearRowFilters()
    {
        _clearingFilters = true;
        try { FilterText = string.Empty; RowNumberFilter = string.Empty; ColumnFilters.Clear(); OnPropertyChanged(nameof(ColumnFilterSummary)); }
        finally { _clearingFilters = false; }
        if (!_loading) ApplyFilter();
    }

    private bool HasColumnFilters => HeaderFilter.Trim().Length > 0 || ValueFilter.Trim().Length > 0;

    internal static string[] FilterTerms(string? text)
        => (text ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static bool MatchesAny(string value, IReadOnlyList<string> terms)
        => terms.Count == 0 || terms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));

    internal static HashSet<int> ParseRowNumbers(string? text)
    {
        var rows = new HashSet<int>();
        foreach (var token in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = token.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 2 && int.TryParse(range[0], out var first) && int.TryParse(range[1], out var last))
                for (var row = Math.Max(1, Math.Min(first, last)); row <= Math.Max(first, last); row++) rows.Add(row);
            else if (range.Length == 1 && int.TryParse(token, out var single) && single >= 1) rows.Add(single);
        }
        return rows;
    }
}
