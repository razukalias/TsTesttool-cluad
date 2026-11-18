using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// The wizard button also sits on cells of the other sheets and says which request cells refer to the cell.
public class OtherSheetWizardTests
{
    private static string Workbook(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using var book = new XLWorkbook(path);
        var notes = book.AddWorksheet("Notes");
        notes.Cell(1, 1).Value = "topic"; notes.Cell(1, 2).Value = "text";
        notes.Cell(2, 1).Value = "auth"; notes.Cell(2, 2).Value = "hourly";
        notes.Cell(3, 1).Value = "limits"; notes.Cell(3, 2).Value = "daily";
        book.Worksheet("request").Cell(2, 8).Value = "<From_Notes_text_3>";
        book.Save();
        return path;
    }

    [Fact]
    public void A_cell_says_which_cells_refer_to_it()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Workbook(dir), "Customers") { ActiveSheet = "Notes" };
        Assert.Equal(["request!H2"], editor.CellUsedBy("text", editor.SheetRows[1]));   // "daily", sheet row 3
        Assert.Empty(editor.CellUsedBy("text", editor.SheetRows[0]));
        Assert.Empty(editor.CellUsedBy("topic", editor.SheetRows[1]));
    }

    [AvaloniaFact]
    public void The_current_cell_of_another_sheet_shows_the_button_and_the_config_sheet_does_not()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Workbook(dir), "Customers");
        window.Width = 1200; window.Height = 700; window.Show();
        window.ViewModel.ActiveSheet = "Notes";
        Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<DataGrid>("SheetGrid")!;
        grid.SelectedItem = window.ViewModel.SheetRows[1];
        grid.CurrentColumn = grid.Columns[1];
        Dispatcher.UIThread.RunJobs();
        Assert.Single(grid.GetVisualDescendants().OfType<Button>(), b => b.Tag as string == "cell-wizard" && b.IsVisible);

        window.ViewModel.ActiveSheet = "config";
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(grid.GetVisualDescendants().OfType<Button>(), b => b.Tag as string == "cell-wizard");
        window.Close();
    }
}
