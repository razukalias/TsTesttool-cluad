using System.Globalization;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Operations behind the right-click menus of the data grid: several rows at once, copy and paste of whole rows, and per-column clean-up.
// Each one is a single undo step.
public sealed partial class DataEditorViewModel
{
    private static string Rows(int n) => n == 1 ? "row" : "rows";

    /// <summary>Appends empty rows at the end of the testcase.</summary>
    public void AddRows(int count)
    {
        if (SelectedTestcase is not { } testcase) return;
        count = Math.Clamp(count, 1, 500);
        var made = RunBatch($"add {count} {Rows(count)}", () =>
        {
            for (var i = 0; i < count; i++) AppendRow(testcase, NextDataId(testcase), copyFrom: null);
            return count;
        });
        if (made > 0) ShowStatus($"Added {made} {Rows(made)}. They are saved with the workbook.", isError: false);
    }

    /// <summary>Appends copies of the selected row at the end of the testcase.</summary>
    public void DuplicateRows(int count)
    {
        if (SelectedTestcase is not { } testcase || SelectedRow is not { } source) return;
        count = Math.Clamp(count, 1, 500);
        var made = RunBatch($"duplicate row {count} times", () =>
        {
            for (var i = 0; i < count; i++) AppendRow(testcase, NextDataId(testcase), source);
            return count;
        });
        if (made > 0) ShowStatus($"Added {made} {(made == 1 ? "copy" : "copies")} of {source.Cells[0]} at the end.", isError: false);
    }

    /// <summary>Copies every selected row once to the end of the testcase, in the order shown.</summary>
    public void DuplicateRows(IReadOnlyList<DataRowViewModel> rows)
    {
        if (SelectedTestcase is not { } testcase || rows.Count == 0) return;
        var made = RunBatch($"duplicate {rows.Count} {Rows(rows.Count)}", () =>
        {
            foreach (var row in rows) AppendRow(testcase, NextDataId(testcase), row);
            return rows.Count;
        });
        if (made > 0) ShowStatus($"Added {made} {(made == 1 ? "copy" : "copies")} at the end, one per selected row.", isError: false);
    }

    /// <summary>Ticks or unticks the run box of every selected row.</summary>
    public void TickRows(IReadOnlyList<DataRowViewModel> rows, bool ticked)
    {
        foreach (var row in rows) row.IsTicked = ticked;
        ShowStatus($"{(ticked ? "Ticked" : "Unticked")} {rows.Count} {Rows(rows.Count)}.", isError: false);
    }

    /// <summary>Empties the request values or the expected values of every selected row (Data ID, tick and result are kept).</summary>
    public void ClearRowValues(IReadOnlyList<DataRowViewModel> rows, bool expected)
    {
        if (rows.Count == 0) return;
        var cells = Columns.Select((c, i) => (c, Cell: i + 1)).Where(x => (x.c.Group == ColumnGroup.Response) == expected).Select(x => x.Cell).ToList();
        Record($"clear {(expected ? "expected" : "request")} values of {rows.Count} {Rows(rows.Count)}");
        var changed = 0;
        foreach (var row in rows) foreach (var cell in cells) if (ApplyCell(row, cell, string.Empty) == CellResult.Changed) changed++;
        FinishBulk(changed, $"Cleared {changed} {(changed == 1 ? "cell" : "cells")} in {rows.Count} {Rows(rows.Count)}", 0, 0);
        AfterCellsChanged();
    }

    /// <summary>
    /// What would break if these rows were removed: the cells (outside the removed rows) that read one of them by row number
    /// (<c>&lt;From_Sheet_header_N&gt;</c>, as of the last save). Empty when nothing refers to them.
    /// </summary>
    public IReadOnlyList<string> RemovalWarnings(IReadOnlyList<DataRowViewModel> rows)
    {
        var doomed = new HashSet<(string Sheet, int Row)>();
        foreach (var row in rows)
        {
            if (row.Request.SourceRow is { } q) doomed.Add((_document.Request.SheetName, q));
            if (row.Response?.SourceRow is { } r) doomed.Add((_document.Response.SheetName, r));
        }
        var warnings = new List<string>();
        foreach (var reference in _document.FindRowReferences())
        {
            if (!doomed.Contains((reference.TargetSheet, reference.TargetRow))) continue;
            var cellRow = int.Parse(new string(reference.Cell.Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);
            if (doomed.Contains((reference.Sheet, cellRow))) continue;   // the reference goes away with its row
            warnings.Add($"{reference.Sheet}!{reference.Cell} reads {reference.TargetSheet} row {reference.TargetRow} ({reference.Token})");
        }
        return warnings;
    }

    public void ShowCancelled(string text) => ShowStatus(text, isError: false);

    /// <summary>Removes every given row (request and its paired expected row) as one step; one Undo brings them all back.</summary>
    public void RemoveRows(IReadOnlyList<DataRowViewModel> rows)
    {
        if (SelectedTestcase is not { } testcase || rows.Count == 0) return;
        Record($"remove {rows.Count} {Rows(rows.Count)}");
        foreach (var row in rows)
        {
            _document.Request.RemoveRow(row.Request);
            if (row.Response is not null) _document.Response.RemoveRow(row.Response);
            _allRows.Remove(row);
            VisibleRows.Remove(row);
        }
        testcase.RowCount = Math.Max(0, testcase.RowCount - rows.Count);
        MarkChanged();
        OnPropertyChanged(nameof(RowCountText));
        ShowStatus($"Removed {rows.Count} {Rows(rows.Count)}. They leave the file when you save; Undo brings them back.", isError: false);
    }

    /// <summary>Inserts empty rows directly above or below the selected row.</summary>
    public void InsertRows(int count, bool above)
    {
        if (SelectedTestcase is not { } testcase || SelectedRow is not { } selected) return;
        count = Math.Clamp(count, 1, 500);
        var index = _allRows.IndexOf(selected);
        var before = above ? selected : index >= 0 && index + 1 < _allRows.Count ? _allRows[index + 1] : null;
        var made = RunBatch($"insert {count} {Rows(count)} {(above ? "above" : "below")}", () =>
        {
            var done = 0;
            for (var i = 0; i < count && InsertRow(testcase, before); i++) done++;
            return done;
        });
        if (made > 0) ShowStatus($"Inserted {made} {Rows(made)} {(above ? "above" : "below")} {selected.Cells[0]}. Saving adds real rows to the request and response sheets and renumbers the <From_…_N> references below them.", isError: false);
    }

    /// <summary>The rows as tab-separated text (Data ID first, then the columns shown), ready for the clipboard.</summary>
    public string CopyRowsText(IEnumerable<DataRowViewModel> rows)
        => ClipboardTable.Format(rows.Select(row => Enumerable.Range(0, Columns.Count + 1).Select(i => row.Cells[i])));

    /// <summary>
    /// Pastes tab-separated lines as new rows below the selected one (or at the end). The first value of each line is a Data ID and is ignored:
    /// every new row gets the next free ID, and the other values fill the columns from the left.
    /// </summary>
    public void PasteRowsBelow(string[][] lines)
    {
        if (SelectedTestcase is not { } testcase || lines.Length == 0) return;
        var index = SelectedRow is { } selected ? _allRows.IndexOf(selected) : _allRows.Count - 1;
        var before = index >= 0 && index + 1 < _allRows.Count ? _allRows[index + 1] : null;
        var made = RunBatch($"paste {lines.Length} {Rows(lines.Length)} as new rows", () =>
        {
            var done = 0;
            foreach (var line in lines)
            {
                if (!InsertRow(testcase, before)) break;
                var row = SelectedRow!;
                for (var c = 1; c < line.Length && c <= Columns.Count; c++) ApplyCell(row, c, line[c]);
                done++;
            }
            return done;
        });
        if (made > 0) ShowStatus($"Pasted {made} new {Rows(made)} with the next free Data IDs.", isError: false);
    }

    /// <summary>Empties every cell of a column in the rows shown. The Data ID column cannot be cleared.</summary>
    public void ClearColumn(int cellIndex)
    {
        if (cellIndex < 1 || cellIndex > Columns.Count) { ShowStatus("Click a request or expected column first. Data IDs must stay unique, so they cannot be cleared.", isError: false); return; }
        var title = Columns[cellIndex - 1].Title;
        Record($"clear column {title}");
        var changed = VisibleRows.ToList().Count(row => ApplyCell(row, cellIndex, string.Empty) == CellResult.Changed);
        FinishBulk(changed, $"Cleared {title} in {changed} {Rows(changed)}", 0, 0);
    }

    /// <summary>Copies the first row's value of a column into every other row shown.</summary>
    public void FillColumnDown(int cellIndex) => FillDown(VisibleRows.ToList(), cellIndex);
}
