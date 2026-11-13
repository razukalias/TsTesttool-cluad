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

    private static string RenameSample(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        workbook.Worksheet("request").Cell(2, 4).Value = "CreateCustomer, Other";           // dontrun
        workbook.Worksheet("config").Cell("F2").Value = "https://api.example.test/<From_response_CreateCustomer.id_2>";
        var response = workbook.Worksheet("response");
        response.Cell(1, 7).Value = "extractvariable";
        response.Cell(2, 7).Value = "CreateCustomer.id:custId|Other.x:y";
        workbook.AddWorksheet("notes").Cell("A1").Value = "see <From_response_CreateCustomer.id_2>";
        workbook.Save();
        return path;
    }

    [Fact]
    public void Renaming_an_existing_step_carries_its_columns_references_dontrun_and_extract_mappings_along()
    {
        using var dir = new TempDir();
        var path = RenameSample(dir);
        var vm = new DataEditorViewModel(path);

        vm.StepRows[0].Cells[0] = "MakeCustomer";

        Assert.False(vm.StatusIsError);
        Assert.Contains("Renamed step CreateCustomer to MakeCustomer", vm.StatusMessage);
        Assert.Contains("still use the old name", vm.StatusMessage); // the notes sheet
        vm.SaveCommand.Execute(null);

        using var workbook = new ClosedXML.Excel.XLWorkbook(path);
        Assert.Equal("MakeCustomer", workbook.Worksheet("config").Cell("C2").GetString());
        Assert.Equal("https://api.example.test/<From_response_MakeCustomer.id_2>", workbook.Worksheet("config").Cell("F2").GetString());
        var request = workbook.Worksheet("request");
        Assert.Equal("MakeCustomer.email", request.Cell(1, 6).GetString());
        Assert.Equal("MakeCustomer, Other", request.Cell(2, 4).GetString());
        var response = workbook.Worksheet("response");
        Assert.Equal("{eq}MakeCustomer.statusCode", response.Cell(1, 4).GetString());
        Assert.Equal("MakeCustomer.id:custId|Other.x:y", response.Cell(2, 7).GetString());
        Assert.Equal("see <From_response_CreateCustomer.id_2>", workbook.Worksheet("notes").Cell("A1").GetString());
        Assert.Equal("customer1@example.test", request.Cell(2, 6).GetString());
    }

    [Fact]
    public void A_rename_that_would_clash_or_use_a_forbidden_character_is_refused_without_changes()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(RenameSample(dir));
        vm.AddStepCommand.Execute(null);
        var other = vm.StepRows[1].CurrentName;

        vm.StepRows[0].Cells[0] = other;
        Assert.True(vm.StatusIsError);
        Assert.Equal("CreateCustomer", vm.StepRows[0].Cells[0]);

        vm.StepRows[0].Cells[0] = "Bad.Name";
        Assert.True(vm.StatusIsError);
        Assert.Equal("CreateCustomer", vm.StepRows[0].Cells[0]);
        Assert.Contains(vm.Columns, c => c.Title == "CreateCustomer.email");
    }

    [Fact]
    public void Undo_restores_the_old_step_name_and_its_columns()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(RenameSample(dir));
        vm.ShowSteps = true;
        vm.StepRows[0].Cells[0] = "MakeCustomer";
        vm.ShowSteps = false;
        Assert.Contains(vm.Columns, c => c.Title == "MakeCustomer.email");

        vm.UndoCommand.Execute(null);

        Assert.Contains(vm.Columns, c => c.Title == "CreateCustomer.email");
        Assert.Equal("CreateCustomer", vm.StepRows[0].Cells[0]);
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
