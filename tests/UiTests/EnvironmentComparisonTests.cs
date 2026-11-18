using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

// The Environments tab: steps side by side across environments, what matches, what differs, and failures grouped by error.
public class EnvironmentComparisonTests
{
    private static DashboardRequest R(string env, string step, string result, string error = "", string row = "row_a", string category = "")
        => new(step, "HTTP", "Customers", row, env, result, 10, category, error, 1, 2, 1);

    [Fact]
    public void Steps_that_ended_the_same_way_match_and_the_rest_differ()
    {
        var report = EnvironmentReport.Build(
        [
            R("dev", "Login", "PASS"), R("test", "Login", "PASS"),                                   // passed everywhere
            R("dev", "Create", "FAIL", "HTTP 500"), R("test", "Create", "FAIL", "HTTP 500"),         // failed everywhere, same error
            R("dev", "Update", "PASS"), R("test", "Update", "FAIL", "HTTP 404"),                     // result differs
            R("dev", "Delete", "FAIL", "HTTP 500"), R("test", "Delete", "FAIL", "HTTP 503"),         // different errors
            R("dev", "OnlyDev", "PASS"),                                                             // one environment: not compared
        ]);

        Assert.Equal(["dev", "test"], report.Environments);
        Assert.Equal(4, report.Compared.Count);
        Assert.Equal(2, report.Matching);
        Assert.Equal(1, report.PassedEverywhere);
        Assert.Equal(1, report.FailedEverywhere);
        Assert.Equal(["Update", "Delete"], report.Differing.Select(d => d.StepName).ToArray());
        Assert.Equal(StepMatch.ResultDiffers, report.Differing[0].Kind);
        Assert.Equal(StepMatch.ErrorDiffers, report.Differing[1].Kind);
    }

    [Fact]
    public void Failures_are_grouped_by_error_with_the_steps_and_environments_under_each()
    {
        var report = EnvironmentReport.Build(
        [
            R("dev", "Create", "FAIL", "HTTP 500"), R("test", "Create", "FAIL", "HTTP 500"),
            R("dev", "Delete", "FAIL", "HTTP 500"), R("test", "Delete", "FAIL", "HTTP 503"),
            R("prod", "Create", "PASS"),
        ]);

        var top = report.Groups[0];
        Assert.Equal("HTTP 500", top.Key);
        Assert.Equal(3, top.Occurrences.Count);
        Assert.Equal(2, top.Steps);
        Assert.Equal(["dev", "test"], top.Environments);
        Assert.Contains(report.Groups, g => g.Key == "HTTP 503" && g.Occurrences.Count == 1);
    }

    [Fact]
    public void The_same_assertion_failing_with_different_values_is_one_group_and_one_environment_means_nothing_to_compare()
    {
        var report = EnvironmentReport.Build(
            [R("dev", "Get", "FAIL", category: "Assertion", row: "a"), R("dev", "Get", "FAIL", category: "Assertion", row: "b")],
            r => r.DataId == "a" ? "name eq: expected Ann, got Bo" : "name eq: expected Cy, got Di");

        Assert.Single(report.Groups);
        Assert.Equal("name eq", report.Groups[0].Key);
        Assert.Equal(2, report.Groups[0].Occurrences.Count);
        Assert.Empty(report.Compared);
        Assert.Single(report.Environments);
    }

    [Fact]
    public void A_blank_environment_is_called_default_and_skipped_steps_match_each_other()
    {
        var report = EnvironmentReport.Build([R("", "Wait", "SKIPPED"), R("test", "Wait", "SKIPPED")]);
        Assert.Equal(["default", "test"], report.Environments);
        Assert.Equal(StepMatch.Match, Assert.Single(report.Compared).Kind);
    }
}
