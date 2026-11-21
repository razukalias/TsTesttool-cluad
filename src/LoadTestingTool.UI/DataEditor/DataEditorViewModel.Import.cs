using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Import rows and columns from an Excel or CSV file into the selected testcase (Table view) or into the open other sheet.
// The dialog (ImportWindow) reads the file and builds an ImportPlan; this part applies it as ONE undo step, nothing is written until Save.
public sealed partial class DataEditorViewModel
{
    /// <summary>Raised when the Import window should open.</summary>
    public event EventHandler? ImportRequested;

    /// <summary>True when an import has somewhere to go: a selected testcase in Table view, or one of the other sheets.</summary>
    public bool CanImport => IsOtherSheet || (ShowDataGrid && SelectedTestcase is not null && !ShowSteps);

    public string ImportDestination => IsOtherSheet ? $"sheet {ActiveSheet}" : $"testcase {SelectedTestcase?.Name}";

    public int ImportExistingRowCount => IsOtherSheet ? ActiveOtherTable?.Rows.Count ?? 0 : _allRows.Count;

    public IReadOnlyList<string> ImportStepNames => SelectedTestcase?.Steps.ToList() ?? [];

    public bool ImportHasDataId => !IsOtherSheet;

    public bool HasSelectedRowForImport => IsOtherSheet ? SelectedSheetRow is not null : SelectedRow is not null;

    public bool HasSelectedColumnForImport => IsOtherSheet ? SheetCurrentColumn >= 0 : CurrentColumn is not null;

    [RelayCommand]
    private void OpenImport()
    {
        if (!CanImport) { ShowStatus("Choose a testcase (Table view) or open one of the other sheets first; that is where the import goes.", isError: true); return; }
        ImportRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The columns of the destination a file column can go into (the Data ID first for a testcase).</summary>
    public IReadOnlyList<ImportTarget> ImportTargetList()
    {
        if (IsOtherSheet)
        {
            var table = ActiveOtherTable;
            return table is null ? [] : table.Headers.Select((h, i) => new ImportTarget(i, h.Length == 0 ? $"({i + 1})" : h, h.Length == 0 ? [] : [h])).ToList();
        }
        var list = new List<ImportTarget> { new(0, "Data ID", ["Data ID", "dataid"]) };
        foreach (var column in Columns)
        {
            var header = TableOf(column).Headers[column.SheetColumn];
            var names = new[] { column.Title, CsvHeader(column), header, StripVerb(header) }.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            list.Add(new ImportTarget(column.CellIndex, (column.Group == ColumnGroup.Response ? "expected · " : "request · ") + column.Title, names));
        }
        return list;
    }

    /// <summary>One line the dialog shows before the user commits: what the chosen mode will do.</summary>
    public string DescribeImpact(ImportPlan plan)
    {
        var rows = plan.TakenRowCount;
        var existing = ImportExistingRowCount;
        var text = plan.Mode switch
        {
            ImportMode.AppendRows => $"Adds {rows} {Rows(rows)} at the end of {ImportDestination}.",
            ImportMode.InsertRowsBelow => $"Adds {rows} {Rows(rows)} below the selected row.",
            ImportMode.ReplaceRows => $"REPLACES all {existing} existing {Rows(existing)} of {ImportDestination} with {rows} {Rows(rows)} from the file.",
            ImportMode.FillExistingRows => $"Writes the matched columns of up to {rows} file {Rows(rows)} into existing rows ({(plan.RowMatch == ImportRowMatch.DataId && !IsOtherSheet ? "matched by Data ID" : "matched by position")}); adds no rows" + (plan.ClearOnEmpty ? "; empty file cells CLEAR the existing value." : "; empty file cells keep the existing value."),
            ImportMode.NewColumns => $"Creates {plan.TakeColumn.Count(t => t)} new columns and fills them for existing rows ({(plan.RowMatch == ImportRowMatch.DataId && !IsOtherSheet ? "matched by Data ID" : "matched by position")}).",
            _ => string.Empty,
        };
        if (plan.Mode == ImportMode.ReplaceRows && !IsOtherSheet && RemovalWarnings(_allRows).Count is var cells and > 0)
            text += $" {cells} {(cells == 1 ? "cell reads" : "cells read")} these rows by row number.";
        return text;
    }

    /// <summary>Replace modes (and clearing cells) overwrite data, so the dialog asks first.</summary>
    public static bool NeedsConfirmation(ImportPlan plan) => plan.Mode == ImportMode.ReplaceRows || (plan.Mode == ImportMode.FillExistingRows && plan.ClearOnEmpty);

    public ImportReport ApplyImport(ImportPlan plan)
    {
        var report = IsOtherSheet ? ApplySheetImport(plan) : ApplyTestcaseImport(plan);
        if (report.Error is not null) ShowStatus(report.Error, isError: true);
        return report;
    }

    // ---- Testcase rows ---------------------------------------------------------------------------------------

    private sealed record NewColumn(int FileColumn, string Header, bool Expected);

    private ImportReport ApplyTestcaseImport(ImportPlan plan)
    {
        var report = new ImportReport();
        if (SelectedTestcase is not { } testcase) { report.Error = "Choose a testcase first."; return report; }
        var targets = ImportTargetList();
        var table = plan.Table;
        var map = plan.Mode == ImportMode.NewColumns ? NewColumnsMap(plan, report.Notes) : ImportMapper.Resolve(plan, targets, report.Notes);
        var idFile = Array.IndexOf(map, 0);
        var byId = plan.RowMatch == ImportRowMatch.DataId;
        var fills = plan.Mode is ImportMode.FillExistingRows or ImportMode.NewColumns;
        if (fills && byId && idFile < 0) { report.Error = "Matching rows by Data ID needs a Data ID column in the file. Tick it, or choose to match by position."; return report; }
        if (map.All(m => m == ImportPlan.Skip)) { report.Error = "None of the ticked file columns can go anywhere. Check the column matching, or let unmatched columns be created."; return report; }
        if (plan.TakenRowCount == 0 && plan.Mode != ImportMode.NewColumns) { report.Error = "No file rows are ticked."; return report; }

        // New columns: work out their headers before anything changes, so a refusal leaves the workbook as it was.
        var created = new List<NewColumn>();
        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] != ImportPlan.Create) continue;
            if (!TryNewColumn(plan, table.Headers[i], testcase, out var header, out var expected, out var step, out var path, out var problem)) { report.Error = problem; return report; }
            var clash = expected ? _document.Response.Headers.Any(h => SamePath(h, step, path)) : _document.Request.FindColumn(header) >= 0;
            if (clash) { report.Notes.Add($"“{table.Headers[i]}” was skipped: the {(expected ? "response" : "request")} sheet already has a column for it"); map[i] = ImportPlan.Skip; continue; }
            created.Add(new NewColumn(i, header, expected));
        }
        if (plan.Mode == ImportMode.NewColumns && created.Count == 0) { report.Error = report.Notes.Count > 0 ? "Nothing to create: " + string.Join("; ", report.Notes) + "." : "Nothing to create."; return report; }

        ClearRowFilters();
        Record(DescribePlan(plan, "into " + testcase.Name));
        _idRenames.Clear();

        if (created.Count > 0)
        {
            var near = plan.Placement != ImportPlacement.End && CurrentColumn is { } current ? current : null;
            foreach (var group in created.GroupBy(c => c.Expected))
            {
                var sheet = group.Key ? _document.Response : _document.Request;
                var position = near is not null && near.Group == (group.Key ? ColumnGroup.Response : ColumnGroup.Request)
                    ? (plan.Placement == ImportPlacement.AfterSelected ? near.SheetColumn + 1 : near.SheetColumn) : -1;
                foreach (var column in group)
                {
                    if (position >= 0) sheet.InsertColumnBefore(position++, column.Header); else sheet.EnsureColumn(column.Header);
                    report.ColumnsCreated.Add(column.Header);
                }
            }
            BuildRows();
            foreach (var column in created)
            {
                var found = Columns.FirstOrDefault(c => c.Group == (column.Expected ? ColumnGroup.Response : ColumnGroup.Request) && string.Equals(TableOf(c).Headers[c.SheetColumn], column.Header, StringComparison.Ordinal));
                if (found is null) { report.Notes.Add($"column {column.Header} was created but is not shown, so its values were skipped"); map[column.FileColumn] = ImportPlan.Skip; }
                else map[column.FileColumn] = found.CellIndex;
            }
        }

        var fileRows = table.Rows.Where((_, i) => plan.TakeRow[i]).Select(r => r.Cells).ToList();
        if (plan.Mode == ImportMode.ReplaceRows)
        {
            var all = _allRows.ToList();
            RemoveRowsCore(testcase, all);
            report.Removed = all.Count;
        }

        if (fills) FillExisting(plan, map, idFile, fileRows, report);
        else AddImportedRows(plan, testcase, map, idFile, fileRows, report);

        if (IdRenameNote() is { Length: > 0 } renameNote) report.Notes.Add(renameNote);
        if (!report.Changed) { DiscardLastRecord(); ShowStatus(report.Describe(), isError: false); return report; }
        MarkChanged();
        ApplyFilter();
        OnPropertyChanged(nameof(RowCountText));
        ShowStatus(report.Describe(), isError: false);
        return report;
    }

    private int[] NewColumnsMap(ImportPlan plan, List<string> notes)
    {
        var map = new int[plan.Table.ColumnCount];
        var idFile = ImportMapper.DataIdColumn(plan.Table);
        for (var i = 0; i < map.Length; i++)
            map[i] = !plan.TakeColumn[i] ? ImportPlan.Skip : i == idFile && plan.RowMatch == ImportRowMatch.DataId ? 0 : i == idFile ? ImportPlan.Skip : plan.Table.Headers[i].Length == 0 ? ImportPlan.Skip : ImportPlan.Create;
        return map;
    }

    /// <summary>Header text of a new column: <c>Step.name</c> or <c>{verb}Step.path</c> (or <c>… [expected]</c>) is understood; a bare name goes to the chosen step.</summary>
    private bool TryNewColumn(ImportPlan plan, string fileHeader, TestcaseItem testcase, out string header, out bool expected, out string step, out string path, out string problem)
    {
        header = string.Empty; problem = string.Empty; step = string.Empty; path = string.Empty;
        var text = fileHeader.Trim();
        expected = plan.Expected;
        var verb = plan.Verb;
        if (text.EndsWith(ExpectedSuffix.Trim(), StringComparison.OrdinalIgnoreCase)) { text = text[..^ExpectedSuffix.Trim().Length].Trim(); expected = true; }
        if (text.StartsWith('{') && text.IndexOf('}') is var closing and > 1) { verb = text[1..closing]; text = text[(closing + 1)..].Trim(); expected = true; }
        var dot = text.IndexOf('.');
        if (dot > 0 && testcase.Steps.FirstOrDefault(s => s.Equals(text[..dot], StringComparison.OrdinalIgnoreCase)) is { } named) { step = named; path = text[(dot + 1)..]; }
        else
        {
            step = plan.Step ?? string.Empty;
            path = text;
            if (step.Length == 0) { problem = $"“{fileHeader}” does not say which step it belongs to. Choose the step the new columns belong to, or name the headers Step.name."; return false; }
            var chosen = step;
            if (testcase.Steps.All(s => !s.Equals(chosen, StringComparison.OrdinalIgnoreCase))) { problem = $"Step “{step}” is not a step of this testcase."; return false; }
        }
        if (path.Length == 0 || path.StartsWith('.') || path.EndsWith('.')) { problem = $"“{fileHeader}” is not a usable column name."; return false; }
        header = expected ? $"{{{verb}}}{step}.{path}" : $"{step}.{path}";
        return true;
    }

    private static string DescribePlan(ImportPlan plan, string where) => plan.Mode switch
    {
        ImportMode.AppendRows => $"import {plan.TakenRowCount} rows {where}",
        ImportMode.InsertRowsBelow => $"import {plan.TakenRowCount} rows below the selected row {where}",
        ImportMode.ReplaceRows => $"import and replace rows {where}",
        ImportMode.FillExistingRows => $"import values into existing rows {where}",
        _ => $"import new columns {where}",
    };

    private void RemoveRowsCore(TestcaseItem testcase, IReadOnlyList<DataRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            _document.Request.RemoveRow(row.Request);
            if (row.Response is not null) _document.Response.RemoveRow(row.Response);
            _allRows.Remove(row);
            VisibleRows.Remove(row);
        }
        testcase.RowCount = Math.Max(0, testcase.RowCount - rows.Count);
    }

    private void AddImportedRows(ImportPlan plan, TestcaseItem testcase, int[] map, int idFile, List<string[]> fileRows, ImportReport report)
    {
        DataRowViewModel? before = null;
        if (plan.Mode == ImportMode.InsertRowsBelow)
        {
            if (SelectedRow is { } selected)
            {
                var index = _allRows.IndexOf(selected);
                before = index >= 0 && index + 1 < _allRows.Count ? _allRows[index + 1] : null;
            }
            else report.Notes.Add("no row was selected, so the rows went to the end");
        }
        var taken = TakenIds(testcase.Index);
        foreach (var cells in fileRows)
        {
            var asked = idFile >= 0 && idFile < cells.Length ? cells[idFile].Trim() : string.Empty;
            var id = asked.Length == 0 ? NextDataId(testcase) : taken.Contains(asked) ? DataIdNumbering.Unique(asked, taken) : asked;
            if (asked.Length > 0 && id != asked) _idRenames.Add((asked, id));
            DataRowViewModel row;
            try { row = NewImportedRow(testcase, id, before); }
            catch (InvalidOperationException ex) { report.Notes.Add("stopped: " + ex.Message); break; }
            taken.Add(id);
            report.Added++;
            for (var c = 0; c < cells.Length && c < map.Length; c++)
            {
                if (map[c] <= 0 || cells[c].Length == 0) continue;
                switch (ApplyCell(row, map[c], cells[c])) { case CellResult.Changed: report.CellsChanged++; break; case CellResult.Rejected: report.CellsRefused++; break; }
            }
        }
    }

    private DataRowViewModel NewImportedRow(TestcaseItem testcase, string id, DataRowViewModel? before)
    {
        var (request, response) = before is null ? _document.AddDataRow(testcase.Index, id) : _document.InsertDataRowBefore(testcase.Index, id, before.CurrentDataId);
        var row = new DataRowViewModel(testcase.Index, request, response, Columns, OnCellEdited, _document.Request, _document.Response);
        if (before is null) _allRows.Add(row); else _allRows.Insert(Math.Max(0, _allRows.IndexOf(before)), row);
        testcase.RowCount++;
        return row;
    }

    private void FillExisting(ImportPlan plan, int[] map, int idFile, List<string[]> fileRows, ImportReport report)
    {
        var existing = _allRows.ToList();
        var byId = new Dictionary<string, DataRowViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in existing) byId.TryAdd(row.Cells[0], row);
        for (var k = 0; k < fileRows.Count; k++)
        {
            var cells = fileRows[k];
            DataRowViewModel? row;
            if (plan.RowMatch == ImportRowMatch.DataId)
                row = idFile < cells.Length && byId.TryGetValue(cells[idFile].Trim(), out var found) ? found : null;
            else row = k < existing.Count ? existing[k] : null;
            if (row is null) { report.NotFound++; continue; }
            var changed = false;
            for (var c = 0; c < cells.Length && c < map.Length; c++)
            {
                if (map[c] <= 0) continue;
                if (cells[c].Length == 0 && !plan.ClearOnEmpty) continue;
                switch (ApplyCell(row, map[c], cells[c])) { case CellResult.Changed: report.CellsChanged++; changed = true; break; case CellResult.Rejected: report.CellsRefused++; break; }
            }
            if (changed) report.Updated++;
        }
        if (report.NotFound > 0) report.Notes.Add(plan.RowMatch == ImportRowMatch.DataId ? "rows whose Data ID does not exist were not created (use an add-rows mode for that)" : "the file has more rows than the sheet");
    }

    // ---- Other sheets ----------------------------------------------------------------------------------------

    private ImportReport ApplySheetImport(ImportPlan plan)
    {
        var report = new ImportReport();
        if (ActiveOtherTable is not { } sheet) { report.Error = "Open one of the other sheets first."; return report; }
        var table = plan.Table;
        var map = plan.Mode == ImportMode.NewColumns
            ? Enumerable.Range(0, table.ColumnCount).Select(i => plan.TakeColumn[i] ? ImportPlan.Create : ImportPlan.Skip).ToArray()
            : ImportMapper.Resolve(plan, ImportTargetList(), report.Notes);
        if (map.All(m => m == ImportPlan.Skip)) { report.Error = "None of the ticked file columns can go anywhere. Check the column matching, or let unmatched columns be created."; return report; }
        if (plan.TakenRowCount == 0 && plan.Mode != ImportMode.NewColumns) { report.Error = "No file rows are ticked."; return report; }

        var headers = new Dictionary<int, string>();
        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] != ImportPlan.Create) continue;
            var header = table.Headers[i].Length > 0 ? table.Headers[i] : $"Column {ImportRange.Letter(table.ColumnNumbers[i])}";
            if (sheet.FindColumn(header) >= 0 || headers.ContainsValue(header)) { report.Notes.Add($"“{header}” was skipped: the sheet already has that column"); map[i] = ImportPlan.Skip; continue; }
            headers[i] = header;
        }
        if (sheet.ColumnCount == 0 && headers.Count == 0) { report.Error = "The sheet has no columns yet; let the file's columns be created."; return report; }

        Record(DescribePlan(plan, "into " + ActiveSheet));
        var rowsBefore = sheet.Rows.ToList();
        var affected = ReferencesAtOrBelow(sheet, sheet.HeaderRowNumber + 1);
        if (headers.Count > 0)
        {
            var current = SheetCurrentColumn >= 0 && SheetCurrentColumn < sheet.ColumnCount ? SheetCurrentColumn : -1;
            var position = plan.Placement switch { ImportPlacement.BeforeSelected when current >= 0 => current, ImportPlacement.AfterSelected when current >= 0 => current + 1, _ => sheet.ColumnCount };
            foreach (var (fileColumn, header) in headers) { sheet.InsertColumnBefore(position++, header); report.ColumnsCreated.Add(header); }
            foreach (var (fileColumn, header) in headers) map[fileColumn] = sheet.FindColumn(header);
        }

        var fileRows = table.Rows.Where((_, i) => plan.TakeRow[i]).Select(r => r.Cells).ToList();
        void Fill(TableRow row, string[] cells, ref bool changed)
        {
            for (var c = 0; c < cells.Length && c < map.Length; c++)
            {
                if (map[c] < 0) continue;
                if (cells[c].Length == 0 && !(plan.Mode == ImportMode.FillExistingRows && plan.ClearOnEmpty)) continue;
                if (row.GetAt(map[c]) == cells[c]) continue;
                row.SetAt(map[c], cells[c]);
                report.CellsChanged++;
                changed = true;
            }
        }

        switch (plan.Mode)
        {
            case ImportMode.FillExistingRows or ImportMode.NewColumns:
                var existing = sheet.Rows.ToList();
                for (var k = 0; k < fileRows.Count; k++)
                {
                    if (k >= existing.Count) { report.NotFound++; continue; }
                    var changed = false;
                    Fill(existing[k], fileRows[k], ref changed);
                    if (changed) report.Updated++;
                }
                if (report.NotFound > 0) report.Notes.Add("the file has more rows than the sheet, and this mode adds no rows");
                break;
            default:
                if (plan.Mode == ImportMode.ReplaceRows)
                {
                    foreach (var row in rowsBefore) sheet.RemoveRow(row);
                    report.Removed = rowsBefore.Count;
                }
                TableRow? neighbor = null;
                if (plan.Mode == ImportMode.InsertRowsBelow)
                {
                    if (SelectedSheetRow?.Row is { } selected) neighbor = NextRow(sheet, selected);
                    else report.Notes.Add("no row was selected, so the rows went to the end");
                }
                foreach (var cells in fileRows)
                {
                    var row = neighbor is null ? sheet.AddRow() : sheet.InsertRowBefore(neighbor);
                    var changed = false;
                    Fill(row, cells, ref changed);
                    report.Added++;
                }
                if (plan.Mode == ImportMode.InsertRowsBelow && neighbor is not null || plan.Mode == ImportMode.ReplaceRows) report.Notes.Add(RenumberNote(affected).TrimEnd('.'));
                break;
        }

        if (!report.Changed) { DiscardLastRecord(); ShowStatus(report.Describe(), isError: false); return report; }
        FinishSheetEdit(report.Describe());
        return report;
    }
}
