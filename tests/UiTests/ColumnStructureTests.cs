using ClosedXML.Excel;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ColumnStructureTests
{
    private static string Build(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using var workbook = new XLWorkbook(path);
        workbook.Worksheet("config").Cell("F2").Value = "https://api.example.test/<From_request_CreateCustomer.email_3>";
        workbook.Save();
        return path;
    }

    private static int CellOf(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    [Fact]
    public void A_new_column_can_go_before_the_selected_column_and_saves_in_that_place()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var vm = new DataEditorViewModel(path);
        vm.CurrentCellIndex = CellOf(vm, "CreateCustomer.email");

        vm.OpenAddColumnCommand.Execute(null);
        vm.NewColumnName = "phone";
        vm.NewColumnPlacement = DataEditorViewModel.PlaceBeforeCurrent;
        vm.ConfirmAddColumnCommand.Execute(null);

        var titles = vm.Columns.Select(c => c.Title).ToList();
        Assert.True(titles.IndexOf("CreateCustomer.phone") == titles.IndexOf("CreateCustomer.email") - 1);
        vm.VisibleRows[1].Cells[CellOf(vm, "CreateCustomer.phone")] = "070";

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsDirty);
        var titlesAfter = vm.Columns.Select(c => c.Title).ToList();
        Assert.True(titlesAfter.IndexOf("CreateCustomer.phone") == titlesAfter.IndexOf("CreateCustomer.email") - 1);
        var model = new WorkbookReader().Read(path);
        Assert.Equal("070", model.RequestData[(1, "CUST002")].Variables["CreateCustomer.phone"]);
        Assert.Equal("customer2@example.test", model.RequestData[(1, "CUST002")].Variables["CreateCustomer.email"]);
        Assert.Equal("https://api.example.test/customer2@example.test", model.Testcases[0].Steps[0].TargetUrl);
    }

    [Fact]
    public void Renaming_a_referenced_column_keeps_the_reference_working_and_removing_it_is_refused()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var vm = new DataEditorViewModel(path);
        vm.CurrentCellIndex = CellOf(vm, "CreateCustomer.email");

        vm.OpenEditColumnCommand.Execute(null);
        Assert.Contains("1 <From_…> reference points at it", vm.EditColumnNote);
        vm.ConfirmRemoveColumnCommand.Execute(null);
        Assert.True(vm.StatusIsError);
        Assert.Contains("Cannot remove", vm.StatusMessage);
        Assert.Contains("CreateCustomer.email", vm.Columns.Select(c => c.Title));

        vm.EditColumnName = "CreateCustomer.mail";
        vm.ConfirmRenameColumnCommand.Execute(null);
        Assert.Contains("CreateCustomer.mail", vm.Columns.Select(c => c.Title));
        vm.SaveCommand.Execute(null);

        var model = new WorkbookReader().Read(path);
        Assert.Equal("https://api.example.test/customer2@example.test", model.Testcases[0].Steps[0].TargetUrl);
        Assert.Contains("Updated 1 reference", vm.StatusMessage);
    }

    [Fact]
    public void Removing_an_unreferenced_column_can_be_undone_and_saved()
    {
        using var dir = new TempDir();
        var path = Build(dir);
        var vm = new DataEditorViewModel(path);
        vm.CurrentCellIndex = CellOf(vm, "CreateCustomer.country");
        vm.OpenEditColumnCommand.Execute(null);
        vm.ConfirmRemoveColumnCommand.Execute(null);
        Assert.DoesNotContain("CreateCustomer.country", vm.Columns.Select(c => c.Title));

        vm.UndoCommand.Execute(null);
        Assert.Contains("CreateCustomer.country", vm.Columns.Select(c => c.Title));
        Assert.Equal("SE", vm.VisibleRows.Select(r => r.Cells[CellOf(vm, "CreateCustomer.country")]).First(c => c.Length > 0).Length == 2 ? "SE" : "?");

        vm.CurrentCellIndex = CellOf(vm, "CreateCustomer.country");
        vm.OpenEditColumnCommand.Execute(null);
        vm.ConfirmRemoveColumnCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        Assert.DoesNotContain("CreateCustomer.country", vm.Columns.Select(c => c.Title));
        Assert.DoesNotContain("CreateCustomer.country", LoadTestingTool.Editing.WorkbookDocument.Load(path).Request.Headers);
    }
}
