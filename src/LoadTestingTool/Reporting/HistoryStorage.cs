using System.Text.Json.Serialization;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;

namespace LoadTestingTool.Reporting;

/// <summary>Where a run's history is kept: the history workbooks (the original way), a SQLite database beside them, or both while the two are compared.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HistoryStorage { Files, Database, Both }

/// <summary>Reads the history of one instance, whatever it is stored in.</summary>
public interface IHistoryStore
{
    /// <summary>One page of the history, newest runs first: <paramref name="take"/> runs after the first <paramref name="skip"/>. <c>Files</c> is how many runs (or files) the page held; fewer than <paramref name="take"/> means there are no older ones.</summary>
    (IReadOnlyList<RunHistoryRow> Rows, int Files) ScanPage(int skip, int take);

    /// <summary>The old data of a re-run: of the given run only, or of the newest run of every data row.</summary>
    IRunStateStore OpenRunState(string? runId);
}

/// <summary>The history in the workbooks: what the runner has always written.</summary>
public sealed class FileHistoryStore(string historyFolder) : IHistoryStore
{
    public (IReadOnlyList<RunHistoryRow> Rows, int Files) ScanPage(int skip, int take) => RunHistoryIndex.ScanPage(historyFolder, skip, take);
    public IRunStateStore OpenRunState(string? runId) => new HistoryRunStateStore(historyFolder, runId);
}

public static class HistoryStores
{
    /// <summary>The store to read from: the database for Database, the workbooks for Files and for Both (while the two are compared, the workbooks stay the source).</summary>
    public static IHistoryStore Reader(string historyFolder, HistoryStorage storage)
        => storage == HistoryStorage.Database ? new SqliteHistoryStore(historyFolder) : new FileHistoryStore(historyFolder);

    /// <summary>Writes the run to the database when the instance's storage asks for it. The workbooks are written by <see cref="HistoryWriter"/>.</summary>
    public static void SaveToDatabase(AppConfig config, RunResult run, RunValueMemory.Snapshot? state, string? rerunOf, byte[]? workbookContent = null)
    {
        if (config.HistoryStorage == HistoryStorage.Files) return;
        var store = new SqliteHistoryStore(config.HistoryFolder);
        store.SaveRun(run, state, rerunOf, new HistoryWriter(config).BuildDetails(run));
        if (workbookContent is not null && config.HistorySnapshotWorkbook && run.Testcases.Count > 0) store.SaveSnapshot(run.RunId, workbookContent);
        if (config.HistoryRetentionDays > 0) store.ApplyRetention(config.HistoryRetentionDays);
        if (config.HistoryRetentionRuns > 0) store.KeepNewestRuns(config.HistoryRetentionRuns);
    }
}
