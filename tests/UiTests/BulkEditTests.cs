using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class BulkEditTests
{
    private static int CellOf(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    // ---- Clipboard text ------------------------------------------------------------------

    [Fact]
    public void Parses_what_excel_puts_on_the_clipboard()
    {
        var table = ClipboardTable.Parse("a\tb\tc\r\n1\t\t3\r\n");
        Assert.Equal(2, table.Length);
        Assert.Equal(["a", "b", "c"], table[0]);
        Assert.Equal(["1", "", "3"], table[1]);
    }

    [Fact]
    public void Quoted_cells_may_hold_tabs_quotes_and_line_breaks()
    {
        var table = ClipboardTable.Parse("\"two\nlines\"\t\"say \"\"hi\"\"\"\t\"a\tb\"\nlast\tx");
        Assert.Equal(2, table.Length);
        Assert.Equal(["two\nlines", "say \"hi\"", "a\tb"], table[0]);
        Assert.Equal(["last", "x"], table[1]);
    }

    [Fact]
    public void Format_and_parse_round_trip()
    {
        string[][] rows = [["plain", "with\ttab", "quote\"d"], ["", "multi\nline", "x"]];
        Assert.Equal(rows, ClipboardTable.Parse(ClipboardTable.Format(rows)));
    }

    // ---- Paste ---------------------------------------------------------------------------

    [Fact]
    public void Pasting_a_block_fills_cells_from_the_current_one_and_is_one_undo_step()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 5);
        var vm = new DataEditorViewModel(path);
        var name = CellOf(vm, "CreateCustomer.name");

        vm.PasteBlock(1, name, ClipboardTable.Parse("Ann\tann@x.test\tSE\nBo\tbo@x.test\tNO\nCy\tcy@x.test\tFI"));

        Assert.Equal(["Ann", "ann@x.test", "SE"], vm.VisibleRows[1].Cells.Skip(name).Take(3));
        Assert.Equal("Cy", vm.VisibleRows[3].Cells[name]);
        Assert.Equal("Customer 1", vm.VisibleRows[0].Cells[name]);
        Assert.Contains("Pasted 9 cells", vm.StatusMessage);

        vm.SaveCommand.Execute(null);
        Assert.Equal("bo@x.test", new WorkbookReader().Read(path).RequestData[(1, "CUST003")].Variables["CreateCustomer.email"]);
    }

    [Fact]
    public void One_undo_reverts_the_whole_paste()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 5));
        var name = CellOf(vm, "CreateCustomer.name");
        vm.PasteBlock(0, name, ClipboardTable.Parse("A\nB\nC\nD\nE\nF\nG"));
        Assert.Equal(7, vm.VisibleRows.Count);

        vm.UndoCommand.Execute(null);

        Assert.Equal(5, vm.VisibleRows.Count);
        Assert.Equal("Customer 1", vm.VisibleRows[0].Cells[name]);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Pasting_past_the_last_row_adds_rows_with_new_data_ids()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        var name = CellOf(vm, "CreateCustomer.name");

        vm.PasteBlock(2, name, ClipboardTable.Parse("Last old\nNew 1\nNew 2"));

        Assert.Equal(5, vm.VisibleRows.Count);
        Assert.Equal(["CUST004", "CUST005"], vm.VisibleRows.Skip(3).Select(r => r.Cells[0]));
        Assert.Equal(5, vm.SelectedTestcase!.RowCount);
        Assert.True(vm.HasNoProblems);
        vm.SaveCommand.Execute(null);
        Assert.Equal("New 2", new WorkbookReader().Read(path).RequestData[(1, "CUST005")].Variables["CreateCustomer.name"]);
    }

    [Fact]
    public void With_a_filter_active_no_rows_are_added_and_the_user_is_told()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 5));
        vm.FilterText = "CUST00";
        var name = CellOf(vm, "CreateCustomer.name");

        vm.PasteBlock(4, name, ClipboardTable.Parse("One\nTwo\nThree"));

        Assert.Equal(5, vm.VisibleRows.Count);
        Assert.Equal("One", vm.VisibleRows[4].Cells[name]);
        Assert.Contains("2 lines past the last row skipped", vm.StatusMessage);
    }

    [Fact]
    public void Cells_past_the_last_column_and_clashing_ids_are_skipped_not_fatal()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        var last = vm.Columns[^1].CellIndex;

        vm.PasteBlock(0, last, ClipboardTable.Parse("v1\textra\textra2"));
        Assert.Equal("v1", vm.VisibleRows[0].Cells[last]);
        Assert.Contains("2 skipped", vm.StatusMessage);

        vm.PasteBlock(0, 0, ClipboardTable.Parse("CUST002")); // already taken
        Assert.Equal("CUST001", vm.VisibleRows[0].Cells[0]);
        Assert.Contains("Nothing changed", vm.StatusMessage);
    }

    [Fact]
    public void Pasting_unchanged_values_creates_no_history()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        var name = CellOf(vm, "CreateCustomer.name");

        vm.PasteBlock(0, name, [[vm.VisibleRows[0].Cells[name]]]);

        Assert.False(vm.CanUndo);
        Assert.False(vm.IsDirty);
    }

    // ---- Fill down -----------------------------------------------------------------------

    [Fact]
    public void Fill_down_copies_the_first_selected_rows_value_and_undoes_in_one_step()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir);
        var vm = new DataEditorViewModel(path);
        var country = CellOf(vm, "CreateCustomer.country");
        vm.VisibleRows[0].Cells[country] = "SE";

        vm.FillDown(vm.VisibleRows.Take(100).ToList(), country);

        Assert.All(vm.VisibleRows.Take(100), r => Assert.Equal("SE", r.Cells[country]));
        Assert.Equal("NL", vm.VisibleRows[100].Cells[country]); // beyond the selection: untouched
        vm.SaveCommand.Execute(null);
        Assert.Equal("SE", new WorkbookReader().Read(path).RequestData[(1, "CUST050")].Variables["CreateCustomer.country"]);
    }

    [Fact]
    public void Fill_down_refuses_the_data_id_column_and_a_single_row()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 4));

        vm.FillDown(vm.VisibleRows.Take(3).ToList(), 0);
        Assert.Contains("Data IDs must stay unique", vm.StatusMessage);
        vm.FillDown(vm.VisibleRows.Take(1).ToList(), 2);
        Assert.Contains("Select two or more rows", vm.StatusMessage);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Fill_down_into_expected_columns_creates_the_missing_response_value()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 6));
        var status = CellOf(vm, "CreateCustomer.statusCode");
        vm.VisibleRows[0].Cells[status] = "200";

        vm.FillDown(vm.VisibleRows.ToList(), status);

        Assert.All(vm.VisibleRows, r => Assert.Equal("200", r.Cells[status]));
    }

    // ---- Find & replace ------------------------------------------------------------------

    [Fact]
    public void Find_counts_matches_and_replace_all_changes_them_in_one_undo_step()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir);
        var vm = new DataEditorViewModel(path);
        vm.OpenFindCommand.Execute(null);

        vm.FindText = "@example.test";
        Assert.Equal("300 matches in 300 cells", vm.MatchSummary);

        vm.ReplaceText = "@staging.test";
        vm.ReplaceAllCommand.Execute(null);

        var email = CellOf(vm, "CreateCustomer.email");
        Assert.Equal("customer7@staging.test", vm.VisibleRows[6].Cells[email]);
        Assert.Equal("No matches", vm.MatchSummary);
        Assert.Contains("Replaced 300 matches in 300 cells", vm.StatusMessage);

        vm.SaveCommand.Execute(null);
        Assert.Equal("customer300@staging.test", new WorkbookReader().Read(path).RequestData[(1, "CUST300")].Variables["CreateCustomer.email"]);
    }

    [Fact]
    public void Replace_all_undoes_in_one_step()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 20));
        vm.OpenFindCommand.Execute(null);
        vm.FindText = "customer";
        vm.ReplaceText = "client";
        vm.ReplaceAllCommand.Execute(null);
        var email = CellOf(vm, "CreateCustomer.email");
        Assert.StartsWith("client", vm.VisibleRows[0].Cells[email]);

        vm.UndoCommand.Execute(null);

        Assert.StartsWith("customer1@", vm.VisibleRows[0].Cells[email]);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Match_case_and_whole_cell_change_what_matches()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 12));
        vm.OpenFindCommand.Execute(null);

        vm.FindText = "customer 1";
        Assert.Equal("8 matches in 8 cells", vm.MatchSummary); // "Customer 1", 10, 11, 12 in both the name and the expected-name column
        vm.FindMatchCase = true;
        Assert.Equal("No matches", vm.MatchSummary);

        vm.FindMatchCase = false;
        vm.FindWholeCell = true;
        Assert.Equal("2 matches in 2 cells", vm.MatchSummary);

        vm.ReplaceText = "ONLY";
        vm.ReplaceAllCommand.Execute(null);
        var name = CellOf(vm, "CreateCustomer.name");
        Assert.Equal("ONLY", vm.VisibleRows[0].Cells[name]);
        Assert.Equal("Customer 10", vm.VisibleRows[9].Cells[name]);
    }

    [Fact]
    public void Scope_can_be_limited_to_the_current_column_and_to_the_rows_shown()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 12));
        var country = CellOf(vm, "CreateCustomer.country");
        vm.OpenFindCommand.Execute(null);
        vm.FindText = "m"; // in every name ("Customer"), never in a country code

        vm.FindScope = DataEditorViewModel.ScopeCurrentColumn;
        Assert.Contains("Click a cell", vm.MatchSummary);
        vm.CurrentCellIndex = country;
        Assert.Equal("No matches", vm.MatchSummary);

        var name = CellOf(vm, "CreateCustomer.name");
        vm.CurrentCellIndex = name;
        Assert.Equal("12 matches in 12 cells", vm.MatchSummary);

        vm.FilterText = "CUST001"; // only the rows shown are searched
        vm.ReplaceText = "M";
        vm.ReplaceAllCommand.Execute(null);
        vm.FilterText = "";
        Assert.Equal("CustoMer 1", vm.VisibleRows[0].Cells[name]);
        Assert.Equal("Customer 2", vm.VisibleRows[1].Cells[name]);
    }

    [Fact]
    public void Replacing_with_nothing_to_find_is_refused()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        vm.ReplaceAllCommand.Execute(null);
        Assert.True(vm.StatusIsError);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanUndo);
    }

    [AvaloniaFact]
    public void Renders_the_find_panel_with_several_rows_selected()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 14), "Customers");
        window.Width = 1440;
        window.Height = 860;
        window.Show();
        var vm = window.ViewModel;
        vm.OpenFindCommand.Execute(null);
        vm.FindText = "customer";
        vm.ReplaceText = "client";
        var grid = window.FindControl<DataGrid>("Grid")!;
        foreach (var row in vm.VisibleRows.Skip(2).Take(4)) grid.SelectedItems.Add(row);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, grid.SelectedItems.Count);
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.GetTempPath();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-find.png"));
        window.Close();
    }
}
