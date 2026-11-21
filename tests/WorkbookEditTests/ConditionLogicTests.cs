using LoadTestingTool.Editing;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

// Conditions combine like code: not, and, xor, or (in that order of precedence) and brackets.
public class ConditionLogicTests
{
    [Theory]
    [InlineData("1 eq 1 and 2 eq 2", true)]
    [InlineData("1 eq 1 and 2 eq 3", false)]
    [InlineData("1 eq 2 or 2 eq 2", true)]
    [InlineData("1 eq 2 or 2 eq 3", false)]
    [InlineData("1 eq 1 xor 2 eq 2", false)]
    [InlineData("1 eq 1 xor 2 eq 3", true)]
    [InlineData("not 1 eq 2", true)]
    [InlineData("not 1 eq 1", false)]
    [InlineData("not not 1 eq 1", true)]
    [InlineData("1 eq 1 AND 2 eq 2", true)]
    [InlineData("1 eq 1 && 2 eq 2", true)]
    [InlineData("1 eq 2 || 2 eq 2", true)]
    [InlineData("1 eq 1 ^ 2 eq 3", true)]
    [InlineData("! 1 eq 2", true)]
    // precedence: not > and > xor > or
    [InlineData("1 eq 1 or 1 eq 2 and 1 eq 2", true)]          // or( true, and(false,false) )
    [InlineData("(1 eq 1 or 1 eq 2) and 1 eq 2", false)]
    [InlineData("not 1 eq 1 or 2 eq 2", true)]                 // (not true) or true
    [InlineData("not (1 eq 1 or 2 eq 2)", false)]
    [InlineData("1 eq 2 xor 1 eq 1 and 2 eq 2", true)]         // xor( false, and(true,true) )
    // brackets, nested and glued to the words
    [InlineData("(1 eq 1)", true)]
    [InlineData("((1 eq 2) or (3 eq 3))", true)]
    [InlineData("(1 eq 1 and (2 eq 2 or 3 eq 4))", true)]
    [InlineData("( 1 eq 2 )", false)]
    [InlineData("(abc notempty) and (x contains x)", true)]
    // single words and combos of them
    [InlineData("true and yes", true)]
    [InlineData("true and no", false)]
    [InlineData("no or 1", true)]
    [InlineData("not no", true)]
    // quotes keep values and keywords together
    [InlineData("\"a and b\" eq \"a and b\"", true)]
    [InlineData("name eq name and \"x or y\" contains \"or\"", true)]
    [InlineData("\"hello world\" contains world and 5 gt 3", true)]
    [InlineData("x eq \"(a)\"", false)]
    [InlineData("\"(a)\" eq \"(a)\" or 1 eq 2", true)]
    // a value that has brackets of its own keeps them unless they close a group
    [InlineData("f(x) eq f(x)", true)]
    [InlineData("(f(x) eq f(x))", true)]
    [InlineData("(1 eq 2 or f(x) eq f(x)) and 1 eq 1", true)]
    // verbs without a right side
    [InlineData("abc notempty and \"\" empty", true)]
    [InlineData("abc empty or abc exists", true)]
    // short words that look like keywords in a value
    [InlineData("android eq android and orange eq orange", true)]
    public void Comparisons_combine_with_not_and_xor_or_and_brackets(string text, bool expected)
        => Assert.Equal(expected, ConditionEvaluator.Evaluate(text));

    [Theory]
    [InlineData("(1 eq 1", "not closed")]
    [InlineData("1 eq 1 )", "matching")]
    [InlineData("1 eq 1 and", "ends where a comparison was expected")]
    [InlineData("and 1 eq 1", "needs a comparison before it")]
    [InlineData("not", "ends where a comparison was expected")]
    [InlineData("1 eq 1 or or 2 eq 2", "needs a comparison before it")]
    [InlineData("1 equals 1 and 2 eq 2", "is not a comparison")]
    [InlineData("(1 eq 1 and 2 eq 2", "not closed")]
    public void A_badly_written_condition_is_explained(string text, string part)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ConditionEvaluator.Evaluate(text));
        Assert.Contains(part, ex.Message);
        Assert.Contains(text, ex.Message);
        Assert.Equal(ex.Message, ConditionEvaluator.Check(text));
    }

    [Fact]
    public void A_bang_glued_to_a_bracket_means_not()
    {
        Assert.True(ConditionEvaluator.Evaluate("!(1 eq 2)"));
        Assert.False(ConditionEvaluator.Evaluate("!( 1 eq 1 )"));
        Assert.True(ConditionEvaluator.Evaluate("3 gte 3 && !( A7 eq B ) || 1 eq 2"));
    }

    [Fact]
    public void Check_reads_a_good_condition_without_evaluating_it()
    {
        Assert.Null(ConditionEvaluator.Check("<From_response_A.b_r> eq 1 and (_x_ gt 2 or not _y_ empty)"));
        Assert.Null(ConditionEvaluator.Check(""));
        Assert.Null(ConditionEvaluator.Check("a eq"));   // an empty right side is allowed ("a" eq "")
    }

}
