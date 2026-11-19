using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Failed cells: the table marks the expected-value cell a failed assertion points at, and the run's actual value can be assigned to it.
public class FailedCellsTests
{
    private static DataEditorViewModel Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        var folder = FakeRun.Folder(path);
        FakeRun.Write(folder, $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Request(1, "CUST001", "CreateCustomer", status: 201),
            FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Customer 1", result: "PASS"),
            FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", status: 201),
            FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Kund 2", message: "Values differ"));
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        return editor;
    }

    private static (DataRowViewModel Row, int Cell) Failed(DataEditorViewModel editor)
        => (editor.VisibleRows.First(r => r.CurrentDataId == "CUST002"), editor.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.name")).CellIndex);

    [Fact]
    public void A_failed_assertion_points_at_its_cell_and_only_that_cell()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var (row, cell) = Failed(editor);
        Assert.Equal("Kund 2", editor.FailureAt(row, cell)!.Actual);
        Assert.Null(editor.FailureAt(editor.VisibleRows.First(r => r.CurrentDataId == "CUST001"), cell));
        Assert.Null(editor.FailureAt(row, cell + 1));
    }

    [Fact]
    public void Assigning_writes_the_actual_value_clears_the_mark_and_can_be_undone()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var (row, cell) = Failed(editor);
        var before = row.Cells[cell];

        Assert.True(editor.AssignActual(row, cell, editor.FailureAt(row, cell)!));

        Assert.Equal("Kund 2", row.Cells[cell]);
        Assert.Null(editor.FailureAt(row, cell));
        editor.UndoCommand.Execute(null);
        row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST002");   // undo rebuilds the rows
        Assert.Equal(before, row.Cells[cell]);
        Assert.NotNull(editor.FailureAt(row, cell));
    }

    [Fact]
    public void A_verb_written_in_the_cell_stays_when_the_value_is_assigned()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var (row, cell) = Failed(editor);
        row.Cells[cell] = "{contains}Customer 2";

        editor.AssignActual(row, cell, editor.FailureAt(row, cell)!);

        Assert.Equal("{contains}Kund 2", row.Cells[cell]);
    }

    [Fact]
    public void A_cell_with_a_reference_or_generated_value_is_not_overwritten()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var (row, cell) = Failed(editor);
        row.Cells[cell] = "<From_request_name_2>";
        var failure = editor.FailureAt(row, cell)!;

        Assert.False(editor.CanAssign(row, cell, failure));
        Assert.False(editor.AssignActual(row, cell, failure));
        Assert.Equal("<From_request_name_2>", row.Cells[cell]);
    }

    [Fact]
    public void The_failed_cells_list_has_one_line_per_failed_cell_with_its_header()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.ShowFailedCells = true;
        var line = Assert.Single(editor.FailedCells);
        Assert.Equal("CUST002", line.DataId);
        Assert.EndsWith("CreateCustomer.name", line.Header);
        Assert.Equal("Kund 2", line.Actual);
        Assert.True(line.CanAssign);
        Assert.False(editor.ShowTableGrid);
        Assert.True(editor.ShowFailedCellsList);

        editor.AssignActual(line.Row, line.CellIndex, editor.FailureAt(line.Row, line.CellIndex)!);
        Assert.Empty(editor.FailedCells);
    }

    [Fact]
    public void The_list_follows_undo_typing_and_the_row_filters_while_it_is_open()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var (row, cell) = Failed(editor);
        editor.ShowFailedCells = true;
        Assert.Single(editor.FailedCells);

        editor.AssignActual(row, cell, editor.FailureAt(row, cell)!);
        Assert.Empty(editor.FailedCells);
        editor.UndoCommand.Execute(null);
        Assert.Single(editor.FailedCells);                       // back after Undo, without toggling the box

        row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST002");
        row.Cells[cell] = "Kund 2";                              // typed by hand: no longer a failed cell
        Assert.Empty(editor.FailedCells);
        row.Cells[cell] = "Something else";
        Assert.Single(editor.FailedCells);

        editor.FilterText = "CUST001";                           // CUST002 is no longer shown
        Assert.Empty(editor.FailedCells);
        editor.FilterText = "";
        Assert.Single(editor.FailedCells);
    }

    [AvaloniaFact]
    public void The_red_mark_shows_on_the_failed_cell_in_the_table_view()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", status: 201),
            FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Kund 2"));
        var window = DataEditorWindow.Open(path, "Customers");
        window.Width = 1440; window.Height = 700; window.Show();
        window.ViewModel.ApplyResults(RunResultsReader.ReadLatest(window.ViewModel.ResultsFolder));
        Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<DataGrid>("Grid")!;
        var failedRow = window.ViewModel.VisibleRows.First(r => r.CurrentDataId == "CUST002");
        var failedCell = window.ViewModel.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.name")).CellIndex;
        grid.ScrollIntoView(failedRow, grid.Columns.First(c => c.Tag is int tag && tag == failedCell));
        Dispatcher.UIThread.RunJobs();
        var all = grid.GetVisualDescendants().OfType<Button>().Where(b => b.Tag as string == "failed-cell").ToList();
        var marks = all.Where(b => b.IsVisible).ToList();
        Assert.True(all.Count > 0, $"no mark buttons at all; columns={grid.Columns.Count}");
        Assert.Single(marks);
        Assert.Equal("CUST002", ((DataRowViewModel)marks[0].FindAncestorOfType<DataGridCell>()!.DataContext!).CurrentDataId);
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(Path.Combine(shots, "failed-cell-mark.png"));
        window.ViewModel.ShowFailedCells = true;
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(Path.Combine(shots, "failed-cells-list.png"));
        window.Close();
    }

    [Fact]
    public void A_cell_that_failed_in_several_environments_is_listed_once_with_all_of_them()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Kund 1", env: "test"),
            FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Kund 1", env: "dev"));
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        editor.ShowFailedCells = true;
        var line = Assert.Single(editor.FailedCells);
        Assert.Contains("test", line.Environment);
        Assert.Contains("dev", line.Environment);
    }
}
