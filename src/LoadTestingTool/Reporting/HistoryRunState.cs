using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;

namespace LoadTestingTool.Reporting;

/// <summary>
/// Keeps the re-run memory in the history workbooks: each history file carries a hidden <c>RunState</c> sheet with the generated values,
/// extracted response values and resolved step text of the data rows it covers. A re-run reads, for every data row, the newest history
/// file that has it, so history is the only source for "old data".
/// </summary>
public sealed class HistoryRunStateStore : IRunStateStore
{
    public const string SheetName = "RunState";
    private const int ChunkLength = 30000;       // an Excel cell holds at most 32767 characters
    private const int MaxFilesScanned = 400;

    private readonly string _historyFolder;
    private readonly string? _runId;

    /// <param name="historyFolder">Where the history files are.</param>
    /// <param name="runId">Read only what this run left (to re-run an older run with its own data); null takes the newest data of every row.</param>
    public HistoryRunStateStore(string historyFolder, string? runId = null) { _historyFolder = historyFolder; _runId = string.IsNullOrWhiteSpace(runId) ? null : runId; }

    /// <summary>Number of data rows found by the last <see cref="Load"/>.</summary>
    public int RowsFound { get; private set; }

    public RunValueMemory.Snapshot? Load()
    {
        var snapshot = new RunValueMemory.Snapshot();
        var seenRows = new HashSet<string>(StringComparer.Ordinal);
        if (!Directory.Exists(_historyFolder)) return snapshot;
        var files = new DirectoryInfo(_historyFolder).EnumerateFiles("*.xlsx", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith("~$", StringComparison.Ordinal)).OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxFilesScanned);
        foreach (var file in files)
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var book = new XLWorkbook(stream);
                if (!book.TryGetWorksheet(SheetName, out var sheet)) continue;
                var chunks = new Dictionary<(string Row, string Kind, string Key, string Name), SortedDictionary<int, string>>();
                foreach (var row in sheet.RowsUsed().Skip(1))
                {
                    var rowKey = row.Cell(1).GetString();
                    if (_runId is not null && !string.Equals(row.Cell(7).GetString(), _runId, StringComparison.Ordinal)) continue;
                    if (seenRows.Contains(rowKey)) continue;          // a newer file already supplied this data row
                    var id = (rowKey, row.Cell(2).GetString(), row.Cell(3).GetString(), row.Cell(4).GetString());
                    if (!chunks.TryGetValue(id, out var parts)) chunks[id] = parts = [];
                    parts[int.TryParse(row.Cell(5).GetString(), out var part) ? part : 0] = row.Cell(6).GetString();
                }
                foreach (var entry in chunks)
                {
                    var value = string.Concat(entry.Value.Values);
                    var (rowKey, kind, key, name) = entry.Key;
                    switch (kind)
                    {
                        case "generated": snapshot.Generated[key] = value; break;
                        case "step": snapshot.Steps[key] = value; break;
                        case "row":
                            if (!snapshot.Rows.TryGetValue(key, out var values)) snapshot.Rows[key] = values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            values[name] = value; break;
                    }
                }
                foreach (var rowKey in chunks.Keys.Select(k => k.Row)) seenRows.Add(rowKey);
            }
            catch (Exception) { /* a history file that cannot be read (open in Excel, damaged, not a workbook) is skipped */ }
        }
        RowsFound = seenRows.Count;
        return snapshot;
    }

    /// <summary>History files carry their own state (see <see cref="WriteSheet"/>), so there is nothing to save separately.</summary>
    public void Save(RunValueMemory.Snapshot snapshot) { }

    /// <summary>
    /// Adds the hidden RunState sheet to a history workbook: what the run remembered for the data rows the workbook covers, how each of those rows
    /// ended, and which run this is (and which run it re-ran). <see cref="RunHistoryIndex"/> reads the last two without the user's sheet choices mattering.
    /// </summary>
    public static void WriteSheet(XLWorkbook workbook, RunValueMemory.Snapshot? state, RunResult run, string? rerunOf = null)
    {
        if (run.Testcases.Count == 0) return;
        var sheet = workbook.AddWorksheet(SheetName);
        string[] headers = ["rowkey", "kind", "key", "name", "part", "value", "runid"];
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        var next = 2;
        void Put(string rowKey, string kind, string key, string name, string value)
        {
            for (var part = 0; part == 0 || part * ChunkLength < value.Length; part++)
            {
                sheet.Cell(next, 1).Value = rowKey; sheet.Cell(next, 2).Value = kind; sheet.Cell(next, 3).Value = key; sheet.Cell(next, 4).Value = name;
                sheet.Cell(next, 5).Value = part;
                sheet.Cell(next, 6).Value = value.Substring(part * ChunkLength, Math.Min(ChunkLength, value.Length - part * ChunkLength));
                sheet.Cell(next, 7).Value = run.RunId;
                next++;
            }
        }
        var rowKeys = run.Testcases.Select(t => RunValueMemory.RowKey(t.TestcaseIndex, t.DataId, t.Iteration, t.Environment)).Distinct().ToList();
        Put(rowKeys[0], "run", run.RunId, "startedat", run.StartedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(rerunOf)) Put(rowKeys[0], "run", run.RunId, "rerunof", rerunOf);
        foreach (var t in run.Testcases)
            Put(RunValueMemory.RowKey(t.TestcaseIndex, t.DataId, t.Iteration, t.Environment), "result", t.Testcase, t.StartedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture), t.Result);
        foreach (var rowKey in rowKeys)
        {
            if (state is null) { Put(rowKey, "marker", rowKey, "", ""); continue; }
            var prefix = rowKey + "|";
            foreach (var pair in state.Generated.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal))) Put(rowKey, "generated", pair.Key, "", pair.Value);
            foreach (var pair in state.Steps.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal))) Put(rowKey, "step", pair.Key, "", pair.Value);
            if (state.Rows.TryGetValue(rowKey, out var values)) foreach (var pair in values) Put(rowKey, "row", rowKey, pair.Key, pair.Value);
            if (!state.Rows.ContainsKey(rowKey) && !state.Generated.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)) && !state.Steps.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)))
                Put(rowKey, "marker", rowKey, "", "");   // the row ran but had nothing to remember: it still hides older files' values
        }
        sheet.Visibility = XLWorksheetVisibility.Hidden;
    }
}

/// <summary>One data row of one run, as found in the history.</summary>
public sealed record RunHistoryRow(string RunId, DateTimeOffset StartedAt, string? RerunOf, int TestcaseIndex, string Testcase, string DataId, int Iteration, string Environment, string Result)
{
    public bool Failed => Result.Equals("FAILED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Lists the runs and data rows found in an instance's history, for choosing what to re-run.</summary>
public static class RunHistoryIndex
{
    public static IReadOnlyList<RunHistoryRow> Scan(string historyFolder, int maxFiles = 400) => ScanPage(historyFolder, 0, maxFiles).Rows;

    /// <summary>
    /// One page of the history: the <paramref name="take"/> newest history workbooks after the first <paramref name="skip"/> (newest first), so a dialog can show the
    /// latest runs at once and read older ones only when asked. <c>Files</c> is how many workbooks the page held (fewer than <paramref name="take"/> means there are no older ones).
    /// </summary>
    public static (IReadOnlyList<RunHistoryRow> Rows, int Files) ScanPage(string historyFolder, int skip, int take)
    {
        var rows = new List<RunHistoryRow>();
        if (!Directory.Exists(historyFolder)) return (rows, 0);
        var seen = new HashSet<(string, string)>();
        var page = new DirectoryInfo(historyFolder).EnumerateFiles("*.xlsx", SearchOption.AllDirectories)
                     .Where(f => !f.Name.StartsWith("~$", StringComparison.Ordinal)).OrderByDescending(f => f.LastWriteTimeUtc).Skip(skip).Take(take).ToList();
        foreach (var file in page)
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var book = new XLWorkbook(stream);
                if (!book.TryGetWorksheet(HistoryRunStateStore.SheetName, out var sheet)) continue;
                string? runId = null, rerunOf = null; DateTimeOffset started = default;
                var results = new List<(string RowKey, string Testcase, string Result)>();
                foreach (var row in sheet.RowsUsed().Skip(1))
                {
                    var kind = row.Cell(2).GetString();
                    if (kind == "run")
                    {
                        runId = row.Cell(3).GetString();
                        if (row.Cell(4).GetString() == "startedat") DateTimeOffset.TryParse(row.Cell(6).GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out started);
                        else if (row.Cell(4).GetString() == "rerunof") rerunOf = row.Cell(6).GetString();
                    }
                    else if (kind == "result") results.Add((row.Cell(1).GetString(), row.Cell(3).GetString(), row.Cell(6).GetString()));
                }
                if (string.IsNullOrEmpty(runId)) continue;
                foreach (var (rowKey, testcase, result) in results)
                {
                    var parts = rowKey.Split('|');
                    if (parts.Length < 4 || !int.TryParse(parts[0], out var index) || !int.TryParse(parts[2], out var iteration) || !seen.Add((runId, rowKey))) continue;
                    rows.Add(new RunHistoryRow(runId, started, string.IsNullOrEmpty(rerunOf) ? null : rerunOf, index, testcase, parts[1], iteration, parts[3] == "default" ? string.Empty : parts[3], result));
                }
            }
            catch (Exception) { /* unreadable history files are skipped */ }
        }
        return (rows.OrderByDescending(r => r.StartedAt).ThenBy(r => r.TestcaseIndex).ThenBy(r => r.DataId).ThenBy(r => r.Iteration).ToList(), page.Count);
    }

    /// <summary>The runs found, newest first.</summary>
    public static IReadOnlyList<(string RunId, DateTimeOffset StartedAt, int Rows, int Failed, string? RerunOf)> Runs(IEnumerable<RunHistoryRow> rows)
        => rows.GroupBy(r => r.RunId).Select(g => (g.Key, g.Min(r => r.StartedAt), g.Count(), g.Count(r => r.Failed), g.Select(r => r.RerunOf).FirstOrDefault(x => x is not null)))
            .OrderByDescending(r => r.Item2).ToList();
}
