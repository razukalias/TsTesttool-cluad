using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

public class AssertionAlternativesTests
{
    private static bool Run(string verb, string cell, string? actual)
    {
        var (primary, alternatives) = AssertionAlternatives.Parse(cell, verb);
        return AssertionAlternatives.Evaluate(verb, primary, alternatives, (v, value) => AssertionEngine.Test(v, actual, value, actual is not null));
    }

    [Theory]
    [InlineData("200 <or: 201>", "200", true)]
    [InlineData("200 <or: 201>", "201", true)]
    [InlineData("200 <or: 201>", "404", false)]
    [InlineData("200 <or: 201> <or: {gte}500>", "503", true)]
    [InlineData("{gt}10 <and: {lt}20>", "15", true)]
    [InlineData("10 <and: {contains}1>", "10", true)]
    [InlineData("10 <and: {contains}2>", "10", false)]
    public void Or_and_and_combine_checks_of_the_same_field(string cell, string actual, bool expected)
    {
        var verb = cell.StartsWith("{gt}") ? "gt" : "eq";
        var text = cell.StartsWith("{gt}") ? cell[4..] : cell;
        Assert.Equal(expected, Run(verb, text, actual));
    }

    [Fact]
    public void And_binds_tighter_than_or()
    {
        // (more than 10 and less than 20) or 99
        Assert.True(Run("gt", "10 <and: {lt}20> <or: {eq}99>", "99"));
        Assert.True(Run("gt", "10 <and: {lt}20> <or: {eq}99>", "15"));
        Assert.False(Run("gt", "10 <and: {lt}20> <or: {eq}99>", "50"));
    }

    [Fact]
    public void Tags_are_parsed_out_and_written_back()
    {
        var (primary, alternatives) = AssertionAlternatives.Parse("200 <or: 201> <and: {contains}1>", "eq");
        Assert.Equal("200", primary);
        Assert.Equal([new AssertionAlternatives.Alternative(true, "eq", "201"), new AssertionAlternatives.Alternative(false, "contains", "1")], alternatives);
        Assert.Equal("<or: 201>", AssertionAlternatives.Tag(true, "eq", "201", "eq"));
        Assert.Equal("<and: {gte}500>", AssertionAlternatives.Tag(false, "gte", "500", "eq"));
        Assert.False(AssertionAlternatives.CanCombine("notempty"));
        Assert.True(AssertionAlternatives.CanCombine("contains"));
    }

    [Fact]
    public void The_engine_passes_an_or_cell_and_explains_a_failure_with_all_checks()
    {
        var engine = new AssertionEngine();
        const string body = "{\"status\":\"202\"}";
        AssertionDefinition Make(string expected, string verb = "eq") => new() { TestcaseIndex = 1, Testcase = "T", DataId = "D", StepName = "S", ResponsePath = "status", AssertionVerb = verb, HeaderAssertionVerb = verb, ExpectedValue = expected };
        var pass = engine.Evaluate(body, "json", [Make("200 <or: 202>")], "r", "q").Single();
        Assert.Equal("PASS", pass.Result);
        var fail = engine.Evaluate(body, "json", [Make("200 <or: 201>")], "r", "q").Single();
        Assert.Equal("FAIL", fail.Result);
        Assert.Contains("eq '200' or eq '201'", fail.FailureMessage);
        var bad = Record.Exception(() => engine.Evaluate(body, "json", [Make("<or: 1>", "notempty")], "r", "q"));
        Assert.Contains("cannot be combined", bad?.Message ?? string.Empty);
    }
}
