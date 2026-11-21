using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class DuplicateDataIdTests
{
    private static DataEditorViewModel Open(TempDir dir, int rows = 3) => new(Samples.CreateLarge(dir, rows: rows));
    private static string[] Ids(DataEditorViewModel vm) => vm.VisibleRows.Select(r => r.Cells[0]).ToArray();

    [Fact]
    public void Pasting_rows_whose_Data_IDs_exist_numbers_the_new_rows_in_paste_order_and_says_so()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var first = Ids(vm)[0];
        var name = vm.Columns.First(c => c.Title.EndsWith(".name")).CellIndex;
        vm.PasteBlock(3, 0, [[first, "x"], [first, "y"], [Ids(vm)[1], "z"]]);   // three new rows under the last one
        var ids = Ids(vm);
        Assert.Equal(6, ids.Length);
        Assert.Equal(6, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(first, ids[0]);                                  // the row that was there keeps its name
        Assert.Equal(["CUST004", "CUST005", "CUST006"], ids[3..]);      // numbered after the highest, zero padding kept, in paste order
        Assert.NotEqual(ids[3], ids[4]);
        Assert.Contains("numbered to stay unique", vm.StatusMessage);
        Assert.Contains($"{first} → {ids[3]}", vm.StatusMessage);
        vm.UndoCommand.Execute(null);                                // one Undo reverts the paste and the numbering
        Assert.Equal(3, vm.VisibleRows.Count);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Pasting_into_the_Data_ID_column_numbers_an_ID_another_row_has_but_a_free_one_stays()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var other = Ids(vm)[0];
        vm.PasteBlock(1, 0, [[other], ["BRAND-NEW"]]);
        var ids = Ids(vm);
        Assert.Equal(other, ids[0]);
        Assert.NotEqual(other, ids[1]);
        Assert.Equal("CUST004", ids[1]);
        Assert.Equal("BRAND-NEW", ids[2]);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_CSV_that_repeats_a_Data_ID_makes_the_second_a_new_numbered_row_instead_of_overwriting_the_first()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var name = vm.Columns.First(c => c.Title.EndsWith(".name"));
        var csv = $"Data ID,{name.Title}\nNEWID,first\nNEWID,second\n{Ids(vm)[0]},changed\n";
        vm.ImportCsv(csv);
        var ids = Ids(vm);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("NEWID", ids);
        Assert.Contains("NEWID2", ids);
        Assert.Equal("first", vm.VisibleRows.First(r => r.Cells[0] == "NEWID").Cells[name.CellIndex]);
        Assert.Equal("second", vm.VisibleRows.First(r => r.Cells[0] == "NEWID2").Cells[name.CellIndex]);
        Assert.Equal("changed", vm.VisibleRows[0].Cells[name.CellIndex]);   // an ID that already existed is still an update
        Assert.Contains("NEWID → NEWID2", vm.StatusMessage);
    }

    [Fact]
    public void Fix_duplicate_Data_IDs_repairs_a_workbook_that_has_them_and_one_Undo_restores()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var doc = new ClosedXML.Excel.XLWorkbook(path))
        {
            foreach (var sheetName in new[] { "request", "response" })
            {
                var sheet = doc.Worksheet(sheetName);
                var last = sheet.LastRowUsed()!.RowNumber();
                foreach (var col in new[] { 1, 2, 3 }) sheet.Cell(last + 1, col).Value = sheet.Cell(2, col).Value;   // a second copy of the first row's key
            }
            doc.Save();
        }
        var vm = new DataEditorViewModel(path);
        Assert.Contains(vm.Problems, p => p.Message.Contains("Duplicate row"));
        vm.FixDuplicateDataIdsCommand.Execute(null);
        Assert.Equal(4, vm.VisibleRows.Count);
        Assert.Equal(4, Ids(vm).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(vm.Problems, p => p.Message.Contains("Duplicate row"));
        Assert.Contains("Renamed 1 duplicate Data ID", vm.StatusMessage);
        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.Problems, p => p.Message.Contains("Duplicate row"));
        vm.FixDuplicateDataIdsCommand.Execute(null);
        vm.FixDuplicateDataIdsCommand.Execute(null);   // nothing left: says so and changes nothing
        Assert.Contains("No Data ID appears twice", vm.StatusMessage);
    }
}
