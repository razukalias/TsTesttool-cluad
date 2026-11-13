using ClosedXML.Excel;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class StepBulkTests
{
    private static DataEditorViewModel WithThreeSteps(TempDir dir, out string path)
    {
        path = Samples.CreateLarge(dir, rows: 2);
        var vm = new DataEditorViewModel(path);
        vm.AddStepCommand.Execute(null);
        vm.AddStepCommand.Execute(null);
        vm.ShowSteps = true;
        return vm;
    }

    private static int Cell(DataEditorViewModel vm, string title) => vm.StepColumns.First(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase)).CellIndex;

    [Fact]
    public void Fill_down_copies_the_first_steps_value_into_the_other_selected_steps_as_one_undo_step()
    {
        using var dir = new TempDir();
        var vm = WithThreeSteps(dir, out _);
        var cell = Cell(vm, "targetUrl");
        vm.StepRows[0].Cells[cell] = "https://one.example.test";

        vm.FillDownSteps(vm.StepRows.ToList(), cell);

        Assert.All(vm.StepRows, s => Assert.Equal("https://one.example.test", s.Cells[cell]));
        vm.UndoCommand.Execute(null);
        Assert.Equal("", vm.StepRows[1].Cells[cell]);
        Assert.Equal("https://one.example.test", vm.StepRows[0].Cells[cell]);
    }

    [Fact]
    public void Paste_fills_a_block_skips_step_names_and_saves()
    {
        using var dir = new TempDir();
        var vm = WithThreeSteps(dir, out var path);
        var url = Cell(vm, "targetUrl");
        var verb = Cell(vm, "verb");

        vm.PasteStepBlock(1, 0, [["Renamed"]]);
        Assert.NotEqual("Renamed", vm.StepRows[1].Cells[0]);
        Assert.Contains("Nothing changed", vm.StatusMessage);

        vm.PasteStepBlock(1, url, [["http://a", "PUT"], ["http://b", "DELETE"], ["past the end", "x"]]);
        Assert.Equal("http://a", vm.StepRows[1].Cells[url]);
        Assert.Equal("DELETE", vm.StepRows[2].Cells[verb]);
        Assert.Contains("skipped", vm.StatusMessage);

        vm.SaveCommand.Execute(null);
        using var workbook = new XLWorkbook(path);
        Assert.Equal("http://b", workbook.Worksheet("config").Cell(4, 6).GetString());
    }

    [Fact]
    public void Find_and_replace_works_on_the_steps_tab_and_leaves_names_alone()
    {
        using var dir = new TempDir();
        var vm = WithThreeSteps(dir, out _);
        var url = Cell(vm, "targetUrl");
        vm.StepRows[0].Cells[url] = "https://old.example.test/a";
        vm.StepRows[1].Cells[url] = "https://old.example.test/b";

        vm.OpenFindCommand.Execute(null);
        vm.FindText = "old.example";
        Assert.Equal("2 matches in 2 cells", vm.MatchSummary);
        vm.ReplaceText = "new.example";
        vm.ReplaceAllCommand.Execute(null);

        Assert.Equal("https://new.example.test/a", vm.StepRows[0].Cells[url]);
        Assert.Equal("https://new.example.test/b", vm.StepRows[1].Cells[url]);
        Assert.Equal("CreateCustomer", vm.StepRows[0].Cells[0]);
    }
}
