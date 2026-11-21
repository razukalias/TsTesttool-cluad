using LoadTestingTool.UI.DataEditor;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

    private static string Inst(string root, string name, string source)
    {
        var folder = System.IO.Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.Copy(source, System.IO.Path.Combine(folder, "DataEngine.xlsx"));
        return System.IO.Path.Combine(folder, "DataEngine.xlsx");
    }

    [Fact]
    public void Creating_the_folders_of_an_instance_adds_only_what_is_missing_and_a_starter_template()
    {
        using var dir = new TempDir();
        var folder = dir.Combine("New");
        Directory.CreateDirectory(System.IO.Path.Combine(folder, "History"));
        var created = InstanceOperations.EnsureFolders(folder);
        foreach (var name in InstanceOperations.RequiredFolders) Assert.True(Directory.Exists(System.IO.Path.Combine(folder, name)), name);
        Assert.DoesNotContain("History", created);
        Assert.Contains("Files", created);
        Assert.Equal("{}\n", File.ReadAllText(System.IO.Path.Combine(folder, "Templates", "empty.json")));
        Assert.Empty(InstanceOperations.EnsureFolders(folder));
    }

    [Fact]
    public void Refresh_adds_new_instance_folders_removes_gone_ones_and_creates_missing_folders()
    {
        using var dir = new TempDir();
        var source = Samples.CreateLarge(dir, rows: 2);
        var root = dir.Combine("inst");
        var a = Inst(root, "A", source);
        var shell = new EditorShellViewModel([("A", a)]);
        var settings = new Settings(root);
        shell.EnvironmentSettings = settings;

        Assert.StartsWith("Instances are up to date", shell.RefreshInstances());
        Assert.True(Directory.Exists(System.IO.Path.Combine(root, "A", "Files")));

        var b = Inst(root, "B", source);
        var message = shell.RefreshInstances();
        Assert.Contains("1 added (B)", message);
        Assert.Equal(["A", "B"], shell.Instances.Select(i => i.Name).Order().ToArray());
        Assert.Contains(("B", null), settings.Registered);
        Assert.True(Directory.Exists(System.IO.Path.Combine(root, "B", "Files")));

        File.Delete(b);
        Assert.Contains("1 removed (B)", shell.RefreshInstances());
        Assert.Equal(["A"], shell.Instances.Select(i => i.Name).ToArray());
    }

    [Fact]
    public void Refresh_keeps_a_removed_instance_that_has_unsaved_edits()
    {
        using var dir = new TempDir();
        var source = Samples.CreateLarge(dir, rows: 2);
        var root = dir.Combine("inst");
        var a = Inst(root, "A", source);
        var b = Inst(root, "B", source);
        var shell = new EditorShellViewModel([("A", a), ("B", b)]);
        shell.EnvironmentSettings = new Settings(root);
        var editor = shell.Instances[1].Editor!;
        editor.StepRows[0].Cells[editor.StepRows[0].CellOf("verb")] = "PUT";
        File.Delete(b);
        var message = shell.RefreshInstances();
        Assert.Contains("unsaved edits", message);
        Assert.Equal(2, shell.Instances.Count);
    }

    [AvaloniaFact]
    public void The_instance_panel_has_a_refresh_button_that_picks_up_a_new_folder()
    {
        using var dir = new TempDir();
        var source = Samples.CreateLarge(dir, rows: 2);
        var root = dir.Combine("inst");
        var shell = new EditorShellViewModel([("A", Inst(root, "A", source))]);
        shell.EnvironmentSettings = new Settings(root);
        var window = new DataEditorWindow(shell);
        window.Show();
        Inst(root, "B", source);
        var button = window.FindControl<Button>("RefreshInstancesButton");
        Assert.NotNull(button);
        window.RefreshInstancesNow();
        Assert.Equal(2, shell.Instances.Count);
        window.Close();
    }
}
