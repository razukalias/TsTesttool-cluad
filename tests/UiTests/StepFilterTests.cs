using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class StepFilterTests
{
    private static string WithSecondStepColumns(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using var workbook = new XLWorkbook(path);
        var request = workbook.Worksheet("request");
        var col = request.LastColumnUsed()!.ColumnNumber() + 1;
        request.Cell(1, col).Value = "OtherStep.note";
        request.Cell(2, col).Value = "hello";
        workbook.Save();
        return path;
    }

    [Fact]
    public void Choosing_a_step_narrows_the_grid_to_that_steps_columns_and_all_steps_restores_it()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(WithSecondStepColumns(dir));
        Assert.Equal(DataEditorViewModel.AllSteps, vm.StepFilter);
        Assert.Contains("CreateCustomer", vm.StepFilterOptions);
        var all = vm.Columns.Count;

        vm.StepFilter = "CreateCustomer";
        Assert.All(vm.Columns.Where(c => c.Title.Contains('.')), c => Assert.Contains("CreateCustomer.", c.Title));   // columns of a step; shared ones (no dot) stay
        Assert.True(vm.Columns.Count < all);

        vm.StepFilter = DataEditorViewModel.AllSteps;
        Assert.Equal(all, vm.Columns.Count);
    }

    [Fact]
    public void Show_its_columns_from_the_steps_tab_filters_and_opens_the_data_tab()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(WithSecondStepColumns(dir));
        vm.ShowSteps = true;
        vm.SelectedStep = vm.StepRows[0];

        vm.ShowStepDataCommand.Execute(null);

        Assert.False(vm.ShowSteps);
        Assert.Equal("CreateCustomer", vm.StepFilter);
        Assert.DoesNotContain(vm.Columns, c => c.Title.StartsWith("OtherStep"));
        Assert.NotEmpty(vm.VisibleRows);
    }

    [AvaloniaFact]
    public void Picking_a_step_in_the_box_does_not_rebuild_its_list_while_it_is_selecting()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(WithSecondStepColumns(dir));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.Shell.Current!;
        var box = window.GetVisualDescendants().OfType<ComboBox>().First(b => ReferenceEquals(b.ItemsSource, vm.StepFilterOptions));
        var changes = 0;
        vm.StepFilterOptions.CollectionChanged += (_, _) => changes++;

        box.SelectedItem = "OtherStep";
        Dispatcher.UIThread.RunJobs();
        box.SelectedItem = DataEditorViewModel.AllSteps;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, changes);                               // the list was left alone
        Assert.Equal(DataEditorViewModel.AllSteps, vm.StepFilter);
    }

    [Fact]
    public void Shared_columns_stay_in_the_grid_when_one_step_is_chosen()
    {
        using var dir = new TempDir();
        var path = WithSecondStepColumns(dir);
        using (var workbook = new XLWorkbook(path))
        {
            var request = workbook.Worksheet("request");
            var col = request.LastColumnUsed()!.ColumnNumber() + 1;
            request.Cell(1, col).Value = "region"; request.Cell(2, col).Value = "north";
            workbook.Save();
        }
        var vm = new DataEditorViewModel(path);
        vm.StepFilter = "CreateCustomer";
        Assert.Contains(vm.Columns, c => c.Title == "CreateCustomer.name");
        Assert.Contains(vm.Columns, c => c.Title == "region");               // shared: belongs to every step
        Assert.DoesNotContain(vm.Columns, c => c.Title.StartsWith("OtherStep."));
    }

    [Fact]
    public void Choosing_a_testcase_or_the_instance_puts_the_step_box_back_to_all_steps()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(WithSecondStepColumns(dir));
        vm.StepFilter = "CreateCustomer";
        vm.ShowWholeInstance();
        Assert.Equal(DataEditorViewModel.AllSteps, vm.StepFilter);

        vm.StepFilter = "CreateCustomer";
        vm.SelectedTestcase = vm.Testcases[0];
        Assert.Equal(DataEditorViewModel.AllSteps, vm.StepFilter);
        Assert.Contains(vm.Columns, c => c.Title.StartsWith("OtherStep."));
    }
}
