using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class EditorShellTests
{
    internal static (EditorShellViewModel Shell, string[] Paths) OpenForInstanceView(TempDir dir) => Open(dir);

    private static (EditorShellViewModel Shell, string[] Paths) Open(TempDir dir)
    {
        var a = Samples.Copy(dir, Samples.IntegrationScenarios);
        var b = Samples.CreateLarge(dir, rows: 40);
        return (new EditorShellViewModel([("DataEngine_IntegrationScenarios", a), ("Customers", b)], b), [a, b]);
    }

    [Fact]
    public void The_tree_hides_the_DataEngine_prefix_but_the_real_name_stays()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir);
        Assert.Equal("IntegrationScenarios", shell.Instances[0].DisplayName);
        Assert.Equal("DataEngine_IntegrationScenarios", shell.Instances[0].Name);
        Assert.Equal("Customers", shell.Instances[1].DisplayName);
        Assert.Equal("DataEngine_", new InstanceNode("DataEngine_", "x").DisplayName);   // nothing left to show: keep the name
    }

    [Fact]
    public void Instances_are_parents_and_open_on_the_requested_one()
    {
        using var dir = new TempDir();
        var (shell, paths) = Open(dir);

        Assert.Equal(["DataEngine_IntegrationScenarios", "Customers"], shell.Instances.Select(i => i.Name));
        Assert.Equal(4, shell.Instances[0].Testcases.Count);
        Assert.Equal(paths[1], shell.Current!.WorkbookPath);
        Assert.IsType<TestcaseItem>(shell.SelectedNode);
        Assert.Equal(40, shell.Current.VisibleRows.Count);
    }

    [Fact]
    public void Choosing_a_testcase_of_another_instance_switches_the_editor()
    {
        using var dir = new TempDir();
        var (shell, paths) = Open(dir);

        shell.SelectedNode = shell.Instances[0].Testcases[1];

        Assert.Equal(paths[0], shell.Current!.WorkbookPath);
        Assert.Equal("GQL001", Assert.Single(shell.Current.VisibleRows).Cells[0]);
    }

    [Fact]
    public void Each_instance_keeps_its_own_unsaved_edits_and_a_dirty_marker()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir);
        var customers = shell.Current!;
        customers.VisibleRows[0].Cells[customers.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex] = "Changed";
        Assert.True(shell.Instances[1].IsDirty);

        shell.SelectedNode = shell.Instances[0];
        Assert.False(shell.Instances[0].IsDirty);
        Assert.True(customers.IsDirty);
        shell.SelectedNode = shell.Instances[1];
        Assert.Equal("Changed", customers.VisibleRows[0].Cells[customers.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex]);
    }

    [Fact]
    public void Closing_asks_about_each_dirty_instance_then_closes()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir);
        var closed = 0;
        shell.CloseRequested += (_, _) => closed++;
        Assert.True(shell.RequestClose());

        foreach (var node in shell.Instances)
        {
            var editor = node.Editor!;
            editor.SelectedTestcase = editor.Testcases[0];
            editor.VisibleRows[0].Cells[editor.Columns.First(c => c.Title.EndsWith(".statusCode") || c.Title.EndsWith(".name")).CellIndex] = "x";
        }

        Assert.False(shell.RequestClose());
        Assert.True(shell.Instances[0].Editor!.ClosePromptVisible);
        shell.Instances[0].Editor!.DiscardAndCloseCommand.Execute(null);
        Assert.Equal(0, closed);
        Assert.Same(shell.Instances[1].Editor, shell.Current);
        shell.Instances[1].Editor!.DiscardAndCloseCommand.Execute(null);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void An_instance_that_cannot_be_opened_is_listed_with_its_error()
    {
        using var dir = new TempDir();
        var good = Samples.CreateLarge(dir, rows: 3);
        var shell = new EditorShellViewModel([("Broken", dir.Combine("missing.xlsx")), ("Good", good)]);

        Assert.True(shell.Instances[0].HasError);
        Assert.Contains("Cannot open", shell.Instances[0].Subtitle);
        Assert.Equal(good, shell.Current!.WorkbookPath);
    }

    [AvaloniaFact]
    public void Renders_the_instance_tree()
    {
        using var dir = new TempDir();
        var a = Samples.Copy(dir, Samples.IntegrationScenarios);
        var b = Samples.Copy(dir, Samples.FileHttpScenarios);
        var c = Samples.CreateLarge(dir);
        var window = DataEditorWindow.Open([("DataEngine_IntegrationScenarios", a), ("FileHttpScenarios", b), ("Customers", c)], c);
        window.Width = 1440;
        window.Height = 860;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.FindControl<DataGrid>("Grid")!.SelectedItem);
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.GetTempPath();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-tree.png"));
        window.Close();
    }
}

public sealed class InstanceViewTests
{
    [Fact]
    public void Clicking_the_instance_shows_every_testcase_in_the_table_and_every_step_in_config_edit_mode()
    {
        using var dir = new TempDir();
        var (shell, _) = EditorShellTests.OpenForInstanceView(dir);
        var editor = shell.Instances[0].Editor!;
        var first = shell.Instances[0].Testcases[0];
        shell.SelectedNode = first;
        var oneTestcaseRows = editor.VisibleRows.Count;
        var oneTestcaseSteps = editor.StepRows.Count;

        shell.SelectedNode = shell.Instances[0];

        Assert.True(editor.ShowInstance);
        Assert.Null(editor.SelectedTestcase);
        Assert.True(editor.VisibleRows.Count > oneTestcaseRows);
        Assert.Equal(shell.Instances[0].Testcases.Sum(t => t.RowCount), editor.VisibleRows.Count);
        Assert.Equal(shell.Instances[0].Testcases.Sum(t => t.Steps.Count), editor.StepRows.Count);
        Assert.True(editor.StepRows.Count > oneTestcaseSteps);
        Assert.Equal(shell.Instances[0].Testcases.Select(t => t.Index).Distinct().Count(), editor.VisibleRows.Select(r => r.TestcaseIndex).Distinct().Count());

        shell.SelectedNode = first;   // choosing a testcase leaves the instance view
        Assert.False(editor.ShowInstance);
        Assert.Equal(oneTestcaseRows, editor.VisibleRows.Count);
    }

    [Fact]
    public void Editing_a_cell_in_the_instance_view_changes_the_row_of_its_own_testcase()
    {
        using var dir = new TempDir();
        var (shell, _) = EditorShellTests.OpenForInstanceView(dir);
        var editor = shell.Instances[0].Editor!;
        shell.SelectedNode = shell.Instances[0];
        var last = editor.VisibleRows[^1];
        var cell = editor.Columns.First(c => c.Group == ColumnGroup.Request && !c.Title.StartsWith("testcase", StringComparison.OrdinalIgnoreCase));

        last.Cells[cell.CellIndex] = "changed-in-instance-view";

        Assert.Equal("changed-in-instance-view", last.Request.GetAt(cell.SheetColumn));
        Assert.True(editor.IsDirty);
    }
}
