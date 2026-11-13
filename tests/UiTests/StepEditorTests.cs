using ClosedXML.Excel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class StepEditorTests
{
    private static int StepCell(DataEditorViewModel vm, string title) => vm.StepColumns.First(c => c.Title == title).CellIndex;

    [Fact]
    public void Lists_the_steps_of_the_selected_testcase_with_their_config_columns()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.Copy(dir, Samples.IntegrationScenarios));

        Assert.Equal(["FetchUser", "CreatePost"], vm.StepRows.Select(s => s.Cells[0]));
        Assert.DoesNotContain(vm.StepColumns, c => c.Title is "testcaseindex" or "testcase" or "stepname");
        var first = vm.StepRows[0];
        Assert.Equal("HTTP", first.Cells[StepCell(vm, "steptype")]);
        Assert.Equal("https://jsonplaceholder.typicode.com/users/1", first.Cells[StepCell(vm, "targetUrl")]);
        Assert.Equal("True", first.Cells[StepCell(vm, "enabled")]);

        vm.SelectedTestcase = vm.Testcases[1];
        Assert.Equal("QueryCountry", vm.StepRows[0].Cells[0]);
    }

    [Fact]
    public void Editing_a_step_cell_is_saved_and_read_by_the_runner()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);

        vm.StepRows[1].Cells[StepCell(vm, "targetUrl")] = "https://example.test/posts";
        vm.StepRows[1].Cells[StepCell(vm, "timeout")] = "42";
        vm.StepRows[1].Cells[StepCell(vm, "stage")] = "Teardown";
        Assert.True(vm.IsDirty);
        vm.SaveCommand.Execute(null);

        var step = new WorkbookReader().Read(path).Testcases.SelectMany(t => t.Steps).First(s => s.StepName == "CreatePost");
        Assert.Equal("https://example.test/posts", step.TargetUrl);
        Assert.Equal(42, step.TimeoutSeconds);
        Assert.Equal(LoadTestingTool.Domain.ExecutionStage.Teardown, step.Stage);
    }

    [Fact]
    public void Existing_step_names_cannot_be_renamed()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.Copy(dir, Samples.IntegrationScenarios));

        vm.StepRows[0].Cells[0] = "Other";

        Assert.Equal("FetchUser", vm.StepRows[0].Cells[0]);
        Assert.True(vm.StatusIsError);
        Assert.Contains("cannot be renamed", vm.StatusMessage);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void A_new_step_can_be_named_and_is_saved_and_runnable()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);

        vm.AddStepCommand.Execute(null);
        var added = vm.SelectedStep!;
        Assert.True(added.IsNew);
        Assert.Equal("Step3", added.Cells[0]);

        added.Cells[0] = "DeletePost";
        added.Cells[StepCell(vm, "targetUrl")] = "https://example.test/posts/1";
        added.Cells[StepCell(vm, "verb")] = "DELETE";
        added.Cells[0] = "FetchUser"; // taken
        Assert.Equal("DeletePost", added.Cells[0]);
        Assert.Contains("already exists", vm.StatusMessage);
        Assert.Contains("DeletePost", vm.SelectedTestcase!.Steps);

        Assert.Contains(vm.Problems, p => p.IsError && p.Message.Contains("templetsource"));
        added.Cells[StepCell(vm, "templetsource")] = "delete-post.json";
        Assert.DoesNotContain(vm.Problems, p => p.IsError && p.Message.Contains("templetsource"));

        vm.SaveCommand.Execute(null);
        var model = new WorkbookReader().Read(path);
        var step = model.Testcases.SelectMany(t => t.Steps).First(s => s.StepName == "DeletePost");
        Assert.Equal(1, step.TestcaseIndex);
        Assert.Equal("https://example.test/posts/1", step.TargetUrl);
    }

    [Fact]
    public void Removing_a_step_clears_it_and_keeps_the_other_rows_where_they_are()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);
        vm.SelectedStep = vm.StepRows[1];

        vm.RemoveStepCommand.Execute(null);

        Assert.Equal(["FetchUser"], vm.StepRows.Select(s => s.Cells[0]));
        Assert.Contains("data columns remain", vm.StatusMessage);
        vm.SaveCommand.Execute(null);
        var names = new WorkbookReader().Read(path).Testcases.SelectMany(t => t.Steps).Select(s => s.StepName).ToList();
        Assert.DoesNotContain("CreatePost", names);
        Assert.Contains("QueryCountry", names);
    }

    [Fact]
    public void Adding_a_step_changes_which_data_columns_are_shown_once_you_return_to_the_data_tab()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        vm.OpenStepsTabCommand.Execute(null);
        vm.AddStepCommand.Execute(null);
        vm.SelectedStep!.Cells[0] = "Extra";

        vm.OpenDataTabCommand.Execute(null);

        Assert.True(vm.ShowData);
        Assert.Contains("Extra", vm.SelectedTestcase!.Steps);
    }

    [Fact]
    public void A_config_problem_jumps_to_the_step_on_the_steps_tab()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var workbook = new XLWorkbook(path))
        {
            workbook.Worksheet("config").Cell(2, 6).Clear(); // targetUrl of the only step
            workbook.Save();
        }
        var vm = new DataEditorViewModel(path);
        var problem = vm.Problems.First(p => p.Problem.Sheet == "config");

        vm.SelectedProblem = problem;

        Assert.True(vm.ShowSteps);
        Assert.Equal("CreateCustomer", vm.SelectedStep!.Cells[0]);
        vm.SelectedStep.Cells[StepCell(vm, "targetUrl")] = "https://api.example.test/customers";
        Assert.DoesNotContain(vm.Problems, p => p.Problem.Sheet == "config" && p.IsError);
    }

    [AvaloniaFact]
    public void Renders_the_steps_tab()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.Copy(dir, Samples.IntegrationScenarios), "DataEngine_IntegrationScenarios");
        window.Width = 1440;
        window.Height = 860;
        window.Show();
        window.ViewModel.OpenStepsTabCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.FindControl<Avalonia.Controls.DataGrid>("StepsGrid")!.SelectedItem);

        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.GetTempPath();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-steps.png"));
        window.Close();
    }
}
