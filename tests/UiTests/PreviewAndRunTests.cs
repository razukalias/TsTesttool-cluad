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
    public async Task Run_testcase_asks_the_host_for_every_row_and_run_row_for_the_selected_one()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var calls = new List<(int Testcase, string Ids)>();
        vm.RunHandler = (testcase, ids) => { calls.Add((testcase, string.Join(",", ids))); return Task.FromResult<string?>(null); };

        await vm.RunTestcaseCommand.ExecuteAsync(null);
        vm.SelectedRow = vm.VisibleRows[2];
        await vm.RunRowCommand.ExecuteAsync(null);

        Assert.Equal([(1, ""), (1, "CUST003")], calls);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public async Task A_run_is_refused_while_there_are_unsaved_changes_and_a_host_error_is_shown()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var started = 0;
        vm.RunHandler = (_, _) => { started++; return Task.FromResult<string?>("Customers is already running; stop it first."); };

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
        window.Shell.RunHandler = (_, _, _) => Task.FromResult<string?>(null);
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
