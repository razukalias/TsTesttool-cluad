using LoadTestingTool.Editing;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class CsvTests
{
    [Fact]
    public void Export_then_import_into_a_fresh_copy_reproduces_the_rows()
    {
        using var dir = new TempDir();
        var source = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 4));
        source.VisibleRows[1].Cells[source.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex] = "Name, with \"quotes\"";
        var csv = source.BuildCsv();
        Assert.StartsWith("Data ID,", csv);

        using var dir2 = new TempDir();
        var target = new DataEditorViewModel(Samples.CreateLarge(dir2, rows: 1));
        target.ImportCsv(csv);

        Assert.Equal(4, target.VisibleRows.Count);
        Assert.Equal("Name, with \"quotes\"", target.VisibleRows[1].Cells[target.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex]);
        Assert.Equal(source.VisibleRows.Select(r => r.Cells.ToList()), target.VisibleRows.Select(r => r.Cells.ToList()));
        Assert.True(target.IsDirty);
        Assert.Contains("3 new, 1 updated", target.StatusMessage);
    }

    [Fact]
    public void Import_updates_matching_ids_adds_new_ones_reads_semicolons_and_undoes_in_one_step()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        vm.ImportCsv("﻿Data ID;CreateCustomer.name;Bogus\nCUST002;Changed;x\nNEW1;Brand new;y\n");

        Assert.Equal(4, vm.VisibleRows.Count);
        var name = vm.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex;
        Assert.Equal("Changed", vm.VisibleRows[1].Cells[name]);
        Assert.Equal("Brand new", vm.VisibleRows[3].Cells[name]);
        Assert.Contains("ignored columns: Bogus", vm.StatusMessage);

        vm.UndoCommand.Execute(null);
        Assert.Equal(3, vm.VisibleRows.Count);
        Assert.Equal("Customer 2", vm.VisibleRows[1].Cells[name]);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Import_saves_to_the_workbook_and_refuses_a_file_with_no_matching_columns()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        var vm = new DataEditorViewModel(path);
        vm.ImportCsv("Data ID,CreateCustomer.email\nCUST900,x@y.test\n");
        vm.SaveCommand.Execute(null);
        Assert.Equal("x@y.test", new WorkbookReader().Read(path).RequestData[(1, "CUST900")].Variables["CreateCustomer.email"]);

        vm.ImportCsv("foo,bar\n1,2\n");
        Assert.True(vm.StatusIsError);
        Assert.Contains("None of the file's columns", vm.StatusMessage);
    }
}

public class NewTestcaseTests
{
    [Fact]
    public void New_testcase_needs_saved_changes_then_reloads_and_selects_the_created_testcase()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        var vm = new DataEditorViewModel(path);
        var asked = new List<int>();
        vm.NewTestcaseRequested += (_, next) => asked.Add(next);
        vm.CreateTestcaseHandler = _ =>
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook(path);
            var config = workbook.Worksheet("config");
            config.Cell(3, 1).Value = 2; config.Cell(3, 2).Value = "Orders"; config.Cell(3, 3).Value = "CreateOrder"; config.Cell(3, 4).Value = "HTTP";
            config.Cell(3, 5).Value = "order.json"; config.Cell(3, 6).Value = "https://api.example.test/orders"; config.Cell(3, 7).Value = "POST";
            var request = workbook.Worksheet("request"); request.Cell(4, 1).Value = 2; request.Cell(4, 2).Value = "Orders"; request.Cell(4, 3).Value = "ORD001";
            var response = workbook.Worksheet("response"); response.Cell(4, 1).Value = 2; response.Cell(4, 2).Value = "Orders"; response.Cell(4, 3).Value = "ORD001";
            workbook.Save();
        };

        vm.VisibleRows[0].Cells[vm.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex] = "Edited";
        vm.NewTestcaseCommand.Execute(null);
        Assert.Empty(asked);
        Assert.Contains("Save your changes first", vm.StatusMessage);

        vm.SaveCommand.Execute(null);
        vm.NewTestcaseCommand.Execute(null);
        Assert.Equal([2], asked);

        vm.CreateTestcase(new LoadTestingTool.UI.TestcaseWizardDraft { TestcaseIndex = 2, TestcaseName = "Orders" });
        vm.FinishNewTestcase(2);

        Assert.Equal(["Customers", "Orders"], vm.Testcases.Select(t => t.Name));
        Assert.Equal(2, vm.SelectedTestcase!.Index);
        Assert.False(vm.IsDirty);
        Assert.Contains("Created testcase 2", vm.StatusMessage);
    }
}
