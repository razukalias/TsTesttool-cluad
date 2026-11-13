using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ColumnRowFilterTests
{
    [Fact]
    public void Header_filter_keeps_columns_whose_header_contains_any_term()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 4));
        vm.HeaderFilter = "email & name";
        Assert.NotEmpty(vm.Columns);
        Assert.All(vm.Columns, c => Assert.True(c.Title.Contains("email", StringComparison.OrdinalIgnoreCase) || c.Title.Contains("name", StringComparison.OrdinalIgnoreCase)));
        vm.HeaderFilter = "";
        Assert.True(vm.Columns.Count > 2);
    }

    [Fact]
    public void Row_filter_takes_numbers_and_ranges_counting_from_the_first_row()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 6));
        vm.RowNumberFilter = "1,3-4";
        Assert.Equal(["CUST001", "CUST003", "CUST004"], vm.VisibleRows.Select(r => r.Cells[0]));
        Assert.Equal("3 of 6 rows", vm.RowCountText);
        vm.RowNumberFilter = "";
        Assert.Equal(6, vm.VisibleRows.Count);
    }

    [Fact]
    public void Cell_value_filter_keeps_only_columns_with_a_matching_value_in_the_rows_shown()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 4));
        vm.ValueFilter = "customer2@example.test";
        Assert.Contains(vm.Columns, c => c.Title == "CreateCustomer.email");
        Assert.DoesNotContain(vm.Columns, c => c.Title == "CreateCustomer.name");
        Assert.Equal(4, vm.VisibleRows.Count); // columns are filtered, not rows, as in the original editor

        vm.RowNumberFilter = "1"; // CUST001 only, which doesn't hold that value
        Assert.DoesNotContain(vm.Columns, c => c.Title == "CreateCustomer.email");
    }
}

public class ColumnValueFilterTests
{
    private static void Add(DataEditorViewModel vm, string column, string op, string value = "")
    {
        vm.NewFilterColumn = column;
        vm.NewFilterOperator = op;
        vm.NewFilterValue = value;
        vm.AddColumnFilterCommand.Execute(null);
    }

    [Fact]
    public void A_column_filter_keeps_only_rows_where_that_column_matches_and_chips_remove_it()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 12)); // countries cycle SE NO DK FI DE NL

        Add(vm, "CreateCustomer.country", DataEditorViewModel.OpEquals, "dk");
        Assert.Equal(2, vm.VisibleRows.Count);
        Assert.All(vm.VisibleRows, r => Assert.Equal("DK", r.Cells[vm.Columns.First(c => c.Title == "CreateCustomer.country").CellIndex]));
        Assert.Equal("2 of 12 rows", vm.RowCountText);
        Assert.Equal("Column filters (1)", vm.ColumnFilterSummary);

        vm.ColumnFilters[0].RemoveCommand.Execute(vm.ColumnFilters[0]);
        Assert.Equal(12, vm.VisibleRows.Count);
    }

    [Fact]
    public void Filters_combine_with_and_support_data_id_empty_and_not_equals()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 12));

        Add(vm, DataEditorViewModel.DataIdColumn, DataEditorViewModel.OpStartsWith, "CUST00");   // CUST001..CUST009
        Add(vm, "CreateCustomer.country", DataEditorViewModel.OpNotEquals, "SE");
        Assert.DoesNotContain(vm.VisibleRows, r => r.Cells[0] is "CUST010" or "CUST011" or "CUST012");
        Assert.DoesNotContain(vm.VisibleRows, r => r.Cells[vm.Columns.First(c => c.Title == "CreateCustomer.country").CellIndex] == "SE");

        vm.ClearColumnFiltersCommand.Execute(null);
        Add(vm, "dontrun", DataEditorViewModel.OpIsEmpty);
        Assert.Equal(12, vm.VisibleRows.Count);
        vm.ClearColumnFiltersCommand.Execute(null);
        Add(vm, "dontrun", DataEditorViewModel.OpIsNotEmpty);
        Assert.Empty(vm.VisibleRows);
    }

    [Fact]
    public void Expected_columns_are_listed_separately_and_a_new_row_clears_the_filters_so_it_is_visible()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 4));
        Assert.Contains("CreateCustomer.name [expected]", vm.FilterColumnOptions);
        Add(vm, "CreateCustomer.country", DataEditorViewModel.OpEquals, "zz");
        Assert.Empty(vm.VisibleRows);

        vm.AddRowCommand.Execute(null);

        Assert.Empty(vm.ColumnFilters);
        Assert.Equal(5, vm.VisibleRows.Count);
    }
}

public class ColumnValueFilterWindowTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_column_filter_panel_lists_the_active_conditions_as_chips()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 12), "Customers");
        window.Width = 1440; window.Height = 860;
        window.Show();
        var vm = window.ViewModel;
        vm.NewFilterColumn = "CreateCustomer.country"; vm.NewFilterOperator = DataEditorViewModel.OpEquals; vm.NewFilterValue = "DK";
        vm.AddColumnFilterCommand.Execute(null);
        vm.ColumnFiltersVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!.Save(System.IO.Path.Combine(shots, "editor-column-filters.png"));
        Assert.Equal(2, vm.VisibleRows.Count);
        window.Close();
    }
}
