using System.Globalization;

namespace LoadTestingTool.UI;

/// <summary>An earlier run of an instance that left an event file, listed by the date and time it started.</summary>
public sealed record SavedRun(string RunId, string Folder, DateTime When)
{
    public string Label => DataEditor.RunLabel.Of(RunId, When);
}

public partial class MainWindow
{
    /// <summary>The runs of an instance that can be shown again, newest first. A run's id starts with its start time (yyyyMMdd_HHmmss).</summary>
    internal static IReadOnlyList<SavedRun> SavedRuns(InstanceInfo item)
    {
        var runs = new Dictionary<string, SavedRun>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { item.ResultsFolder, item.LogsFolder }.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try { files = Directory.GetFiles(folder, "events_*.ndjson", SearchOption.AllDirectories); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file)["events_".Length..];
                var runId = name.Length >= 25 ? name[..25] : name;
                var when = name.Length >= 15 && DateTime.TryParseExact(name[..15], "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : File.GetLastWriteTime(file);
                if (!runs.ContainsKey(runId)) runs[runId] = new SavedRun(runId, Path.GetDirectoryName(file) ?? folder, when);
            }
        }
        return runs.Values.OrderByDescending(r => r.When).ToList();
    }

    /// <summary>
    /// Shows an earlier run's status in the Overview panel by replaying its event file, the way the last run is shown at start.
    /// Not possible while the instance is running. Returns false (with a message in the log) when it cannot.
    /// </summary>
    internal bool LoadSavedRun(InstanceInfo item, SavedRun run)
    {
        if (item.Process is { HasExited: false }) { UiLog($"Saved run not loaded. instance={item.Name}; runId={run.RunId}; reason=instance-is-running"); return false; }
        item.Requests.Clear();
        item.Assertions.Clear();
        item.ManualActions.Clear();
        item.Issues.Clear();
        item.EventMessages.Clear();
        item.EventLinePositions.Clear();
        item.EventPosition = 0;
        item.EventLineIndex = 0;
        item.StartedAt = null; item.CompletedAt = null; item.CompletedRequests = null; item.AverageMs = null; item.P95Ms = null; item.RequestsPerSecond = null;
        item.Status = "Ready";
        item.LastEvent = $"Showing the run from {run.When:yyyy-MM-dd HH:mm:ss}";
        item.CurrentRunId = run.RunId;
        item.CurrentRunFolder = run.Folder;
        UiLog($"Loading a saved run. instance={item.Name}; runId={run.RunId}; folder={run.Folder}");
        ReadEvents(item);   // replays the event file now, so the panel is complete when it redraws
        // The table follows: it shows this run too (no choice = the newest run)
        DataEditor.ShownRunChoice.Set(item.ResultsFolder, SavedRuns(item).FirstOrDefault()?.RunId.Equals(run.RunId, StringComparison.OrdinalIgnoreCase) == true ? null : run.RunId);
        return true;
    }

    /// <summary>The table's Run drop-down (or "Show the latest run") changed the shared choice: show that run in the dashboard too.</summary>
    private void OnShownRunChosen(string resultsFolderKey, string? runId)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { Avalonia.Threading.Dispatcher.UIThread.Post(() => OnShownRunChosen(resultsFolderKey, runId)); return; }
        var item = _instances.FirstOrDefault(i => DataEditor.ShownRunChoice.Same(i.ResultsFolder, resultsFolderKey));
        if (item is null || item.Process is { HasExited: false }) return;
        var runs = SavedRuns(item);
        var run = runId is null ? runs.FirstOrDefault() : runs.FirstOrDefault(r => r.RunId.Equals(runId, StringComparison.OrdinalIgnoreCase));
        if (run is null || run.RunId.Equals(item.CurrentRunId, StringComparison.OrdinalIgnoreCase)) return;
        if (LoadSavedRun(item, run) && ReferenceEquals(item, _selectedInstance)) RefreshDashboardDetails(true);
    }
}
