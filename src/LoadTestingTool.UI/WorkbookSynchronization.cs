using ClosedXML.Excel;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI;

internal static class WorkbookSynchronization
{
    internal sealed record StepChange(string TestcaseIndex, string OldName, string? NewName);
    internal sealed record ColumnChange(string OldHeader, string? NewHeader);

    public static void SynchronizeRequestResponse(IXLWorkbook workbook)
    {
        var request = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase));
        var response = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("response", StringComparison.OrdinalIgnoreCase));
        if (request is null || response is null) return;

        var requestHeaders = Headers(request);
        var responseHeaders = Headers(response);
        if (!TryColumn(requestHeaders, "testcaseindex", out var requestTestcase) || !TryColumn(requestHeaders, "dataid", out var requestDataId)
            || !TryColumn(responseHeaders, "testcaseindex", out var responseTestcase) || !TryColumn(responseHeaders, "dataid", out var responseDataId)) return;

        var requestRows = new List<(string Testcase, string DataId)>();
        foreach (var row in request.RowsUsed().Skip(1))
        {
            var key = (row.Cell(requestTestcase).GetString().Trim(), row.Cell(requestDataId).GetString().Trim());
            requestRows.Add(key);
        }

        var width = Math.Max(responseHeaders.Values.DefaultIfEmpty(1).Max(), response.LastColumnUsed()?.ColumnNumber() ?? 1);
        var existing = new Dictionary<(string Testcase, string DataId), string[]>(new RowKeyComparer());
        var responseRows = response.RowsUsed().Skip(1).ToList();
        foreach (var row in responseRows)
        {
            var key = (row.Cell(responseTestcase).GetString().Trim(), row.Cell(responseDataId).GetString().Trim());
            if (!string.IsNullOrWhiteSpace(key.Item1) && !string.IsNullOrWhiteSpace(key.Item2) && !existing.ContainsKey(key))
                existing[key] = Enumerable.Range(1, width).Select(row.Cell).Select(x => x.GetString()).ToArray();
        }

        var oldLastRow = response.LastRowUsed()?.RowNumber() ?? 1;
        var oldLastColumn = response.LastColumnUsed()?.ColumnNumber() ?? width;
        if (oldLastRow > 1 || oldLastColumn > 0) response.Range(1, 1, Math.Max(1, oldLastRow), Math.Max(1, oldLastColumn)).Clear(XLClearOptions.Contents);
        foreach (var pair in responseHeaders) response.Cell(1, pair.Value).Value = pair.Key;

        var outputRow = 2;
        for (var requestIndex = 0; requestIndex < requestRows.Count; requestIndex++)
        {
            var key = requestRows[requestIndex];
            if (existing.TryGetValue(key, out var values) || (requestIndex < responseRows.Count && !string.IsNullOrWhiteSpace(responseRows[requestIndex].Cell(responseTestcase).GetString())))
            {
                values ??= Enumerable.Range(1, width).Select(responseRows[requestIndex].Cell).Select(x => x.GetString()).ToArray();
                for (var column = 1; column <= width; column++) response.Cell(outputRow, column).Value = values.ElementAtOrDefault(column - 1) ?? "";
            }
            response.Cell(outputRow, responseTestcase).Value = key.Testcase;
            response.Cell(outputRow, responseDataId).Value = key.DataId;
            outputRow++;
        }
        for (var row = 1; row < outputRow; row++) response.Row(row).Height = 22;
        response.Row(1).Style.Font.Bold = true;
        response.SheetView.FreezeRows(1);
    }

    public static IReadOnlyList<string> SynchronizeStepColumns(IXLWorkbook workbook, IEnumerable<StepChange> changes)
    {
        var warnings = new List<string>();
        var sheets = workbook.Worksheets.Where(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("response", StringComparison.OrdinalIgnoreCase)).ToList();
        // A deleted config row can produce more than one StepChange for the
        // same step (for example after a filtered/shifted row operation).
        // Resolve each old step once before mutating any worksheet. Deleting
        // columns while iterating the live header map can otherwise make the
        // next change address a shifted column and trigger duplicate-key
        // failures in ClosedXML or the UI model.
        var distinctChanges = changes
            .Where(x => !string.IsNullOrWhiteSpace(x.OldName))
            .GroupBy(x => x.OldName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Any(x => x.NewName is null)
                ? new StepChange(group.First().TestcaseIndex, group.Key, null)
                : group.Last())
            .ToList();

        foreach (var sheet in sheets)
        {
            var headers = Headers(sheet);
            var columnsToDelete = distinctChanges
                .Where(x => x.NewName is null)
                .SelectMany(change => headers.Where(x => IsStepHeader(x.Key, change.OldName)).Select(x => x.Value))
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();
            foreach (var column in columnsToDelete) sheet.Column(column).Delete();

            foreach (var change in distinctChanges.Where(x => x.NewName is not null))
            {
                headers = Headers(sheet);
                var matching = headers.Where(x => IsStepHeader(x.Key, change.OldName)).OrderByDescending(x => x.Value).ToList();
                foreach (var column in matching)
                {
                    var updated = RenameStepHeader(column.Key, change.OldName, change.NewName!);
                    sheet.Cell(1, column.Value).Value = updated;
                }
            }
        }

        // Reference replacement happens after all column mutations, so it is
        // performed against the final worksheet layout and only once per step.
        foreach (var change in distinctChanges)
            foreach (var sheet in sheets)
                ReplaceReferences(sheet, change.OldName, change.NewName);
        return warnings;
    }

    public static void SynchronizeColumnReferences(IXLWorkbook workbook, IEnumerable<ColumnChange> changes)
    {
        var sheets = workbook.Worksheets.Where(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("response", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var change in changes.Where(x => !string.IsNullOrWhiteSpace(x.OldHeader)))
        {
            var oldHeader = CleanHeader(change.OldHeader);
            if (string.IsNullOrWhiteSpace(oldHeader)) continue;
            var pattern = $"(<From_(?:(?:request|response)_)?){Regex.Escape(oldHeader)}(?=_(?:r|\\d+)>)";
            var replacement = change.NewHeader is null ? "$1[deleted-column]" : "$1" + CleanHeader(change.NewHeader);
            foreach (var sheet in sheets)
                foreach (var cell in sheet.CellsUsed().ToArray())
                {
                    var value = cell.GetString();
                    if (string.IsNullOrEmpty(value)) continue;
                    var updated = Regex.Replace(value, pattern, replacement, RegexOptions.IgnoreCase);
                    if (!string.Equals(updated, value, StringComparison.Ordinal)) cell.Value = updated;
                }
        }
    }

    private static Dictionary<string, int> Headers(IXLWorksheet sheet)
        => sheet.FirstRowUsed()?.CellsUsed()
            .Select(x => (Header: x.GetString().Trim(), Column: x.Address.ColumnNumber))
            .Where(x => !string.IsNullOrWhiteSpace(x.Header))
            .GroupBy(x => x.Header, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Column, StringComparer.OrdinalIgnoreCase)
            ?? new(StringComparer.OrdinalIgnoreCase);
    private static bool TryColumn(Dictionary<string, int> headers, string name, out int column) => headers.TryGetValue(name, out column);
    private static bool IsStepHeader(string header, string stepName)
    {
        var clean = Regex.Replace(header.Trim(), @"^\{[^{}]*\}", "").Trim();
        return clean.Equals(stepName, StringComparison.OrdinalIgnoreCase) || clean.StartsWith(stepName + ".", StringComparison.OrdinalIgnoreCase);
    }
    private static string RenameStepHeader(string header, string oldName, string newName)
    {
        var marker = Regex.Match(header, @"^\{[^{}]*\}");
        var prefix = marker.Success ? marker.Value : "";
        var clean = marker.Success ? header[marker.Length..] : header;
        return prefix + (clean.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : newName + clean[oldName.Length..]);
    }
    private static string CleanHeader(string header) => Regex.Replace(header.Trim(), @"^\{[^{}]*\}", "").Trim();
    private static void ReplaceReferences(IXLWorksheet sheet, string oldName, string? newName)
    {
        foreach (var cell in sheet.CellsUsed())
        {
            var value = cell.GetString();
            if (string.IsNullOrEmpty(value)) continue;
            // Supported runner forms include:
            // <From_Step.field_r>, <From_request_Step.field_r>, and
            // <From_response_Step.field_r>. Keep the source prefix intact.
            var pattern = $"(<From_(?:(?:request|response)_)?){Regex.Escape(oldName)}(?=[_.>])";
            var replacement = newName is null ? "$1[deleted-step]" : "$1" + newName;
            var updated = Regex.Replace(value, pattern, replacement, RegexOptions.IgnoreCase);
            if (!ReferenceEquals(updated, value) && updated != value) cell.Value = updated;
        }
    }

    private sealed class RowKeyComparer : IEqualityComparer<(string Testcase, string DataId)>
    {
        public bool Equals((string Testcase, string DataId) x, (string Testcase, string DataId) y) => string.Equals(x.Testcase, y.Testcase, StringComparison.OrdinalIgnoreCase) && string.Equals(x.DataId, y.DataId, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Testcase, string DataId) value) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Testcase), StringComparer.OrdinalIgnoreCase.GetHashCode(value.DataId));
    }
}
