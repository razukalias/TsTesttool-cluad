using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class PreviewAndRunTests
{
    private static DataEditorViewModel Open(TempDir dir, bool withTemplate = true)
    {
        var path = Samples.CreateLarge(dir, rows: 5);
        if (withTemplate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dir.Combine("Templates/create.json"))!);
            File.WriteAllText(dir.Combine("Templates/create.json"), "{ \"name\": \"_name_\", \"email\": \"_email_\", \"country\": \"_country_\" }");
        }
        var vm = new DataEditorViewModel(path);
        vm.PreviewVisible = true;
        return vm;
    }

    [Fact]
    public void Preview_fills_the_template_with_the_selected_rows_values()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        vm.SelectedRow = vm.VisibleRows[1];

        var preview = Assert.IsType<RequestPreview>(vm.Preview);
        Assert.Equal("CreateCustomer", preview.StepName);
        Assert.Equal("POST", preview.Verb);
        Assert.Equal("https://api.example.test/customers", preview.Url);
        Assert.Contains("\"name\": \"Customer 2\"", preview.Body);
        Assert.Contains("customer2@example.test", preview.Body);
        Assert.False(preview.HasWarning);
    }

    [Fact]
    public void Preview_follows_unsaved_edits_and_the_selected_row()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        vm.VisibleRows[0].Cells[vm.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex] = "Edited";
        Assert.Contains("\"name\": \"Edited\"", vm.Preview!.Body);

        vm.SelectedRow = vm.VisibleRows[3];
        Assert.Contains("Customer 4", vm.Preview!.Body);
    }

    [Fact]
    public void Preview_warns_when_the_template_is_missing()
    {
        using var dir = new TempDir();
        var vm = Open(dir, withTemplate: false);
        Assert.True(vm.Preview!.HasWarning);
        Assert.Contains("create.json", vm.Preview.Warning);
    }

    [Fact]
    public void Preview_lists_placeholders_that_have_no_data_column()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        File.WriteAllText(dir.Combine("Templates/create.json"), "{ \"name\": \"_name_\", \"token\": \"_authToken_\" }");
        vm.RefreshPreview();
        Assert.Contains("_authToken_", vm.Preview!.Warning);
        Assert.DoesNotContain("_name_", vm.Preview.Warning);
    }

    [Fact]
    public void Edit_template_hands_the_view_the_steps_template_and_a_save_callback_that_repoints_the_step()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        TemplateEditRequest? request = null;
        vm.EditTemplateRequested += (_, r) => request = r;

        vm.EditTemplateCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal("create.json", request!.Source);
        Assert.Equal("CreateCustomer", request.Step);
        Assert.EndsWith("Templates", request.TemplatesFolder);
        File.WriteAllText(dir.Combine("Templates/create-v2.json"), "{ \"name\": \"V2 _name_\" }");
        request.Saved("create-v2.json");

        Assert.True(vm.IsDirty);
        Assert.Contains("\"name\": \"V2 Customer 1\"", vm.Preview!.Body);
        Assert.Equal("create-v2.json", vm.StepRows[0].Row.Get("templetsource"));
    }

    [Fact]
    public async Task Run_testcase_asks_the_host_for_every_row_and_run_row_for_the_selected_one()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var calls = new List<string>();
        vm.RunHandler = r => { calls.Add($"{string.Join("+", r.Testcases)}|{string.Join(",", r.DataIds)}"); return Task.FromResult<string?>(null); };

        await vm.RunTestcaseCommand.ExecuteAsync(null);
        vm.SelectedRow = vm.VisibleRows[2];
        await vm.RunRowCommand.ExecuteAsync(null);

        Assert.Equal(["1|", "1|CUST003"], calls);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public async Task Run_options_cover_chosen_testcases_and_the_whole_instance_with_their_modes()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);
        var requests = new List<RunRequest>();
        vm.RunHandler = r => { requests.Add(r); return Task.FromResult<string?>(null); };

        vm.RunScope = DataEditorViewModel.ScopeInstance;
        vm.TestcaseOrder = DataEditorViewModel.TestcaseOrderOptions[1];
        vm.RowsMode = "Sequential";
        await vm.RunCommand.ExecuteAsync(null);

        vm.RunScope = DataEditorViewModel.ScopeChosen;
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Contains("Tick at least one", vm.StatusMessage);
        vm.Testcases[1].IsChosen = true;
        vm.Testcases[3].IsChosen = true;
        Assert.Contains("2 chosen testcases, at the same time", vm.RunSummary);
        await vm.RunCommand.ExecuteAsync(null);

        vm.RunScope = DataEditorViewModel.ScopeTestcase;
        vm.RowsMode = "Instance setting";
        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(3, requests.Count);
        Assert.Empty(requests[0].Testcases);
        Assert.Equal("sequential", requests[0].RowsMode);
        Assert.True(requests[0].TestcasesParallel);
        Assert.Equal([2, 4], requests[1].Testcases);
        Assert.Equal([1], requests[2].Testcases);
        Assert.Null(requests[2].RowsMode);
        Assert.Null(requests[2].TestcasesParallel);
    }

    [Fact]
    public async Task A_run_is_refused_while_there_are_unsaved_changes_and_a_host_error_is_shown()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var started = 0;
        vm.RunHandler = _ => { started++; return Task.FromResult<string?>("Customers is already running; stop it first."); };

        vm.VisibleRows[0].Cells[vm.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex] = "Edited";
        await vm.RunTestcaseCommand.ExecuteAsync(null);
        Assert.Equal(0, started);
        Assert.Contains("Save your changes first", vm.StatusMessage);

        vm.SaveCommand.Execute(null);
        await vm.RunTestcaseCommand.ExecuteAsync(null);
        Assert.Equal(1, started);
        Assert.True(vm.StatusIsError);
        Assert.Contains("already running", vm.StatusMessage);
    }
}

public class PreviewWindowTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Window_shows_the_preview_panel_and_run_buttons()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 12);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dir.Combine("Templates/create.json"))!);
        File.WriteAllText(dir.Combine("Templates/create.json"), "{\n  \"name\": \"_name_\",\n  \"email\": \"_email_\",\n  \"country\": \"_country_\"\n}");
        var window = DataEditorWindow.Open(path, "Customers");
        window.Shell.RunHandler = (_, _) => Task.FromResult<string?>(null);
        window.Width = 1440; window.Height = 860;
        window.Show();
        window.ViewModel.PreviewVisible = true;
        window.ViewModel.SelectedRow = window.ViewModel.VisibleRows[2];

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var dirShots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(dirShots);
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(System.IO.Path.Combine(dirShots, "editor-preview.png"));
        window.Close();
    }
}

public class CompareRunsTests
{
    [Fact]
    public void Compare_runs_opens_the_history_for_the_instance_and_selected_testcase_name()
    {
        using var dir = new TempDir();
        var shell = new EditorShellViewModel([("Sample", Samples.CreateLarge(dir, rows: 2))]);
        string? instance = null, testcase = null;
        shell.HistoryHandler = (i, t) => { instance = i; testcase = t; };

        shell.Current!.OpenHistoryCommand.Execute(null);

        Assert.Equal("Sample", instance);
        Assert.Equal("Customers", testcase);
        Assert.False(shell.Current.StatusIsError);
    }

    [Fact]
    public void Compare_runs_explains_itself_when_the_editor_is_not_hosted_by_the_dashboard()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2));

        vm.OpenHistoryCommand.Execute(null);

        Assert.True(vm.StatusIsError);
        Assert.Contains("dashboard", vm.StatusMessage);
    }
}
