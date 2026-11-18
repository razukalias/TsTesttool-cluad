using System.Linq;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class DashboardOrderTests
{
    private static DashboardRequest R(string step, int row, int order, int tc = 1)
        => new(step, "HTTP", "tc", "d", "dev", "Passed", 1, "", "", tc, row, order);

    [Fact]
    public void Steps_are_listed_in_workbook_order_not_finish_order()
    {
        var finished = new[] { R("b2", 7, 2), R("a1", 3, 1), R("b1", 7, 1), R("a2", 3, 2) };
        var sorted = DashboardRequest.InWorkbookOrder(finished).Select(r => r.StepName).ToArray();
        Assert.Equal(new[] { "a1", "a2", "b1", "b2" }, sorted);
    }

    [Fact]
    public void Testcase_comes_before_row_and_old_runs_keep_finish_order()
    {
        var sorted = DashboardRequest.InWorkbookOrder(new[] { R("x", 2, 1, 2), R("y", 9, 1, 1) }).Select(r => r.StepName).ToArray();
        Assert.Equal(new[] { "y", "x" }, sorted);
        var old = DashboardRequest.InWorkbookOrder(new[] { R("p", 0, 0, 0), R("q", 0, 0, 0) }).Select(r => r.StepName).ToArray();
        Assert.Equal(new[] { "p", "q" }, old);
    }
}
