using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Insert a data row in the middle of the testcase. On save the request and response sheets get real rows at that
// place and every <From_sheet_header_N> reference in the workbook is renumbered (and verified).
public sealed partial class DataEditorViewModel
{
    private IReadOnlyList<RowReference>? _referenceScan;

    private IReadOnlyList<RowReference> ReferenceScan => _referenceScan ??= _document.FindRowReferences();

    [RelayCommand]
    private void InsertRowAbove()
    {
        if (SelectedTestcase is not { } testcase || SelectedRow is not { } selected) return;
        InsertRow(testcase, before: selected);
    }

    [RelayCommand]
    private void InsertRowBelow()
    {
        if (SelectedTestcase is not { } testcase || SelectedRow is not { } selected) return;
        var index = _allRows.IndexOf(selected);
        InsertRow(testcase, before: index >= 0 && index + 1 < _allRows.Count ? _allRows[index + 1] : null);
    }

    private void InsertRow(TestcaseItem testcase, DataRowViewModel? before)
    {
        if (before is null) { AppendRow(testcase, NextDataId(testcase), copyFrom: null); return; } // nothing below: a plain append

        var dataId = NextDataId(testcase);
        var beforeId = before.CurrentDataId;
        var position = before.Request.SourceRow;
        Record($"insert row {dataId} above {beforeId}");
        try
        {
            var (request, response) = _document.InsertDataRowBefore(testcase.Index, dataId, beforeId);
            FilterText = string.Empty;
            var row = new DataRowViewModel(testcase.Index, request, response, Columns, OnCellEdited);
            _allRows.Insert(Math.Max(0, _allRows.IndexOf(before)), row);
            ApplyFilter();
            testcase.RowCount++;
            SelectedRow = row;
            MarkChanged();

            var affected = position is int p
                ? ReferenceScan.Count(r => (r.TargetSheet.Equals(_document.Request.SheetName, StringComparison.OrdinalIgnoreCase) || r.TargetSheet.Equals(_document.Response.SheetName, StringComparison.OrdinalIgnoreCase)) && r.TargetRow >= p)
                : 0;
            ShowStatus($"Inserted {dataId} above {beforeId}. Saving adds a real row to the request and response sheets" +
                (affected == 0 ? "; no <From_…_N> references need renumbering." : $" and renumbers {affected} <From_…_N> {(affected == 1 ? "reference" : "references")} that point at rows below it (they are checked afterwards, and nothing is saved if one would point somewhere else)."), isError: false);
        }
        catch (InvalidOperationException ex)
        {
            DiscardLastRecord();
            ShowStatus(ex.Message, isError: true);
        }
    }

    /// <summary>After a save that inserted rows the sheet row numbers of everything below moved, so read the file again.</summary>
    private void ReloadAfterRowsMoved(string? keepDataId)
    {
        var keep = SelectedTestcase?.Index;
        _document = WorkbookDocument.Load(WorkbookPath);
        _referenceScan = null;
        LoadTestcases(keep);
        if (keepDataId is not null && VisibleRows.FirstOrDefault(r => r.Cells[0] == keepDataId) is { } row) SelectedRow = row;
    }
}
