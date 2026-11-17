using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class EnvironmentListTests
{
    private sealed class ListSettings(params string[] environments) : IEnvironmentSettings
    {
        public List<string> Names { get; } = environments.ToList();
        public Dictionary<string, string[]> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? FailWith { get; set; }
        public IReadOnlyList<string> Environments => Names;
        public IReadOnlyList<string> GetSelection(string instanceName) => Saved.GetValueOrDefault(instanceName) ?? [];
        public bool RunParallel => false;
        public string InstancesRoot => System.IO.Path.GetTempPath();
        public string WorkbookName => "DataEngine.xlsx";
        public string? RegisterInstance(string name, string? copyFrom) => null;
        public string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections) { foreach (var (n, l) in selections) Saved[n] = l.ToArray(); return null; }
        public string? SaveRunParallel(bool value) => null;
        public (string Testcases, string Steps) GetRunSelection(string instanceName) => ("0", string.Empty);
        public string? SaveRunSelection(string instanceName, string testcases, string stepsJson) => null;
        public string? SaveEnvironmentList(IReadOnlyList<string> environments)
        {
            if (FailWith is not null) return FailWith;
            Names.Clear(); Names.AddRange(environments);
            foreach (var key in Saved.Keys.ToList()) Saved[key] = Saved[key].Where(x => environments.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
            return null;
        }
        public ExecutionValues GetExecution(string? instance) => ExecutionValues.None;
        public string? SaveExecution(string? instance, string key, string? value) => null;
    }

    private static (EditorShellViewModel Shell, ListSettings Settings) Open(TempDir dir, params string[] environments)
    {
        var settings = new ListSettings(environments);
        var shell = new EditorShellViewModel([("Sample", Samples.CreateLarge(dir, rows: 2))]) { EnvironmentSettings = settings };
        return (shell, settings);
    }

    [Fact]
    public void Adding_an_environment_saves_it_and_it_shows_up_unticked_where_a_selection_exists()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir, "t", "u");
        settings.Saved["Sample"] = ["t"];
        shell.EnvironmentSettings = settings;                         // reload the saved selection

        Assert.Null(shell.AddEnvironment("  qa "));

        Assert.Equal(["t", "u", "qa"], settings.Names);
        Assert.Equal(["t", "u", "qa"], shell.GlobalEnvironmentChoices.Select(c => c.Name));
        Assert.Equal(["t"], shell.Instances[0].ChosenEnvironments);   // a new environment is not switched on by itself
        Assert.Contains("Added qa", shell.EnvironmentEditMessage);
    }

    [Theory]
    [InlineData("", "Type a name")]
    [InlineData("   ", "Type a name")]
    [InlineData("two words", "letters, digits")]
    [InlineData("a,b", "letters, digits")]
    [InlineData("a|b", "letters, digits")]
    [InlineData("Default", "reserved")]
    [InlineData("T", "already exists")]
    public void Bad_environment_names_are_refused_with_a_reason_and_nothing_is_saved(string name, string reason)
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir, "t", "u");
        var problem = shell.AddEnvironment(name);
        Assert.NotNull(problem);
        Assert.Contains(reason, problem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["t", "u"], settings.Names);
    }

    [Fact]
    public void Removing_an_environment_drops_it_from_the_list_and_from_instance_selections()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir, "t", "u");
        settings.Saved["Sample"] = ["t", "u"];
        shell.EnvironmentSettings = settings;

        Assert.Null(shell.RemoveEnvironment("U"));

        Assert.Equal(["t"], settings.Names);
        Assert.Equal(["t"], shell.GlobalEnvironmentChoices.Select(c => c.Name));
        Assert.Equal(["t"], shell.Instances[0].ChosenEnvironments);
        Assert.Contains("will not run until you retag", shell.EnvironmentEditMessage);
        Assert.NotNull(shell.RemoveEnvironment("u"));                  // already gone
    }

    [Fact]
    public void The_manage_button_stays_available_with_no_environments_so_the_first_one_can_be_added()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir);
        Assert.True(shell.CanManageEnvironments);
        Assert.False(shell.HasEnvironments);
        Assert.Null(shell.AddEnvironment("t"));
        Assert.True(shell.HasEnvironments);

        Assert.Null(shell.RemoveEnvironment("t"));
        Assert.False(shell.HasEnvironments);
        Assert.True(shell.CanManageEnvironments);
    }

    [Fact]
    public void A_failed_save_changes_nothing_and_says_why()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir, "t");
        settings.FailWith = "disk is full";
        Assert.Equal("disk is full", shell.AddEnvironment("qa"));
        Assert.Equal(["t"], shell.GlobalEnvironmentChoices.Select(c => c.Name));
        Assert.Equal("disk is full", shell.EnvironmentEditMessage);
    }

    [AvaloniaFact]
    public void The_host_writes_the_list_and_prunes_selections_in_appsettings_json()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "envlist_" + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(file, """
            { "Environments": ["pt","t","u"],
              "Instances": { "A": { "EnvironmentSelection": "t,u", "EnvironmentSelectionJson": "[\"t\",\"u\"]" },
                             "B": { "EnvironmentSelection": "pt", "EnvironmentSelectionJson": "[\"pt\"]" },
                             "C": { "Enabled": true } } }
            """);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var dashboard = new LoadTestingTool.UI.MainWindow();
        var config = typeof(LoadTestingTool.UI.MainWindow).GetField("_config", flags)!.GetValue(dashboard)!;
        config.GetType().GetProperty("SettingsFile")!.SetValue(config, file);                         // never touch the real appsettings.json
        var host = typeof(LoadTestingTool.UI.MainWindow).GetNestedTypes(flags).Single(t => t.Name == "EditorEnvironmentSettings");
        var settings = (IEnvironmentSettings)Activator.CreateInstance(host, flags, null, [dashboard], null)!;

        Assert.Null(settings.SaveEnvironmentList(["pt", "qa"]));                                      // t and u removed, qa added

        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        Assert.Equal(["pt", "qa"], root["Environments"]!.AsArray().Select(x => x!.GetValue<string>()));
        var instances = root["Instances"]!.AsObject();
        Assert.Equal("", instances["A"]!["EnvironmentSelection"]!.GetValue<string>());                // nothing valid left = all
        Assert.Equal("pt", instances["B"]!["EnvironmentSelection"]!.GetValue<string>());
        Assert.Equal("[\"pt\"]", instances["B"]!["EnvironmentSelectionJson"]!.GetValue<string>());
        Assert.Null(instances["C"]!["EnvironmentSelection"]);                                         // untouched
        Assert.Equal(["pt", "qa"], settings.Environments);                                            // the running app sees it too
        File.Delete(file);
    }

    [AvaloniaFact]
    public void Add_and_remove_live_in_the_existing_environment_dropdown_not_in_a_separate_button()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 2));
        var settings = new ListSettings();
        window.Shell.EnvironmentSettings = settings;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        Assert.DoesNotContain(buttons, b => b.Content as string == "＋ / ✕");
        Assert.Contains(buttons, b => b.Classes.Contains("treeenv"));                 // the Env dropdown is there even with no environments
        Assert.Equal("none yet", window.Shell.GlobalEnvironmentsText);
        window.Close();
    }
}
