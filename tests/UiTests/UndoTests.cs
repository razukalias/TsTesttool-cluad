using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class UndoTests
{
    private static int CellOf(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    [Fact]
    public void Undo_and_redo_a_cell_edit_and_return_to_clean()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 5));
        var cell = CellOf(vm, "CreateCustomer.name");

        vm.VisibleRows[2].Cells[cell] = "Changed";
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanUndo);
        Assert.Contains("edit CreateCustomer.name of CUST003", vm.UndoTip);

        vm.UndoCommand.Execute(null);
        Assert.Equal("Customer 3", vm.VisibleRows[2].Cells[cell]);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);

        vm.RedoCommand.Execute(null);
        Assert.Equal("Changed", vm.VisibleRows[2].Cells[cell]);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Several_edits_undo_in_reverse_order_and_a_new_edit_drops_the_redo_history()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 5));
        var cell = CellOf(vm, "CreateCustomer.country");
        var original = vm.VisibleRows[0].Cells[cell];
        vm.VisibleRows[0].Cells[cell] = "AA";
        vm.VisibleRows[0].Cells[cell] = "BB";
        vm.VisibleRows[1].Cells[cell] = "CC";

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal("AA", vm.VisibleRows[0].Cells[cell]);
        Assert.True(vm.CanRedo);

        vm.VisibleRows[3].Cells[cell] = "ZZ";
        Assert.False(vm.CanRedo);
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(original, vm.VisibleRows[0].Cells[cell]);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Undo_brings_back_a_removed_row_and_removes_an_added_one()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 5);
        var vm = new DataEditorViewModel(path);
        vm.SelectedRow = vm.VisibleRows[1];
        vm.RemoveRowCommand.Execute(null);
        Assert.Equal(4, vm.VisibleRows.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(5, vm.VisibleRows.Count);
        Assert.Equal("CUST002", vm.VisibleRows[1].Cells[0]);
        Assert.Equal(5, vm.SelectedTestcase!.RowCount);
        Assert.False(vm.IsDirty);

        vm.AddRowCommand.Execute(null);
        vm.DuplicateRowCommand.Execute(null);
        Assert.Equal(7, vm.VisibleRows.Count);
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(5, vm.VisibleRows.Count);
        Assert.False(vm.IsDirty);

        vm.SelectedRow = vm.VisibleRows[1];
        vm.RemoveRowCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.Equal(5, new WorkbookReader().Read(path).RequestData.Count);
    }

    [Fact]
    public void Undo_of_a_data_id_rename_restores_both_rows()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        vm.VisibleRows[0].Cells[0] = "RENAMED";
        vm.UndoCommand.Execute(null);

        Assert.Equal("CUST001", vm.VisibleRows[0].Cells[0]);
        Assert.True(vm.HasNoProblems);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Rejected_and_unchanged_edits_do_not_create_history()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));

        vm.VisibleRows[0].Cells[0] = "cust002"; // clashes
        Assert.False(vm.CanUndo);

        var cell = CellOf(vm, "CreateCustomer.name");
        vm.VisibleRows[0].Cells[cell] = vm.VisibleRows[0].Cells[cell]; // same value
        Assert.False(vm.CanUndo);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Steps_and_columns_can_be_undone_too()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        vm.OpenStepsTabCommand.Execute(null);
        vm.AddStepCommand.Execute(null);
        vm.SelectedStep!.Cells[vm.StepColumns.First(c => c.Title == "targetUrl").CellIndex] = "https://x.test";
        Assert.Equal(2, vm.StepRows.Count);

        vm.OpenDataTabCommand.Execute(null);
        vm.OpenAddColumnCommand.Execute(null);
        vm.NewColumnStep = "CreateCustomer";
        vm.NewColumnName = "phone";
        vm.ConfirmAddColumnCommand.Execute(null);
        Assert.Contains(vm.Columns, c => c.Title == "CreateCustomer.phone");

        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(vm.Columns, c => c.Title == "CreateCustomer.phone");

        vm.UndoCommand.Execute(null);
        Assert.True(vm.ShowSteps); // undo takes you to where the change was made
        Assert.Equal("", vm.StepRows[1].Cells[vm.StepColumns.First(c => c.Title == "targetUrl").CellIndex]);

        vm.UndoCommand.Execute(null);
        Assert.Single(vm.StepRows);
        Assert.Equal(["CreateCustomer"], vm.SelectedTestcase!.Steps);
        Assert.False(vm.IsDirty);

        vm.RedoCommand.Execute(null);
        Assert.Equal(2, vm.StepRows.Count);
        Assert.Equal(2, vm.SelectedTestcase.Steps.Count);
    }

    [Fact]
    public void Undo_returns_to_the_testcase_where_the_change_was_made()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.Copy(dir, Samples.IntegrationScenarios));
        vm.SelectedTestcase = vm.Testcases[1];
        vm.VisibleRows[0].Cells[CellOf(vm, "QueryCountry.code")] = "SE";
        vm.SelectedTestcase = vm.Testcases[3];

        vm.UndoCommand.Execute(null);

        Assert.Equal(2, vm.SelectedTestcase!.Index);
        Assert.NotEqual("SE", vm.VisibleRows[0].Cells[CellOf(vm, "QueryCountry.code")]);
    }

    [Fact]
    public void Saving_or_reloading_starts_the_history_over_and_undone_changes_are_not_saved()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        var cell = CellOf(vm, "CreateCustomer.name");

        vm.VisibleRows[0].Cells[cell] = "Kept";
        vm.VisibleRows[1].Cells[cell] = "Undone";
        vm.UndoCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);
        var model = new WorkbookReader().Read(path);
        Assert.Equal("Kept", model.RequestData[(1, "CUST001")].Variables["CreateCustomer.name"]);
        Assert.Equal("Customer 2", model.RequestData[(1, "CUST002")].Variables["CreateCustomer.name"]);

        vm.VisibleRows[2].Cells[cell] = "x";
        vm.ReloadCommand.Execute(null);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Undo_works_quickly_on_a_workbook_with_hundreds_of_rows()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir));
        var cell = CellOf(vm, "CreateCustomer.name");
        for (var i = 0; i < 100; i++) vm.VisibleRows[i].Cells[cell] = "Edit " + i;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) vm.UndoCommand.Execute(null);
        watch.Stop();

        Assert.False(vm.IsDirty);
        Assert.Equal("Customer 1", vm.VisibleRows[0].Cells[cell]);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"100 undos took {watch.Elapsed}");
    }
}
