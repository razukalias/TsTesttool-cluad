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
