using System.Globalization;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Row and column menus of the other sheets (not config/request/response): insert rows in the middle, copy and paste rows, fill down and clear a column.
// A row inserted in the middle is a real sheet row once saved, and every <From_Sheet_header_N> reference to the rows below it is renumbered
// (the same save step the request and response sheets use), so nothing that pointed at a row points somewhere else afterwards.
public sealed partial class DataEditorViewModel
{
    /// <summary>References (in any sheet) to rows of the open sheet at or below <paramref name="position"/>: the ones an insert there renumbers.</summary>
    private int ReferencesAtOrBelow(SheetTable table, int? position)
    {
        if (position is not int from) return 0;
        var name = table.OriginalSheetName ?? table.SheetName;
        return ReferenceScan.Count(r => r.TargetSheet.Equals(name, StringComparison.OrdinalIgnoreCase) && r.TargetRow >= from);
    }

    private string RenumberNote(int affected) => affected == 0
        ? "No <From_…_N> references need renumbering."
        : $"Saving renumbers {affected} <From_…_N> {(affected == 1 ? "reference" : "references")} that point at rows below (each is checked afterwards, and nothing is written if one would point somewhere else).";

    private const string EmptyRowNote = " An empty row is only written to the file once it has a value.";

    /// <summary>Adds empty rows at the end of the open sheet.</summary>
    public void AddSheetRows(int count)
    {
        if (ActiveOtherTable is not { } table) return;
        if (table.ColumnCount == 0) { ShowStatus("Add a column first; a sheet with no columns has nowhere to put a row.", isError: true); return; }
        count = Math.Clamp(count, 1, 500);
        Record($"add {count} {Rows(count)} to {ActiveSheet}");
        for (var i = 0; i < count; i++) table.AddRow();
        FinishSheetEdit($"Added {count} {Rows(count)} at the end of {ActiveSheet}.{EmptyRowNote}");
    }

    /// <summary>Inserts empty rows directly above or below the selected row (several at once is one undo step).</summary>
    public void InsertSheetRows(int count, bool above)
    {
        if (ActiveOtherTable is not { } table || SelectedSheetRow?.Row is not { } selected) { ShowStatus("Click a row first.", isError: true); return; }
        if (table.ColumnCount == 0) { ShowStatus("Add a column first; a sheet with no columns has nowhere to put a row.", isError: true); return; }
        count = Math.Clamp(count, 1, 500);
        var neighbor = above ? selected : NextRow(table, selected);
        Record($"insert {count} {Rows(count)} {(above ? "above" : "below")} in {ActiveSheet}");
        var affected = neighbor is null ? 0 : ReferencesAtOrBelow(table, neighbor.SourceRow);
        for (var i = 0; i < count; i++) _ = neighbor is null ? table.AddRow() : table.InsertRowBefore(neighbor);
        FinishSheetEdit($"Inserted {count} {Rows(count)} {(above ? "above" : "below")} the selected row. {RenumberNote(affected)}{EmptyRowNote}");
    }

    private static TableRow? NextRow(SheetTable table, TableRow row)
    {
        var index = table.Rows.ToList().IndexOf(row);
        return index >= 0 && index + 1 < table.Rows.Count ? table.Rows[index + 1] : null;
    }

    /// <summary>The rows as tab-separated text for the clipboard (all columns).</summary>
    public string CopySheetRowsText(IEnumerable<SheetRowViewModel> rows)
        => ClipboardTable.Format(rows.Select(row => row.Cells.AsEnumerable()));

    /// <summary>Pastes tab-separated lines as new rows below the selected one (or at the end), filling the columns from the left.</summary>
    public void PasteSheetRowsBelow(string[][] lines)
    {
        if (ActiveOtherTable is not { } table || lines.Length == 0) return;
        if (table.ColumnCount == 0) { ShowStatus("Add a column first; a sheet with no columns has nowhere to put a row.", isError: true); return; }
        var neighbor = SelectedSheetRow?.Row is { } selected ? NextRow(table, selected) : null;
        Record($"paste {lines.Length} {Rows(lines.Length)} into {ActiveSheet}");
        var affected = neighbor is null ? 0 : ReferencesAtOrBelow(table, neighbor.SourceRow);
        foreach (var line in lines)
        {
            var row = neighbor is null ? table.AddRow() : table.InsertRowBefore(neighbor);
            for (var c = 0; c < line.Length && c < table.ColumnCount; c++) if (line[c].Length > 0) row.SetAt(c, line[c]);
        }
        FinishSheetEdit($"Pasted {lines.Length} new {Rows(lines.Length)}. {RenumberNote(affected)}");
    }

    /// <summary>Copies the first row's value of the clicked column into every row below it.</summary>
    public void FillSheetColumnDown(int column)
    {
        if (ActiveOtherTable is not { } table || column < 0 || column >= table.ColumnCount) { ShowStatus("Click a cell in the column first.", isError: true); return; }
        var rows = table.Rows.ToList();
        if (rows.Count < 2) { ShowStatus("Fill down needs at least two rows.", isError: false); return; }
        var value = rows[0].GetAt(column);
        Record($"fill down {DescribeColumn(table, column)} in {ActiveSheet}");
        var changed = 0;
        foreach (var row in rows.Skip(1)) if (row.GetAt(column) != value) { row.SetAt(column, value); changed++; }
        if (changed == 0) { DiscardLastRecord(); ShowStatus("Nothing to change.", isError: false); return; }
        FinishSheetEdit($"Filled {DescribeColumn(table, column)} down: {changed} {Rows(changed)} now hold \"{value}\".");
    }

    /// <summary>Empties every cell of the clicked column.</summary>
    public void ClearSheetColumn(int column)
    {
        if (ActiveOtherTable is not { } table || column < 0 || column >= table.ColumnCount) { ShowStatus("Click a cell in the column first.", isError: true); return; }
        var refs = table.Headers[column].Length == 0 ? [] : _document.FindColumnReferences(table.SheetName, table.Headers[column]);
        Record($"clear column {DescribeColumn(table, column)} in {ActiveSheet}");
        var changed = 0;
        foreach (var row in table.Rows) if (row.GetAt(column).Length > 0) { row.SetAt(column, string.Empty); changed++; }
        if (changed == 0) { DiscardLastRecord(); ShowStatus("The column is already empty.", isError: false); return; }
        FinishSheetEdit($"Cleared {DescribeColumn(table, column)} in {changed} {Rows(changed)}." + (refs.Count > 0 ? $" Note: {refs.Count} <From_…> {(refs.Count == 1 ? "reference reads" : "references read")} this column." : string.Empty));
    }

    /// <summary>Opens the add-columns panel with the placement and count already chosen (the column header menu).</summary>
    public void OpenInsertSheetColumns(bool left, int count)
    {
        if (ActiveOtherTable is null) return;
        OpenAddSheetColumns();
        SheetPanelPlacement = SheetCurrentColumn >= 0 ? (left ? PlaceColumnBefore : PlaceColumnAfter) : PlaceColumnAtEnd;
        SheetPanelCount = count;
    }

    private void FinishSheetEdit(string message)
    {
        var keep = SelectedSheetRow?.Row;
        MarkChanged();
        LoadActiveSheet();
        if (keep is not null) SelectedSheetRow = SheetRows.FirstOrDefault(r => ReferenceEquals(r.Row, keep));
        ShowStatus(message, isError: false);
    }
}
