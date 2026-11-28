using System.Text.Json;
using LoadTestingTool.Domain;
using LoadTestingTool.Editing;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Xunit;

namespace WorkbookEditTests;

public class ScriptVariableTests
{
    [Fact]
    public void Lines_are_plain_strings_added_after_the_ones_already_at_the_top()
    {
        var code = ScriptVariableLines.AddToTop("return str + other;", "str", "_orderId_");
        code = ScriptVariableLines.AddToTop(code, "other", "_Login.token_");
        Assert.Equal("var str = \"_orderId_\";\nvar other = \"_Login.token_\";\nreturn str + other;", code);
        Assert.Equal("var a = \"_x_\";\r\nreturn a;", ScriptVariableLines.AddToTop("return a;\r\n".TrimEnd('\r', '\n') , "a", "_x_").Replace("\n", "\r\n"));
    }

    [Theory]
    [InlineData("", false)] [InlineData("order id", false)] [InlineData("1abc", false)] [InlineData("class", false)] [InlineData("var", false)] [InlineData("orderId", true)] [InlineData("_x1", true)]
    public void A_name_must_be_a_usable_C_sharp_name(string name, bool ok) => Assert.Equal(ok, ScriptVariableLines.CheckName(name) is null);

    [Fact]
    public void A_name_that_is_already_declared_is_refused_and_a_placeholder_cannot_hold_spaces()
    {
        Assert.NotNull(ScriptVariableLines.CheckName("str", "var str = \"_a_\";\nreturn str;"));
        Assert.Null(ScriptVariableLines.Placeholder("Order id", out var problem)); Assert.NotNull(problem);
        Assert.Equal("_Login.token_", ScriptVariableLines.Placeholder("Login.token", out _));
    }

    [Fact]
    public async Task A_value_with_quotes_and_line_breaks_arrives_in_the_script_as_the_same_string()
    {
        var value = "he said \"hi\"\r\nback\\slash";
        var config = JsonSerializer.Serialize(new { Inline = ScriptVariableLines.AddToTop("return str;", "str", "_a_") });
        var filled = StepConfig.Fill(config, new Dictionary<string, string> { ["a"] = value });
        var code = JsonDocument.Parse(filled).RootElement.GetProperty("Inline").GetString()!;
        Assert.Equal(value, await CSharpScript.EvaluateAsync<string>(code));
    }
}
