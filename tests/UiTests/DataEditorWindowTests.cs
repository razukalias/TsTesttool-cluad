using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class DataEditorWindowTests
{
    private static string ShotDir()
    {
        var dir = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [AvaloniaFact]
    public void Window_builds_one_grid_column_per_view_model_column_and_renders()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir), "Customers");
        window.Width = 1440;
        window.Height = 860;
        window.Show();

        var grid = window.FindControl<DataGrid>("Grid")!;
        Assert.Equal(2 + window.ViewModel.Columns.Count, grid.Columns.Count); // data ID, last run, then the data columns
        Assert.Equal(300, window.ViewModel.VisibleRows.Count);

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame!.Save(System.IO.Path.Combine(ShotDir(), "editor-large.png"));
        window.Close();
    }

    [AvaloniaFact]
    public void Window_renders_problems_for_a_broken_workbook()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 12);
        using (var workbook = new ClosedXML.Excel.XLWorkbook(path))
        {
            workbook.Worksheet("response").Row(3).Clear(ClosedXML.Excel.XLClearOptions.Contents);
            workbook.Save();
        }
        var window = DataEditorWindow.Open(path, "Customers");
        window.Width = 1440;
        window.Height = 860;
        window.Show();
        window.ViewModel.VisibleRows[4].Cells[window.ViewModel.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex] = "Edited";

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(ShotDir(), "editor-problems.png"));
        window.Close();
    }
}
