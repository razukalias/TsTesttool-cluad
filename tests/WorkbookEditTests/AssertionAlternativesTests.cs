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
        Assert.False(AssertionAlternatives.CanCombine("size"));
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
        var bad = Record.Exception(() => engine.Evaluate(body, "json", [Make("<or: 1>", "size")], "r", "q"));
        Assert.Contains("cannot be combined", bad?.Message ?? string.Empty);
    }
}

public class HeaderOrAndEscapeTests
{
    [Theory]
    [InlineData("eq|contains", "abc", "xabcx", true)]
    [InlineData("eq|contains", "abc", "zzz", false)]
    [InlineData("empty|eq", "5", "", true)]
    [InlineData("empty|eq", "5", "5", true)]
    [InlineData("empty|eq", "5", "6", false)]
    public void A_verb_list_passes_when_any_verb_accepts(string verb, string expected, string actual, bool pass)
        => Assert.Equal(pass, LoadTestingTool.Execution.AssertionAlternatives.TestAny(verb, v => LoadTestingTool.Execution.AssertionEngine.Test(v, actual, expected, true)));

    [Fact]
    public void Greater_than_and_ampersand_survive_the_tag_round_trip()
    {
        var tag = LoadTestingTool.Execution.AssertionAlternatives.Tag(true, "contains", "a>b & c", "eq");
        Assert.Equal("<or: {contains}a&gt;b &amp; c>", tag);
        var (_, alts) = LoadTestingTool.Execution.AssertionAlternatives.Parse("x " + tag, "eq");
        Assert.Equal("a>b & c", Assert.Single(alts).Value);
    }

    [Fact]
    public void Empty_can_be_combined_and_header_lists_are_valid_verbs()
    {
        Assert.True(LoadTestingTool.Execution.AssertionAlternatives.CanCombine("empty"));
        Assert.False(LoadTestingTool.Execution.AssertionAlternatives.CanCombine("size"));
        Assert.True(LoadTestingTool.Editing.ValueParts.SplitCell("{eq|contains}abc").Verb == "eq|contains");
        Assert.Equal("equals or contains", LoadTestingTool.Editing.ValueParts.WordsOf("eq|contains"));
    }
}

public class ObjectRootAssertionTests
{
    private const string Body = """{"datasetId":"d1","metadata":{"generatedAt":"2026-11-22T15:49:00+01:00","currency":"SEK"},"items":[{"id":1},{"id":2}]}""";

    private static AssertionDefinition Def(string path, string verb, string expected)
        => new() { TestcaseIndex = 1, Testcase = "t", DataId = "d", StepName = "S", ResponsePath = path, AssertionVerb = verb, ExpectedValue = expected };

    [Fact]
    public void An_object_or_array_path_is_tested_as_its_json_text_and_covers_its_fields()
    {
        var engine = new AssertionEngine();
        var results = engine.Evaluate(Body, "json", [Def("metadata", "contains", "generatedAt"), Def("items", "contains", "\"id\":2"), Def("datasetId", "eq", "d1")], "r", "q");
        Assert.All(results, r => Assert.Equal("PASS", r.Result));
        Assert.Equal(3, results.Count);   // metadata.generatedAt, metadata.currency, items[0].id ... are covered, none reported as unexpected
    }

    [Fact]
    public void A_wrong_text_inside_the_object_still_fails()
    {
        var results = new AssertionEngine().Evaluate(Body, "json", [Def("metadata", "contains", "nope"), Def("items", "notempty", ""), Def("datasetId", "eq", "d1")], "r", "q");
        Assert.Single(results, r => r.Result != "PASS");
    }
}
