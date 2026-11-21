using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

// The wizard's rows and the condition text are the same thing: text -> rows -> text keeps the meaning.
public class ConditionPartsTests
{
    [Theory]
    [InlineData("1 eq 1")]
    [InlineData("1 eq 1 and 2 eq 3")]
    [InlineData("1 eq 1 or 2 eq 3 and 4 eq 4")]
    [InlineData("1 eq 2 xor 3 eq 3")]
    [InlineData("not 1 eq 2")]
    [InlineData("(1 eq 1 or 2 eq 3) and 4 eq 4")]
    [InlineData("not (1 eq 1 or 2 eq 3)")]
    [InlineData("((1 eq 2) or (3 eq 3)) and 5 gt 4")]
    [InlineData("(not 1 eq 2 and 3 eq 3) or 9 eq 8")]
    [InlineData("true and no")]
    [InlineData("abc notempty and \"\" empty")]
    [InlineData("\"hello world\" contains world or x eq y")]
    [InlineData("name eq John Smith and age gte 18")]
    [InlineData("f(x) eq f(x)")]
    [InlineData("(f(x) eq f(x) or 1 eq 2) and 3 eq 3")]
    [InlineData("1 eq 1 && 2 eq 2 || 3 eq 4")]
    [InlineData("\"a and b\" eq \"a and b\"")]
    [InlineData("\"<From_response_A.b_r>\" ne DONE and \"<From_response_A.c_r>\" gt 3")]
    public void Rows_and_text_describe_the_same_condition(string text)
    {
        var parts = ConditionParts.Parse(text);
        Assert.NotNull(parts);
        var rebuilt = ConditionParts.Build(parts!);
        Assert.Equal(ConditionEvaluator.Evaluate(text), ConditionEvaluator.Evaluate(rebuilt));
        Assert.Null(ConditionEvaluator.Check(rebuilt));
        Assert.Equal(parts!.Count, ConditionParts.Parse(rebuilt)!.Count);   // and it reads back to the same rows
    }

    [Fact]
    public void The_rows_carry_join_not_brackets_and_the_comparison()
    {
        var parts = ConditionParts.Parse("not (a eq 1 or (not b gt 2)) and c notempty")!;
        Assert.Equal(3, parts.Count);
        Assert.Equal(new ConditionPart("", true, 1, false, "a", "eq", "1", 0), parts[0]);
        Assert.Equal(new ConditionPart("or", false, 1, true, "b", "gt", "2", 2), parts[1]);
        Assert.Equal(new ConditionPart("and", false, 0, false, "c", "notempty", "", 0), parts[2]);
    }

    [Fact]
    public void A_single_word_has_no_verb()
    {
        var parts = ConditionParts.Parse("true or flag")!;
        Assert.Equal(["true", "flag"], parts.Select(p => p.Left).ToArray());
        Assert.All(parts, p => Assert.Equal("", p.Verb));
    }

    [Fact]
    public void Building_quotes_what_could_be_misread_and_leaves_plain_words()
    {
        var text = ConditionParts.Build([
            new("", false, 0, false, "<From_response_A.b_r>", "eq", "two words", 0),
            new("or", false, 0, true, "_name_", "contains", "and", 0),
            new("xor", false, 1, false, "x", "empty", "ignored", 1),
            new("and", false, 0, false, "5", "gt", "3", 0)]);
        Assert.Equal("\"<From_response_A.b_r>\" eq \"two words\" or not \"_name_\" contains \"and\" xor (x empty) and 5 gt 3", text);
        Assert.Null(ConditionEvaluator.Check(text));
    }

    [Fact]
    public void A_shape_the_rows_cannot_show_is_reported_as_null_and_a_bad_text_too()
    {
        Assert.Null(ConditionParts.Parse("not ( not (1 eq 1 or 2 eq 2) and 3 eq 3)"));
        Assert.Null(ConditionParts.Parse("1 eq 1 and"));
        Assert.Empty(ConditionParts.Parse("")!);
        Assert.Empty(ConditionParts.Parse("   ")!);
    }

    [Fact]
    public void Join_defaults_to_and_when_building()
    {
        Assert.Equal("a eq 1 and b eq 2", ConditionParts.Build([new("", false, 0, false, "a", "eq", "1", 0), new("", false, 0, false, "b", "eq", "2", 0)]));
    }
}

public class ConditionScriptConversionTests
{
    [Theory]
    [InlineData("<From_response_A.s_r> eq 200", true)]
    [InlineData("1 eq 1 and not ( 2 gt 3 or x empty )", true)]
    [InlineData("1 eq 2 or 5 gt 3 xor 1 eq 1", false)]
    [InlineData("abc notempty and \"\" empty", true)]
    [InlineData("yes", true)]
    public async Task A_wizard_condition_converted_to_csharp_gives_the_same_answer(string text, bool expected)
    {
        var parts = ConditionParts.Parse(text)!;
        var code = ConditionScript.FromParts(parts);
        Assert.Null(ConditionScript.Check("cs: " + code));
        Assert.Equal(ConditionEvaluator.Evaluate(text.Replace("<From_response_A.s_r>", "200")), await ConditionScript.EvaluateAsync("cs: " + code, [], t => Task.FromResult(t.StartsWith("<From") ? "200" : t)));
        Assert.Equal(expected, ConditionEvaluator.Evaluate(text.Replace("<From_response_A.s_r>", "200")));
    }
}
