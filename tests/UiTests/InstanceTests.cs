using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class InstanceTests
{
    private sealed class Settings(string root) : IEnvironmentSettings
    {
        public List<(string Name, string? From)> Registered { get; } = [];
        public IReadOnlyList<string> Environments => [];
        public IReadOnlyList<string> GetSelection(string instanceName) => [];
        public bool RunParallel => false;
        public string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections) => null;
        public (string Testcases, string Steps) GetRunSelection(string instanceName) => ("", "");
        public string? SaveRunSelection(string instanceName, string testcases, string stepsJson) => null;
        public string? SaveRunParallel(bool value) => null;
        public string InstancesRoot => root;
        public string WorkbookName => "DataEngine.xlsx";
        public string? RegisterInstance(string name, string? copyFrom) { Registered.Add((name, copyFrom)); return null; }
    }

    [Fact]
    public void Duplicating_copies_workbook_templates_and_files_but_not_results_logs_or_history()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var folder = System.IO.Path.GetDirectoryName(path)!;
        foreach (var skipped in new[] { "Results", "Logs", "History" })
        {
            Directory.CreateDirectory(System.IO.Path.Combine(folder, skipped));
            File.WriteAllText(System.IO.Path.Combine(folder, skipped, "old.txt"), "x");
        }
        Directory.CreateDirectory(System.IO.Path.Combine(folder, "Templates"));
        File.WriteAllText(System.IO.Path.Combine(folder, "Templates", "create.json"), "{}");
        Directory.CreateDirectory(System.IO.Path.Combine(folder, "Files"));
        File.WriteAllText(System.IO.Path.Combine(folder, "Files", "a.bin"), "x");
        var root = System.IO.Path.GetDirectoryName(folder)!;
        var settings = new Settings(root);
        var shell = new EditorShellViewModel([("Source", path)]);
        shell.EnvironmentSettings = settings;
        var name = "Copy_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var node = shell.DuplicateInstance(shell.Instances[0], name);
            Assert.NotNull(node);
            var target = System.IO.Path.Combine(root, name);
            Assert.True(File.Exists(System.IO.Path.Combine(target, System.IO.Path.GetFileName(path))));
            Assert.True(File.Exists(System.IO.Path.Combine(target, "Templates", "create.json")));
            Assert.True(File.Exists(System.IO.Path.Combine(target, "Files", "a.bin")));
            Assert.False(File.Exists(System.IO.Path.Combine(target, "Results", "old.txt")));
            Assert.False(File.Exists(System.IO.Path.Combine(target, "Logs", "old.txt")));
            Assert.Equal([(name, "Source")], settings.Registered);
            Assert.Equal(2, shell.Instances.Count);
            Assert.Same(node, shell.InstanceFor(shell.SelectedNode));
            Assert.Equal(shell.Instances[0].Testcases.Count, node!.Testcases.Count);
        }
        finally { var t = System.IO.Path.Combine(root, name); if (Directory.Exists(t)) Directory.Delete(t, true); }
    }

    [Fact]
    public void Duplicating_refuses_unsaved_instances_and_taken_names()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var root = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path)!)!;
        var shell = new EditorShellViewModel([("Source", path)]);
        shell.EnvironmentSettings = new Settings(root);
        shell.Instances[0].Editor!.StepRows[0].Cells[shell.Instances[0].Editor!.StepRows[0].CellOf("verb")] = "PUT";
        Assert.Null(shell.DuplicateInstance(shell.Instances[0], "Anything_" + Guid.NewGuid().ToString("N")[..6]));
        Assert.Contains("unsaved", shell.Current!.StatusMessage);
        Assert.NotNull(InstanceOperations.CheckName(root, System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)!)));
    }
}
