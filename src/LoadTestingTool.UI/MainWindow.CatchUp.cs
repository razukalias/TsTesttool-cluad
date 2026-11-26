using Avalonia.Threading;
using LoadTestingTool.Domain;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _caughtUp = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The first time an instance is shown in a session (and its history is in the database), what a killed runner left behind is picked up: logs of dead runs are renamed,
    /// runs that never saved their history are rebuilt from their event files, and logs that were not stored yet are stored. It runs in the background and never blocks the window.
    /// </summary>
    private void CatchUpInBackground(InstanceInfo? item)
    {
        if (item is null || item.HistoryStorage == HistoryStorage.Files || !_caughtUp.Add(item.FolderPath)) return;
        var config = new AppConfig
        {
            HistoryFolder = item.HistoryFolder, LogsFolder = item.LogsFolder, HistoryStorage = item.HistoryStorage, HistoryLogsInDatabase = item.HistoryLogsInDatabase,
            SaveRequestBodyToHistory = item.SaveRequestBodyToHistory, SaveResponseBodyToHistory = item.SaveResponseBodyToHistory, MaskSensitiveData = item.MaskSensitiveData,
            HistoryDetail = item.HistoryDetail, HistorySampleEvery = item.HistorySampleEvery, HistorySheets = new Dictionary<string, bool>(item.HistorySheets, StringComparer.OrdinalIgnoreCase),
        };
        var resultsFolder = item.ResultsFolder; var environments = item.EnvironmentOptions.ToList(); var name = item.Name;
        _ = Task.Run(() =>
        {
            try
            {
                var result = RunRecovery.CatchUp(config, resultsFolder, environments);
                if (result.Nothing) return;
                Dispatcher.UIThread.Post(() =>
                {
                    UiLog($"Catch-up after an earlier crash. instance={name}; runsRebuilt={result.RunsRecovered}; logsRenamed={result.LogsRenamed}; logsStored={result.LogsStored}");
                    if (ReferenceEquals(_selectedInstance, item)) RefreshArtifacts();
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { UiLog($"Catch-up skipped. instance={name}; reason={ex.Message}"); }
        });
    }
}
