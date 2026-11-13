using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClosedXML.Excel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoadTestingTool.UI;

internal sealed record HistoryComparisonOptions(IReadOnlyList<string> Sheets, bool OpenInExcel);

internal sealed class HistoryComparisonSettings
{
    public List<string> SelectedSheets { get; set; } = ["Comparison", "FailedOnly", "Differences", "Summary"];

    public static HistoryComparisonSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new HistoryComparisonSettings();
            var node = JsonNode.Parse(File.ReadAllText(path));
            var sheets = node?["HistoryComparison"]?["SelectedSheets"]?.AsArray()
                .Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
            return new HistoryComparisonSettings { SelectedSheets = sheets is { Count: > 0 } ? sheets : ["Comparison", "FailedOnly", "Differences", "Summary"] };
        }
        catch { return new HistoryComparisonSettings(); }
    }

    public void Save(string path)
    {
        try
        {
            var node = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : new JsonObject();
            node ??= new JsonObject();
            var selected = new JsonArray();
            foreach (var sheet in SelectedSheets) selected.Add(sheet);
            node["HistoryComparison"] = new JsonObject
            {
                ["SelectedSheets"] = selected,
                ["OpenInExcelByDefault"] = true
            };
            File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* comparison settings must never prevent history browsing */ }
    }
}

internal sealed class HistoryComparisonWindow : Window
{
    private readonly IReadOnlyList<HistoryFileItem> _files;
    private readonly IReadOnlyList<string> _availableSheets;
    private readonly IReadOnlyList<string> _defaults;
    private readonly List<CheckBox> _sheetChecks = [];
    private readonly TextBlock _error = DashboardStyle.Text("", 12, false, DashboardStyle.Red);

    public HistoryComparisonWindow(IReadOnlyList<HistoryFileItem> files, IReadOnlyList<string> availableSheets, IReadOnlyList<string> defaults)
    {
        _files = files; _availableSheets = availableSheets; _defaults = defaults;
        Title = "History comparison"; Width = 900; Height = 720; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildView();
    }

    private Control BuildView()
    {
        var sourcePanel = new StackPanel { Spacing = 4 };
        sourcePanel.Children.Add(DashboardStyle.Text("Selected history files", 15, true));
        foreach (var file in _files)
        {
            var source = new TextBox
            {
                Text = $"{file.Instance}  ·  {file.Environment}  ·  {Path.GetFileName(file.Path)}",
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(0), Foreground = DashboardStyle.Muted
            };
            sourcePanel.Children.Add(source);
        }

        var sheetPanel = new StackPanel { Spacing = 5 };
        sheetPanel.Children.Add(DashboardStyle.Text("History sheets to compare", 15, true));
        sheetPanel.Children.Add(DashboardStyle.Text("Choose the source sheets to compare. The result always contains Comparison, FailedOnly, Differences, and Summary. The selection is saved to the central appsettings.json.", 12, false, DashboardStyle.Muted));
        foreach (var sheet in _availableSheets)
        {
            var check = new CheckBox { Content = sheet, IsChecked = _defaults.Contains(sheet, StringComparer.OrdinalIgnoreCase), Margin = new Thickness(0, 2) };
            _sheetChecks.Add(check); sheetPanel.Children.Add(check);
        }

        var reset = DashboardStyle.Action("Reset to recommended sheets", () =>
        {
            foreach (var check in _sheetChecks) check.IsChecked = _defaults.Contains(check.Content?.ToString() ?? "", StringComparer.OrdinalIgnoreCase);
        });
        var cancel = DashboardStyle.Action("Cancel", () => Close(null));
        var editor = DashboardStyle.Action("Open in spreadsheet editor", () => Complete(false), "primary");
        var excel = DashboardStyle.Action("Open in Excel", () => Complete(true));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { reset, cancel, editor, excel } };
        var body = new StackPanel { Spacing = 14, Children = { DashboardStyle.Text("Compare selected history files", 22, true), sourcePanel, sheetPanel, _error, buttons } };
        return new Border { Padding = new Thickness(22), Child = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    }

    private void Complete(bool openInExcel)
    {
        var sheets = _sheetChecks.Where(x => x.IsChecked == true).Select(x => x.Content?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
        if (sheets.Count == 0) { _error.Text = "Select at least one sheet."; return; }
        Close(new HistoryComparisonOptions(sheets, openInExcel));
    }
}

internal static class HistoryComparisonWorkbook
{
    public static void Create(IReadOnlyList<HistoryFileItem> files, IReadOnlyList<string> selectedSheets, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var workbooks = files.Select(file => new XLWorkbook(file.Path)).ToList();
        try
        {
            using var output = new XLWorkbook();
            var rows = ReadRows(files, workbooks, selectedSheets).ToList();
            WriteComparison(output.Worksheets.Add("Comparison"), files, rows);
            WriteComparison(output.Worksheets.Add("FailedOnly"), files, rows.Where(x => x.FailureFlags.Any(x => x)).ToList());
            WriteComparison(output.Worksheets.Add("Differences"), files, rows.Where(x => x.Values.Where(x => x is not null).Distinct(StringComparer.Ordinal).Count() > 1).ToList());
            WriteSummary(output.Worksheets.Add("Summary"), files, rows);
            output.SaveAs(outputPath);
        }
        finally { foreach (var workbook in workbooks) workbook.Dispose(); }
    }

    private static IEnumerable<ComparisonCell> ReadRows(IReadOnlyList<HistoryFileItem> files, IReadOnlyList<XLWorkbook> books, IReadOnlyList<string> selectedSheets)
    {
        var sheets = selectedSheets.Where(x => !new[] { "Comparison", "FailedOnly", "Differences", "Summary" }.Contains(x, StringComparer.OrdinalIgnoreCase));
        foreach (var sheetName in sheets)
        {
            var views = books.Select(book => book.Worksheets.FirstOrDefault(x => x.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase))).ToList();
            var maxRows = views.Max(x => x?.LastRowUsed()?.RowNumber() ?? 0);
            var maxColumns = views.Max(x => x?.LastColumnUsed()?.ColumnNumber() ?? 0);
            for (var row = 1; row <= maxRows; row++)
                for (var column = 1; column <= maxColumns; column++)
                {
                    var values = views.Select(sheet => sheet?.Cell(row, column).GetFormattedString() ?? "").ToList();
                    var failures = views.Select(sheet => sheet is not null && IsFailureCell(sheet, sheetName, row, column)).ToList();
                    if (values.Any(x => !string.IsNullOrWhiteSpace(x))) yield return new ComparisonCell(sheetName, XLHelper.GetColumnLetterFromNumber(column) + row, values, failures);
                }
        }
    }

    private static void WriteComparison(IXLWorksheet sheet, IReadOnlyList<HistoryFileItem> files, IReadOnlyList<ComparisonCell> rows)
    {
        sheet.Cell(1, 1).Value = "Sheet"; sheet.Cell(1, 2).Value = "Cell";
        for (var i = 0; i < files.Count; i++) sheet.Cell(1, i + 3).Value = SourceLabel(files[i]);
        sheet.Cell(1, files.Count + 3).Value = "Different across selected files";
        for (var r = 0; r < rows.Count; r++)
        {
            var row = r + 2; var item = rows[r]; sheet.Cell(row, 1).Value = item.Sheet; sheet.Cell(row, 2).Value = item.Cell;
            for (var i = 0; i < files.Count; i++) sheet.Cell(row, i + 3).Value = item.Values[i];
            sheet.Cell(row, files.Count + 3).Value = item.Values.Where(x => x is not null).Distinct(StringComparer.Ordinal).Count() > 1 ? "YES" : "NO";
        }
        Style(sheet);
    }

    private static void WriteSummary(IXLWorksheet sheet, IReadOnlyList<HistoryFileItem> files, IReadOnlyList<ComparisonCell> rows)
    {
        sheet.Cell(1, 1).Value = "Instance"; sheet.Cell(1, 2).Value = "Environment"; sheet.Cell(1, 3).Value = "Filename"; sheet.Cell(1, 4).Value = "Compared cells"; sheet.Cell(1, 5).Value = "Different cells"; sheet.Cell(1, 6).Value = "Failure cells";
        for (var i = 0; i < files.Count; i++)
        {
            var row = i + 2; var values = rows.Select(x => x.Values[i]).ToList();
            sheet.Cell(row, 1).Value = files[i].Instance; sheet.Cell(row, 2).Value = files[i].Environment; sheet.Cell(row, 3).Value = Path.GetFileName(files[i].Path); sheet.Cell(row, 4).Value = values.Count; sheet.Cell(row, 5).Value = rows.Count(x => x.Values.Where(y => y is not null).Distinct(StringComparer.Ordinal).Count() > 1); sheet.Cell(row, 6).Value = rows.Count(x => x.FailureFlags.ElementAtOrDefault(i));
        }
        Style(sheet);
    }

    private static string SourceLabel(HistoryFileItem file) => $"{file.Instance} | {file.Environment} | {Path.GetFileName(file.Path)}";
    private static bool IsFailureStatus(string value) => value.Trim().Equals("FAIL", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("FAILED", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("ERROR", StringComparison.OrdinalIgnoreCase);
    private static bool IsFailureCell(IXLWorksheet sheet, string sheetName, int row, int column)
    {
        if (row == 1) return false;
        var header = sheet.Cell(1, column).GetString().Trim().ToLowerInvariant();
        var value = sheet.Cell(row, column).GetFormattedString().Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (sheetName.Equals("RuntimeReferenceErrors", StringComparison.OrdinalIgnoreCase)) return true;
        if (sheetName.Equals("Metrics", StringComparison.OrdinalIgnoreCase))
        {
            var metric = sheet.Cell(row, 1).GetString().Trim().ToLowerInvariant();
            return (metric is "failedrequests" or "assertionfailures" or "cancelledrequests") && decimal.TryParse(value, out var count) && count > 0;
        }
        if (header is "result" or "overallresult" or "requestresult" or "assertionresult") return IsFailureStatus(value);
        if (header is "failurecategory" or "errormessage" or "failuremessage") return !value.Equals("none", StringComparison.OrdinalIgnoreCase) && !value.Equals("[none]", StringComparison.OrdinalIgnoreCase);
        return false;
    }
    private static string SafeSheetName(string name) => (name.Length > 31 ? name[..31] : name).Replace("[", "_").Replace("]", "_").Replace(":", "_").Replace("*", "_").Replace("/", "_").Replace("\\", "_").Replace("?", "_");
    private static void Style(IXLWorksheet sheet) { sheet.Row(1).Style.Font.Bold = true; sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightBlue; sheet.SheetView.FreezeRows(1); sheet.Columns().AdjustToContents(); foreach (var column in sheet.Columns()) if (column.Width > 70) column.Width = 70; }
    private sealed record ComparisonCell(string Sheet, string Cell, IReadOnlyList<string> Values, IReadOnlyList<bool> FailureFlags);
}
