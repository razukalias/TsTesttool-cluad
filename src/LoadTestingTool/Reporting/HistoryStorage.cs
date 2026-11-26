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
    /// <summary>Finished internal logs of this process, waiting to be stored once the run itself has been saved.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentQueue<(string Testcase, string Environment, string Result, string Path)> FinishedLogs = new();

    /// <summary>Called when a testcase's internal log has been closed and given its final name. Only remembered when the database keeps logs.</summary>
    public static void LogFinished(AppConfig config, string testcase, string environment, string result, string path)
    {
        if (config.HistoryStorage != HistoryStorage.Files && config.HistoryLogsInDatabase && File.Exists(path)) FinishedLogs.Enqueue((testcase, environment, result, path));
    }

    /// <summary>Stores the remembered logs under the run. With <see cref="AppConfig.HistoryDeleteLogFiles"/> a file is deleted only after its text is in the database.</summary>
    public static int SaveLogs(AppConfig config, string runId)
    {
        if (config.HistoryStorage == HistoryStorage.Files) return 0;
        var store = new SqliteHistoryStore(config.HistoryFolder);
        var saved = 0;
        while (FinishedLogs.TryDequeue(out var log))
        {
            try
            {
                store.SaveLog(runId, System.IO.Path.GetFileName(log.Path), log.Testcase, log.Environment, log.Result, File.ReadAllText(log.Path));
                saved++;
                if (config.HistoryDeleteLogFiles) File.Delete(log.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            { Console.Error.WriteLine($"[Warning] The log {System.IO.Path.GetFileName(log.Path)} could not be stored in the history database: {ex.Message}"); }
        }
        return saved;
    }

    public static void SaveToDatabase(AppConfig config, RunResult run, RunValueMemory.Snapshot? state, string? rerunOf, byte[]? workbookContent = null)
    {
        if (config.HistoryStorage == HistoryStorage.Files) return;
        var store = new SqliteHistoryStore(config.HistoryFolder);
        store.SaveRun(run, state, rerunOf, new HistoryWriter(config).BuildDetails(run));
        if (workbookContent is not null && config.HistorySnapshotWorkbook && run.Testcases.Count > 0) store.SaveSnapshot(run.RunId, workbookContent);
        if (config.HistoryRetentionDays > 0) store.ApplyRetention(config.HistoryRetentionDays);
        if (config.HistoryRetentionRuns > 0) store.KeepNewestRuns(config.HistoryRetentionRuns);
        if (config.HistoryRetentionMaxMb > 0) store.KeepUnderSize(config.HistoryRetentionMaxMb * 1024L * 1024L);
    }
}
