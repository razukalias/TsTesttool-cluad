using Avalonia.Headless;
using ClosedXML.Excel;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class TestcaseActionTests
{
    // Testcase 1 "Customers" (CreateCustomer, 3 rows) plus testcase 2 "Orders" (CreateOrder, 1 row).
    private static string Build(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using var workbook = new XLWorkbook(path);
        var config = workbook.Worksheet("config");
        config.Cell(3, 1).Value = 2; config.Cell(3, 2).Value = "Orders"; config.Cell(3, 3).Value = "CreateOrder"; config.Cell(3, 4).Value = "HTTP";
        config.Cell(3, 5).Value = "order.json"; config.Cell(3, 6).Value = "https://api.example.test/orders"; config.Cell(3, 7).Value = "POST";
        var request = workbook.Worksheet("request");
        request.Cell(1, 8).Value = "CreateOrder.sku";
        request.Cell(5, 1).Value = 2; request.Cell(5, 2).Value = "Orders"; request.Cell(5, 3).Value = "ORD001"; request.Cell(5, 8).Value = "SKU-1";
        var response = workbook.Worksheet("response");
        response.Cell(1, 7).Value = "{eq}CreateOrder.statusCode";
        response.Cell(5, 1).Value = 2; response.Cell(5, 2).Value = "Orders"; response.Cell(5, 3).Value = "ORD001"; response.Cell(5, 7).Value = "201";
        workbook.Save();
        return path;
    }

    private static void Select(DataEditorViewModel vm, int index) => vm.SelectedTestcase = vm.Testcases.First(t => t.Index == index);

    [Fact]
    public void Rename_changes_the_name_in_the_tree_and_saves()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var vm = new DataEditorViewModel(path);
        Select(vm, 2);

        vm.OpenRenameTestcaseCommand.Execute(null);
        vm.TestcasePanelName = "Purchases";
        vm.ConfirmTestcaseActionCommand.Execute(null);

        Assert.Equal("2 · Purchases", vm.SelectedTestcase!.Title);
        Assert.True(vm.IsDirty);
        vm.SaveCommand.Execute(null);
        Assert.Equal(["Customers", "Purchases"], new WorkbookReader().Read(path).Testcases.Select(t => t.Testcase));
    }

    [Fact]
    public void Copy_adds_a_selected_testcase_with_its_rows_and_undo_removes_it()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Build(dir));
        Select(vm, 1);

        vm.OpenCopyTestcaseCommand.Execute(null);
        Assert.Equal("Customers copy", vm.TestcasePanelName);
        vm.ConfirmTestcaseActionCommand.Execute(null);

        Assert.Equal(3, vm.Testcases.Count);
        Assert.Equal("3 · Customers copy", vm.SelectedTestcase!.Title);
        Assert.Equal(3, vm.VisibleRows.Count);
        Assert.Contains(vm.Columns, c => c.Title == "CreateCustomerCopy.email");
        Assert.Contains("Copied testcase 1 as 3", vm.StatusMessage);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Testcases.Count);
    }

    [Fact]
    public void Move_changes_run_order_and_the_selection_follows_the_moved_testcase()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var vm = new DataEditorViewModel(path);
        Select(vm, 2);

        vm.MoveTestcaseUpCommand.Execute(null);

        Assert.Equal(["Orders", "Customers"], vm.Testcases.Select(t => t.Name));
        Assert.Equal("Orders", vm.SelectedTestcase!.Name);
        vm.MoveTestcaseUpCommand.Execute(null);
        Assert.True(vm.StatusIsError || vm.StatusMessage.Contains("first"));
        vm.SaveCommand.Execute(null);
        Assert.Equal(["Orders", "Customers"], new WorkbookReader().Read(path).Testcases.Select(t => t.Testcase));
    }

    [Fact]
    public void Delete_asks_first_removes_the_testcase_and_selects_a_neighbour()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var vm = new DataEditorViewModel(path);
        Select(vm, 2);

        vm.OpenDeleteTestcaseCommand.Execute(null);
        Assert.True(vm.TestcasePanelVisible);
        Assert.Contains("1 data row", vm.TestcasePanelNote);
        Assert.Equal(2, vm.Testcases.Count); // nothing happens until confirmed
        vm.ConfirmTestcaseActionCommand.Execute(null);

        Assert.Single(vm.Testcases);
        Assert.Equal("Customers", vm.SelectedTestcase!.Name);
        vm.SaveCommand.Execute(null);
        Assert.Single(new WorkbookReader().Read(path).Testcases);
    }

    [Fact]
    public void Steps_can_be_moved_up_and_down()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Build(dir));
        Select(vm, 1);
        vm.AddStepCommand.Execute(null);
        var added = vm.SelectedStep!.CurrentName;
        vm.ShowSteps = true;

        vm.MoveStepUpCommand.Execute(null);

        Assert.Equal([added, "CreateCustomer"], vm.StepRows.Select(s => s.CurrentName));
        Assert.Equal(added, vm.SelectedStep!.CurrentName);
        vm.MoveStepUpCommand.Execute(null);
        Assert.Contains("already runs first", vm.StatusMessage);
    }

    [Fact]
    public void The_shell_selection_follows_a_copied_testcase()
    {
        using var dir = new TempDir();
        var shell = new EditorShellViewModel([("Sample", Build(dir))]);
        var editor = shell.Current!;
        shell.SelectedNode = editor.Testcases.First(t => t.Index == 1);

        editor.OpenCopyTestcaseCommand.Execute(null);
        editor.ConfirmTestcaseActionCommand.Execute(null);

        Assert.True(shell.TestcaseNodeSelected);
        Assert.Equal(3, ((TestcaseItem)shell.SelectedNode!).Index);
    }
}

public class TestcaseActionWindowTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_delete_panel_shows_what_will_be_removed_and_cancel_closes_it()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 4), "Customers");
        window.Width = 1440; window.Height = 860;
        window.Show();
        var vm = window.ViewModel;

        vm.OpenDeleteTestcaseCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-delete-testcase.png"));
        Assert.True(vm.TestcasePanelVisible);

        vm.CloseTestcasePanelCommand.Execute(null);
        Assert.False(vm.TestcasePanelVisible);
        window.Close();
    }
}
