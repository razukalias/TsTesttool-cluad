using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

namespace UiTests;

public class SheetTabsTests
{
    private static DataEditorViewModel OpenWithNotesSheet(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var book = new XLWorkbook(path))
        {
            var notes = book.AddWorksheet("Notes");
            notes.Cell(1, 1).Value = "topic"; notes.Cell(1, 2).Value = "text";
            notes.Cell(2, 1).Value = "auth"; notes.Cell(2, 2).Value = "token expires hourly";
            book.Save();
        }
        return new DataEditorViewModel(path, "Customers");
    }

    [Fact]
    public void Every_sheet_of_the_workbook_is_a_tab_by_default_and_the_filter_hides_some()
    {
        using var dir = new TempDir();
        var editor = OpenWithNotesSheet(dir);

        Assert.Equal([DataEditorViewModel.DataSheet, "config", "Notes"], editor.OpenSheets);
        Assert.Equal("All sheets", editor.SheetFilterText);

        editor.SheetChoices.Single(c => c.Name == "Notes").IsShown = false;

        Assert.Equal([DataEditorViewModel.DataSheet, "config"], editor.OpenSheets);
        Assert.Equal("2 of 3 sheets", editor.SheetFilterText);
        Assert.Contains("Notes", EditorSettings.Load().HiddenSheets[editor.WorkbookPath]);

        var reopened = new DataEditorViewModel(editor.WorkbookPath, "Customers");   // remembered per instance
        Assert.Equal([DataEditorViewModel.DataSheet, "config"], reopened.OpenSheets);
    }

    [Fact]
    public void Another_sheet_is_shown_read_only_with_its_rows()
    {
        using var dir = new TempDir();
        var editor = OpenWithNotesSheet(dir);

        editor.ActiveSheet = "Notes";

        Assert.True(editor.ShowSheetGrid);
        Assert.False(editor.ShowDataGrid);
        Assert.False(editor.SheetIsEditable);
        Assert.Equal(["topic", "text"], editor.SheetHeaders);
        Assert.Equal(["auth", "token expires hourly"], editor.SheetRows.Single().Cells);
    }

    [Fact]
    public void The_config_sheet_follows_the_selected_testcase_and_edits_write_through()
    {
        using var dir = new TempDir();
        var editor = OpenWithNotesSheet(dir);
        editor.ActiveSheet = "config";

        Assert.True(editor.SheetIsEditable);
        var shown = editor.SheetRows.Count;
        Assert.Equal(editor.StepRows.Count, shown);          // one testcase: its steps
        editor.ShowAllRows = true;
        Assert.True(editor.SheetRows.Count >= shown);

        var row = editor.SheetRows[0];
        var column = editor.SheetHeaders.ToList().FindIndex(h => h.Equals("timeout", StringComparison.OrdinalIgnoreCase));
        if (column < 0) column = editor.SheetHeaders.Count - 1;
        row.Cells[column] = "edited-in-sheet-grid";

        Assert.Equal("edited-in-sheet-grid", row.Row!.GetAt(column));
        Assert.True(editor.IsDirty);
        editor.ShowSteps = true;   // Config edit mode picks the edit up
        Assert.Contains(editor.StepRows, s => s.Row.GetAt(column) == "edited-in-sheet-grid");
    }

    [Fact]
    public void The_whole_instance_config_sheet_lists_every_step()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var editor = new DataEditorViewModel(path, "Scenarios");
        editor.ShowWholeInstance();
        editor.ActiveSheet = "config";

        Assert.Equal(editor.Testcases.Sum(t => t.Steps.Count), editor.SheetRows.Count);
        Assert.False(editor.SheetHasRowToggle);
    }
}

public class SheetTabsWindowTests
{
    [AvaloniaFact]
    public void The_sheet_grid_gets_its_columns_when_a_tab_is_chosen_and_the_data_grid_hides()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 860;
        window.Show();
        var vm = window.ViewModel;

        vm.ActiveSheet = "config";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var sheet = window.FindControl<Avalonia.Controls.DataGrid>("SheetGrid")!;
        Assert.True(sheet.IsVisible);
        Assert.False(window.FindControl<Avalonia.Controls.DataGrid>("Grid")!.IsVisible);
        Assert.Equal(vm.SheetHeaders.Count, sheet.Columns.Count);
        Assert.NotNull(window.FindControl<Avalonia.Controls.ListBox>("SheetTabs"));
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-sheet-tabs.png"));
    }
}

public class OverLimitPasteTests
{
    [Fact]
    public void A_value_longer_than_an_excel_cell_is_refused_and_the_old_value_stays()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2), "Customers");
        var row = editor.VisibleRows[0];
        var cell = editor.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex;

        row.Cells[cell] = new string('x', 40000);

        Assert.Equal("Customer 1", row.Cells[cell]);
        Assert.False(editor.IsDirty);
        Assert.True(editor.StatusIsError);
        Assert.Contains("32,767", editor.StatusMessage);
        Assert.Contains("<from_filepath>", editor.StatusMessage);

        row.Cells[cell] = new string('x', 32767);   // exactly the limit is fine
        Assert.Equal(32767, row.Cells[cell].Length);
    }
}
