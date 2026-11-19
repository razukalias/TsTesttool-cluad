using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// A big run (thousands of cells, most of them failed) must open, filter and switch steps without hanging or crashing, and "Failed columns only" shows just the failed columns.
public class LargeRunTests
{
    private const int Steps = 10, Rows = 120, Columns = 4;

    private static string BuildWorkbook(TempDir dir)
    {
        var path = dir.Combine("big.xlsx");
        using var workbook = new XLWorkbook();
        var config = workbook.AddWorksheet("config"); var request = workbook.AddWorksheet("request"); var response = workbook.AddWorksheet("response");
        string[] configHeaders = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb"];
        for (var i = 0; i < configHeaders.Length; i++) config.Cell(1, i + 1).Value = configHeaders[i];
        var requestHeaders = new List<string> { "testcaseindex", "testcase", "dataid", "dontrun" };
        var responseHeaders = new List<string> { "testcaseindex", "testcase", "dataid" };
        for (var s = 1; s <= Steps; s++)
        {
            string[] values = ["1", "Big", "S" + s, "HTTP", "t.json", "https://x/" + s, "POST"];
            for (var i = 0; i < values.Length; i++) config.Cell(s + 1, i + 1).Value = values[i];
            for (var c = 1; c <= Columns; c++) { requestHeaders.Add($"S{s}.f{c}"); responseHeaders.Add($"{{eq}}S{s}.r{c}"); }
        }
        for (var i = 0; i < requestHeaders.Count; i++) request.Cell(1, i + 1).Value = requestHeaders[i];
        for (var i = 0; i < responseHeaders.Count; i++) response.Cell(1, i + 1).Value = responseHeaders[i];
        for (var n = 1; n <= Rows; n++)
        {
            var id = "R" + n.ToString("000");
            request.Cell(n + 1, 1).Value = 1; request.Cell(n + 1, 2).Value = "Big"; request.Cell(n + 1, 3).Value = id;
            response.Cell(n + 1, 1).Value = 1; response.Cell(n + 1, 2).Value = "Big"; response.Cell(n + 1, 3).Value = id;
            for (var c = 5; c <= requestHeaders.Count; c++) request.Cell(n + 1, c).Value = $"value {n} {c}";
            for (var c = 4; c <= responseHeaders.Count; c++) response.Cell(n + 1, c).Value = $"expected {n} {c}";
        }
        workbook.SaveAs(path);
        return path;
    }

    /// <summary>The big instance with a run in which two out of three steps failed every assertion.</summary>
    private static string BuildWithRun(TempDir dir)
    {
        var path = BuildWorkbook(dir);
        var events = new List<object>();
        for (var n = 1; n <= Rows; n++)
            for (var s = 1; s <= Steps; s++)
            {
                var id = "R" + n.ToString("000");
                var fail = (n + s) % 3 != 0;
                events.Add(FakeRun.Request(1, id, "S" + s, result: fail ? "FAIL" : "PASS"));
                if (fail) for (var c = 1; c <= Columns; c++) events.Add(FakeRun.Assertion(1, id, "S" + s, "r" + c, "eq", $"expected {n} {4 + (s - 1) * Columns + c}", "actual", message: "Values differ"));
            }
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson", events.ToArray());
        return path;
    }

    [Fact]
    public void Thousands_of_failed_cells_are_listed_at_once_and_the_filters_stay_quick()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(BuildWithRun(dir), "Big");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        var notifications = 0;
        editor.FailedCells.CollectionChanged += (_, _) => notifications++;
        var rows = 0;
        editor.VisibleRows.CollectionChanged += (_, _) => rows++;

        var clock = Stopwatch.StartNew();
        editor.ShowFailedCells = true;
        editor.ShowFailedOnly = true;
        foreach (var step in new[] { "S1", "S5", "S9" }) editor.StepFilter = step;
        editor.StepFilter = editor.StepFilterOptions[0];
        editor.ShowFailedOnly = false;
        clock.Stop();

        Assert.True(editor.FailedCells.Count > 3000, $"only {editor.FailedCells.Count} failed cells");
        Assert.True(notifications <= 20, $"{notifications} list notifications (one per refresh expected)");   // not one per cell
        Assert.True(rows <= 40, $"{rows} grid notifications (one per refresh expected)");                    // not one per row
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed.TotalSeconds:0.0} s");
    }

    [Fact]
    public void A_cell_that_does_not_exist_in_a_row_has_no_failure_and_does_not_throw()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(BuildWithRun(dir), "Big");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        var row = editor.VisibleRows[0];
        // while another step is chosen the grid can still ask a new row about a column of the old column set
        Assert.Null(editor.FailureAt(row, row.Cells.Count + 40));
        Assert.Null(editor.FailureAt(row, -1));
        Assert.False(editor.CanAssign(row, row.Cells.Count + 5, new FailureDetail("S1", "r1", "eq", "x", "y", "m", "")));
    }

    [AvaloniaFact]
    public void Switching_steps_with_the_failed_views_on_does_not_crash_and_the_list_is_virtualized()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(BuildWithRun(dir), "Big");
        window.Width = 1440; window.Height = 820; window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.ViewModel;
        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));
        Dispatcher.UIThread.RunJobs();

        var clock = Stopwatch.StartNew();
        vm.ShowFailedOnly = true; Dispatcher.UIThread.RunJobs();
        vm.ShowFailedCells = true; Dispatcher.UIThread.RunJobs();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Tag as string == "failed-cells-list");
        Assert.True(vm.FailedCells.Count > 3000);
        Assert.True(window.GetVisualDescendants().OfType<Button>().Count(b => b.Tag as string == "failed-cell-assign") < 200, "the failed cells list draws every line");
        vm.ShowFailedCells = false; Dispatcher.UIThread.RunJobs();
        foreach (var step in new[] { "S1", "S5", "S9", "S2" }) { vm.StepFilter = step; Dispatcher.UIThread.RunJobs(); }
        vm.StepFilter = vm.StepFilterOptions[0]; Dispatcher.UIThread.RunJobs();
        vm.ShowFailedOnly = false; Dispatcher.UIThread.RunJobs();
        clock.Stop();

        Assert.NotNull(list);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed.TotalSeconds:0.0} s");
        window.Close();
    }

    [AvaloniaFact]
    public void Failed_columns_only_hides_every_column_without_a_failure_and_follows_assigning()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", status: 201),
            FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Kund 2", message: "Values differ"));
        var window = DataEditorWindow.Open(path, "Customers");
        window.Width = 1440; window.Height = 820; window.Show();
        var vm = window.ViewModel;
        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));
        Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<DataGrid>("Grid")!;
        var all = grid.Columns.Count(c => c.IsVisible);

        vm.ShowFailedColumns = true;
        Dispatcher.UIThread.RunJobs();
        var cell = vm.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.name")).CellIndex;
        Assert.Equal([cell], vm.FailedColumnCells.ToArray());
        var shown = grid.Columns.Where(c => c.IsVisible).ToList();
        Assert.Equal(4, shown.Count);                                         // tick, Data ID, result and the failed column
        Assert.Contains(shown, c => c.Tag is int t && t == cell);
        Assert.True(all > shown.Count);

        var row = vm.VisibleRows.First(r => r.CurrentDataId == "CUST002");
        vm.AssignActual(row, cell, vm.FailureAt(row, cell)!);                 // fixed: nothing failed any more
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.FailedColumnCells);

        vm.ShowFailedColumns = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(all, grid.Columns.Count(c => c.IsVisible));
        window.Close();
    }
}

public class EnvironmentChoiceTests
{
    [Fact]
    public void The_failed_views_can_be_limited_to_one_environment()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "x", env: "dev"),
            FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "y", env: "test"),
            FakeRun.Assertion(1, "CUST003", "CreateCustomer", "name", "eq", "Customer 3", "z", env: "test"));
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        Assert.Equal(["All environments", "dev", "test"], editor.EnvironmentOptions.ToArray());

        editor.ShowFailedOnly = true; editor.ShowFailedCells = true;
        Assert.Equal(3, editor.VisibleRows.Count);
        Assert.Equal(3, editor.FailedCells.Count);

        editor.EnvironmentChoice = "test";
        Assert.Equal(["CUST002", "CUST003"], editor.VisibleRows.Select(r => r.CurrentDataId).ToArray());
        Assert.Equal(2, editor.FailedCells.Count);
        Assert.All(editor.FailedCells, c => Assert.Equal("test", c.Environment));

        editor.EnvironmentChoice = "dev";
        Assert.Equal(["CUST001"], editor.VisibleRows.Select(r => r.CurrentDataId).ToArray());

        editor.EnvironmentChoice = EnvironmentChoiceTests_All;
        Assert.Equal(3, editor.VisibleRows.Count);
    }

    private const string EnvironmentChoiceTests_All = DataEditorViewModel.AllEnvironments;
}

public class TagAsBugTests
{
    [Fact]
    public void Any_expected_cell_can_be_tagged_as_a_known_bug_and_untagged_again()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var editor = new DataEditorViewModel(path, "Customers");
        var cell = editor.Columns.First(c => c.Group == ColumnGroup.Response).CellIndex;
        var row = editor.VisibleRows[0];
        var before = row.Cells[cell];

        editor.TagAsBug([row], cell, "dev,test", "login is slow");   // no run at all: the status does not matter
        var (value, tag) = LoadTestingTool.Execution.BugTag.Parse(row.Cells[cell]);
        Assert.Equal(before.Trim(), value);
        Assert.Equal(["dev", "test"], tag!.Environments.ToArray());
        Assert.Equal("login is slow", tag.Description);

        editor.TagAsBug([row], cell, "all", "again");                // replaces the tag, never stacks two
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(row.Cells[cell], "<bug"));

        editor.RemoveBugTag([row], cell);
        Assert.Equal(before.Trim(), row.Cells[cell].Trim());
        editor.UndoCommand.Execute(null);   // undo rebuilds the rows
        Assert.Contains("<bug", editor.VisibleRows[0].Cells[cell]);
    }

    [Fact]
    public void A_request_cell_cannot_be_tagged()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2), "Customers");
        var request = editor.Columns.First(c => c.Group == ColumnGroup.Request).CellIndex;
        var row = editor.VisibleRows[0];
        var before = row.Cells[request];
        editor.TagAsBug([row], request, "all");
        Assert.Equal(before, row.Cells[request]);
        Assert.False(editor.IsExpectedCell(request));
    }

    [Fact]
    public void A_failed_cell_gets_what_the_run_got_as_the_description()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Kund 1", env: "test"));
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        var cell = editor.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.name")).CellIndex;
        var row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST001");
        editor.TagAsBug([row], cell, "test");
        Assert.Equal("got Kund 1", LoadTestingTool.Execution.BugTag.Parse(row.Cells[cell]).Tag!.Description);
    }
}

public class BugMenuTests
{
    [AvaloniaFact]
    public void The_cell_menu_offers_the_bug_tag_on_expected_cells_and_not_on_request_cells()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 820; window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<DataGrid>("Grid")!;
        var editor = window.ViewModel;
        var expected = editor.Columns.First(c => c.Group == ColumnGroup.Response).CellIndex;
        var request = editor.Columns.First(c => c.Group == ColumnGroup.Request).CellIndex;

        MenuItem Bug(int cell)
        {
            editor.CellSelection.Select(editor.VisibleRows[0], cell);
            editor.CellSelection.ExtendTo(editor.VisibleRows[1], cell);   // the bug item shows for several cells
            return window.BuildCellMenu(editor, grid, grid).Items.OfType<MenuItem>().Single(i => (i.Header as string)!.Contains("known bug"));
        }
        Assert.True(Bug(expected).IsEnabled);
        Assert.Contains(Bug(expected).Items.OfType<MenuItem>(), i => (i.Header as string) == "In all environments");
        Assert.False(Bug(request).IsEnabled);
        editor.CellSelection.Select(editor.VisibleRows[0], expected);   // one cell: only Edit and the row actions
        var single = window.BuildCellMenu(editor, grid, grid).Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.DoesNotContain(single, h => h!.Contains("known bug") || h.Contains("Wizard"));
        Assert.Contains("Edit", single);
        window.Close();
    }
}

public class BugCountAndCommentTests
{
    [Fact]
    public void A_row_counts_its_bug_tagged_cells_beside_the_status()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2), "Customers");
        var expected = editor.Columns.Where(c => c.Group == ColumnGroup.Response).Select(c => c.CellIndex).Take(2).ToList();
        var row = editor.VisibleRows[0];
        Assert.Equal(string.Empty, row.BugTagText);
        editor.TagCellsAsBug([(row, expected[0]), (row, expected[1])], "all", "ticket 12");
        Assert.Equal("⚑ 2", row.BugTagText);
        editor.RemoveBugTags([(row, expected[0])]);
        Assert.Equal("⚑ 1", row.BugTagText);
    }

    [Fact]
    public void Changing_the_comment_keeps_the_environments()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2), "Customers");
        var cell = editor.Columns.First(c => c.Group == ColumnGroup.Response).CellIndex;
        var row = editor.VisibleRows[0];
        editor.TagCellsAsBug([(row, cell)], "test, dev", "old");
        var tag = LoadTestingTool.Execution.BugTag.Parse(row.Cells[cell]).Tag!;
        editor.TagCellsAsBug([(row, cell)], string.Join(", ", tag.Environments), "ticket 99");
        var after = LoadTestingTool.Execution.BugTag.Parse(row.Cells[cell]).Tag!;
        Assert.Equal("ticket 99", after.Description);
        Assert.Equal(tag.Environments, after.Environments);
    }
}

public class AutofitTests
{
    [AvaloniaFact]
    public void A_column_is_as_wide_as_its_longest_value()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 820; window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.ViewModel;
        var cell = editor.Columns.First(c => c.Group == ColumnGroup.Request).CellIndex;
        editor.VisibleRows[1].Cells[cell] = new string('W', 90);
        window.AutofitColumns();
        var grid = window.FindControl<DataGrid>("Grid")!;
        var column = grid.Columns.OfType<FormattedTextColumn>().First(c => c.Tag is int t && t == cell);
        Assert.True(column.MinWidth > 330, $"width {column.MinWidth}");
        window.Close();
    }
}

public class CellSelectionTests
{
    private static (DataEditorViewModel Editor, int Request, int Expected) Open(TempDir dir)
    {
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 4), "Customers");
        return (editor, editor.Columns.First(c => c.Group == ColumnGroup.Request).CellIndex, editor.Columns.First(c => c.Group == ColumnGroup.Response).CellIndex);
    }

    [Fact]
    public void Ctrl_click_adds_and_a_block_extends_but_only_within_one_kind()
    {
        using var dir = new TempDir();
        var (editor, request, expected) = Open(dir);
        var s = editor.CellSelection;
        s.Select(editor.VisibleRows[0], request);
        Assert.True(s.Toggle(editor.VisibleRows[2], request));
        Assert.Equal(2, s.Count);
        Assert.False(s.Toggle(editor.VisibleRows[1], expected));   // other kind refused
        Assert.Equal(2, s.Count);
        s.Select(editor.VisibleRows[0], request);
        s.ExtendTo(editor.VisibleRows[3], request);   // a block runs from the anchor, as in Excel
        Assert.Equal(4, s.Count);
        Assert.Equal("4 cells selected", editor.SelectionText);
        s.Clear();
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void Set_clear_and_paste_work_on_every_selected_cell_and_undo_in_one_step()
    {
        using var dir = new TempDir();
        var (editor, request, _) = Open(dir);
        var s = editor.CellSelection;
        s.Select(editor.VisibleRows[0], request);
        s.ExtendTo(editor.VisibleRows[2], request);
        editor.SetSelectedCells("X");
        Assert.All(editor.VisibleRows.Take(3), r => Assert.Equal("X", r.Cells[request]));
        editor.PasteIntoSelection([["Y"]]);
        Assert.All(editor.VisibleRows.Take(3), r => Assert.Equal("Y", r.Cells[request]));
        editor.SetSelectedCells("");
        Assert.All(editor.VisibleRows.Take(3), r => Assert.Equal("", r.Cells[request]));
        editor.UndoCommand.Execute(null);
        Assert.All(editor.VisibleRows.Take(3), r => Assert.Equal("Y", r.Cells[request]));
    }

    [Fact]
    public void Tagging_selected_expected_cells_writes_a_bug_tag_into_each()
    {
        using var dir = new TempDir();
        var (editor, _, expected) = Open(dir);
        var s = editor.CellSelection;
        s.Select(editor.VisibleRows[0], expected);
        s.ExtendTo(editor.VisibleRows[1], expected);
        editor.TagCellsAsBug(s.Cells, "all");
        Assert.All(editor.VisibleRows.Take(2), r => Assert.True(LoadTestingTool.Execution.BugTag.Parse(r.Cells[expected]).Tag is not null));
    }
}

public class WizardBugTagTests
{
    // The bar that the wizard and the failed-cell popup show: environment choice, "Tag as known bug" and "Remove tag".
    private static Avalonia.Controls.StackPanel Bar(DataEditorViewModel editor, IReadOnlyList<DataRowViewModel> rows, int cell, Action close)
        => (Avalonia.Controls.StackPanel)typeof(DataEditorWindow).GetMethod("BugTagBar", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [editor, rows, cell, close])!;

    [AvaloniaFact]
    public void The_bug_bar_of_the_wizard_tags_and_untags_the_cell_of_every_given_row()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Kund 2", env: "test"),
            FakeRun.Assertion(1, "CUST003", "CreateCustomer", "name", "eq", "Customer 3", "Kund 3", env: "dev"));
        var vm = new DataEditorViewModel(path, "Customers");
        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));
        var cell = vm.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.name")).CellIndex;
        var rows = vm.VisibleRows.Take(2).ToList();
        var closed = 0;

        var bar = Bar(vm, rows, cell, () => closed++);
        var environment = bar.Children.OfType<Avalonia.Controls.ComboBox>().Single();
        Assert.Equal(["all", "dev", "test"], environment.Items.OfType<string>().ToArray());
        var untag = bar.Children.OfType<Avalonia.Controls.Button>().Single(b => b.Tag as string == "bug-untag");
        Assert.False(untag.IsEnabled);                                   // nothing to remove yet

        environment.SelectedItem = "test";
        bar.Children.OfType<Avalonia.Controls.Button>().Single(b => b.Tag as string == "bug-tag").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Equal(1, closed);
        Assert.All(vm.VisibleRows.Take(2), r => Assert.Contains("<bug: test", r.Cells[cell]));
        Assert.True(Bar(vm, vm.VisibleRows.Take(2).ToList(), cell, () => { }).Children.OfType<Avalonia.Controls.Button>().Single(b => b.Tag as string == "bug-untag").IsEnabled);
    }
}

public class DashboardBugOverlayTests
{
    private static readonly System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

    [AvaloniaFact]
    public void A_tag_written_after_the_run_makes_the_failure_a_known_bug_in_the_dashboard()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var dashboard = new LoadTestingTool.UI.MainWindow();
        var item = Activator.CreateInstance(typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        var type = item.GetType();
        type.GetProperty("Name")!.SetValue(item, "Demo");
        type.GetProperty("WorkbookPath")!.SetValue(item, path);
        ((System.Collections.IList)type.GetProperty("Requests")!.GetValue(item)!).Add(new LoadTestingTool.UI.DashboardRequest("CreateCustomer", "HTTP", "Customers", "CUST002", "", "FAIL", 5, "Assertion", ""));
        var assertion = new LoadTestingTool.UI.AssertionInfo { TestcaseIndex = 1, Testcase = "Customers", DataId = "CUST002", StepName = "CreateCustomer", ResponsePath = "name", Result = "FAIL", ExpectedValue = "Customer 2", ActualValue = "Kund 2" };
        ((System.Collections.IList)type.GetProperty("Assertions")!.GetValue(item)!).Add(assertion);
        var scoped = typeof(LoadTestingTool.UI.MainWindow).GetMethod("ScopedAssertions", Flags)!;
        var requests = typeof(LoadTestingTool.UI.MainWindow).GetMethod("ScopedRequests", Flags)!;

        scoped.Invoke(dashboard, [item]);
        Assert.Equal("FAIL", assertion.Result);                           // not tagged: as recorded

        void SetCell(string text)
        {
            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("response");
                var column = sheet.Row(1).CellsUsed().First(c => c.GetString().EndsWith("CreateCustomer.name")).Address.ColumnNumber;
                var idColumn = sheet.Row(1).CellsUsed().First(c => c.GetString().Equals("dataid", StringComparison.OrdinalIgnoreCase)).Address.ColumnNumber;
                var row = sheet.RowsUsed().First(r => r.Cell(idColumn).GetString() == "CUST002").RowNumber();
                sheet.Cell(row, column).Value = text;
                workbook.Save();
            }
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(Random.Shared.Next(5, 5000)));   // the cache looks at the write time
        }

        SetCell("Customer 2 <bug: all; wrong name>");
        scoped.Invoke(dashboard, [item]);
        Assert.Equal("KNOWN_BUG", assertion.Result);
        Assert.True(assertion.KnownBug);
        Assert.Equal("wrong name", assertion.BugDescription);
        var list = (List<LoadTestingTool.UI.DashboardRequest>)requests.Invoke(dashboard, [item])!;
        Assert.Equal("PASS", list[0].Result);                             // the step only failed because of the bug

        SetCell("Customer 2");                                            // the tag is taken out again
        scoped.Invoke(dashboard, [item]);
        Assert.Equal("FAIL", assertion.Result);
        Assert.Equal("FAIL", ((List<LoadTestingTool.UI.DashboardRequest>)requests.Invoke(dashboard, [item])!)[0].Result);
    }
}

public class KnownBugStandingTests
{
    [Fact]
    public void A_row_whose_only_failure_is_a_tagged_known_bug_is_a_bug_not_a_failure()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Request(1, "CUST001", "CreateCustomer", status: 201),
            FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Customer 1", result: "PASS"),
            FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", status: 201),
            FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Kund 2", message: "Values differ"));
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        var cell = editor.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.name")).CellIndex;
        var row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST002");
        Assert.Equal("FAIL", row.ResultText);
        Assert.Equal("1 failed", row.CountsText);
        Assert.Contains("1 failed", editor.RunSummaryText);

        editor.ShowFailedOnly = true;
        Assert.Contains(row, editor.VisibleRows);
        editor.TagAsBug([row], cell, "all");                           // no re-run needed
        Assert.Equal("BUG", row.ResultText);
        Assert.Contains("0 failed", editor.RunSummaryText);
        Assert.Contains("1 known bug", editor.RunSummaryText);
        Assert.Contains("0 failed cells, 1 bug cell", editor.RunSummaryText);
        Assert.Equal("1 bug", row.CountsText);
        Assert.Null(editor.FailureAt(row, cell));                      // no red mark
        editor.ShowFailedOnly = false; editor.ShowFailedOnly = true;
        Assert.DoesNotContain(editor.VisibleRows, r => r.CurrentDataId == "CUST002");   // not listed as a failed row

        editor.RemoveBugTag([editor.VisibleRows.FirstOrDefault() ?? row], cell);
    }

    [Fact]
    public void A_bug_tag_for_another_environment_does_not_hide_the_failure()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", status: 201),
            FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Kund 2", env: "prod"));
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        var cell = editor.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.name")).CellIndex;
        var row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST002");
        editor.TagAsBug([row], cell, "dev");
        Assert.Equal("FAIL", row.ResultText);
    }
}

public class BugColumnsOnlyTests
{
    [AvaloniaFact]
    public void Bug_columns_only_keeps_the_columns_with_a_bug_tag_and_follows_tagging()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 820; window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = window.ViewModel;
        var grid = window.FindControl<DataGrid>("Grid")!;
        var all = grid.Columns.Count(c => c.IsVisible);
        var cell = vm.Columns.First(c => c.Group == ColumnGroup.Response).CellIndex;

        vm.ShowBugColumns = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.BugColumnCells);
        Assert.DoesNotContain(grid.Columns.Where(c => c.IsVisible), c => c.Tag is int t && t >= 1);   // nothing tagged: only Data ID, tick and result stay

        vm.TagAsBug([vm.VisibleRows[1]], cell, "all", "known");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal([cell], vm.BugColumnCells.ToArray());
        Assert.Single(grid.Columns.Where(c => c.IsVisible && c.Tag is int t && t >= 1));

        vm.ShowBugColumns = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(all, grid.Columns.Count(c => c.IsVisible));
        window.Close();
    }
}

public class AssertionCategoryTests
{
    [Fact]
    public void A_step_that_failed_only_because_of_its_assertions_is_not_a_second_failure_and_the_tooltip_lists_each_failure()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", status: 200, category: "Assertion", env: "dev"),
            FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Kund 2", env: "dev"));
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        var row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST002");
        Assert.Equal("1 failed", row.CountsText);                       // the assertion; the step is explained by it
        Assert.Contains("FAILED CreateCustomer.name [dev]", row.CountsDetail);
        var cell = editor.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer.name")).CellIndex;
        editor.TagAsBug([row], cell, "all");
        Assert.Equal("BUG", row.ResultText);
        Assert.Contains("BUG    CreateCustomer.name [dev]", row.CountsDetail);
    }
}
