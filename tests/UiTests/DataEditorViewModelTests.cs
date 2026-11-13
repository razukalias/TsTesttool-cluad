using ClosedXML.Excel;
using LoadTestingTool.Editing;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class DataEditorViewModelTests
{
    private static int CellOf(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    // ---- Showing the data ----------------------------------------------------------------

    [Fact]
    public void Opens_on_the_first_testcase_with_its_rows_paired()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.Copy(dir, Samples.IntegrationScenarios));

        Assert.Equal(["HttpFlow", "GraphQLFlow", "FileScriptFlow", "NegativeAndRecovery"], vm.Testcases.Select(t => t.Name));
        Assert.Equal(1, vm.SelectedTestcase?.Index);
        var row = Assert.Single(vm.VisibleRows);
        Assert.Equal("HTTP001", row.Cells[0]);
        Assert.NotNull(row.Response);
        Assert.False(vm.IsDirty);
        Assert.True(vm.HasNoProblems);
    }

    [Fact]
    public void Shows_only_the_columns_that_matter_to_the_selected_testcase()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.Copy(dir, Samples.IntegrationScenarios));

        var titles = vm.Columns.Select(c => c.Title).ToList();
        Assert.Contains("CreatePost.title", titles);
        Assert.Contains("CreatePost.statusCode", titles);
        Assert.DoesNotContain("QueryCountry.code", titles);

        vm.ShowAllColumns = true;
        Assert.Contains("QueryCountry.code", vm.Columns.Select(c => c.Title));
    }

    [Fact]
    public void Switching_testcase_changes_rows_and_columns()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.Copy(dir, Samples.IntegrationScenarios));
        var changed = 0;
        vm.ColumnsChanged += (_, _) => changed++;

        vm.SelectedTestcase = vm.Testcases[1];

        Assert.Equal("GQL001", Assert.Single(vm.VisibleRows).Cells[0]);
        Assert.Contains("QueryCountry.code", vm.Columns.Select(c => c.Title));
        Assert.DoesNotContain("CreatePost.title", vm.Columns.Select(c => c.Title));
        Assert.True(changed > 0);
    }

    [Fact]
    public void Filter_narrows_the_rows_and_clearing_it_brings_them_back()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir));
        Assert.Equal(300, vm.VisibleRows.Count);

        vm.FilterText = "CUST01";
        Assert.Equal(10, vm.VisibleRows.Count);
        Assert.Equal("10 of 300 rows", vm.RowCountText);

        vm.FilterText = "";
        Assert.Equal(300, vm.VisibleRows.Count);
    }

    // ---- Editing -------------------------------------------------------------------------

    [Fact]
    public void Editing_request_and_response_cells_reaches_the_workbook_and_the_runner()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);

        vm.SelectedRow!.Cells[CellOf(vm, "CreatePost.title")] = "Edited title";
        vm.SelectedRow!.Cells[CellOf(vm, "FetchUser.statusCode")] = "204";
        Assert.True(vm.IsDirty);
        Assert.Equal("Unsaved changes", vm.SaveStatusText);

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.NotNull(vm.LastBackupPath);
        var model = new WorkbookReader().Read(path);
        Assert.Equal("Edited title", model.RequestData[(1, "HTTP001")].Variables["CreatePost.title"]);
        Assert.Contains(model.Assertions[(1, "HTTP001", "FetchUser")], a => a.ExpectedValue == "204");
    }

    [Fact]
    public void Renaming_a_data_id_keeps_the_request_and_response_rows_together()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);

        vm.SelectedRow!.Cells[0] = "HTTP001-renamed";
        vm.SaveCommand.Execute(null);

        var model = new WorkbookReader().Read(path);
        Assert.True(model.RequestData.ContainsKey((1, "HTTP001-renamed")));
        Assert.Contains(model.Assertions.Keys, k => k.Item1 == 1 && k.Item2 == "HTTP001-renamed");
        Assert.True(vm.HasNoProblems);
    }

    [Fact]
    public void A_duplicate_or_empty_data_id_is_refused_and_the_cell_goes_back()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 5));
        var first = vm.VisibleRows[0];

        first.Cells[0] = "cust002";
        Assert.Equal("CUST001", first.Cells[0]);
        Assert.True(vm.StatusIsError);
        Assert.Contains("already has a data row", vm.StatusMessage);

        first.Cells[0] = "  ";
        Assert.Equal("CUST001", first.Cells[0]);
        Assert.Contains("cannot be empty", vm.StatusMessage);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Add_row_creates_a_paired_row_with_the_next_data_id()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir);
        var vm = new DataEditorViewModel(path);

        vm.AddRowCommand.Execute(null);

        Assert.Equal(301, vm.VisibleRows.Count);
        Assert.Equal("CUST301", vm.SelectedRow!.Cells[0]);
        Assert.Equal(301, vm.SelectedTestcase!.RowCount);
        Assert.True(vm.IsDirty);
        Assert.True(vm.HasNoProblems);
        vm.SaveCommand.Execute(null);
        Assert.True(new WorkbookReader().Read(path).RequestData.ContainsKey((1, "CUST301")));
    }

    [Fact]
    public void Duplicate_row_copies_the_values_under_a_new_data_id()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 5);
        var vm = new DataEditorViewModel(path);
        vm.SelectedRow = vm.VisibleRows[2];

        vm.DuplicateRowCommand.Execute(null);

        var copy = vm.SelectedRow!;
        Assert.Equal("CUST006", copy.Cells[0]);
        Assert.Equal(vm.VisibleRows[2].Cells.Skip(1), copy.Cells.Skip(1));
        vm.SaveCommand.Execute(null);
        var model = new WorkbookReader().Read(path);
        Assert.Equal("Customer 3", model.RequestData[(1, "CUST006")].Variables["CreateCustomer.name"]);
    }

    [Fact]
    public void Remove_row_drops_both_rows_and_reload_brings_it_back_until_saved()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 5);
        var vm = new DataEditorViewModel(path);
        vm.SelectedRow = vm.VisibleRows[1];

        vm.RemoveRowCommand.Execute(null);
        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.True(vm.IsDirty);

        vm.ReloadCommand.Execute(null);
        Assert.Equal(5, vm.VisibleRows.Count);
        Assert.False(vm.IsDirty);

        vm.SelectedRow = vm.VisibleRows[1];
        vm.RemoveRowCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.Equal(4, new WorkbookReader().Read(path).RequestData.Count);
    }

    // ---- Problems ------------------------------------------------------------------------

    [Fact]
    public void A_row_without_a_response_row_is_reported_and_fixed_by_editing_it()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 5);
        using (var workbook = new XLWorkbook(path))
        {
            workbook.Worksheet("response").Row(3).Clear(XLClearOptions.Contents); // response row of CUST002
            workbook.Save();
        }

        var vm = new DataEditorViewModel(path);
        Assert.Equal(1, vm.ErrorCount);
        Assert.Contains("CUST002", vm.Problems[0].Message);

        vm.SelectedProblem = vm.Problems[0];
        Assert.Equal("CUST002", vm.SelectedRow!.Cells[0]);
        Assert.Null(vm.SelectedProblem);

        vm.SelectedRow.Cells[CellOf(vm, "CreateCustomer.statusCode")] = "201";
        Assert.Equal(0, vm.ErrorCount);
    }

    // ---- Saving safely, reloading and closing --------------------------------------------

    [Fact]
    public void A_file_changed_on_disk_is_reported_instead_of_overwritten()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        using (var workbook = new XLWorkbook(path)) { workbook.AddWorksheet("elsewhere"); workbook.Save(); }

        vm.VisibleRows[0].Cells[CellOf(vm, "CreateCustomer.name")] = "Mine";
        vm.SaveCommand.Execute(null);

        Assert.True(vm.ConflictVisible);
        Assert.True(vm.IsDirty);
        Assert.True(vm.StatusIsError);

        vm.OverwriteAndSaveCommand.Execute(null);
        Assert.False(vm.ConflictVisible);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Closing_with_unsaved_changes_asks_first()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        Assert.True(vm.RequestClose());

        vm.VisibleRows[0].Cells[CellOf(vm, "CreateCustomer.name")] = "Changed";
        Assert.False(vm.RequestClose());
        Assert.True(vm.ClosePromptVisible);

        vm.KeepEditingCommand.Execute(null);
        Assert.False(vm.ClosePromptVisible);
        Assert.Equal(0, closed);

        vm.DiscardAndCloseCommand.Execute(null);
        Assert.Equal(1, closed);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Save_and_close_writes_the_file_then_closes()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        var closed = 0;
        vm.CloseRequested += (_, _) => closed++;
        vm.VisibleRows[0].Cells[CellOf(vm, "CreateCustomer.name")] = "Saved on close";

        vm.RequestClose();
        vm.SaveAndCloseCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal("Saved on close", new WorkbookReader().Read(path).RequestData[(1, "CUST001")].Variables["CreateCustomer.name"]);
    }
}
