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
        Assert.All(vm.Columns, c => Assert.Contains("CreateCustomer.", c.Title));
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
}
