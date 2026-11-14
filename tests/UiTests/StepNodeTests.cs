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
    public void Choosing_a_step_node_narrows_the_data_grid_to_that_step_and_keeps_the_selection()
    {
        using var dir = new TempDir();
        var shell = Shell(dir, out _);
        var testcase = shell.Instances[0].Testcases[0];
        var node = testcase.StepNodes[0];
        shell.SelectedNode = node;
        var vm = shell.Current!;
        Assert.Equal(node.Name, vm.StepFilter);
        Assert.False(vm.ShowSteps);
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

public class ExecutionModeTests
{
    [Fact]
    public async Task Instance_and_global_dropdowns_set_how_runs_start()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var shell = new EditorShellViewModel([("Sample", path)]);
        var runs = new List<RunRequest>();
        shell.Instances[0].Editor!.RunHandler = r => { runs.Add(r); return Task.FromResult<string?>(null); };
        var instance = shell.Instances[0];

        Assert.Equal("Instance setting", shell.AllRowsMode);
        shell.AllRowsMode = "Sequential";
        shell.AllTestcaseOrder = "At the same time";
        Assert.Equal("Sequential", instance.RowsMode);
        await shell.RunNodeAsync(instance);
        Assert.Equal("sequential", runs[^1].RowsMode);
        Assert.True(runs[^1].TestcasesParallel);

        instance.RowsMode = "Parallel";
        Assert.Equal("Parallel", shell.AllRowsMode);
        await shell.RunNodeAsync(instance.Testcases[0]);
        Assert.Equal("parallel", runs[^1].RowsMode);
    }
}
