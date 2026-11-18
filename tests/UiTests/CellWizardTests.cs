using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Table view: a small wizard button on the current cell opens the Record view's line for that cell.
public class CellWizardTests
{
    private static (DataEditorWindow Window, DataGrid Grid) Open(TempDir dir)
    {
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 700;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.FindControl<DataGrid>("Grid")!);
    }

    [AvaloniaFact]
    public void Every_data_column_has_the_wizard_and_the_id_tick_and_result_columns_do_not()
    {
        using var dir = new TempDir();
        var (window, grid) = Open(dir);
        var data = grid.Columns.OfType<FormattedTextColumn>().Where(c => c.Tag is int tag && tag > 0).ToList();
        Assert.Equal(window.ViewModel.Columns.Count, data.Count);
        Assert.All(data, c => Assert.NotNull(c.OpenWizard));
        Assert.Null(grid.Columns.OfType<FormattedTextColumn>().First(c => c.Tag is int tag && tag == 0).OpenWizard);   // Data ID
        window.Close();
    }

    [AvaloniaFact]
    public void The_button_shows_only_on_the_current_cell()
    {
        using var dir = new TempDir();
        var (window, grid) = Open(dir);
        var column = grid.Columns.OfType<FormattedTextColumn>().First(c => c.Tag is int tag && tag > 0);
        grid.SelectedItem = window.ViewModel.VisibleRows[1];
        grid.CurrentColumn = column;
        Dispatcher.UIThread.RunJobs();

        var buttons = grid.GetVisualDescendants().OfType<Button>().Where(b => b.Tag as string == "cell-wizard").ToList();
        Assert.NotEmpty(buttons);
        var shown = buttons.Where(b => b.IsVisible).ToList();
        Assert.Single(shown);
        Assert.Same(window.ViewModel.VisibleRows[1], shown[0].FindAncestorOfType<DataGridCell>()!.DataContext);
        window.Close();
    }
}

public class CellWizardShot
{
    [AvaloniaFact]
    public void Wizard_popup_renders_for_the_selected_cell()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 700; window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<DataGrid>("Grid")!;
        var column = grid.Columns.OfType<FormattedTextColumn>().First(c => c.Tag is int tag && tag > 0);
        grid.SelectedItem = window.ViewModel.VisibleRows[1];
        grid.CurrentColumn = column;
        Dispatcher.UIThread.RunJobs();
        var button = grid.GetVisualDescendants().OfType<Button>().First(b => b.Tag as string == "cell-wizard" && b.IsVisible);
        column.OpenWizard!(button.FindAncestorOfType<DataGridCell>()!, button);
        Dispatcher.UIThread.RunJobs();
        var dirShots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(dirShots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(dirShots, "cell-wizard.png"));
        window.Close();
    }
}
