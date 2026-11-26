using LoadTestingTool.UI.CentralRerun;

namespace LoadTestingTool.UI;

public partial class MainWindow
{
    private CentralRerunWindow? _centralRerun;

    /// <summary>Opens the re-run center (one window; asking again brings it to the front).</summary>
    internal CentralRerunWindow OpenRerunCenter()
    {
        if (_centralRerun is { IsVisible: true } open) { open.Activate(); return open; }
        var window = new CentralRerunWindow(new CentralHost(this));
        window.Closed += (_, _) => { if (ReferenceEquals(_centralRerun, window)) _centralRerun = null; };
        _centralRerun = window;
        window.Show(this);
        return window;
    }

    /// <summary>Gives the re-run center the dashboard's instances and starts and watches their runs.</summary>
    private sealed class CentralHost(MainWindow window) : ICentralRerunHost
    {
        public IReadOnlyList<CentralInstance> Instances
        {
            get
            {
                var list = new List<CentralInstance>();
                foreach (var item in window._instances.ToList())
                {
                    window.ReloadWorkbookCatalog(item);   // a testcase or step added in the editor since the app started must be known
                    var ids = item.DataIdOptions;
                    list.Add(new CentralInstance(item.Name, item.ResultsFolder, item.TestcaseOptions,
                        (testcase, id) => ids.TryGetValue(testcase, out var known) ? known.FirstOrDefault(k => k.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? id : id));
                }
                return list;
            }
        }

        public string ReportsFolder => Path.Combine(Path.GetDirectoryName(window._config.SettingsFile) ?? ".", "Reports");

        public Task<string?> StartAsync(string instance, DataEditor.RunRequest request) => window.RunFromEditorAsync(instance, request);

        public async Task WaitUntilFinishedAsync(string instance, CancellationToken cancellation)
        {
            var item = window._instances.FirstOrDefault(i => i.Name == instance);
            if (item is null) return;
            await Task.Delay(300, cancellation);
            while (item.Process is { HasExited: false }) await Task.Delay(300, cancellation);
            await Task.Delay(800, cancellation);   // the dashboard's poll reads the last events and the history is on disk
        }

        public void Open(string path)
        {
            try { SystemDefaultFileOpener.Open(path); window.UiLog($"Opened {path}"); }
            catch (Exception ex) { window.UiLog($"Could not open {path}: {ex}"); window._details.Text = $"Could not open {path}: {ex.Message}"; }
        }
    }
}
