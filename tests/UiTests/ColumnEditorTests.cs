using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ColumnEditorTests
{
    private static int CellOf(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    private static void Add(DataEditorViewModel vm, string step, string kind, string name, string verb = "eq")
    {
        vm.OpenAddColumnCommand.Execute(null);
        vm.NewColumnStep = step;
        vm.NewColumnKind = kind;
        vm.NewColumnVerb = verb;
        vm.NewColumnName = name;
        vm.ConfirmAddColumnCommand.Execute(null);
    }

    [Fact]
    public void A_request_column_appears_in_the_grid_and_reaches_the_runner()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);

        Add(vm, "CreateCustomer", DataEditorViewModel.RequestKind, "phone");

        Assert.False(vm.AddColumnVisible);
        Assert.True(vm.IsDirty);
        vm.VisibleRows[1].Cells[CellOf(vm, "CreateCustomer.phone")] = "+46 70 000";
        vm.SaveCommand.Execute(null);
        Assert.Equal("+46 70 000", new WorkbookReader().Read(path).RequestData[(1, "CUST002")].Variables["CreateCustomer.phone"]);
    }

    [Fact]
    public void An_expected_column_uses_its_verb_and_is_asserted_by_the_runner()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);

        Add(vm, "CreateCustomer", DataEditorViewModel.ExpectedKind, "address.city", verb: "contains");
        vm.VisibleRows[0].Cells[CellOf(vm, "{contains}CreateCustomer.address.city")] = "Stock";
        vm.SaveCommand.Execute(null);

        var model = new WorkbookReader().Read(path);
        var assertion = model.Assertions[(1, "CUST001", "CreateCustomer")].Single(a => a.ResponsePath == "address.city");
        Assert.Equal("contains", assertion.AssertionVerb);
        Assert.Equal("Stock", assertion.ExpectedValue);
    }

    [Fact]
    public void A_typed_step_prefix_is_not_doubled_and_duplicates_are_refused()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2));

        Add(vm, "CreateCustomer", DataEditorViewModel.RequestKind, "CreateCustomer.fax");
        Assert.Contains(vm.Columns, c => c.Title == "CreateCustomer.fax");
        Assert.DoesNotContain(vm.Columns, c => c.Title == "CreateCustomer.CreateCustomer.fax");

        Add(vm, "CreateCustomer", DataEditorViewModel.RequestKind, "fax");
        Assert.True(vm.StatusIsError);
        Assert.Contains("already has a column", vm.StatusMessage);

        Add(vm, "CreateCustomer", DataEditorViewModel.ExpectedKind, "name", verb: "ne"); // {eq}CreateCustomer.name exists
        Assert.Contains("already has a column", vm.StatusMessage);
    }

    [Fact]
    public void An_empty_name_keeps_the_panel_open_with_a_message()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2));

        Add(vm, "CreateCustomer", DataEditorViewModel.RequestKind, "  ");

        Assert.True(vm.AddColumnVisible);
        Assert.True(vm.StatusIsError);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void A_whole_new_step_with_its_columns_and_values_runs_through_the_reader()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        var vm = new DataEditorViewModel(path);

        vm.OpenStepsTabCommand.Execute(null);
        vm.AddStepCommand.Execute(null);
        var step = vm.SelectedStep!;
        step.Cells[0] = "Lookup";
        step.Cells[vm.StepColumns.First(c => c.Title == "templetsource").CellIndex] = "lookup.json";
        step.Cells[vm.StepColumns.First(c => c.Title == "targetUrl").CellIndex] = "https://api.example.test/lookup";
        vm.OpenDataTabCommand.Execute(null);
        Add(vm, "Lookup", DataEditorViewModel.RequestKind, "id");
        Add(vm, "Lookup", DataEditorViewModel.ExpectedKind, "status", verb: "eq");
        vm.VisibleRows[0].Cells[CellOf(vm, "Lookup.id")] = "42";
        vm.VisibleRows[0].Cells[CellOf(vm, "Lookup.status")] = "active";

        Assert.Equal(0, vm.ErrorCount);
        vm.SaveCommand.Execute(null);
        var model = new WorkbookReader().Read(path);
        Assert.Equal(["CreateCustomer", "Lookup"], model.Testcases.Single().Steps.Select(s => s.StepName));
        Assert.Equal("42", model.RequestData[(1, "CUST001")].Variables["Lookup.id"]);
        Assert.Equal("active", model.Assertions[(1, "CUST001", "Lookup")].Single().ExpectedValue);
    }

    [AvaloniaFact]
    public void Renders_the_add_column_panel()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 8), "Customers");
        window.Width = 1440;
        window.Height = 860;
        window.Show();
        window.ViewModel.OpenAddColumnCommand.Execute(null);
        window.ViewModel.NewColumnKind = DataEditorViewModel.ExpectedKind;
        window.ViewModel.NewColumnName = "address.city";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.GetTempPath();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-addcolumn.png"));
        window.Close();
    }
}
