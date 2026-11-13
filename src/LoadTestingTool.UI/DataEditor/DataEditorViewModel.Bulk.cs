using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Editing many cells at once: paste a block from Excel, fill a column down, and find & replace.
// Each is a single undo step.
public sealed partial class DataEditorViewModel
{
    public const string ScopeAllColumns = "All columns";
    public const string ScopeCurrentColumn = "Current column";

    public static IReadOnlyList<string> FindScopes { get; } = [ScopeAllColumns, ScopeCurrentColumn];

    /// <summary>Grid position of the cell being worked in (0 is the data ID); set by the view.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CurrentColumnTitle))]
    private int _currentCellIndex = -1;

    [ObservableProperty] private bool _findVisible;
    [ObservableProperty] private string _findText = string.Empty;
    [ObservableProperty] private string _replaceText = string.Empty;
    [ObservableProperty] private bool _findMatchCase;
    [ObservableProperty] private bool _findWholeCell;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FindScopeIsCurrent))]
    private string _findScope = ScopeAllColumns;

    public bool FindScopeIsCurrent => FindScope == ScopeCurrentColumn;
    [ObservableProperty] private string _matchSummary = string.Empty;

    public string CurrentColumnTitle => CurrentCellIndex > 0 && CurrentCellIndex <= Columns.Count ? Columns[CurrentCellIndex - 1].Title : "none selected";

    partial void OnFindTextChanged(string value) => UpdateMatches();
    partial void OnFindMatchCaseChanged(bool value) => UpdateMatches();
    partial void OnFindWholeCellChanged(bool value) => UpdateMatches();
    partial void OnFindScopeChanged(string value) => UpdateMatches();
    partial void OnCurrentCellIndexChanged(int value) => UpdateMatches();

    // ---- Paste -----------------------------------------------------------------------------

    /// <summary>
    /// Pastes a block of text cells with its top-left corner at the given grid row and cell. Rows past the
    /// end become new data rows (unless a filter is active); cells past the last column are ignored.
    /// </summary>
    public void PasteBlock(int startRow, int startCell, string[][] block)
    {
        if (SelectedTestcase is not { } testcase || block.Length == 0 || startRow < 0 || startCell < 0) return;
        Record($"paste {block.Length} {(block.Length == 1 ? "row" : "rows")}");

        var skippedLines = 0;
        var missing = startRow + block.Length - VisibleRows.Count;
        if (missing > 0)
        {
            if (FilterText.Trim().Length > 0) skippedLines = missing;
            else for (var i = 0; i < missing; i++) AddPastedRow(testcase);
        }

        var changed = 0;
        var skippedCells = 0;
        for (var r = 0; r < block.Length; r++)
        {
            if (startRow + r >= VisibleRows.Count) break;
            var row = VisibleRows[startRow + r];
            for (var c = 0; c < block[r].Length; c++)
            {
                var cell = startCell + c;
                if (cell > Columns.Count) { skippedCells++; continue; }
                switch (ApplyCell(row, cell, block[r][c]))
                {
                    case CellResult.Changed: changed++; break;
                    case CellResult.Rejected: skippedCells++; break;
                }
            }
        }
        FinishBulk(changed, $"Pasted {changed} {(changed == 1 ? "cell" : "cells")}", skippedCells, skippedLines);
    }

    private void AddPastedRow(TestcaseItem testcase)
    {
        var (request, response) = _document.AddDataRow(testcase.Index, NextDataId(testcase));
        var row = new DataRowViewModel(testcase.Index, request, response, Columns, OnCellEdited);
        _allRows.Add(row);
        VisibleRows.Add(row);
        testcase.RowCount++;
        OnPropertyChanged(nameof(RowCountText));
    }

    // ---- Fill down -------------------------------------------------------------------------

    /// <summary>Copies the first row's value in a column into the other rows, which must be in grid order.</summary>
    public void FillDown(IReadOnlyList<DataRowViewModel> rows, int cellIndex)
    {
        if (rows.Count < 2) { ShowStatus("Select two or more rows (Ctrl+click or Shift+click), click the column, then press Ctrl+D.", isError: false); return; }
        if (cellIndex < 1 || cellIndex > Columns.Count) { ShowStatus("Click a cell in a request or expected column first. Data IDs must stay unique, so they cannot be filled.", isError: false); return; }
        Record($"fill down {Columns[cellIndex - 1].Title}");
        var value = rows[0].Cells[cellIndex];
        var changed = rows.Skip(1).Count(row => ApplyCell(row, cellIndex, value) == CellResult.Changed);
        FinishBulk(changed, $"Filled {Columns[cellIndex - 1].Title} down {changed} {(changed == 1 ? "row" : "rows")}", 0, 0);
    }

    // ---- Find & replace --------------------------------------------------------------------

    [RelayCommand]
    private void OpenFind()
    {
        FindVisible = true;
        UpdateMatches();
    }

    [RelayCommand]
    private void CloseFind() => FindVisible = false;

    [RelayCommand]
    private void ReplaceAll()
    {
        if (FindText.Length == 0) { ShowStatus("Enter the text to find.", isError: true); return; }
        var total = 0;
        var cellsChanged = 0;
        Record($"replace \"{Shorten(FindText)}\" with \"{Shorten(ReplaceText)}\"");
        foreach (var (row, cell, text) in MatchingCells())
        {
            var replaced = Replace(text);
            if (ApplyCell(row, cell, replaced) == CellResult.Changed) { cellsChanged++; total += CountMatches(text); }
        }
        FinishBulk(cellsChanged, $"Replaced {total} {(total == 1 ? "match" : "matches")} in {cellsChanged} {(cellsChanged == 1 ? "cell" : "cells")}", 0, 0);
        UpdateMatches();
    }

    private IEnumerable<(DataRowViewModel Row, int Cell, string Text)> MatchingCells()
    {
        if (FindText.Length == 0) yield break;
        var currentOnly = FindScope == ScopeCurrentColumn;
        for (var cell = 1; cell <= Columns.Count; cell++)
        {
            if (currentOnly && cell != CurrentCellIndex) continue;
            foreach (var row in VisibleRows)
            {
                var text = row.Cells[cell];
                if (IsMatch(text)) yield return (row, cell, text);
            }
        }
    }

    private StringComparison Comparison => FindMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private bool IsMatch(string text) => FindWholeCell ? string.Equals(text, FindText, Comparison) : text.Contains(FindText, Comparison);

    private string Replace(string text) => FindWholeCell ? ReplaceText : text.Replace(FindText, ReplaceText, Comparison);

    private int CountMatches(string text)
    {
        if (FindWholeCell) return 1;
        var count = 0;
        for (var at = text.IndexOf(FindText, Comparison); at >= 0; at = text.IndexOf(FindText, at + FindText.Length, Comparison)) count++;
        return count;
    }

    private void UpdateMatches()
    {
        if (!FindVisible || FindText.Length == 0) { MatchSummary = string.Empty; return; }
        if (FindScope == ScopeCurrentColumn && (CurrentCellIndex < 1 || CurrentCellIndex > Columns.Count)) { MatchSummary = "Click a cell to choose the column."; return; }
        var cells = MatchingCells().ToList();
        var matches = cells.Sum(c => CountMatches(c.Text));
        MatchSummary = matches == 0 ? "No matches" : $"{matches} {(matches == 1 ? "match" : "matches")} in {cells.Count} {(cells.Count == 1 ? "cell" : "cells")}";
    }

    // ---- Shared ----------------------------------------------------------------------------

    private enum CellResult { Unchanged, Changed, Rejected }

    /// <summary>Writes one cell for a bulk operation without recording history or status (the caller does that once).</summary>
    private CellResult ApplyCell(DataRowViewModel row, int cellIndex, string value)
    {
        if (cellIndex == 0)
        {
            if (value == row.CurrentDataId) return CellResult.Unchanged;
            try
            {
                if (!_document.RenameDataRow(row.TestcaseIndex, row.CurrentDataId, value))
                {
                    row.Request.Set("dataid", value);
                    row.Response?.Set("dataid", value);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return CellResult.Rejected;
            }
            row.CurrentDataId = value.Trim();
            row.SetCellSilently(0, row.CurrentDataId);
            return CellResult.Changed;
        }

        var column = Columns[cellIndex - 1];
        var existing = column.Group == ColumnGroup.Request ? row.Request.GetAt(column.SheetColumn) : row.Response?.GetAt(column.SheetColumn) ?? string.Empty;
        if (existing == value) return CellResult.Unchanged;
        if (column.Group == ColumnGroup.Request) row.Request.SetAt(column.SheetColumn, value);
        else EnsureResponse(row).SetAt(column.SheetColumn, value);
        row.SetCellSilently(cellIndex, value);
        return CellResult.Changed;
    }

    private void FinishBulk(int changed, string message, int skippedCells, int skippedLines)
    {
        if (changed == 0 && skippedLines == 0)
        {
            DiscardLastRecord();
            ShowStatus(skippedCells > 0 ? $"Nothing changed; {skippedCells} cells were skipped." : "Nothing changed.", isError: skippedCells > 0);
            return;
        }
        var notes = new List<string>();
        if (skippedCells > 0) notes.Add($"{skippedCells} skipped (past the last column or a refused data ID)");
        if (skippedLines > 0) notes.Add($"{skippedLines} lines past the last row skipped because a filter is active");
        MarkChanged();
        OnPropertyChanged(nameof(RowCountText));
        ShowStatus(notes.Count == 0 ? message + "." : $"{message}; {string.Join("; ", notes)}.", isError: false);
    }

    private static string Shorten(string text) => text.Length <= 20 ? text : text[..20] + "…";
}
