using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class GlobalEnvironmentTests
{
    private sealed class Store(string root, params string[] environments) : IEnvironmentSettings
    {
        public Dictionary<string, List<string>> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Writes { get; private set; }
        public IReadOnlyList<string> Environments { get; } = environments;
        public IReadOnlyList<string> GetSelection(string instanceName) => Saved.TryGetValue(instanceName, out var list) ? list : [];
        public bool RunParallel => false;
        public string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections)
        {
            Writes++;
            foreach (var (name, list) in selections) Saved[name] = list.ToList();
            return null;
        }
        public (string Testcases, string Steps) GetRunSelection(string instanceName) => ("", "");
        public string? SaveRunSelection(string instanceName, string testcases, string stepsJson) => null;
        public string? SaveRunParallel(bool value) => null;
        public string InstancesRoot => root;
        public string WorkbookName => "DataEngine.xlsx";
        public string? RegisterInstance(string name, string? copyFrom) { if (copyFrom is not null && Saved.TryGetValue(copyFrom, out var from)) Saved[name] = [.. from]; return null; }
    }

    private static EditorShellViewModel Shell(TempDir dir, int count, out Store store)
    {
        var source = Samples.CreateLarge(dir, rows: 2);
        var root = dir.Combine("inst");
        var list = new List<(string, string)>();
        for (var i = 1; i <= count; i++)
        {
            var folder = Path.Combine(root, "I" + i);
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "DataEngine.xlsx");
            File.Copy(source, target);
            list.Add(("I" + i, target));
        }
        store = new Store(root, "dev", "test", "prod");
        var shell = new EditorShellViewModel(list);
        shell.EnvironmentSettings = store;
        return shell;
    }

    private static EnvironmentChoice Global(EditorShellViewModel shell, string name) => shell.GlobalEnvironmentChoices.First(c => c.Name == name);

    [Fact]
    public void Unticking_an_environment_globally_changes_and_saves_every_instance_and_ticking_it_again_restores_them()
    {
        using var dir = new TempDir();
        var shell = Shell(dir, 4, out var store);
        Assert.All(shell.GlobalEnvironmentChoices, c => Assert.True(c.IsChosen));

        Global(shell, "prod").IsChosen = false;
        Assert.All(shell.Instances, i => Assert.Equal(["dev", "test"], i.ChosenEnvironments.ToArray()));
        Assert.Equal(4, store.Saved.Count);
        Assert.All(store.Saved.Values, v => Assert.Equal(["dev", "test"], v.ToArray()));
        Assert.Equal("dev, test", shell.GlobalEnvironmentsText);

        Global(shell, "prod").IsChosen = true;
        Assert.All(shell.Instances, i => Assert.Equal(["dev", "test", "prod"], i.ChosenEnvironments.ToArray()));
        Assert.All(store.Saved.Values, v => Assert.Equal(["dev", "test", "prod"], v.ToArray()));
    }

    [Fact]
    public void A_global_tick_overrides_what_single_instances_had_chosen_for_that_environment_only()
    {
        using var dir = new TempDir();
        var shell = Shell(dir, 3, out var store);
        shell.Instances[1].EnvironmentChoices.First(c => c.Name == "test").IsChosen = false;   // I2 alone does not run in test
        Assert.False(Global(shell, "test").IsChosen);                                          // so the global box is not ticked
        Global(shell, "test").IsChosen = true;                                                 // ticking it applies it to all
        Assert.All(shell.Instances, i => Assert.Contains("test", i.ChosenEnvironments));
        Assert.All(store.Saved.Values, v => Assert.Contains("test", v));
    }

    [Fact]
    public void The_global_choice_survives_reopening_the_settings_and_a_new_instance_made_by_duplicating_keeps_the_source_choice()
    {
        using var dir = new TempDir();
        var shell = Shell(dir, 2, out var store);
        Global(shell, "dev").IsChosen = false;
        var again = new EditorShellViewModel(shell.Instances.Select(i => (i.Name, i.Editor!.WorkbookPath)).ToList()) { EnvironmentSettings = store };
        Assert.All(again.Instances, i => Assert.Equal(["test", "prod"], i.ChosenEnvironments.ToArray()));
        Assert.False(Global(again, "dev").IsChosen);
        Assert.True(Global(again, "test").IsChosen);
    }
}
