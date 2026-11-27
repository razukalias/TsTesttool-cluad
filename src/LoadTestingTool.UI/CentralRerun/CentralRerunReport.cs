using System.Net;
using System.Text;
using ClosedXML.Excel;

namespace LoadTestingTool.UI.CentralRerun;

/// <summary>The report of a finished batch: an HTML page and an Excel workbook with the same content.</summary>
public static class CentralRerunReport
{
    /// <summary>Writes both files into a new folder under <paramref name="reportsFolder"/>; returns (html, xlsx).</summary>
    public static (string Html, string Excel) Write(string reportsFolder, BatchResult batch)
    {
        var folder = Path.Combine(reportsFolder, $"Rerun_{batch.BatchId}");
        var n = 1;
        while (Directory.Exists(folder)) folder = Path.Combine(reportsFolder, $"Rerun_{batch.BatchId}_{++n}");
        Directory.CreateDirectory(folder);
        var html = Path.Combine(folder, "rerun-report.html");
        var excel = Path.Combine(folder, "rerun-report.xlsx");
        File.WriteAllText(html, Html(batch), new UTF8Encoding(false));
        WriteExcel(excel, batch);
        return (html, excel);
    }

    private static int Count(IEnumerable<BatchRow> rows, string after) => rows.Count(r => r.After == after);

    public static string Html(BatchResult batch)
    {
        var rows = batch.Rows;
        static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Re-run report ").Append(E(batch.BatchId)).Append("</title><style>")
          .Append("body{font:14px/1.45 Segoe UI,Arial,sans-serif;margin:24px;color:#1b2a3a;background:#fff}h1{font-size:22px}h2{font-size:16px;margin-top:28px}")
          .Append("table{border-collapse:collapse;width:100%;margin-top:8px}th,td{border:1px solid #d5dde8;padding:5px 8px;text-align:left;vertical-align:top}th{background:#eef3f9}")
          .Append(".tiles{display:flex;gap:12px;flex-wrap:wrap}.tile{border:1px solid #d5dde8;border-radius:6px;padding:8px 16px;min-width:110px}.tile b{display:block;font-size:22px}")
          .Append(".ok{color:#0a7d4f;font-weight:600}.bad{color:#c0273d;font-weight:600}.mute{color:#6b7c90}code{font-family:Consolas,monospace}")
          .Append("</style></head><body>");
        sb.Append("<h1>Re-run report</h1><p class=\"mute\">Batch ").Append(E(batch.BatchId)).Append(" · started ").Append(batch.Started.ToString("yyyy-MM-dd HH:mm:ss")).Append(" · finished ").Append(batch.Finished.ToString("yyyy-MM-dd HH:mm:ss")).Append(" · took ").Append(E((batch.Finished - batch.Started).ToString(@"hh\:mm\:ss"))).Append("</p>");
        sb.Append("<div class=\"tiles\">")
          .Append(Tile("Selected", rows.Count)).Append(Tile("Now passed", Count(rows, BatchRow.NowPassed), "ok")).Append(Tile("Still failed", Count(rows, BatchRow.StillFailed), "bad")).Append(Tile("Not run", Count(rows, BatchRow.NotRun))).Append("</div>");
        foreach (var (instance, problem) in batch.Problems) sb.Append("<p class=\"bad\">").Append(E(instance)).Append(": ").Append(E(problem)).Append("</p>");

        sb.Append("<h2>Per instance</h2><table><tr><th>Instance</th><th>Selected</th><th>Now passed</th><th>Still failed</th><th>Not run</th></tr>");
        foreach (var group in rows.GroupBy(r => r.Before.Instance, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            sb.Append("<tr><td>").Append(E(group.Key)).Append("</td><td>").Append(group.Count()).Append("</td><td>").Append(Count(group, BatchRow.NowPassed)).Append("</td><td>").Append(Count(group, BatchRow.StillFailed)).Append("</td><td>").Append(Count(group, BatchRow.NotRun)).Append("</td></tr>");
        sb.Append("</table>");

        if (batch.StepsByTestcase.Count > 0)
        {
            sb.Append("<h2>Steps that were run</h2><table><tr><th>Instance › testcase</th><th>Steps</th></tr>");
            foreach (var (name, steps) in batch.StepsByTestcase.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)) sb.Append("<tr><td>").Append(E(name)).Append("</td><td>").Append(E(string.Join(", ", steps))).Append("</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<h2>Before and after</h2><table><tr><th>Instance</th><th>Testcase</th><th>Row</th><th>Environment</th><th>Before</th><th>After</th><th>Steps run</th><th>Slowest step</th></tr>");
        foreach (var r in rows.OrderBy(r => r.Before.Instance, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Before.TestcaseIndex).ThenBy(r => r.Before.DataId, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Before.Environment, StringComparer.OrdinalIgnoreCase))
            sb.Append("<tr><td>").Append(E(r.Before.Instance)).Append("</td><td>").Append(E(r.Before.Testcase)).Append("</td><td>").Append(E(r.Before.DataId)).Append("</td><td>").Append(E(r.Before.EnvironmentText)).Append("</td><td>").Append(E(r.Before.Kind)).Append("</td><td class=\"")
              .Append(r.After == BatchRow.NowPassed ? "ok" : r.After == BatchRow.StillFailed ? "bad" : "mute").Append("\">").Append(E(r.After)).Append("</td><td>").Append(E(r.StepsText)).Append("</td><td>").Append(r.DurationMs > 0 ? r.DurationMs + " ms" : "").Append("</td></tr>");
        sb.Append("</table>");

        var still = rows.Where(r => r.IsStillFailed).ToList();
        sb.Append("<h2>Still failing</h2>");
        if (still.Count == 0) sb.Append("<p class=\"ok\">Nothing is failing any more.</p>");
        else
        {
            sb.Append("<table><tr><th>Instance</th><th>Testcase</th><th>Row</th><th>Environment</th><th>Step</th><th>Cell</th><th>Verb</th><th>Expected</th><th>Actual</th><th>Message</th></tr>");
            foreach (var r in still)
                foreach (var f in r.AfterFailures.DefaultIfEmpty())
                    sb.Append("<tr><td>").Append(E(r.Before.Instance)).Append("</td><td>").Append(E(r.Before.Testcase)).Append("</td><td>").Append(E(r.Before.DataId)).Append("</td><td>").Append(E(r.Before.EnvironmentText)).Append("</td><td>").Append(E(f?.StepName)).Append("</td><td><code>").Append(E(f?.ResponsePath)).Append("</code></td><td>").Append(E(f?.Verb)).Append("</td><td>").Append(E(f?.Expected)).Append("</td><td>").Append(E(f?.Actual)).Append("</td><td>").Append(E(f is null ? "failed" : f.Message.Length > 0 ? f.Message : f.Category)).Append("</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Tile(string title, int value, string css = "") => $"<div class=\"tile\"><span class=\"mute\">{WebUtility.HtmlEncode(title)}</span><b class=\"{css}\">{value}</b></div>";

    private static void WriteExcel(string path, BatchResult batch)
    {
        using var book = new XLWorkbook();
        var summary = book.AddWorksheet("Summary");
        string[][] top =
        [
            ["Batch", batch.BatchId], ["Started", batch.Started.ToString("yyyy-MM-dd HH:mm:ss")], ["Finished", batch.Finished.ToString("yyyy-MM-dd HH:mm:ss")],
            ["Selected", batch.Rows.Count.ToString()], ["Now passed", Count(batch.Rows, BatchRow.NowPassed).ToString()], ["Still failed", Count(batch.Rows, BatchRow.StillFailed).ToString()], ["Not run", Count(batch.Rows, BatchRow.NotRun).ToString()]
        ];
        var line = 1;
        foreach (var pair in top) { summary.Cell(line, 1).Value = pair[0]; summary.Cell(line, 2).Value = pair[1]; summary.Cell(line, 1).Style.Font.Bold = true; line++; }
        line++;
        foreach (var header in new[] { "Instance", "Selected", "Now passed", "Still failed", "Not run" }.Select((h, i) => (h, i))) { summary.Cell(line, header.i + 1).Value = header.h; summary.Cell(line, header.i + 1).Style.Font.Bold = true; }
        foreach (var group in batch.Rows.GroupBy(r => r.Before.Instance, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            line++;
            summary.Cell(line, 1).Value = group.Key; summary.Cell(line, 2).Value = group.Count(); summary.Cell(line, 3).Value = Count(group, BatchRow.NowPassed); summary.Cell(line, 4).Value = Count(group, BatchRow.StillFailed); summary.Cell(line, 5).Value = Count(group, BatchRow.NotRun);
        }
        line += 2;
        foreach (var (name, steps) in batch.StepsByTestcase.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)) { summary.Cell(line, 1).Value = "Steps: " + name; summary.Cell(line, 2).Value = string.Join(", ", steps); line++; }
        foreach (var (instance, problem) in batch.Problems) { summary.Cell(line, 1).Value = instance; summary.Cell(line, 2).Value = problem; line++; }
        summary.Columns().AdjustToContents();

        var rows = book.AddWorksheet("Rows");
        string[] rowHeaders = ["Instance", "Testcase", "Row", "Environment", "Before", "Reason before", "After", "Steps run", "Slowest step (ms)"];
        for (var i = 0; i < rowHeaders.Length; i++) { rows.Cell(1, i + 1).Value = rowHeaders[i]; rows.Cell(1, i + 1).Style.Font.Bold = true; }
        var r1 = 2;
        foreach (var r in batch.Rows.OrderBy(r => r.Before.Instance, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Before.TestcaseIndex).ThenBy(r => r.Before.DataId, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Before.Environment, StringComparer.OrdinalIgnoreCase))
        {
            rows.Cell(r1, 1).Value = r.Before.Instance; rows.Cell(r1, 2).Value = r.Before.Testcase; rows.Cell(r1, 3).Value = r.Before.DataId; rows.Cell(r1, 4).Value = r.Before.EnvironmentText;
            rows.Cell(r1, 5).Value = r.Before.Kind; rows.Cell(r1, 6).Value = r.Before.Reason; rows.Cell(r1, 7).Value = r.After; rows.Cell(r1, 8).Value = r.StepsText; rows.Cell(r1, 9).Value = r.DurationMs;
            rows.Cell(r1, 7).Style.Font.FontColor = r.After == BatchRow.NowPassed ? XLColor.DarkGreen : r.After == BatchRow.StillFailed ? XLColor.Red : XLColor.Gray;
            r1++;
        }
        rows.Columns().AdjustToContents(); rows.SheetView.FreezeRows(1);

        var cells = book.AddWorksheet("StillFailing");
        string[] cellHeaders = ["Instance", "Testcase", "Row", "Environment", "Step", "Cell", "Verb", "Expected", "Actual", "Message"];
        for (var i = 0; i < cellHeaders.Length; i++) { cells.Cell(1, i + 1).Value = cellHeaders[i]; cells.Cell(1, i + 1).Style.Font.Bold = true; }
        var c1 = 2;
        foreach (var r in batch.Rows.Where(r => r.IsStillFailed))
            foreach (var f in r.AfterFailures.DefaultIfEmpty())
            {
                cells.Cell(c1, 1).Value = r.Before.Instance; cells.Cell(c1, 2).Value = r.Before.Testcase; cells.Cell(c1, 3).Value = r.Before.DataId; cells.Cell(c1, 4).Value = r.Before.EnvironmentText;
                cells.Cell(c1, 5).Value = f?.StepName ?? string.Empty; cells.Cell(c1, 6).Value = f?.ResponsePath ?? string.Empty; cells.Cell(c1, 7).Value = f?.Verb ?? string.Empty;
                cells.Cell(c1, 8).Value = f?.Expected ?? string.Empty; cells.Cell(c1, 9).Value = f?.Actual ?? string.Empty; cells.Cell(c1, 10).Value = f is null ? "failed" : f.Message.Length > 0 ? f.Message : f.Category;
                c1++;
            }
        cells.Columns().AdjustToContents(); cells.SheetView.FreezeRows(1);
        book.SaveAs(path);
    }
}
