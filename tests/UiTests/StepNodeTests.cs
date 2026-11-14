using Avalonia.Headless;
using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class StepNodeTests
{
    private static EditorShellViewModel Shell(TempDir dir, out string path)
    {
        path = Samples.CreateLarge(dir, rows: 3);
        return new EditorShellViewModel([("Sample", path)]);
    }

    [Fact]
    public void Testcases_list_their_steps_as_tree_children()
    {
        using var dir = new TempDir();
        var shell = Shell(dir, out _);
        var testcase = shell.Instances[0].Testcases[0];
        Assert.NotEmpty(testcase.StepNodes);
        Assert.Equal(testcase.Steps, testcase.StepNodes.Select(n => n.Name));
    }

    [Fact]
    public void Choosing_a_step_node_shows_only_that_step_as_a_form_and_keeps_the_selection()
    {
        using var dir = new TempDir();
        var shell = Shell(dir, out _);
        var testcase = shell.Instances[0].Testcases[0];
        var node = testcase.StepNodes[0];
        shell.SelectedNode = node;
        var vm = shell.Current!;
        Assert.True(vm.ShowSteps);
        Assert.Equal(node.Name, vm.FocusedStep?.CurrentName);
        Assert.Same(node, shell.SelectedNode);
        Assert.True(shell.StepNodeSelected);
    }

    [Fact]
    public void Renaming_a_step_from_the_tree_menu_updates_the_nodes()
    {
        using var dir = new TempDir();
        var shell = Shell(dir, out _);
        var testcase = shell.Instances[0].Testcases[0];
        var old = testcase.StepNodes[0].Name;
        shell.SelectedNode = testcase.StepNodes[0];
        var vm = shell.Current!;
        vm.OpenRenameStepCommand.Execute(null);
        vm.TestcasePanelName = "Renamed";
        vm.ConfirmTestcaseActionCommand.Execute(null);
        Assert.Contains("Renamed", vm.SelectedTestcase!.StepNodes.Select(n => n.Name));
        Assert.DoesNotContain(old, vm.SelectedTestcase.StepNodes.Select(n => n.Name));
        Assert.True(vm.IsDirty);
    }
}

public class TreeInlineEditTests
{
    private static EditorShellViewModel Shell(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var workbook = new XLWorkbook(path))
        {
            var config = workbook.Worksheet("config");
            config.Cell(3, 1).Value = 2; config.Cell(3, 2).Value = "Orders"; config.Cell(3, 3).Value = "CreateOrder"; config.Cell(3, 4).Value = "HTTP";
            config.Cell(3, 5).Value = "order.json"; config.Cell(3, 6).Value = "https://api.example.test/orders"; config.Cell(3, 7).Value = "POST";
            workbook.Worksheet("request").Cell(1, 8).Value = "CreateOrder.sku";
            workbook.Worksheet("response").Cell(1, 7).Value = "{eq}CreateOrder.statusCode";
            workbook.Save();
        }
        return new EditorShellViewModel([("Sample", path)]);
    }

    [Fact]
    public void Renaming_a_testcase_in_the_tree_needs_no_panel()
    {
        using var dir = new TempDir();
        var shell = Shell(dir);
        var orders = shell.Instances[0].Testcases.First(t => t.Index == 2);
        shell.BeginRename(orders);
        Assert.True(orders.IsEditing);
        orders.EditText = "Purchases";
        shell.CommitRename(orders);
        Assert.Contains(shell.Instances[0].Testcases, t => t.Index == 2 && t.Name == "Purchases");
        Assert.False(shell.Current!.TestcasePanelVisible);
        Assert.True(shell.Current.IsDirty);
    }

    [Fact]
    public void Escape_leaves_the_name_alone()
    {
        using var dir = new TempDir();
        var shell = Shell(dir);
        var orders = shell.Instances[0].Testcases.First(t => t.Index == 2);
        shell.BeginRename(orders);
        orders.EditText = "Nope";
        shell.CancelRename(orders);
        shell.CommitRename(orders); // focus loss after Escape does nothing
        Assert.Equal("Orders", shell.Instances[0].Testcases.First(t => t.Index == 2).Name);
        Assert.False(shell.Current!.IsDirty);
    }

    [Fact]
    public void Renaming_a_step_in_the_tree_renames_the_node()
    {
        using var dir = new TempDir();
        var shell = Shell(dir);
        var orders = shell.Instances[0].Testcases.First(t => t.Index == 2);
        var step = orders.StepNodes[0];
        shell.BeginRename(step);
        step.EditText = "PlaceOrder";
        shell.CommitRename(step);
        var after = shell.Instances[0].Testcases.First(t => t.Index == 2);
        Assert.Equal(["PlaceOrder"], after.StepNodes.Select(n => n.Name));
        Assert.Equal("PlaceOrder", (shell.SelectedNode as StepNode)?.Name);
    }

    [Fact]
    public void The_arrows_move_testcases_and_steps()
    {
        using var dir = new TempDir();
        var shell = Shell(dir);
        var orders = shell.Instances[0].Testcases.First(t => t.Index == 2);
        shell.MoveNode(orders, -1);
        Assert.Equal(["Orders", "Customers"], shell.Instances[0].Testcases.OrderBy(t => t.Index).Select(t => t.Name).ToArray());

        var vm = shell.Current!;
        var customers = vm.Testcases.First(t => t.Name == "Customers");
        vm.SelectedTestcase = customers;
        vm.AddStepCommand.Execute(null);
        var nodes = vm.SelectedTestcase!.StepNodes;
        var last = nodes[^1].Name;
        shell.MoveNode(nodes[^1], -1);
        Assert.Equal(last, vm.SelectedTestcase!.StepNodes[^2].Name);
    }
}

public class TreeRunTests
{
    private static (EditorShellViewModel Shell, List<RunRequest> Runs) Shell(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var workbook = new XLWorkbook(path))
        {
            var config = workbook.Worksheet("config");
            config.Cell(3, 1).Value = 2; config.Cell(3, 2).Value = "Orders"; config.Cell(3, 3).Value = "CreateOrder"; config.Cell(3, 4).Value = "HTTP";
            config.Cell(3, 5).Value = "order.json"; config.Cell(3, 6).Value = "https://api.example.test/orders"; config.Cell(3, 7).Value = "POST";
            workbook.Worksheet("request").Cell(1, 8).Value = "CreateOrder.sku";
            workbook.Worksheet("response").Cell(1, 7).Value = "{eq}CreateOrder.statusCode";
            workbook.Save();
        }
        var shell = new EditorShellViewModel([("Sample", path)]);
        var runs = new List<RunRequest>();
        shell.RunHandler = (_, request) => { runs.Add(request); return Task.FromResult<string?>(null); };
        shell.Instances[0].Editor!.RunHandler = request => { runs.Add(request); return Task.FromResult<string?>(null); };
        return (shell, runs);
    }

    [Fact]
    public async Task The_run_icons_run_exactly_what_they_sit_beside()
    {
        using var dir = new TempDir();
        var (shell, runs) = Shell(dir);
        var instance = shell.Instances[0];
        var orders = instance.Testcases.First(t => t.Index == 2);

        await shell.RunNodeAsync(orders);
        Assert.Equal([2], runs[^1].Testcases);
        Assert.Null(runs[^1].Steps);

        await shell.RunNodeAsync(orders.StepNodes[0]);
        Assert.Equal([2], runs[^1].Testcases);
        Assert.Equal(["CreateOrder"], runs[^1].Steps![2]);

        await shell.RunNodeAsync(instance);
        Assert.Empty(runs[^1].Testcases);
    }

    [Fact]
    public async Task Ticks_combine_across_testcases_and_a_partly_ticked_testcase_runs_only_its_ticked_steps()
    {
        using var dir = new TempDir();
        var (shell, runs) = Shell(dir);
        var vm = shell.Instances[0].Editor!;
        var customers = vm.Testcases.First(t => t.Index == 1);
        vm.SelectedTestcase = customers;
        vm.AddStepCommand.Execute(null);
        vm.SaveCommand.Execute(null); // a run reads the saved workbook
        customers = vm.Testcases.First(t => t.Index == 1);
        var orders = vm.Testcases.First(t => t.Index == 2);

        Assert.False(shell.HasTicked);
        orders.ChosenState = true;
        customers.StepNodes[0].IsChosen = true;
        Assert.Null(customers.ChosenState);       // some steps ticked
        Assert.True(orders.ChosenState);
        Assert.Null(shell.Instances[0].ChosenState);
        Assert.Equal("2 ticked", shell.TickedText);

        await shell.RunTickedAsync();
        var request = runs[^1];
        Assert.Equal([1, 2], request.Testcases);
        Assert.Equal([customers.StepNodes[0].Name], request.Steps![1]);
        Assert.Equal(["CreateOrder"], request.Steps[2]);

        shell.Instances[0].ChosenState = false;
        Assert.False(shell.HasTicked);
    }
}

public class TreeShotTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Tree_screenshot()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var window = DataEditorWindow.Open(path, null);
        window.Width = 1200; window.Height = 700;
        window.Show();
        var vm = window.ViewModel;
        vm.Testcases[0].StepNodes[0].IsChosen = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-tree.png"));
        window.Close();
    }
}

public class EnvironmentPickerTests
{
    private sealed class FakeSettings(params string[] environments) : IEnvironmentSettings
    {
        public Dictionary<string, string[]> Saved { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Writes { get; private set; }
        public bool Parallel { get; set; }
        public IReadOnlyList<string> Environments { get; } = environments;
        public IReadOnlyList<string> GetSelection(string instanceName) => Saved.GetValueOrDefault(instanceName) ?? [];
        public bool RunParallel => Parallel;
        public string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections)
        {
            Writes++;
            foreach (var (name, list) in selections) Saved[name] = list.ToArray();
            return null;
        }
        public string? SaveRunParallel(bool value) { Parallel = value; return null; }
        public Dictionary<string, (string Testcases, string Steps)> Selection { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int SelectionWrites { get; private set; }
        public (string Testcases, string Steps) GetRunSelection(string instanceName) => Selection.GetValueOrDefault(instanceName, ("0", string.Empty));
        public string? SaveRunSelection(string instanceName, string testcases, string stepsJson)
        {
            SelectionWrites++;
            Selection[instanceName] = (testcases, stepsJson);
            return null;
        }
    }

    private static (EditorShellViewModel Shell, FakeSettings Settings, List<RunRequest> Runs) Setup(TempDir a, TempDir b)
    {
        var shell = new EditorShellViewModel([("One", Samples.CreateLarge(a, rows: 2)), ("Two", Samples.CreateLarge(b, rows: 2))]);
        var runs = new List<RunRequest>();
        foreach (var node in shell.Instances) node.Editor!.RunHandler = r => { runs.Add(r); return Task.FromResult<string?>(null); };
        var settings = new FakeSettings("local", "development", "staging", "production");
        shell.EnvironmentSettings = settings;
        return (shell, settings, runs);
    }

    [Fact]
    public void An_instance_without_a_saved_selection_runs_in_all_environments()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var (shell, _, _) = Setup(a, b);
        Assert.True(shell.HasEnvironments);
        Assert.Equal("All environments", shell.Instances[0].EnvironmentsText);
        Assert.Equal("All environments", shell.GlobalEnvironmentsText);
    }

    [Fact]
    public void Ticking_environments_in_one_instance_saves_only_that_instance()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var (shell, settings, _) = Setup(a, b);
        var one = shell.Instances[0];
        one.EnvironmentChoices.First(c => c.Name == "production").IsChosen = false;
        one.EnvironmentChoices.First(c => c.Name == "staging").IsChosen = false;
        Assert.Equal(["local", "development"], settings.Saved["One"]);
        Assert.False(settings.Saved.ContainsKey("Two"));
        Assert.Equal("local, development", one.EnvironmentsText);
        Assert.Equal("Mixed", shell.GlobalEnvironmentsText);
    }

    [Fact]
    public void The_last_environment_cannot_be_unticked()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var (shell, settings, _) = Setup(a, b);
        var one = shell.Instances[0];
        foreach (var c in one.EnvironmentChoices.Skip(1).ToList()) c.IsChosen = false;
        one.EnvironmentChoices[0].IsChosen = false;
        Assert.Equal(["local"], settings.Saved["One"]);
        Assert.True(one.EnvironmentChoices[0].IsChosen);
    }

    [Fact]
    public void The_global_drop_down_sets_every_instance_in_one_write()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var (shell, settings, _) = Setup(a, b);
        shell.GlobalEnvironmentChoices.First(c => c.Name == "production").IsChosen = false;
        Assert.Equal(1, settings.Writes);
        Assert.Equal(["local", "development", "staging"], settings.Saved["One"]);
        Assert.Equal(["local", "development", "staging"], settings.Saved["Two"]);
        Assert.Equal("local, development, staging", shell.GlobalEnvironmentsText);
        Assert.Equal("local, development, staging", shell.Instances[1].EnvironmentsText);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Environment_pickers_screenshot()
    {
        using var a = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(a, rows: 2), null);
        window.Width = 1200; window.Height = 700;
        window.Shell.EnvironmentSettings = new FakeSettings("local", "development", "staging", "production");
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var button = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Button>().First(x => x.Classes.Contains("treeenv"));
        button.Flyout!.ShowAt(button);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-environments.png"));
        window.Close();
    }

    [Fact]
    public async Task Parallel_is_saved_globally_and_each_instance_can_differ_for_its_own_runs()
    {
        using var a = new TempDir(); using var b = new TempDir();
        var (shell, settings, runs) = Setup(a, b);
        shell.GlobalRunParallel = true;
        Assert.True(settings.Parallel);
        Assert.All(shell.Instances, i => Assert.True(i.RunParallel));

        shell.Instances[1].RunParallel = false;
        Assert.False(shell.GlobalRunParallel);

        await shell.RunNodeAsync(shell.Instances[0]);
        Assert.True(runs[^1].TestcasesParallel);
        await shell.RunNodeAsync(shell.Instances[1]);
        Assert.False(runs[^1].TestcasesParallel);
        await shell.RunNodeAsync(shell.Instances[0].Testcases[0]);
        Assert.True(runs[^1].TestcasesParallel);
    }
}

public class SelectionPersistenceTests
{
    private sealed class Settings : IEnvironmentSettings
    {
        public Dictionary<string, (string Testcases, string Steps)> Selection { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Writes { get; private set; }
        public IReadOnlyList<string> Environments { get; } = [];
        public IReadOnlyList<string> GetSelection(string instanceName) => [];
        public bool RunParallel => false;
        public string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections) => null;
        public string? SaveRunParallel(bool value) => null;
        public (string Testcases, string Steps) GetRunSelection(string instanceName) => Selection.GetValueOrDefault(instanceName, ("0", string.Empty));
        public string? SaveRunSelection(string instanceName, string testcases, string stepsJson)
        {
            Writes++;
            Selection[instanceName] = (testcases, stepsJson);
            return null;
        }
    }

    // Testcase 1 "Customers" (CreateCustomer) and testcase 2 "Orders" (CreateOrder); testcase 1 gets a second step.
    private static (EditorShellViewModel Shell, Settings Settings) Open(TempDir dir, Action<Settings>? seed = null)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var workbook = new XLWorkbook(path))
        {
            var config = workbook.Worksheet("config");
            config.Cell(3, 1).Value = 2; config.Cell(3, 2).Value = "Orders"; config.Cell(3, 3).Value = "CreateOrder"; config.Cell(3, 4).Value = "HTTP";
            config.Cell(3, 5).Value = "order.json"; config.Cell(3, 6).Value = "https://api.example.test/orders"; config.Cell(3, 7).Value = "POST";
            workbook.Worksheet("request").Cell(1, 8).Value = "CreateOrder.sku";
            workbook.Worksheet("response").Cell(1, 7).Value = "{eq}CreateOrder.statusCode";
            workbook.Save();
        }
        var settings = new Settings();
        seed?.Invoke(settings);
        var shell = new EditorShellViewModel([("Sample", path)]) { EnvironmentSettings = settings };
        return (shell, settings);
    }

    [Fact]
    public void Saved_selection_is_ticked_when_the_editor_opens()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir, s => s.Selection["Sample"] = ("2", string.Empty));
        var testcases = shell.Instances[0].Testcases;
        Assert.False(testcases.First(t => t.Index == 1).IsChosen);
        Assert.True(testcases.First(t => t.Index == 2).IsChosen);
        Assert.True(testcases.First(t => t.Index == 2).StepNodes.All(n => n.IsChosen));
        Assert.Equal(0, settings.Writes); // loading does not write back
    }

    [Fact]
    public void Everything_is_ticked_for_the_default_selection()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir);
        Assert.All(shell.Instances[0].Testcases, t => Assert.True(t.IsChosen));
        Assert.Equal(2, shell.TickedCount);
    }

    [Fact]
    public void Unticking_a_testcase_saves_the_remaining_ones()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir);
        shell.Instances[0].Testcases.First(t => t.Index == 1).ChosenState = false;
        Assert.Equal(("2", string.Empty), settings.Selection["Sample"]);
        shell.Instances[0].Testcases.First(t => t.Index == 1).ChosenState = true;
        Assert.Equal(("0", string.Empty), settings.Selection["Sample"]); // everything again
    }

    [Fact]
    public void Unticking_a_step_saves_the_step_list()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir);
        var vm = shell.Instances[0].Editor!;
        vm.SelectedTestcase = vm.Testcases.First(t => t.Index == 1);
        vm.AddStepCommand.Execute(null);
        var customers = vm.Testcases.First(t => t.Index == 1);
        var writes = settings.Writes;
        customers.StepNodes[^1].IsChosen = false;
        Assert.Equal(writes + 1, settings.Writes);
        var saved = settings.Selection["Sample"];
        Assert.Equal("0", saved.Testcases);
        var steps = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string[]>>(saved.Steps)!;
        Assert.Equal(["CreateCustomer"], steps[1]);
        Assert.Equal(["CreateOrder"], steps[2]);
    }

    [Fact]
    public void The_instance_checkbox_saves_once_and_nothing_ticked_keeps_the_last_selection()
    {
        using var dir = new TempDir();
        var (shell, settings) = Open(dir, s => s.Selection["Sample"] = ("1", string.Empty));
        var instance = shell.Instances[0];
        instance.ChosenState = true;
        Assert.Equal(1, settings.Writes);
        Assert.Equal("0", settings.Selection["Sample"].Testcases);

        instance.ChosenState = false;
        Assert.Equal(1, settings.Writes);              // nothing to save for an empty selection
        Assert.Equal("0", settings.Selection["Sample"].Testcases);
        Assert.False(shell.HasTicked);
    }
}
