using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>What the window should ask a file name for: the table's format (csv or xlsx) and whether only the selected cells go.</summary>
public sealed record ExportRequest(string Format, bool SelectionOnly);

// Export the shown table (all rows and columns, or the selected cells) as CSV or Excel. The columns use the same headers the import understands,
// so what is exported can be imported again.
public sealed partial class DataEditorViewModel
{
    public event EventHandler<ExportRequest>? ExportRequested;

    public void RequestExport(string format, bool selectionOnly) => ExportRequested?.Invoke(this, new ExportRequest(format, selectionOnly));

    [RelayCommand] private void ExportCsvFile() => RequestExport("csv", false);
    [RelayCommand] private void ExportExcelFile() => RequestExport("xlsx", false);

    /// <summary>
    /// The table as text rows, header first. A testcase exports every row (or the rows of the selected cells) with the Data ID first and the columns as shown (or the
    /// selected cells' columns); another sheet exports all its rows and headers.
    /// </summary>
    public List<string[]> ExportTable(bool selectionOnly = false)
    {
        if (IsOtherSheet)
        {
            var headers = SheetHeaders.ToArray();
            var lines = new List<string[]> { headers };
            lines.AddRange(SheetRows.Select(r => r.Cells.Take(headers.Length).ToArray()));
            return lines;
        }
        var cells = Enumerable.Range(1, Columns.Count).ToList();
        IEnumerable<DataRowViewModel> rows = _allRows;
        if (selectionOnly && _cellSelection is { Count: > 0 } selection)
        {
            var chosen = selection.Cells;
            var wanted = chosen.Select(c => c.Cell).ToHashSet();
            cells = (selection.CellOrder?.Invoke() ?? cells).Where(wanted.Contains).ToList();
            var picked = chosen.Select(c => c.Row).ToHashSet();
            rows = VisibleRows.Where(picked.Contains);
        }
        var header = new[] { "Data ID" }.Concat(cells.Select(c => CsvHeader(Columns[c - 1]))).ToArray();
        var table = new List<string[]> { header };
        table.AddRange(rows.Select(r => new[] { r.Cells[0] }.Concat(cells.Select(c => r.Cells[c] ?? string.Empty)).ToArray()));
        return table;
    }

    public string ExportBaseName => IsOtherSheet ? $"{InstanceName}-{ActiveSheet}" : $"{InstanceName}-{SelectedTestcase?.Name ?? "testcase"}";

    public string BuildExportCsv(bool selectionOnly = false) => ClipboardTable.Format(ExportTable(selectionOnly).Select(r => (IEnumerable<string>)r), ',');

    /// <summary>Writes the table as an Excel file with one sheet. Plain numbers are written as numbers, everything else (leading zeros, long digit strings, text) as text.</summary>
    public static void WriteXlsx(string path, string sheetName, IReadOnlyList<string[]> table)
    {
        using var workbook = new XLWorkbook();
        var name = Regex.Replace(string.IsNullOrWhiteSpace(sheetName) ? "Export" : sheetName, @"[\[\]\*\?/\\:]", "_");
        var sheet = workbook.AddWorksheet(name.Length > 31 ? name[..31] : name);
        for (var r = 0; r < table.Count; r++)
            for (var c = 0; c < table[r].Length; c++)
            {
                var text = table[r][c];
                if (text.Length == 0) continue;
                if (r > 0 && PlainNumber.IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) sheet.Cell(r + 1, c + 1).Value = number;
                else sheet.Cell(r + 1, c + 1).Value = text;
            }
        if (table.Count > 0) sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(table.Count, 200));
        workbook.SaveAs(path);
    }

    private static readonly Regex PlainNumber = new(@"^-?(0|[1-9]\d{0,14})(\.\d{1,10})?$", RegexOptions.Compiled);
}
