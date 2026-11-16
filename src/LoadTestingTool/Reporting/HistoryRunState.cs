using ClosedXML.Excel;
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
    public HistoryRunStateStore(string historyFolder) => _historyFolder = historyFolder;

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

    /// <summary>Adds the hidden RunState sheet to a history workbook, for the data rows (<paramref name="rowKeys"/>) that workbook covers.</summary>
    public static void WriteSheet(XLWorkbook workbook, RunValueMemory.Snapshot? state, IReadOnlyCollection<string> rowKeys)
    {
        if (state is null || rowKeys.Count == 0) return;
        var sheet = workbook.AddWorksheet(SheetName);
        string[] headers = ["rowkey", "kind", "key", "name", "part", "value"];
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        var next = 2;
        void Put(string rowKey, string kind, string key, string name, string value)
        {
            for (var part = 0; part == 0 || part * ChunkLength < value.Length; part++)
            {
                sheet.Cell(next, 1).Value = rowKey; sheet.Cell(next, 2).Value = kind; sheet.Cell(next, 3).Value = key; sheet.Cell(next, 4).Value = name;
                sheet.Cell(next, 5).Value = part;
                sheet.Cell(next, 6).Value = value.Substring(part * ChunkLength, Math.Min(ChunkLength, value.Length - part * ChunkLength));
                next++;
            }
        }
        foreach (var rowKey in rowKeys)
        {
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
