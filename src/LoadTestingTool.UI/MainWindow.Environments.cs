using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoadTestingTool.UI;

// Lets the data editor choose environments and the parallel flag in the same central settings file the dashboard and runner use.
public sealed partial class MainWindow
{
    // Hosts the dashboard's detail tabs in the editor and keeps them on the instance selected there.
    private sealed class EditorRunDetails(MainWindow window) : DataEditor.IRunDetails
    {
        public Avalonia.Controls.TabControl TakeTabs()
        {
            if (window._detailTabs.Parent is Avalonia.Controls.Panel panel) panel.Children.Remove(window._detailTabs);
            else if (window._detailTabs.Parent is Avalonia.Controls.ContentControl holder) holder.Content = null;
            return window._detailTabs;
        }

        private readonly HashSet<string> _lastRunLoaded = new(StringComparer.OrdinalIgnoreCase);

        public DataEditor.RunStatusSnapshot GetStatus(string instanceName)
        {
            var item = window._instances.FirstOrDefault(x => x.Name == instanceName);
            if (item is null) return DataEditor.RunStatusSnapshot.Empty;
            if (_lastRunLoaded.Add(instanceName)) window.LoadLastRun(item);
            var running = false;
            try { running = item.Process is { HasExited: false }; } catch (InvalidOperationException) { }
            var outcomes = new List<DataEditor.StepOutcome>();
            foreach (var request in item.Requests.ToList())
            {
                if (DataEditor.RunClassifier.Classify(request.Result, request.FailureCategory, request.Error) is not { } state) continue;
                outcomes.Add(new DataEditor.StepOutcome(request.Testcase, request.StepName, request.DataId, request.Environment, state, request.Error ?? string.Empty));
            }
            // Issues the runner raised for a step that has no failed request of its own (for example a validation problem before anything was sent).
            foreach (var issue in item.Issues.ToList().Where(i => i.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)))
            {
                if (issue.StepName.Length > 0 && outcomes.Any(o => o.State != DataEditor.RunState.Passed && o.Testcase.Equals(issue.Testcase, StringComparison.OrdinalIgnoreCase) && o.Step.Equals(issue.StepName, StringComparison.OrdinalIgnoreCase) && o.DataId.Equals(issue.DataId, StringComparison.OrdinalIgnoreCase))) continue;
                outcomes.Add(new DataEditor.StepOutcome(issue.Testcase, issue.StepName, issue.DataId, issue.Environment, DataEditor.RunClassifier.FromCategory(issue.Category, issue.Message), issue.Message));
            }
            return new DataEditor.RunStatusSnapshot(running, outcomes);
        }

        public void Focus(string instanceName, DataEditor.RunFocus focus)
        {
            Select(instanceName, focus.Testcase.Length == 0 ? null : new DataEditor.RunScope(focus.Testcase, focus.TestcaseIndex, focus.Step));
            if (focus.State == DataEditor.RunState.AssertionFailed) { window._failedOnly.IsChecked = true; window.RefreshAssertionList(); }
        }

        public void OpenStorageCleanup() => window.ShowStorageCleanup();

        public void Stop(string instanceName)
        {
            if (window._instances.FirstOrDefault(x => x.Name == instanceName) is { } item && item.Process is { HasExited: false }) window.StopOne(item, false);
            window.RefreshRows();
        }

        public void Show(string instanceName) => Select(instanceName, null);

        public void Select(string instanceName, DataEditor.RunScope? scope)
        {
            window._scope = scope;
            window._scopeInstance = instanceName;
            var item = window._instances.FirstOrDefault(x => x.Name == instanceName);
            if (item is null) return;
            window.LoadLastRun(item);
            window._selectedInstance = item;
            window.ShowDetails(item);
        }
    }

    // With no run in this session, replays the newest saved run so the views show its last results.
    private void LoadLastRun(InstanceInfo item)
    {
        if (item.Process is { HasExited: false } || !string.IsNullOrWhiteSpace(item.CurrentRunFolder) || item.Requests.Count > 0 || item.EventLinePositions.Count > 0) return;
        try
        {
            var newest = new[] { item.ResultsFolder, item.LogsFolder }
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.GetFiles(folder, "events_*.ndjson", SearchOption.AllDirectories))
                .Select(file => new FileInfo(file))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is null) return;
            var name = Path.GetFileNameWithoutExtension(newest.Name)["events_".Length..];
            item.CurrentRunId = name.Length >= 25 ? name[..25] : name;
            item.CurrentRunFolder = newest.DirectoryName ?? string.Empty;
            UiLog($"Loading the last saved run for the editor. instance={item.Name}; runId={item.CurrentRunId}; folder={item.CurrentRunFolder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { UiLog($"Could not find the last run of {item.Name}: {ex.Message}"); }
    }

    private sealed class EditorEnvironmentSettings(MainWindow window) : DataEditor.IEnvironmentSettings
    {
        public IReadOnlyList<string> Environments
            => window._config.Environments.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public bool RunParallel => window._config.RunEnvironmentsParallel;

        public string InstancesRoot => Path.GetFullPath(window._config.InstancesRoot);
        public string WorkbookName => window._config.WorkbookName;

        public string? RegisterInstance(string name, string? copyFrom)
        {
            try
            {
                window.RegisterCentralInstance(Path.Combine(InstancesRoot, name));
                if (copyFrom is not null)
                {
                    var root = ReadRoot();
                    var instances = root["Instances"] as JsonObject ?? new JsonObject();
                    if (instances[copyFrom] is JsonObject source)
                    {
                        var copy = JsonNode.Parse(source.ToJsonString()) as JsonObject ?? new JsonObject();
                        copy["Enabled"] = true;
                        instances[name] = copy;
                        root["Instances"] = instances;
                        Write(root);
                    }
                }
                window.RefreshInstances();
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                return ex.Message;
            }
        }

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

        public (string Testcases, string Steps) GetRunSelection(string instanceName)
        {
            var item = window._instances.FirstOrDefault(x => x.Name == instanceName);
            return (item?.TestcaseSelection ?? string.Empty, item?.StepSelectionJson ?? string.Empty);
        }

        public string? SaveRunSelection(string instanceName, string testcases, string stepsJson)
        {
            try
            {
                var root = ReadRoot();
                var instances = root["Instances"] as JsonObject ?? new JsonObject();
                var instance = instances[instanceName] as JsonObject ?? new JsonObject();
                instance["TestcaseSelection"] = testcases;
                instance["StepSelection"] = stepsJson;
                instances[instanceName] = instance;
                root["Instances"] = instances;
                Write(root);
                if (window._config.Instances.TryGetValue(instanceName, out var central))
                {
                    central.TestcaseSelection = testcases;
                    central.StepSelection = stepsJson;
                }
                if (window._instances.FirstOrDefault(x => x.Name == instanceName) is { } item)
                {
                    item.TestcaseSelection = testcases;
                    item.StepSelectionJson = stepsJson;
                    item.HasAppliedSelection = true;
                }
                window.UiLog($"Run selection saved from the data editor. instance={instanceName}; testcases={testcases}; steps={stepsJson}");
                return null;
            }
            catch (Exception ex)
            {
                window.UiLog($"Could not save the run selection: {ex}");
                return $"Could not save the selection to {Path.GetFileName(window._config.SettingsFile)}: {ex.Message}";
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

        public DataEditor.ExecutionValues GetExecution(string? instance)
        {
            var config = window._config;
            if (instance is null)
                return new DataEditor.ExecutionValues(NormalizeRowsExecutionMode(config.RowsExecutionMode), Math.Clamp(config.MaxParallelRows, 1, 100), Math.Max(1, config.Iterations),
                    Math.Max(0, config.ParallelRowStartIntervalMs), Math.Max(0, config.IterationIntervalMs));
            try
            {
                if (ReadRoot()["Instances"] is not JsonObject instances || instances[instance] is not JsonObject own) return DataEditor.ExecutionValues.None;
                int? Number(params string[] names)
                {
                    foreach (var name in names)
                        if (own[name] is { } node && int.TryParse(node.ToString(), out var value)) return value;
                    return null;
                }
                var mode = own["RowsExecutionMode"]?.ToString() ?? own["ExecutionMode"]?.ToString();
                return new DataEditor.ExecutionValues(string.IsNullOrWhiteSpace(mode) ? null : NormalizeRowsExecutionMode(mode), Number("MaxParallelRows", "MaxConcurrentRows"), Number("Iterations"),
                    Number("ParallelRowStartIntervalMs", "ThreadIntervalMs"), Number("IterationIntervalMs"));
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { return DataEditor.ExecutionValues.None; }
        }

        public string? SaveExecution(string? instance, string key, string? value)
        {
            if (value is not null)
            {
                var invalid = DataEditor.ExecutionKeys.Validate(key, value, out var normalized);
                if (invalid is not null) return invalid;
                value = normalized;
            }
            else if (instance is null) return "A global value cannot be empty.";
            try
            {
                var root = ReadRoot();
                var instances = root["Instances"] as JsonObject ?? new JsonObject();
                // Older files used these names for the same settings; remove them so the saved value is the one that is read.
                string[] aliases = key switch
                {
                    DataEditor.ExecutionKeys.MaxRows => ["MaxConcurrentRows"],
                    DataEditor.ExecutionKeys.StartInterval => ["ThreadIntervalMs"],
                    DataEditor.ExecutionKeys.Mode => ["ExecutionMode"],
                    _ => []
                };
                JsonNode? Typed() => key == DataEditor.ExecutionKeys.Mode ? JsonValue.Create(value) : JsonValue.Create(int.Parse(value!));
                if (instance is null)
                {
                    root[key] = Typed();
                    foreach (var pair in instances)
                        if (pair.Value is JsonObject configured)
                        {
                            configured.Remove(key);
                            foreach (var alias in aliases) configured.Remove(alias);
                        }
                }
                else
                {
                    var own = instances[instance] as JsonObject ?? new JsonObject();
                    foreach (var alias in aliases) own.Remove(alias);
                    if (value is null) own.Remove(key); else own[key] = Typed();
                    instances[instance] = own;
                    root["Instances"] = instances;
                }
                Write(root);
                ApplyExecution(instance, key, value);
                window.UiLog($"Row execution setting saved from the data editor. scope={instance ?? "global"}; {key}={value ?? "(inherit)"}");
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                window.UiLog($"Could not save the row execution setting: {ex}");
                return $"Could not save {key} to {Path.GetFileName(window._config.SettingsFile)}: {ex.Message}";
            }
        }

        // Keeps the dashboard's in-memory settings in step with the file, so runs started afterwards use the new values.
        private void ApplyExecution(string? instance, string key, string? value)
        {
            var config = window._config;
            if (instance is null)
            {
                switch (key)
                {
                    case DataEditor.ExecutionKeys.Mode: config.RowsExecutionMode = value!; break;
                    case DataEditor.ExecutionKeys.MaxRows: config.MaxParallelRows = int.Parse(value!); break;
                    case DataEditor.ExecutionKeys.Iterations: config.Iterations = int.Parse(value!); break;
                    case DataEditor.ExecutionKeys.StartInterval: config.ParallelRowStartIntervalMs = int.Parse(value!); break;
                    case DataEditor.ExecutionKeys.IterationInterval: config.IterationIntervalMs = int.Parse(value!); break;
                }
                foreach (var central in config.Instances.Values) ClearOverride(central, key);
            }
            else if (config.Instances.TryGetValue(instance, out var central))
            {
                if (value is null) ClearOverride(central, key); else SetOverride(central, key, value);
            }
            foreach (var item in window._instances.Where(i => instance is null || i.Name == instance))
            {
                config.Instances.TryGetValue(item.Name, out var central);
                switch (key)
                {
                    case DataEditor.ExecutionKeys.Mode: item.RowsExecutionMode = NormalizeRowsExecutionMode(central?.RowsExecutionMode ?? central?.ExecutionMode ?? config.RowsExecutionMode); break;
                    case DataEditor.ExecutionKeys.MaxRows: item.MaxParallelRows = Math.Clamp(central?.MaxParallelRows ?? central?.MaxConcurrentRows ?? config.MaxParallelRows, 1, 100); break;
                    case DataEditor.ExecutionKeys.Iterations: item.Iterations = Math.Max(1, central?.Iterations ?? config.Iterations); break;
                    case DataEditor.ExecutionKeys.StartInterval: item.ParallelRowStartIntervalMs = Math.Max(0, central?.ParallelRowStartIntervalMs ?? central?.ThreadIntervalMs ?? config.ParallelRowStartIntervalMs); break;
                    case DataEditor.ExecutionKeys.IterationInterval: item.IterationIntervalMs = Math.Max(0, central?.IterationIntervalMs ?? config.IterationIntervalMs); break;
                }
            }
        }

        private static void ClearOverride(UiInstanceDefinition central, string key)
        {
            switch (key)
            {
                case DataEditor.ExecutionKeys.Mode: central.RowsExecutionMode = null; central.ExecutionMode = null; break;
                case DataEditor.ExecutionKeys.MaxRows: central.MaxParallelRows = null; central.MaxConcurrentRows = null; break;
                case DataEditor.ExecutionKeys.Iterations: central.Iterations = null; break;
                case DataEditor.ExecutionKeys.StartInterval: central.ParallelRowStartIntervalMs = null; central.ThreadIntervalMs = null; break;
                case DataEditor.ExecutionKeys.IterationInterval: central.IterationIntervalMs = null; break;
            }
        }

        private static void SetOverride(UiInstanceDefinition central, string key, string value)
        {
            switch (key)
            {
                case DataEditor.ExecutionKeys.Mode: central.RowsExecutionMode = value; central.ExecutionMode = null; break;
                case DataEditor.ExecutionKeys.MaxRows: central.MaxParallelRows = int.Parse(value); central.MaxConcurrentRows = null; break;
                case DataEditor.ExecutionKeys.Iterations: central.Iterations = int.Parse(value); break;
                case DataEditor.ExecutionKeys.StartInterval: central.ParallelRowStartIntervalMs = int.Parse(value); central.ThreadIntervalMs = null; break;
                case DataEditor.ExecutionKeys.IterationInterval: central.IterationIntervalMs = int.Parse(value); break;
            }
        }

        private JsonObject ReadRoot()
            => (File.Exists(window._config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(window._config.SettingsFile)) as JsonObject : null) ?? new JsonObject();

        private void Write(JsonObject root)
            => File.WriteAllText(window._config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }
}
