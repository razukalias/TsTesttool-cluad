using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Threading;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

/// <summary>Green cells (passed, with the response value), yellow cells (field not in the response), extra columns for fields the workbook has no column for, and their filters.</summary>
public class CellMarkTests
{
    private static string Event(string kind, string env, string id, string path, string verb, string result, string actual, string message = "", string expected = "")
        => JsonSerializer.Serialize(new { eventType = kind, runId = "20261125_130000_cellmark001", testcaseIndex = 1, dataId = id, stepName = "CreateCustomer", responsePath = path, assertionVerb = verb, expectedValue = expected, actualValue = actual, result, failureMessage = message, environment = env, httpStatus = 201, durationMs = 5 });

    private static (DataEditorWindow Window, DataEditorViewModel Editor) OpenWithRun(TempDir dir, params string[] extraLines)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        var folder = Path.Combine(Path.GetDirectoryName(editor.WorkbookPath)!, "Results", "failed", "Run_1", "log");
        Directory.CreateDirectory(folder);
        var lines = new List<string>
        {
            Event("request-completed", "dev", "CUST001", "", "", "FAIL", ""),
            Event("assertion-completed", "dev", "CUST001", "id", "eq", "PASS", "C1", expected: "C1"),
            Event("assertion-completed", "dev", "CUST001", "statusCode", "eq", "PASS", "201", expected: "201"),
            Event("assertion-completed", "dev", "CUST001", "name", "notexists", "PASS", "<missing>"),   // passed because the field is not there
            Event("assertion-completed", "dev", "CUST001", "tags", "contract", "FAIL", "vip", "Unexpected response path 'tags' returned by payload."),
            Event("assertion-completed", "dev", "CUST001", "notes", "contract", "FAIL", "", "Unexpected response path 'notes' returned by payload."),   // empty: no column
        };
        lines.AddRange(extraLines);
        File.WriteAllLines(Path.Combine(folder, "events_20261125_130000_cellmark001_dev.ndjson"), lines);
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        Dispatcher.UIThread.RunJobs();
        return (window, editor);
    }

    private static int Cell(DataEditorViewModel editor, string path) => editor.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer." + path, StringComparison.OrdinalIgnoreCase)).CellIndex;

    [AvaloniaFact]
    public void A_passed_cell_is_green_with_its_value_and_a_missing_field_is_yellow()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir);
        var row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST001");
        var id = editor.PassAt(row, Cell(editor, "id"));
        Assert.NotNull(id); Assert.False(id!.Absent); Assert.Equal("C1", id.Value);
        var name = editor.PassAt(row, Cell(editor, "name"));
        Assert.NotNull(name); Assert.True(name!.Absent); Assert.Equal(DataEditorViewModel.MissingText, name.Value);
        Assert.Null(editor.PassAt(editor.VisibleRows.First(r => r.CurrentDataId == "CUST002"), Cell(editor, "id")));   // a row the run did not touch
        window.Close();
    }

    [AvaloniaFact]
    public void The_table_draws_the_green_and_the_yellow_mark()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir);
        window.Height = 900; window.Width = 1600;
        Dispatcher.UIThread.RunJobs();
        var texts = window.GetVisualDescendants().OfType<Border>().Where(b => (string?)b.Tag == "pass-mark" && b.IsEffectivelyVisible).Select(b => (b.Child as TextBlock)?.Text).ToList();
        Assert.Contains("✓ C1", texts);
        Assert.Contains("∅ " + DataEditorViewModel.MissingText, texts);
        if (Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            editor.ShowPassedColumns = true; editor.ShowMissingColumns = true; Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(shots, "cell-marks.png"));
        }
        window.Close();
    }

    [AvaloniaFact]
    public void A_field_with_a_value_and_no_column_becomes_an_extra_column_after_the_environment_column_but_an_empty_one_does_not()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir);
        Assert.Equal(["CreateCustomer.tags"], editor.ExtraColumnKeys);
        Assert.Equal("vip", editor.ExtraValue(editor.VisibleRows.First(r => r.CurrentDataId == "CUST001"), "CreateCustomer.tags"));
        var grid = window.FindControl<DataGrid>("Grid")!;
        var extra = grid.Columns.Where(c => c.Tag is "extra").ToList();
        Assert.Single(extra);
        Assert.True(grid.Columns.IndexOf(extra[0]) <= 4, "right after the tick, Data ID, result and environment columns");
        window.Close();
    }

    [AvaloniaFact]
    public void The_passed_and_missing_column_filters_keep_only_their_columns()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir);
        editor.ShowPassedColumns = true;
        Assert.Contains(Cell(editor, "id"), editor.PassedColumnCells);
        Assert.Contains(Cell(editor, "statusCode"), editor.PassedColumnCells);
        Assert.DoesNotContain(Cell(editor, "name"), editor.PassedColumnCells);
        editor.ShowPassedColumns = false;
        editor.ShowMissingColumns = true;
        Assert.Equal([Cell(editor, "name")], editor.MissingColumnCells.ToList());
        Assert.Contains("Missing columns", editor.ShowOptionsTitle);
        var grid = window.FindControl<DataGrid>("Grid")!;
        Dispatcher.UIThread.RunJobs();
        var shown = grid.Columns.Where(c => c.IsVisible && c.Tag is int cell && cell > 0).Select(c => (int)c.Tag!).ToList();
        Assert.Equal([Cell(editor, "name")], shown);
        window.Close();
    }

    [AvaloniaFact]
    public void A_failed_cell_stays_red_and_has_no_green_mark()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir, Event("assertion-completed", "dev", "CUST001", "id", "eq", "FAIL", "C9", "Assertion failed", expected: "C1"));
        var row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST001");
        Assert.NotNull(editor.FailureAt(row, Cell(editor, "id")));
        window.Close();
    }

    [AvaloniaFact]
    public void Passed_rows_only_keeps_the_rows_that_passed_and_switches_failed_rows_only_off()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir, Event("request-completed", "dev", "CUST002", "", "", "PASS", ""), Event("assertion-completed", "dev", "CUST002", "id", "eq", "PASS", "C2", expected: "C2"));
        editor.ShowPassedOnly = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["CUST002"], editor.VisibleRows.Select(r => r.CurrentDataId).ToList());   // CUST001 failed, CUST003 did not run
        Assert.Contains("Passed rows", editor.ShowOptionsTitle);
        editor.ShowFailedOnly = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(editor.ShowPassedOnly);
        Assert.Equal(["CUST001"], editor.VisibleRows.Select(r => r.CurrentDataId).ToList());
        window.Close();
    }

    [AvaloniaFact]
    public void An_extra_column_becomes_a_real_expected_column_filled_with_the_run_values_and_undo_takes_it_back()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir);
        var grid = window.FindControl<DataGrid>("Grid")!;
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => (string?)b.Tag == "extra-add");
        editor.AddExtraColumn("CreateCustomer.tags");
        Dispatcher.UIThread.RunJobs();
        var cell = Cell(editor, "tags");
        Assert.Equal("vip", editor.VisibleRows.First(r => r.CurrentDataId == "CUST001").Cells[cell]);
        Assert.Equal("", editor.VisibleRows.First(r => r.CurrentDataId == "CUST002").Cells[cell] ?? "");   // the run did not return it there
        Assert.Empty(editor.ExtraColumnKeys);
        Assert.True(!grid.Columns.Any(c => c.Tag is "extra"), "the extra column is still in the grid");
        Assert.Contains(editor.Columns, c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.tags"));
        editor.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(!editor.Columns.Any(c => c.Title.EndsWith("CreateCustomer.tags")), "undo left the column: " + string.Join(",", editor.Columns.Select(c => c.Title)));
        window.Close();
    }
}
