using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoadTestingTool.UI;

// Lets the data editor choose environments and the parallel flag in the same central settings file the dashboard and runner use.
public sealed partial class MainWindow
{
    private sealed class EditorEnvironmentSettings(MainWindow window) : DataEditor.IEnvironmentSettings
    {
        public IReadOnlyList<string> Environments
            => window._config.Environments.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public bool RunParallel => window._config.RunEnvironmentsParallel;

        public IReadOnlyList<string> GetSelection(string instanceName)
        {
            var saved = window._instances.FirstOrDefault(x => x.Name == instanceName)?.EnvironmentSelection ?? string.Empty;
            return saved.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        public string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections)
        {
            try
            {
                var root = ReadRoot();
                var instances = root["Instances"] as JsonObject ?? new JsonObject();
                foreach (var (name, environments) in selections)
                {
                    var instance = instances[name] as JsonObject ?? new JsonObject();
                    instance["EnvironmentSelection"] = string.Join(',', environments);
                    instances[name] = instance;
                }
                root["Instances"] = instances;
                Write(root);
                foreach (var (name, environments) in selections)
                {
                    if (window._config.Instances.TryGetValue(name, out var central)) central.EnvironmentSelection = string.Join(',', environments);
                    if (window._instances.FirstOrDefault(x => x.Name == name) is not { } item) continue;
                    item.EnvironmentSelection = string.Join(',', environments);
                    item.EnvironmentSelectionJson = JsonSerializer.Serialize(environments);
                }
                window.UiLog($"Environments saved from the data editor. {string.Join("; ", selections.Select(p => $"{p.Key}={string.Join(',', p.Value)}"))}");
                return null;
            }
            catch (Exception ex)
            {
                window.UiLog($"Could not save environment selection: {ex}");
                return $"Could not save the environments to {Path.GetFileName(window._config.SettingsFile)}: {ex.Message}";
            }
        }

        public string? SaveRunParallel(bool value)
        {
            try
            {
                var root = ReadRoot();
                root["RunEnvironmentsParallel"] = value;
                // The runner and dashboard keep one central flag; an older per-instance value would hide it.
                if (root["Instances"] is JsonObject instances)
                    foreach (var pair in instances)
                        if (pair.Value is JsonObject configured) configured.Remove("RunEnvironmentsParallel");
                Write(root);
                window._config.RunEnvironmentsParallel = value;
                foreach (var item in window._instances) item.RunEnvironmentsParallel = value;
                window.UiLog($"Run environments in parallel set to {value} from the data editor.");
                return null;
            }
            catch (Exception ex)
            {
                window.UiLog($"Could not save the parallel setting: {ex}");
                return $"Could not save the parallel setting to {Path.GetFileName(window._config.SettingsFile)}: {ex.Message}";
            }
        }

        private JsonObject ReadRoot()
            => (File.Exists(window._config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(window._config.SettingsFile)) as JsonObject : null) ?? new JsonObject();

        private void Write(JsonObject root)
            => File.WriteAllText(window._config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }
}
