using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ExecutionTests
{
    // Mimics appsettings.json: root values plus per-instance overrides; a global write clears that key from every instance.
    private sealed class Settings : IEnvironmentSettings
    {
        public Dictionary<string, string> Root { get; } = new() { ["RowsExecutionMode"] = "parallel", ["MaxParallelRows"] = "3", ["Iterations"] = "2", ["ParallelRowStartIntervalMs"] = "1000", ["IterationIntervalMs"] = "500" };
        public Dictionary<string, Dictionary<string, string>> Instances { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Writes { get; private set; }
        public IReadOnlyList<string> Environments => [];
        public IReadOnlyList<string> GetSelection(string instanceName) => [];
        public bool RunParallel => false;
        public string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections) => null;
        public (string Testcases, string Steps) GetRunSelection(string instanceName) => ("0", "");
        public string? SaveRunSelection(string instanceName, string testcases, string stepsJson) => null;
        public string? SaveRunParallel(bool value) => null;
        public string InstancesRoot => Path.GetTempPath();
        public string WorkbookName => "DataEngine.xlsx";
        public string? RegisterInstance(string name, string? copyFrom) => null;

        public ExecutionValues GetExecution(string? instance)
        {
            var source = instance is null ? Root : Instances.GetValueOrDefault(instance) ?? [];
            int? N(string k) => source.TryGetValue(k, out var v) ? int.Parse(v) : null;
            return new ExecutionValues(source.GetValueOrDefault("RowsExecutionMode"), N("MaxParallelRows"), N("Iterations"), N("ParallelRowStartIntervalMs"), N("IterationIntervalMs"));
        }

        public string? SaveExecution(string? instance, string key, string? value)
        {
            Writes++;
            if (instance is null)
            {
                Root[key] = value!;
                foreach (var own in Instances.Values) own.Remove(key);
                return null;
            }
            if (!Instances.TryGetValue(instance, out var o)) Instances[instance] = o = [];
            if (value is null) o.Remove(key); else o[key] = value;
            return null;
        }
    }

    private static (EditorShellViewModel Shell, Settings Settings) Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        var settings = new Settings();
        return (new EditorShellViewModel([("Sample", path)]) { EnvironmentSettings = settings }, settings);
    }

    [Fact]
    public void Global_boxes_show_the_root_values_and_instances_start_without_overrides()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir);
        var global = shell.Execution!;
        Assert.Equal(("parallel", "3", "2", "1000", "500"), (global.Mode, global.MaxRows, global.Iterations, global.StartInterval, global.IterationInterval));
        var own = shell.Instances[0].Execution!;
        Assert.Equal(string.Empty, own.MaxRows);
        Assert.Equal("3", own.InheritedMaxRows);
        Assert.False(own.HasOverrides);
        Assert.Equal("parallel · 3 rows · ×2", own.Summary);
    }

    [Fact]
    public void Instance_override_is_saved_and_clearing_the_box_inherits_again()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir);
        var own = shell.Instances[0].Execution!;
        own.MaxRows = "7"; Assert.True(own.Commit(ExecutionKeys.MaxRows));
        Assert.Equal("7", settings.Instances["Sample"]["MaxParallelRows"]);
        Assert.Equal("3", settings.Root["MaxParallelRows"]);
        Assert.True(own.HasOverrides);
        Assert.Equal("parallel · 7 rows · ×2", own.Summary);
        own.MaxRows = ""; Assert.True(own.Commit(ExecutionKeys.MaxRows));
        Assert.False(settings.Instances["Sample"].ContainsKey("MaxParallelRows"));
        Assert.Equal("parallel · 3 rows · ×2", own.Summary);
    }

    [Fact]
    public void Global_value_replaces_instance_overrides_of_that_key_only()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir);
        var own = shell.Instances[0].Execution!;
        own.MaxRows = "7"; own.Commit(ExecutionKeys.MaxRows);
        own.Iterations = "5"; own.Commit(ExecutionKeys.Iterations);
        var global = shell.Execution!;
        global.MaxRows = "10"; Assert.True(global.Commit(ExecutionKeys.MaxRows));
        Assert.Equal("10", settings.Root["MaxParallelRows"]);
        Assert.Equal(string.Empty, own.MaxRows);            // override cleared and view refreshed
        Assert.Equal("10", own.InheritedMaxRows);
        Assert.Equal("5", own.Iterations);                   // other keys keep their override
    }

    [Fact]
    public void Invalid_values_are_refused_and_revert()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir);
        var global = shell.Execution!;
        var problems = new List<string>();
        global.Problem += (_, m) => problems.Add(m);
        foreach (var (key, bad) in new[] { (ExecutionKeys.MaxRows, "0"), (ExecutionKeys.MaxRows, "101"), (ExecutionKeys.MaxRows, "abc"), (ExecutionKeys.Iterations, "0"), (ExecutionKeys.StartInterval, "-1"), (ExecutionKeys.IterationInterval, "1.5") })
        {
            switch (key)
            {
                case ExecutionKeys.MaxRows: global.MaxRows = bad; break;
                case ExecutionKeys.Iterations: global.Iterations = bad; break;
                case ExecutionKeys.StartInterval: global.StartInterval = bad; break;
                default: global.IterationInterval = bad; break;
            }
            Assert.False(global.Commit(key));
        }
        Assert.Equal(6, problems.Count);
        Assert.Equal("3", global.MaxRows);
        Assert.Equal("3", settings.Root["MaxParallelRows"]);
        Assert.Equal(0, settings.Writes);
        global.MaxRows = ""; Assert.False(global.Commit(ExecutionKeys.MaxRows)); // a global value cannot be empty
        Assert.Equal("3", global.MaxRows);
    }

    [Fact]
    public void Sequential_mode_disables_the_parallel_boxes_and_enables_the_iteration_interval()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir);
        var global = shell.Execution!;
        Assert.True(global.ParallelFieldsEnabled);
        Assert.False(global.IterationIntervalEnabled);
        global.Mode = "sequential"; global.Commit(ExecutionKeys.Mode);
        Assert.False(global.ParallelFieldsEnabled);
        Assert.True(global.IterationIntervalEnabled);
        Assert.Equal("sequential · ×2", global.Summary);
        var own = shell.Instances[0].Execution!;
        Assert.Equal("sequential · ×2", own.Summary);        // an instance inherits the mode
        own.Mode = "parallel"; own.Commit(ExecutionKeys.Mode);
        Assert.True(own.ParallelFieldsEnabled);
    }

    [AvaloniaFact]
    public void Panel_renders_in_both_scopes()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir);
        var panel = new ExecutionPanel { DataContext = shell.Instances[0].Execution };
        var window = new Window { Width = 360, Height = 380, Content = panel };
        window.Show();
        Assert.Equal("3 (global)", panel.MaxRowsBox!.Watermark);
        Assert.Equal(string.Empty, panel.MaxRowsBox.Text ?? string.Empty);
        panel.MaxRowsBox.Text = "4";
        panel.MaxRowsBox.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(InputElementLostFocus));
        Assert.Equal("4", shell.Instances[0].Execution!.MaxRows);
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "execution-instance.png"));
        panel.DataContext = shell.Execution;
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "execution-global.png"));
        window.Close();
    }

    private static Avalonia.Interactivity.RoutedEvent InputElementLostFocus => Avalonia.Input.InputElement.LostFocusEvent;
}
