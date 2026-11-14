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
