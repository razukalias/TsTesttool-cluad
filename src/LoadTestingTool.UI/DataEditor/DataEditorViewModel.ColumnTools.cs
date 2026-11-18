using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

// Working on several columns at once (picked by their letters) and the question asked when a rename would change references.
public sealed partial class DataEditorViewModel
{
    /// <summary>The Excel letter of a data column in its own sheet (request or response); a column added in the editor shows its position and a "+" until it is saved.</summary>
    public string ExcelLetterOf(GridColumn column)
    {
        if (column.Group == ColumnGroup.Id || column.SheetColumn < 0) return string.Empty;
        var source = TableOf(column).SourceColumnOf(column.SheetColumn);
        return ClosedXML.Excel.XLHelper.GetColumnLetterFromNumber(source ?? column.SheetColumn + 1) + (source is null ? "+" : string.Empty);
    }

    /// <summary>The header text of a data column as it stands in its sheet (including a {verb} prefix).</summary>
    public string HeaderText(int cellIndex)
        => cellIndex >= 1 && cellIndex <= Columns.Count ? TableOf(Columns[cellIndex - 1]).Headers[Columns[cellIndex - 1].SheetColumn] : string.Empty;

    private static string Cols(int n) => n == 1 ? "column" : "columns";

    /// <summary>The picked columns' cells of the given rows as tab-separated text (with the headers on the first line), ready for the clipboard.</summary>
    public string CopyCellsText(IReadOnlyList<int> cells, IReadOnlyList<DataRowViewModel> rows)
    {
        var used = cells.Where(c => c >= 0 && c <= Columns.Count).OrderBy(c => c).ToList();
        var header = used.Select(c => c == 0 ? "Data ID" : Columns[c - 1].Title);
        return ClipboardTable.Format(new[] { header }.Concat(rows.Select(row => used.Select(c => row.Cells[c]))));
    }

    /// <summary>Empties the cells of the picked columns in the given rows (the Data ID column is never cleared).</summary>
    public void ClearCells(IReadOnlyList<int> cells, IReadOnlyList<DataRowViewModel> rows)
    {
        var used = cells.Where(c => c >= 1 && c <= Columns.Count).Distinct().ToList();
        if (used.Count == 0) { ShowStatus("Pick a request or expected column first. Data IDs must stay unique, so they cannot be cleared.", isError: false); return; }
        Record($"clear {used.Count} {Cols(used.Count)}");
        var changed = rows.Sum(row => used.Count(c => ApplyCell(row, c, string.Empty) == CellResult.Changed));
        FinishBulk(changed, $"Cleared {changed} {(changed == 1 ? "cell" : "cells")} in {used.Count} {Cols(used.Count)}", 0, 0);
    }

    /// <summary>Copies the first row's value of each picked column into the other given rows.</summary>
    public void FillCellsDown(IReadOnlyList<int> cells, IReadOnlyList<DataRowViewModel> rows)
    {
        var used = cells.Where(c => c >= 1 && c <= Columns.Count).Distinct().ToList();
        if (used.Count == 0) { ShowStatus("Pick a request or expected column first. Data IDs must stay unique, so they cannot be filled.", isError: false); return; }
        if (rows.Count < 2) { ShowStatus("There is only one row to fill from. Select two or more rows, or clear the row filter.", isError: false); return; }
        Record($"fill down {used.Count} {Cols(used.Count)}");
        var changed = used.Sum(c => rows.Skip(1).Count(row => ApplyCell(row, c, rows[0].Cells[c]) == CellResult.Changed));
        FinishBulk(changed, $"Filled {used.Count} {Cols(used.Count)} down, {changed} {(changed == 1 ? "cell" : "cells")} changed", 0, 0);
    }

    /// <summary>Removes the picked columns from their sheets. Refused as a whole while a &lt;From_…&gt; reference points at one of them.</summary>
    public bool RemoveColumns(IReadOnlyList<int> cells)
    {
        var picked = cells.Where(c => c >= 1 && c <= Columns.Count).Distinct().Select(c => Columns[c - 1]).ToList();
        if (picked.Count == 0) { ShowStatus("Pick a request or expected column first.", isError: false); return false; }
        foreach (var column in picked)
        {
            var header = TableOf(column).Headers[column.SheetColumn];
            var refs = _document.FindColumnReferences(TableOf(column).SheetName, header);
            if (refs.Count > 0)
            {
                ShowStatus($"Cannot remove {header}: {refs.Count} <From_…> {(refs.Count == 1 ? "reference" : "references")} point at it (for example {refs[0].Sheet}!{refs[0].Cell}). Change those first. Nothing was removed.", isError: true);
                return false;
            }
        }
        var names = string.Join(", ", picked.Select(c => TableOf(c).Headers[c.SheetColumn]));
        Record($"remove {picked.Count} {Cols(picked.Count)}");
        // highest sheet column first, so the positions of the others stay valid
        foreach (var column in picked.OrderByDescending(c => c.SheetColumn)) TableOf(column).RemoveColumn(column.SheetColumn);
        MarkChanged();
        BuildRows();
        ShowStatus($"Removed {names}. Use Undo to bring {(picked.Count == 1 ? "it" : "them")} back. Saving empties the column in the sheet; the columns after it stay where they are.", isError: false);
        return true;
    }

    // ---- Saving a rename: ask what happens to the references ---------------------------------------

    [ObservableProperty] private bool _renameFixVisible;
    [ObservableProperty] private string _renameFixSummary = string.Empty;
    [ObservableProperty] private string _renameFixList = string.Empty;
    private bool? _fixReferencesChoice;

    /// <summary>True (and the question is shown) when saving would rewrite references because a column was renamed and the user has not decided yet.</summary>
    private bool AskAboutRenamedReferences()
    {
        if (_fixReferencesChoice is not null || !_document.HasRenamedColumns) return false;
        var fixes = _document.PendingReferenceFixes();
        if (fixes.Count == 0) return false;
        RenameFixSummary = $"A renamed column is used by {fixes.Count} {(fixes.Count == 1 ? "cell" : "cells")}. Rename and fix rewrites them to the new name; only rename leaves them pointing at the old name (the runner will then not find the value).";
        RenameFixList = string.Join("\n", fixes.Take(12).Select(f => $"{f.Sheet}!{f.Cell}:  {ChangedTokens(f.Before, f.After)}")) + (fixes.Count > 12 ? $"\n+ {fixes.Count - 12} more" : string.Empty);
        RenameFixVisible = true;
        return true;
    }

    /// <summary>Just the references that change in a cell, e.g. "&lt;From_request_email_2&gt; → &lt;From_request_mail_2&gt;".</summary>
    private static string ChangedTokens(string before, string after)
    {
        var old = System.Text.RegularExpressions.Regex.Matches(before, "<From_[^>]*>").Select(m => m.Value).ToList();
        var now = System.Text.RegularExpressions.Regex.Matches(after, "<From_[^>]*>").Select(m => m.Value).ToList();
        var pairs = old.Zip(now).Where(p => p.First != p.Second).Select(p => $"{p.First} → {p.Second}").ToList();
        return pairs.Count > 0 ? string.Join(", ", pairs) : Shorten(before, 50) + " → " + Shorten(after, 50);
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    [RelayCommand]
    private void SaveFixingReferences() { _fixReferencesChoice = true; RenameFixVisible = false; SaveCore(new LoadTestingTool.Editing.WorkbookSaveOptions()); _fixReferencesChoice = null; }

    [RelayCommand]
    private void SaveRenameOnly() { _fixReferencesChoice = false; RenameFixVisible = false; SaveCore(new LoadTestingTool.Editing.WorkbookSaveOptions()); _fixReferencesChoice = null; }

    [RelayCommand]
    private void CancelRenameFix() => RenameFixVisible = false;
}
