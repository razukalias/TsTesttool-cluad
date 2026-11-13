using Avalonia.Headless;
using ClosedXML.Excel;
using LoadTestingTool.Editing;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class FormattingTests
{
    private static int CellOf(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    [Fact]
    public void Highlight_and_wrap_apply_to_cells_save_to_the_workbook_and_survive_a_reload_and_undo()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        var name = CellOf(vm, "CreateCustomer.name");

        vm.FormatCells([(vm.VisibleRows[1], name)], f => f with { Fill = "#FFEB9C", Wrap = true }, "Highlighted");
        Assert.Equal("#FFEB9C", vm.VisibleRows[1].Formats[name].Fill);
        Assert.True(vm.IsDirty);

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.VisibleRows[1].Formats[name].Fill);
        Assert.False(vm.VisibleRows[1].Formats[name].Wrap);

        vm.FormatCells([(vm.VisibleRows[1], name)], f => f with { Fill = "#FFEB9C", Wrap = true }, "Highlighted");
        vm.SaveCommand.Execute(null);

        using (var workbook = new XLWorkbook(path))
        {
            var cell = workbook.Worksheet("request").Cell(3, 5); // CUST002 name
            Assert.Equal("FFFFEB9C", cell.Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8"));
            Assert.True(cell.Style.Alignment.WrapText);
        }
        var reopened = new DataEditorViewModel(path);
        Assert.Equal("#FFEB9C", reopened.VisibleRows[1].Formats[CellOf(reopened, "CreateCustomer.name")].Fill);
        Assert.True(reopened.VisibleRows[1].Formats[CellOf(reopened, "CreateCustomer.name")].Wrap);
    }

    [Fact]
    public void Whole_rows_and_expected_cells_can_be_highlighted_and_cleared()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        var cells = Enumerable.Range(0, vm.Columns.Count + 1).Select(c => ((IFormattedRow)vm.VisibleRows[0], c)).ToList();

        vm.FormatCells(cells, f => f with { Fill = "#C6EFCE" }, "Highlighted");
        Assert.All(vm.VisibleRows[0].Formats, f => Assert.Equal("#C6EFCE", f.Fill));
        Assert.All(vm.VisibleRows[1].Formats, f => Assert.Null(f.Fill));
        vm.SaveCommand.Execute(null);

        using (var workbook = new XLWorkbook(path))
        {
            Assert.Equal(XLFillPatternValues.Solid, workbook.Worksheet("response").Cell(2, 6).Style.Fill.PatternType); // an expected cell too
            Assert.Equal(XLFillPatternValues.None, workbook.Worksheet("request").Cell(3, 5).Style.Fill.PatternType);
        }

        var again = new DataEditorViewModel(path);
        var all = Enumerable.Range(0, again.Columns.Count + 1).Select(c => ((IFormattedRow)again.VisibleRows[0], c)).ToList();
        again.FormatCells(all, f => f with { Fill = null }, "Cleared highlight");
        again.SaveCommand.Execute(null);
        using var workbook2 = new XLWorkbook(path);
        Assert.Equal(XLFillPatternValues.None, workbook2.Worksheet("request").Cell(2, 5).Style.Fill.PatternType);
    }

    [Fact]
    public void Step_cells_can_be_highlighted_too_and_the_cell_editor_writes_long_text_back()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        var vm = new DataEditorViewModel(path);
        var step = vm.StepRows[0];
        var url = vm.StepColumns.First(c => c.Title == "targetUrl").CellIndex;

        vm.FormatCells([(step, url)], f => f with { Fill = "#BDD7EE" }, "Highlighted");
        Assert.Equal("#BDD7EE", step.Formats[url].Fill);

        var body = "{\n  \"a\": 1,\n  \"b\": [1, 2, 3]\n}";
        vm.OpenCellEditor(vm.VisibleRows[0], CellOf(vm, "CreateCustomer.name"), "CUST001 · CreateCustomer.name", vm.VisibleRows[0].Cells[CellOf(vm, "CreateCustomer.name")]);
        Assert.True(vm.CellEditorVisible);
        vm.CellEditorText = body;
        vm.ApplyCellEditorCommand.Execute(null);

        Assert.False(vm.CellEditorVisible);
        Assert.Equal(body, vm.VisibleRows[0].Request.Get("CreateCustomer.name"));
        vm.SaveCommand.Execute(null);
        using var workbook = new XLWorkbook(path);
        Assert.Equal("#BDD7EE", "#" + workbook.Worksheet("config").Cell(2, 6).Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8")[2..]);
        Assert.Equal(body, workbook.Worksheet("request").Cell(2, 5).GetString());
    }
}

public class FormattingWindowTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Highlighted_and_wrapped_cells_show_in_the_grid_and_rows_grow_to_fit_wrapped_text()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 4);
        var window = DataEditorWindow.Open(path, "Customers");
        window.Width = 1440; window.Height = 860;
        window.Show();
        var vm = window.ViewModel;
        var name = vm.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex;
        var email = vm.Columns.First(c => c.Title == "CreateCustomer.email").CellIndex;
        vm.VisibleRows[1].Cells[name] = "A rather long customer name that would not fit on one line in a narrow column, so it should wrap onto several lines when wrapping is on";
        vm.FormatCells([(vm.VisibleRows[1], name), (vm.VisibleRows[1], email)], f => f with { Fill = "#FFEB9C", Wrap = true }, "Highlighted");
        vm.FormatCells([(vm.VisibleRows[2], email)], f => f with { Fill = "#C6EFCE" }, "Highlighted");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-highlight-wrap.png"));
        Assert.Equal("#FFEB9C", vm.VisibleRows[1].Formats[name].Fill);
        window.Close();
    }
}
