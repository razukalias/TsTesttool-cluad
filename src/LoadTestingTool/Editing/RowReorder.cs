using ClosedXML.Excel;

namespace LoadTestingTool.Editing;

/// <summary>
/// Puts the rows of every sheet that has a <c>testcaseindex</c> column in testcase order, so the rows of one testcase sit together
/// (rows added for testcase 1 after testcase 2 already existed end up at the bottom of the sheet until this runs on save).
/// The order inside a testcase is kept. Every <c>&lt;From_…_N&gt;</c> row reference in the workbook is renumbered and verified, like a row insert does.
/// A sheet that cannot be reordered safely (a blank or non-numeric testcase index, a cell with rich text, a comment or a link) is left as it is.
/// </summary>
internal static class RowReorder
{
    private sealed record SheetRows(IXLWorksheet Sheet, List<int> Rows, List<int> Order);

    /// <summary>Reorders the sheets in place. Returns how many rows changed place, and sets <paramref name="references"/> to how many references were rewritten.</summary>
    public static int Apply(XLWorkbook workbook, out int references)
    {
        references = 0;
        var plans = workbook.Worksheets.Select(Plan).Where(p => p is not null).Select(p => p!).ToList();
        if (plans.Count == 0) return 0;

        // old sheet row -> new sheet row, per sheet
        var maps = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
            maps[plan.Sheet.Name] = plan.Order.Select((oldRow, k) => (oldRow, slot: plan.Rows[k])).Where(x => x.oldRow != x.slot).ToDictionary(x => x.oldRow, x => x.slot);

        // 1. work out every rewritten reference first, while nothing has changed yet, and remember what each one pointed at
        var checks = new List<(string Sheet, string Header, int OldRow, int NewRow, string Before)>();
        var updates = new List<(IXLCell Cell, string Text)>();
        foreach (var sheet in workbook.Worksheets)
            foreach (var cell in sheet.CellsUsed())
            {
                var text = RowInsertion.TextOf(cell);
                if (text is null || !text.Contains("<From_", StringComparison.OrdinalIgnoreCase)) continue;
                var updated = RowInsertion.Rewrite(text, sheet.Name, [], [], (target, header, oldRow, newRow, _) =>
                {
                    if (oldRow != newRow) checks.Add((target, header, oldRow, newRow, RowInsertion.ReadCell(workbook, target, header, oldRow)));
                }, maps);
                if (updated == text) continue;
                if (cell.HasRichText) throw new InvalidOperationException($"{sheet.Name}!{cell.Address} holds rich text with a row reference; update it by hand before saving.");
                updates.Add((cell, updated));
            }
        foreach (var (cell, text) in updates) { if (cell.HasFormula) cell.FormulaA1 = text; else cell.Value = text; }

        // 2. move the rows
        var moved = 0;
        foreach (var plan in plans) moved += Move(plan);

        // 3. every reference must still land on the text it pointed at before
        foreach (var check in checks)
        {
            var after = RowInsertion.ReadCell(workbook, check.Sheet, check.Header, check.NewRow);
            var expected = RowInsertion.Rewrite(check.Before, check.Sheet, [], [], null, maps);   // the pointed-at cell may itself hold a reference
            if (!string.Equals(after, expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"Reordering the rows was stopped: a reference to {check.Sheet} '{check.Header}' row {check.OldRow} would have pointed at different data after moving to row {check.NewRow}. Nothing was saved.");
        }
        references = checks.Count;
        return moved;
    }

    /// <summary>The rows of a sheet in testcase order, or null when the sheet is already in order or cannot be reordered safely.</summary>
    private static SheetRows? Plan(IXLWorksheet sheet)
    {
        var header = sheet.FirstRowUsed();
        var last = sheet.LastRowUsed();
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (header is null || last is null || lastColumn == 0) return null;
        var column = header.CellsUsed().FirstOrDefault(c => c.GetString().Trim().Equals("testcaseindex", StringComparison.OrdinalIgnoreCase))?.Address.ColumnNumber;
        if (column is null) return null;

        var rows = new List<int>();
        var keys = new Dictionary<int, int>();
        for (var r = header.RowNumber() + 1; r <= last.RowNumber(); r++)
        {
            var row = sheet.Row(r);
            var blank = true;
            for (var c = 1; c <= lastColumn && blank; c++) blank = string.IsNullOrWhiteSpace(sheet.Cell(r, c).GetString());
            if (blank) continue;
            if (!int.TryParse(sheet.Cell(r, column.Value).GetString().Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var key)) return null;
            for (var c = 1; c <= lastColumn; c++)
            {
                var cell = sheet.Cell(r, c);
                if (cell.HasRichText || cell.HasComment || cell.HasHyperlink) return null;
            }
            rows.Add(r);
            keys[r] = key;
        }
        var order = rows.OrderBy(r => keys[r]).ThenBy(r => r).ToList();   // stable: the order inside a testcase stays
        return order.SequenceEqual(rows) ? null : new SheetRows(sheet, rows, order);
    }

    private sealed record Snapshot(int Column, XLCellValue Value, string? Formula, IXLStyle Style);

    /// <summary>Writes each row's content into the slot it now belongs in (the occupied row numbers stay the same set, so gaps stay gaps).</summary>
    private static int Move(SheetRows plan)
    {
        var sheet = plan.Sheet;
        var lastColumn = sheet.LastColumnUsed()!.ColumnNumber();
        var saved = new Dictionary<int, (List<Snapshot> Cells, double Height)>();
        foreach (var r in plan.Rows)
        {
            var cells = new List<Snapshot>();
            for (var c = 1; c <= lastColumn; c++)
            {
                var cell = sheet.Cell(r, c);
                cells.Add(new Snapshot(c, cell.Value, cell.HasFormula ? cell.FormulaA1 : null, cell.Style));
            }
            saved[r] = (cells, sheet.Row(r).Height);
        }
        var moved = 0;
        for (var k = 0; k < plan.Rows.Count; k++)
        {
            var slot = plan.Rows[k];
            var (cells, height) = saved[plan.Order[k]];
            if (plan.Order[k] != slot) moved++;
            foreach (var snapshot in cells)
            {
                var target = sheet.Cell(slot, snapshot.Column);
                target.Style = snapshot.Style;
                if (snapshot.Formula is not null) target.FormulaA1 = snapshot.Formula;
                else target.Value = snapshot.Value;
            }
            if (Math.Abs(height - sheet.RowHeight) > 0.01) sheet.Row(slot).Height = height; else sheet.Row(slot).ClearHeight();   // keep a chosen height, let Excel size the rest
        }
        return moved;
    }
}
