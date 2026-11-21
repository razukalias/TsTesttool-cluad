using ClosedXML.Excel;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI;

/// <summary>One row of the Bugs tab's statistics: an instance, one of its testcases, or one of their steps, with its bug-tag count.</summary>
internal sealed record BugStatNode(string Kind, string Name, string Instance, string? Testcase, string? Step, int Count, IReadOnlyList<BugStatNode> Children);

/// <summary>What one workbook holds: its testcases with their steps, and every bug tag with where it sits.</summary>
internal sealed record WorkbookBugs(IReadOnlyList<(int Index, string Name, IReadOnlyList<string> Steps)> Testcases, IReadOnlyList<(int TestcaseIndex, string Step, string Path, string DataId)> Tags);

/// <summary>Bug tags (&lt;bug: …&gt; in expected-value cells) counted per instance, testcase and step, read from the workbooks as they are now: no run needed.</summary>
internal static class BugStatistics
{
    /// <summary>Reads the workbook's testcases, steps and bug tags (empty when the file is missing or being saved).</summary>
    public static WorkbookBugs Scan(string workbookPath)
    {
        var testcases = new List<(int, string, IReadOnlyList<string>)>();
        var tags = new List<(int, string, string, string)>();
        try
        {
            if (!File.Exists(workbookPath)) return new WorkbookBugs(testcases, tags);
            using var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(stream);
            IXLWorksheet? Sheet(string name) => workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            static Dictionary<int, string> Headers(IXLWorksheet sheet) => sheet.Row(1).CellsUsed().ToDictionary(c => c.Address.ColumnNumber, c => c.GetString().Trim());
            static int Find(Dictionary<int, string> headers, string name) => headers.FirstOrDefault(h => h.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;

            if (Sheet("config") is { } config && config.LastRowUsed() is { } configLast)
            {
                var headers = Headers(config);
                int index = Find(headers, "testcaseindex"), name = Find(headers, "testcase"), step = Find(headers, "stepname");
                if (index > 0 && step > 0)
                {
                    var found = new SortedDictionary<int, (string Name, List<string> Steps)>();
                    for (var row = 2; row <= configLast.RowNumber(); row++)
                    {
                        if (!int.TryParse(config.Cell(row, index).GetString(), out var number)) continue;
                        var stepName = config.Cell(row, step).GetString().Trim();
                        if (stepName.Length == 0) continue;
                        if (!found.TryGetValue(number, out var entry)) found[number] = entry = (name > 0 ? config.Cell(row, name).GetString().Trim() : number.ToString(), []);
                        entry.Steps.Add(stepName);
                    }
                    foreach (var (number, entry) in found) testcases.Add((number, entry.Name, entry.Steps));
                }
            }

            if (Sheet("response") is { } response && response.LastRowUsed() is { } last)
            {
                var headers = Headers(response);
                int idColumn = Find(headers, "dataid"), testcaseColumn = Find(headers, "testcaseindex");
                if (idColumn > 0)
                    for (var row = 2; row <= last.RowNumber(); row++)
                    {
                        var dataId = response.Cell(row, idColumn).GetString().Trim();
                        if (dataId.Length == 0) continue;
                        var testcase = testcaseColumn > 0 && int.TryParse(response.Cell(row, testcaseColumn).GetString(), out var number) ? number : 0;
                        foreach (var (column, header) in headers)
                        {
                            if (column == idColumn || column == testcaseColumn) continue;
                            var text = response.Cell(row, column).GetString();
                            if (text.IndexOf("<bug", StringComparison.OrdinalIgnoreCase) < 0 || BugTag.Parse(text).Tag is null) continue;
                            var path = (header.StartsWith('{') && header.IndexOf('}') is var close and > 0 ? header[(close + 1)..] : header).Trim();
                            var dot = path.IndexOf('.');
                            tags.Add((testcase, dot > 0 ? path[..dot] : path, dot > 0 ? path[(dot + 1)..] : string.Empty, dataId));
                        }
                    }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }
        return new WorkbookBugs(testcases, tags);
    }

    /// <summary>One instance with its testcases and steps (steps without a tag stay listed with 0). A tag whose testcase number is 0 (a workbook without a testcase column) counts for every testcase that has the step.</summary>
    public static BugStatNode Build(string instance, WorkbookBugs bugs)
    {
        var testcases = bugs.Testcases.Select(tc =>
        {
            var steps = tc.Steps.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(s => new BugStatNode("Step", s, instance, tc.Name, s, bugs.Tags.Count(t => (t.TestcaseIndex == tc.Index || t.TestcaseIndex == 0) && t.Step.Equals(s, StringComparison.OrdinalIgnoreCase)), []))
                .ToList();
            return new BugStatNode("Testcase", tc.Name, instance, tc.Name, null, steps.Sum(s => s.Count), steps);
        }).ToList();
        return new BugStatNode("Instance", instance, instance, null, null, testcases.Sum(t => t.Count), testcases);
    }
}
