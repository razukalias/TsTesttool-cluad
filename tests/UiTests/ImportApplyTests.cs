using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ImportApplyTests
{
    private static DataEditorViewModel Open(TempDir dir, int rows = 3) => new(Samples.CreateLarge(dir, rows: rows));
    private static string[] Ids(DataEditorViewModel vm) => vm.VisibleRows.Select(r => r.Cells[0]).ToArray();
    private static int Cell(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    private static ImportPlan Plan(string csv, ImportMode mode, Action<ImportPlanBuilder>? configure = null)
    {
        var table = ImportSource.FromCsvText("t.csv", csv).Slice(0, ImportRange.Whole, headerRow: 1);
        var builder = new ImportPlanBuilder { Mode = mode };
        configure?.Invoke(builder);
        return builder.Build(table);
    }

    private sealed class ImportPlanBuilder
    {
        public ImportMode Mode; public ImportMatch Match = ImportMatch.ByHeader; public int[]? Manual; public bool CreateUnmatched, ClearOnEmpty;
        public ImportRowMatch RowMatch = ImportRowMatch.DataId; public string? Step; public bool Expected; public ImportPlacement Placement; public Func<int, bool>? Column; public Func<int, bool>? Row;
        public ImportPlan Build(ImportTable table) => new()
        {
            Table = table, Mode = Mode, Match = Match, Manual = Manual, CreateUnmatched = CreateUnmatched, ClearOnEmpty = ClearOnEmpty, RowMatch = RowMatch,
            Step = Step, Expected = Expected, Placement = Placement,
            TakeColumn = Enumerable.Range(0, table.ColumnCount).Select(i => Column?.Invoke(i) ?? true).ToArray(),
            TakeRow = Enumerable.Range(0, table.Rows.Count).Select(i => Row?.Invoke(i) ?? true).ToArray(),
        };
    }

    [Fact]
    public void Append_adds_rows_at_the_end_matching_columns_by_header_and_numbers_a_taken_Data_ID()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var report = vm.ApplyImport(Plan("Data ID,CreateCustomer.name,CreateCustomer.country,Unknown\nNEW1,Zed,SE,x\nCUST001,Yan,NO,y\n,Xi,FI,z", ImportMode.AppendRows));
        Assert.Equal(2 + 1, report.Added);
        var ids = Ids(vm);
        Assert.Equal(6, ids.Length);
        Assert.Equal("NEW1", ids[3]);
        Assert.Equal("CUST004", ids[4]);                 // CUST001 exists: numbered
        Assert.Equal("CUST005", ids[5]);                 // no ID in the file: the next free one
        Assert.Equal("Yan", vm.VisibleRows[4].Cells[Cell(vm, "CreateCustomer.name")]);
        Assert.Equal("FI", vm.VisibleRows[5].Cells[Cell(vm, "CreateCustomer.country")]);
        Assert.Contains("CUST001 → CUST004", vm.StatusMessage);
        vm.UndoCommand.Execute(null);
        Assert.Equal(3, vm.VisibleRows.Count);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Only_ticked_rows_and_columns_are_taken()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var csv = "Data ID,CreateCustomer.name,CreateCustomer.country\nA,n1,c1\nB,n2,c2\nC,n3,c3";
        vm.ApplyImport(Plan(csv, ImportMode.AppendRows, b => { b.Row = i => i != 1; b.Column = i => i != 2; }));
        Assert.Equal(["A", "C"], Ids(vm)[3..]);
        Assert.Equal("n3", vm.VisibleRows[4].Cells[Cell(vm, "CreateCustomer.name")]);
        Assert.Equal(string.Empty, vm.VisibleRows[4].Cells[Cell(vm, "CreateCustomer.country")]);
    }

    [Fact]
    public void Insert_below_puts_the_rows_under_the_selected_row_in_file_order()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        vm.SelectedRow = vm.VisibleRows[0];
        vm.ApplyImport(Plan("Data ID,CreateCustomer.name\nA,n1\nB,n2", ImportMode.InsertRowsBelow));
        Assert.Equal(["CUST001", "A", "B", "CUST002", "CUST003"], Ids(vm));
    }

    [Fact]
    public void Replace_removes_the_old_rows_and_keeps_the_file_Data_IDs()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var report = vm.ApplyImport(Plan("Data ID,CreateCustomer.name\nCUST001,fresh\nCUST002,fresher", ImportMode.ReplaceRows));
        Assert.Equal(3, report.Removed);
        Assert.Equal(["CUST001", "CUST002"], Ids(vm));
        Assert.Equal("fresh", vm.VisibleRows[0].Cells[Cell(vm, "CreateCustomer.name")]);
        Assert.True(ImportApplyHelpers.NeedsConfirm(Plan("a\n1", ImportMode.ReplaceRows)));
        vm.UndoCommand.Execute(null);
        Assert.Equal(["CUST001", "CUST002", "CUST003"], Ids(vm));
        Assert.Equal("Customer 1", vm.VisibleRows[0].Cells[Cell(vm, "CreateCustomer.name")]);
    }

    [Fact]
    public void Fill_existing_rows_matches_by_Data_ID_adds_none_and_empty_cells_keep_or_clear()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var csv = "Data ID,CreateCustomer.name,CreateCustomer.country\nCUST002,Changed,\nNOPE,Ghost,SE";
        var keep = vm.ApplyImport(Plan(csv, ImportMode.FillExistingRows));
        Assert.Equal(1, keep.Updated);
        Assert.Equal(1, keep.NotFound);
        Assert.Equal(3, vm.VisibleRows.Count);
        Assert.Equal("Changed", vm.VisibleRows[1].Cells[Cell(vm, "CreateCustomer.name")]);
        var country = vm.VisibleRows[1].Cells[Cell(vm, "CreateCustomer.country")];
        Assert.NotEmpty(country);                                        // the empty file cell kept the value
        vm.ApplyImport(Plan(csv, ImportMode.FillExistingRows, b => b.ClearOnEmpty = true));
        Assert.Equal(string.Empty, vm.VisibleRows[1].Cells[Cell(vm, "CreateCustomer.country")]);
        Assert.True(ImportApplyHelpers.NeedsConfirm(Plan(csv, ImportMode.FillExistingRows, b => b.ClearOnEmpty = true)));
    }

    [Fact]
    public void Fill_existing_rows_by_position_needs_no_Data_ID()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        vm.ApplyImport(Plan("CreateCustomer.name\nfirst\nsecond", ImportMode.FillExistingRows, b => b.RowMatch = ImportRowMatch.Position));
        Assert.Equal(["first", "second", "Customer 3"], vm.VisibleRows.Select(r => r.Cells[Cell(vm, "CreateCustomer.name")]).ToArray());
        var refused = vm.ApplyImport(Plan("CreateCustomer.name\nx", ImportMode.FillExistingRows));
        Assert.Contains("Data ID", refused.Error);
    }

    [Fact]
    public void Manual_mapping_and_position_mapping_choose_the_columns()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var email = Cell(vm, "CreateCustomer.email");
        vm.ApplyImport(Plan("a,b\nCUST001,mail@x.test", ImportMode.FillExistingRows, b => { b.Match = ImportMatch.Manual; b.Manual = [0, email]; }));
        Assert.Equal("mail@x.test", vm.VisibleRows[0].Cells[email]);
        vm.ApplyImport(Plan("x,y\nCUST002,Named", ImportMode.FillExistingRows, b => b.Match = ImportMatch.ByPosition));   // position 0 = Data ID, 1 = the first column
        Assert.Equal("Named", vm.VisibleRows[1].Cells[vm.Columns[0].CellIndex]);
    }

    [Fact]
    public void New_columns_are_created_from_Step_dot_name_headers_and_filled_by_Data_ID()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var report = vm.ApplyImport(Plan("Data ID,CreateCustomer.phone,{eq}CreateCustomer.phoneOut\nCUST001,111,111\nCUST003,333,333", ImportMode.NewColumns));
        Assert.Equal(["CreateCustomer.phone", "{eq}CreateCustomer.phoneOut"], report.ColumnsCreated.ToArray());
        Assert.Equal(2, report.Updated);
        var phone = Cell(vm, "CreateCustomer.phone");
        Assert.Equal(["111", "", "333"], vm.VisibleRows.Select(r => r.Cells[phone]).ToArray());
        var expected = vm.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.Contains("phoneOut"));
        Assert.Equal("111", vm.VisibleRows[0].Cells[expected.CellIndex]);
        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(vm.Columns, c => c.Title.Contains("phone"));
    }

    [Fact]
    public void A_bare_header_needs_a_step_and_a_clashing_header_is_reported_not_overwritten()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var noStep = vm.ApplyImport(Plan("Data ID,phone\nCUST001,1", ImportMode.NewColumns));
        Assert.Contains("which step", noStep.Error);
        var withStep = vm.ApplyImport(Plan("Data ID,phone,CreateCustomer.name\nCUST001,1,Hacked", ImportMode.NewColumns, b => b.Step = "CreateCustomer"));
        Assert.Equal(["CreateCustomer.phone"], withStep.ColumnsCreated.ToArray());
        Assert.Contains("already has a column", withStep.Notes.First());
        Assert.Equal("Customer 1", vm.VisibleRows[0].Cells[Cell(vm, "CreateCustomer.name")]);
    }

    [Fact]
    public void Unmatched_columns_can_be_created_and_nothing_to_map_is_an_error()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var none = vm.ApplyImport(Plan("zzz\n1", ImportMode.AppendRows));
        Assert.NotNull(none.Error);
        var created = vm.ApplyImport(Plan("Data ID,CreateCustomer.name,CreateCustomer.vip\nV1,Vee,yes", ImportMode.AppendRows, b => b.CreateUnmatched = true));
        Assert.Equal(["CreateCustomer.vip"], created.ColumnsCreated.ToArray());
        Assert.Equal("yes", vm.VisibleRows.Last().Cells[Cell(vm, "CreateCustomer.vip")]);
    }

    [Fact]
    public void The_impact_line_names_what_will_be_replaced()
    {
        using var dir = new TempDir();
        var vm = Open(dir);
        var text = vm.DescribeImpact(Plan("Data ID\nA\nB", ImportMode.ReplaceRows));
        Assert.Contains("REPLACES all 3 existing rows", text);
        Assert.Contains("2 rows from the file", text);
    }
}

internal static class ImportApplyHelpers
{
    public static bool NeedsConfirm(ImportPlan plan) => DataEditorViewModel.NeedsConfirmation(plan);
}
