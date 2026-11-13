using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace LoadTestingTool.Editing;

/// <summary>A cell whose text refers to a worksheet row by number, e.g. <c>&lt;From_request_title_4&gt;</c>.</summary>
public sealed record RowReference(string Sheet, string Cell, string Token, string TargetSheet, int TargetRow);

/// <summary>
/// Inserts rows in the middle of a sheet without breaking absolute-row references. Every
/// <c>&lt;From_sheet_header_N&gt;</c> and <c>&lt;From_header_N&gt;</c> in any cell of any sheet is renumbered, and the
/// result is verified: each renumbered reference must still point at a cell with the same text as before.
/// </summary>
internal static class RowInsertion
{
    private static readonly Regex Full = new(@"<From_([^_>]+)_([^_>]+)_(\d+)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Short = new(@"<From_([^_>]+)_(\d+)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// <c>&lt;From_response_Step.output_2&gt;</c> is not a row reference: the runner leaves a response reference whose name contains a dot
    /// alone and resolves it from earlier step responses at run time, so its number must never be touched.
    /// </summary>
    internal static bool IsRuntimeReference(string sheet, string header)
        => sheet.Equals("response", StringComparison.OrdinalIgnoreCase) && header.Contains('.', StringComparison.Ordinal);

    /// <summary>How many numeric row references one cell's text holds.</summary>
    internal static int CountRowReferences(string text)
        => Full.Matches(text).Count(m => !IsRuntimeReference(m.Groups[1].Value, m.Groups[2].Value)) + Short.Matches(text).Count;

    /// <summary>All numeric row references in the workbook (used to tell the user what an insert will touch).</summary>
    public static List<RowReference> Scan(XLWorkbook workbook)
    {
        var found = new List<RowReference>();
        foreach (var sheet in workbook.Worksheets)
            foreach (var cell in sheet.CellsUsed())
            {
                var text = TextOf(cell);
                if (text is null || !text.Contains("<From_", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (Match m in Full.Matches(text).Where(m => !IsRuntimeReference(m.Groups[1].Value, m.Groups[2].Value))) found.Add(new RowReference(sheet.Name, cell.Address.ToStringRelative(), m.Value, m.Groups[1].Value, int.Parse(m.Groups[3].Value)));
                foreach (Match m in Short.Matches(text)) found.Add(new RowReference(sheet.Name, cell.Address.ToStringRelative(), m.Value, sheet.Name, int.Parse(m.Groups[2].Value)));
            }
        return found;
    }

    /// <summary>
    /// Inserts the pending rows of the given tables (already positioned by the caller) and renumbers references.
    /// Returns how many references were rewritten. Throws, leaving the in-memory workbook unusable, if verification fails.
    /// </summary>
    public static int Apply(XLWorkbook workbook, IReadOnlyList<SheetTable> tables)
    {
        var renames = tables.ToDictionary(t => t.SheetName, t => t.RenamedHeaders(), StringComparer.OrdinalIgnoreCase);
        var inserts = tables.ToDictionary(t => t, t => t.PendingInserts(), ReferenceEqualityComparer.Instance as IEqualityComparer<SheetTable>);
        var positions = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
            if (inserts[table].Count > 0) positions[table.SheetName] = inserts[table].Select(i => i.Before).OrderBy(x => x).ToList();
        if (positions.Count == 0 && renames.Values.All(r => r.Count == 0)) return 0;

        // 1. Rewrite every reference (text only; the cells still sit at their old rows) and remember what each pointed at.
        var checks = new List<(string Sheet, string Header, int OldRow, int NewRow, string Before)>();
        foreach (var sheet in workbook.Worksheets)
            foreach (var cell in sheet.CellsUsed())
            {
                var text = TextOf(cell);
                if (text is null || !text.Contains("<From_", StringComparison.OrdinalIgnoreCase)) continue;
                var updated = Rewrite(text, sheet.Name, positions, renames, (target, header, oldRow, newRow, renamed) =>
                {
                    if (oldRow != newRow || renamed) checks.Add((target, header, oldRow, newRow, ReadCell(workbook, target, header, oldRow)));
                });
                if (updated == text) continue;
                if (cell.HasRichText) throw new InvalidOperationException($"{sheet.Name}!{cell.Address} holds rich text with a row reference; update it by hand before inserting rows.");
                if (cell.HasFormula) cell.FormulaA1 = updated; else cell.Value = updated;
            }

        // 2. Insert the blank rows, bottom first so earlier positions stay valid.
        foreach (var (sheetName, list) in positions)
        {
            var sheet = workbook.Worksheets.First(w => w.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
            foreach (var position in list.OrderByDescending(x => x)) sheet.Row(position).InsertRowsAbove(1);
        }

        // 3. Every reference must still land on the same text it pointed at before.
        foreach (var check in checks)
        {
            var after = ReadCell(workbook, check.Sheet, check.Header, check.NewRow);
            if (!string.Equals(after, check.Before, StringComparison.Ordinal))
                throw new InvalidOperationException($"Row insert aborted: a reference to {check.Sheet} '{check.Header}' row {check.OldRow} would have pointed at different data after renumbering to row {check.NewRow}. Nothing was saved.");
        }

        // 4. Fill the new rows (their text uses the numbering the editor showed, so it is renumbered too).
        foreach (var table in tables)
        {
            var sheet = workbook.Worksheets.First(w => w.Name.Equals(table.SheetName, StringComparison.OrdinalIgnoreCase));
            table.WriteInsertedRows(sheet, inserts[table], text => Rewrite(text, table.SheetName, positions, renames, null));
        }
        return checks.Count;
    }

    private static string Rewrite(string text, string cellSheet, Dictionary<string, List<int>> positions, Dictionary<string, Dictionary<string, string>> renames, Action<string, string, int, int, bool>? seen)
    {
        int Map(string sheet, int row) => positions.TryGetValue(sheet, out var list) ? row + list.Count(p => p <= row) : row;
        string Header(string sheet, string header) => renames.TryGetValue(sheet, out var map) && map.TryGetValue(header, out var renamedTo) ? renamedTo : header;
        var result = Full.Replace(text, m =>
        {
            var sheet = m.Groups[1].Value; var header = m.Groups[2].Value; var row = int.Parse(m.Groups[3].Value);
            if (IsRuntimeReference(sheet, header)) return m.Value;
            var mapped = Map(sheet, row);
            var newHeader = Header(sheet, header);
            seen?.Invoke(sheet, newHeader, row, mapped, newHeader != header);
            return mapped == row && newHeader == header ? m.Value : $"<From_{sheet}_{newHeader}_{mapped}>";
        });
        return Short.Replace(result, m =>
        {
            var header = m.Groups[1].Value; var row = int.Parse(m.Groups[2].Value);
            var mapped = Map(cellSheet, row);
            var newHeader = Header(cellSheet, header);
            seen?.Invoke(cellSheet, newHeader, row, mapped, newHeader != header);
            return mapped == row && newHeader == header ? m.Value : $"<From_{newHeader}_{mapped}>";
        });
    }

    private static string? TextOf(IXLCell cell)
        => cell.HasFormula ? cell.FormulaA1 : cell.DataType == XLDataType.Text ? cell.GetString() : null;

    private static string ReadCell(XLWorkbook workbook, string sheetName, string header, int row)
    {
        var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
        var headerRow = sheet?.FirstRowUsed();
        var column = headerRow?.CellsUsed().FirstOrDefault(c => c.GetString().Trim().Equals(header, StringComparison.OrdinalIgnoreCase))?.Address.ColumnNumber;
        return sheet is null || column is null ? string.Empty : sheet.Cell(row, column.Value).GetString();
    }
}
