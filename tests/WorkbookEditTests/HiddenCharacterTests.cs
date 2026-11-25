using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

public sealed class HiddenCharacterTests
{
    [Theory]
    [InlineData("eq", "true ", "True")]
    [InlineData("eq", "​true", "true")]
    [InlineData("eq", "tr﻿ue", "true")]
    [InlineData("eq", " true ", "TRUE")]
    [InlineData("contains", "hello​ world", "hello world")]
    [InlineData("in", "b ", "a|b|c")]
    [InlineData("ne", "x", "y")]
    public void Text_verbs_ignore_hidden_characters_and_edge_spaces(string verb, string actual, string expected) =>
        Assert.True(AssertionEngine.Test(verb, actual, expected, true));

    [Fact]
    public void A_visible_difference_still_fails() => Assert.False(AssertionEngine.Test("eq", "true1", "true", true));

    [Fact]
    public void A_space_inside_the_text_is_kept() => Assert.False(AssertionEngine.Test("eq", "a b", "ab", true));

    [Fact]
    public void Regex_patterns_are_not_cleaned() => Assert.False(AssertionEngine.Test("regex", "ab", "a​b", true));
}
