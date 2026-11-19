using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI;

// The dashboard shows a run as it was recorded. A cell tagged <bug: …> after the run (in the editor, then saved) should already count as a known bug in
// the Overview, the Assertions list, the Bugs tab and the step status, without a re-run. The tags are read from the workbook as it is now.
public sealed partial class MainWindow
{
    private sealed record BugTagMap(string Stamp, Dictionary<string, BugTag> Tags);

    private readonly Dictionary<string, BugTagMap> _bugTagMaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<InstanceInfo, string> _bugOverlayStamps = [];

    /// <summary>Re-reads the workbook's bug tags and updates the assertions and steps of the instance's run; does nothing when neither the workbook nor the run changed.</summary>
    private void ApplyBugTags(InstanceInfo item)
    {
        if (item.Assertions.Count == 0 || string.IsNullOrWhiteSpace(item.WorkbookPath) || !File.Exists(item.WorkbookPath)) return;
        var map = TagsOf(item.WorkbookPath);
        var stamp = $"{map.Stamp}|{item.Assertions.Count}|{item.Requests.Count}";
        if (_bugOverlayStamps.TryGetValue(item, out var done) && done == stamp) return;
        _bugOverlayStamps[item] = stamp;

        foreach (var a in item.Assertions)
        {
            if (a.RunResult.Length == 0) { a.RunResult = a.Result; a.RunKnownBug = a.KnownBug; }
            if (a.RunResult == "PASS") continue;                       // a pass stays a pass (its tag, if any, only says the bug may be fixed)
            var tag = TagFor(map, a);
            if (tag is not null) { a.Result = "KNOWN_BUG"; a.KnownBug = true; a.BugDescription = tag.Description; a.BugEnvironments = string.Join(",", tag.Environments); }
            else if (a.RunResult == "KNOWN_BUG") { a.Result = "FAIL"; a.KnownBug = false; }   // the tag was taken out since the run
            else { a.Result = a.RunResult; a.KnownBug = a.RunKnownBug; }
        }

        // A step that only failed because of assertions that are now known bugs is not a failed step
        var assertionsByStep = item.Assertions.ToLookup(a => StepKey(a.Testcase, a.DataId, a.StepName, a.Environment), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < item.Requests.Count; i++)
        {
            var request = item.Requests[i];
            var original = request.RunResult.Length > 0 ? request.RunResult : request.Result;
            var result = original;
            if (original.Equals("FAIL", StringComparison.OrdinalIgnoreCase) && request.Error.Length == 0 && (request.FailureCategory.Length == 0 || request.FailureCategory == FailureCategories.Assertion))
            {
                var failedBefore = assertionsByStep[StepKey(request.Testcase, request.DataId, request.StepName, request.Environment)].Where(a => a.RunResult == "FAIL").ToList();
                if (failedBefore.Count > 0 && failedBefore.All(a => a.Result == "KNOWN_BUG")) result = "PASS";
            }
            if (result != request.Result || request.RunResult.Length == 0) item.Requests[i] = request with { Result = result, RunResult = original };
        }
    }

    private static string StepKey(string testcase, string dataId, string step, string environment)
        => $"{testcase}|{dataId}|{step}|{(string.IsNullOrWhiteSpace(environment) ? "default" : environment)}";

    private static BugTag? TagFor(BugTagMap map, AssertionInfo a)
    {
        var header = $"{a.StepName}.{a.ResponsePath}".ToLowerInvariant();
        var key = $"{a.DataId.Trim().ToUpperInvariant()}|{header}";
        if (!map.Tags.TryGetValue($"{a.TestcaseIndex}|{key}", out var tag) && !map.Tags.TryGetValue($"0|{key}", out tag)) return null;
        return tag.AppliesTo(a.Environment) ? tag : null;
    }

    /// <summary>Every expected-value cell of the workbook's response sheet that carries a bug tag, by testcase index, data ID and "step.path" (cached until the file changes).</summary>
    private BugTagMap TagsOf(string workbookPath)
    {
        var info = new FileInfo(workbookPath);
        var stamp = $"{info.LastWriteTimeUtc.Ticks}:{info.Length}";
        if (_bugTagMaps.TryGetValue(workbookPath, out var cached) && cached.Stamp == stamp) return cached;
        var tags = new Dictionary<string, BugTag>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(stream);
            if (workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("response", StringComparison.OrdinalIgnoreCase)) is { } sheet && sheet.LastRowUsed() is { } last)
            {
                var columns = sheet.Row(1).CellsUsed().ToDictionary(c => c.Address.ColumnNumber, c => c.GetString().Trim());
                var idColumn = columns.FirstOrDefault(c => c.Value.Equals("dataid", StringComparison.OrdinalIgnoreCase)).Key;
                var testcaseColumn = columns.FirstOrDefault(c => c.Value.Equals("testcaseindex", StringComparison.OrdinalIgnoreCase)).Key;
                if (idColumn > 0)
                    for (var row = 2; row <= last.RowNumber(); row++)
                    {
                        var dataId = sheet.Cell(row, idColumn).GetString().Trim().ToUpperInvariant();
                        if (dataId.Length == 0) continue;
                        var testcase = testcaseColumn > 0 && int.TryParse(sheet.Cell(row, testcaseColumn).GetString(), out var index) ? index : 0;
                        foreach (var (column, header) in columns)
                        {
                            if (column == idColumn || column == testcaseColumn) continue;
                            var text = sheet.Cell(row, column).GetString();
                            if (text.IndexOf("<bug", StringComparison.OrdinalIgnoreCase) < 0 || BugTag.Parse(text).Tag is not { } tag) continue;
                            var name = header.StartsWith('{') && header.IndexOf('}') is var close and > 0 ? header[(close + 1)..] : header;
                            tags[$"{testcase}|{dataId}|{name.Trim().ToLowerInvariant()}"] = tag;
                        }
                    }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { }   // being saved right now: try again at the next refresh
        return _bugTagMaps[workbookPath] = new BugTagMap(stamp, tags);
    }
}
