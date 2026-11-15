using LoadTestingTool.Editing;
using Xunit;

namespace WorkbookEditTests;

public class StepFieldRulesTests
{
    [Theory]
    [InlineData("wait", "", FieldState.Ok)]
    [InlineData("wait", "0", FieldState.Ok)]
    [InlineData("wait", "500", FieldState.Info)]
    [InlineData("wait", "Please approve", FieldState.Info)]
    [InlineData("wait", "-5", FieldState.Warning)]
    [InlineData("timeout", "30", FieldState.Ok)]
    [InlineData("timeout", "soon", FieldState.Warning)]
    [InlineData("timeout", "-1", FieldState.Warning)]
    [InlineData("manualtimeoutminutes", "15", FieldState.Ok)]
    [InlineData("manualtimeoutminutes", "x", FieldState.Warning)]
    [InlineData("expectedstatus", "200", FieldState.Ok)]
    [InlineData("expectedstatus", "200-299", FieldState.Ok)]
    [InlineData("expectedstatus", "2xx", FieldState.Ok)]
    [InlineData("expectedstatus", "{{expected}}", FieldState.Ok)]
    [InlineData("expectedstatus", "20O", FieldState.Warning)]
    [InlineData("expectedstatus", "99", FieldState.Warning)]
    [InlineData("expectedstatus", "299-200", FieldState.Warning)]
    [InlineData("expectedstatus", "6xx", FieldState.Warning)]
    public void The_rules_judge_each_field_the_way_the_runner_reads_it(string header, string value, FieldState expected)
        => Assert.Equal(expected, StepFieldRules.Check(header, value).State);

    [Fact]
    public void The_validator_reports_the_same_warnings()
    {
        using var dir = new TempDir();
        var document = WorkbookDocument.Load(Workbooks.CreateSmall(dir));
        document.Config.Rows[0]["wait"] = "-5";
        document.Config.Rows[0]["expectedstatus"] = "20O";
        var problems = document.Validate();
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Warning && p.Message.Contains("wait '-5'"));
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Warning && p.Message.Contains("expectedstatus '20O'"));
        document.Config.Rows[0]["wait"] = "Please approve";
        Assert.DoesNotContain(document.Validate(), p => p.Message.StartsWith("wait"));
    }
}
