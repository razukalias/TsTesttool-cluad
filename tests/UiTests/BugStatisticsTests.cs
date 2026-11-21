using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

// The Bugs tab's statistics: known bugs per instance, testcase and step.
public class BugStatisticsTests
{
    private static AssertionInfo A(int tcIndex, string testcase, string step, string result) => new() { TestcaseIndex = tcIndex, Testcase = testcase, StepName = step, Result = result, KnownBug = result != "FAIL" && result != "PASS" || result == "PASS" };

    [Fact]
    public void Counts_add_up_from_steps_to_testcases_to_the_instance()
    {
        var root = BugStatistics.Build("large", new[]
        {
            A(1, "Customers", "Create", "KNOWN_BUG"), A(1, "Customers", "Create", "KNOWN_BUG"), A(1, "Customers", "Get", "KNOWN_BUG"),
            A(1, "Customers", "Delete", "PASS"), A(2, "Orders", "Place", "KNOWN_BUG"), A(2, "Orders", "Place", "FAIL"),
        });
        Assert.Equal("Instance", root.Kind);
        Assert.Equal(4, root.Count);
        var customers = root.Children[0];
        Assert.Equal(3, customers.Count);
        Assert.Equal(new[] { 2, 1, 0 }, customers.Children.Select(s => s.Count).ToArray());
        Assert.Equal(1, root.Children[1].Count);
    }

    [Fact]
    public void May_be_fixed_entries_and_failures_are_not_counted_and_steps_without_bugs_stay_listed()
    {
        var root = BugStatistics.Build("x", new[] { A(1, "t", "s", "PASS"), A(1, "t", "s", "FAIL") });
        Assert.Equal(0, root.Count);
        Assert.Single(root.Children[0].Children);
    }

    [Fact]
    public void Testcases_follow_the_workbook_order_and_steps_know_their_testcase()
    {
        var root = BugStatistics.Build("x", new[] { A(2, "B", "s2", "KNOWN_BUG"), A(1, "A", "s1", "KNOWN_BUG") });
        Assert.Equal(new[] { "A", "B" }, root.Children.Select(t => t.Name).ToArray());
        var step = root.Children[1].Children[0];
        Assert.Equal("B", step.Testcase);
        Assert.Equal("s2", step.Step);
        Assert.Null(root.Testcase);
    }
}
