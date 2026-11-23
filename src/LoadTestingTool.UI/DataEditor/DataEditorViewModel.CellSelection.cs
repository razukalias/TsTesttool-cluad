namespace LoadTestingTool.UI.DataEditor;

// What can be done with the selected cells: copy, paste, set one value, clear, tag as known bug, assign the actual value of the latest run.
public sealed partial class DataEditorViewModel
{
    private CellSelection? _cellSelection;

    /// <summary>The cells selected in the table (Excel style); one kind of cell at a time.</summary>
    public CellSelection CellSelection
    {
        get
        {
            if (_cellSelection is null)
            {
                _cellSelection = new CellSelection(this);
                _cellSelection.Changed += (_, _) => { OnPropertyChanged(nameof(SelectionText)); if (FindVisible && FindScope == ScopeSelectedCells) UpdateMatches(); };
            }
            return _cellSelection;
        }
    }

    /// <summary>"5 cells selected" beside the row count; empty for none or one cell.</summary>
    public string SelectionText => _cellSelection is { Count: > 1 } selection ? $"{selection.Count} cells selected" : string.Empty;

    /// <summary>The selected cells as tab-separated text: the block from the first to the last selected row and column, as shown.</summary>
    public string CopySelectionText()
    {
        var cells = CellSelection.Cells;
        if (cells.Count == 0) return string.Empty;
        var order = (CellSelection.CellOrder?.Invoke() ?? Enumerable.Range(1, Columns.Count).ToList()).ToList();
        var rows = cells.Select(c => VisibleRows.IndexOf(c.Row)).Where(i => i >= 0).ToList();
        var positions = cells.Select(c => order.IndexOf(c.Cell)).Where(i => i >= 0).ToList();
        if (rows.Count == 0 || positions.Count == 0) return string.Empty;
        var lines = new List<IEnumerable<string>>();
        for (var r = rows.Min(); r <= rows.Max(); r++)
            lines.Add(Enumerable.Range(positions.Min(), positions.Max() - positions.Min() + 1).Select(p => VisibleRows[r].Cells[order[p]] ?? string.Empty).ToList());
        return ClipboardTable.Format(lines);
    }

    /// <summary>Writes one value into every selected cell, as one undo step.</summary>
    public void SetSelectedCells(string value)
    {
        var cells = CellSelection.Cells;
        if (cells.Count == 0) { ShowStatus("Select one or more cells first.", isError: false); return; }
        Record($"set {cells.Count} {(cells.Count == 1 ? "cell" : "cells")}");
        var changed = cells.Count(c => ApplyCell(c.Row, c.Cell, value) == CellResult.Changed);
        FinishBulk(changed, value.Length == 0 ? $"Cleared {changed} {(changed == 1 ? "cell" : "cells")}" : $"Set {changed} {(changed == 1 ? "cell" : "cells")}", 0, 0);
        AfterCellsChanged();
    }

    /// <summary>
    /// Pastes copied cells. One copied value goes into every selected cell; a block goes into the cells below and to the right of the first selected cell,
    /// but only into cells of the same kind as the selection (request or expected). Rows are never added.
    /// </summary>
    public void PasteIntoSelection(string[][] block)
    {
        var cells = CellSelection.Cells;
        if (cells.Count == 0 || block.Length == 0 || block.All(line => line.Length == 0)) return;
        if (block.Length == 1 && block[0].Length == 1) { SetSelectedCells(block[0][0]); return; }
        var order = (CellSelection.CellOrder?.Invoke() ?? Enumerable.Range(1, Columns.Count).ToList()).ToList();
        var startRow = cells.Min(c => VisibleRows.IndexOf(c.Row));
        var startPosition = cells.Min(c => order.IndexOf(c.Cell));
        var kind = CellSelection.Kind;
        Record($"paste {block.Length} {(block.Length == 1 ? "row" : "rows")} into cells");
        int changed = 0, skipped = 0;
        for (var r = 0; r < block.Length && startRow + r < VisibleRows.Count; r++)
            for (var c = 0; c < block[r].Length; c++)
            {
                if (startPosition + c >= order.Count) { skipped++; continue; }
                var cell = order[startPosition + c];
                if (!CellSelection.Selectable(cell) || CellSelection.KindOf(cell) != kind) { skipped++; continue; }
                if (ApplyCell(VisibleRows[startRow + r], cell, block[r][c]) == CellResult.Changed) changed++;
            }
        FinishBulk(changed, $"Pasted into {changed} {(changed == 1 ? "cell" : "cells")}", skipped, 0);
        AfterCellsChanged();
    }

    /// <summary>For each selected expected cell that failed in the latest run, writes what the run got into it (one undo step); other cells are left alone.</summary>
    public int AssignActualToSelection()
    {
        var made = RunBatch("assign actual values", () =>
        {
            var done = 0;
            foreach (var (row, cell) in CellSelection.Cells)
                if (IsExpectedCell(cell) && FailureAt(row, cell) is { } failure && AssignActual(row, cell, failure)) done++;
            return done;
        });
        ShowStatus(made == 0 ? "None of the selected cells has a failed value that can be assigned." : $"Assigned the actual value to {made} {(made == 1 ? "cell" : "cells")}.", isError: false);
        AfterCellsChanged();
        return made;
    }

    /// <summary>A short note in the status line (not an error).</summary>
    public void Hint(string text) => ShowStatus(text, isError: false);

    private void AfterCellsChanged()
    {
        if (ShowFailedCells) RefreshFailedCells();
        if (ShowFailedColumns || ShowBugColumns) RefreshFailedColumns();
    }
}
