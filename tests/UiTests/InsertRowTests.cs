using ClosedXML.Excel;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class InsertRowTests
{
    private static string WithReference(TempDir dir, int rows = 5)
    {
        var path = Samples.CreateLarge(dir, rows);
        using var workbook = new XLWorkbook(path);
        // the step's URL points at the fourth data row's email by absolute row number (row 5 of the sheet)
        workbook.Worksheet("config").Cell("F2").Value = "https://api.example.test/<From_request_CreateCustomer.email_5>";
        workbook.Save();
        return path;
    }

    [Fact]
    public void Insert_above_puts_the_row_in_place_tells_what_it_will_renumber_and_saves_correctly()
    {
        using var dir = new TempDir();
        var path = WithReference(dir);
        var vm = new DataEditorViewModel(path);
        vm.SelectedRow = vm.VisibleRows[2]; // CUST003

        vm.InsertRowAboveCommand.Execute(null);

        Assert.Equal(["CUST001", "CUST002", "CUST006", "CUST003", "CUST004", "CUST005"], vm.VisibleRows.Select(r => r.Cells[0]));
        Assert.Equal("CUST006", vm.SelectedRow!.Cells[0]);
        Assert.Contains("renumbers 1 <From_…_N> reference", vm.StatusMessage);
        vm.SelectedRow.Cells[vm.Columns.First(c => c.Title == "CreateCustomer.email").CellIndex] = "new@example.test";

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.Contains("Inserted 1 row", vm.StatusMessage);
        Assert.Equal(["CUST001", "CUST002", "CUST006", "CUST003", "CUST004", "CUST005"], vm.VisibleRows.Select(r => r.Cells[0])); // after the reload
        var model = new WorkbookReader().Read(path);
        Assert.Equal("https://api.example.test/customer4@example.test", model.Testcases[0].Steps[0].TargetUrl); // still the fourth original row
        Assert.Equal("new@example.test", model.RequestData[(1, "CUST006")].Variables["CreateCustomer.email"]);
        Assert.True(vm.HasNoProblems);
    }

    [Fact]
    public void Insert_below_the_last_row_is_a_plain_append_and_undo_removes_an_insert()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(WithReference(dir, rows: 3));
        vm.SelectedRow = vm.VisibleRows[2];
        vm.InsertRowBelowCommand.Execute(null);
        Assert.Equal("CUST004", vm.VisibleRows[3].Cells[0]);

        vm.SelectedRow = vm.VisibleRows[0];
        vm.InsertRowBelowCommand.Execute(null);
        Assert.Equal(["CUST001", "CUST005", "CUST002", "CUST003", "CUST004"], vm.VisibleRows.Select(r => r.Cells[0]));

        vm.UndoCommand.Execute(null);
        Assert.Equal(["CUST001", "CUST002", "CUST003", "CUST004"], vm.VisibleRows.Select(r => r.Cells[0]));
    }
}
