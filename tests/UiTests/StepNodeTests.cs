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
