using LoadTestingTool.Editing;
using Xunit;

namespace WorkbookEditTests;

public class ScriptCompletionTests
{
    /// <summary>"|" marks the caret.</summary>
    private static ScriptSuggestions? At(ScriptKind kind, string codeWithCaret, bool forced = false, IReadOnlyList<string>? variables = null)
    {
        var caret = codeWithCaret.IndexOf('|');
        return ScriptCompletion.Suggest(kind, codeWithCaret.Remove(caret, 1), caret, forced, variables);
    }

    private static string[] Labels(ScriptSuggestions? s) => s?.Items.Select(i => i.Label).ToArray() ?? [];

    [Fact]
    public void After_a_dot_the_members_of_the_runtime_are_offered_with_their_signatures()
    {
        var s = At(ScriptKind.Template, "Runtime.|");
        Assert.NotNull(s);
        Assert.Equal(["Assert", "Get", "Log", "Logger", "Response", "Set"], Labels(s).Where(l => l is "Assert" or "Get" or "Log" or "Logger" or "Response" or "Set").Order().ToArray());
        Assert.DoesNotContain("MemberwiseClone", Labels(s));
        Assert.Contains("Get(string name)", s!.Items.Single(i => i.Label == "Get").Detail);
        Assert.Equal("Runtime.".Length, s.Start);
    }

    [Fact]
    public void The_typed_part_after_the_dot_filters_and_the_start_covers_it()
    {
        var s = At(ScriptKind.Template, "var x = Runtime.Lo|");
        Assert.Equal(["Log", "Logger"], Labels(s));
        Assert.Equal("var x = Runtime.".Length, s!.Start);
    }

    [Fact]
    public void Static_members_of_a_type_and_instance_members_of_a_string_and_linq_extensions()
    {
        Assert.Contains("NewGuid", Labels(At(ScriptKind.Template, "Guid.|")));
        Assert.DoesNotContain("ToString", Labels(At(ScriptKind.Template, "Guid.|")));   // an instance member, not on the type
        var text = At(ScriptKind.Template, "var t = \"abc\"; t.|");
        Assert.Contains("ToUpperInvariant", Labels(text)); Assert.Contains("Substring", Labels(text)); Assert.Contains("Length", Labels(text));
        Assert.DoesNotContain("Empty", Labels(text));            // static string members are not on an instance
        var list = At(ScriptKind.Template, "var l = new List<int>(); l.|");
        Assert.Contains("Add", Labels(list)); Assert.Contains("Where", Labels(list)); Assert.Contains("Count", Labels(list));
        Assert.Contains("Empty", Labels(At(ScriptKind.Template, "string.|")));
    }

    [Fact]
    public void Chained_calls_and_null_conditional_access_are_followed()
    {
        Assert.Contains("ToLowerInvariant", Labels(At(ScriptKind.Template, "Runtime.Get(\"env\")?.ToString()?.|")));
        Assert.Contains("Trim", Labels(At(ScriptKind.Template, "(Runtime.Get(\"env\")?.ToString() ?? \"\").|")));
    }

    [Fact]
    public void A_placeholder_variable_counts_as_text_and_keeps_the_positions()
    {
        var s = At(ScriptKind.Template, "var env = _env_.To|");
        Assert.Contains("ToLower", Labels(s)); Assert.Contains("ToUpper", Labels(s));
        Assert.Equal("var env = _env_.".Length, s!.Start);
    }

    [Fact]
    public void Typing_a_name_offers_locals_what_the_script_can_use_types_keywords_and_snippets_but_not_before_two_letters()
    {
        var s = At(ScriptKind.Template, "var total = 1; var tag = \"x\"; to|");
        Assert.Contains("total", Labels(s)); Assert.Contains("tag", Labels(At(ScriptKind.Template, "var total = 1; var tag = \"x\"; ta|")));
        Assert.Contains("Runtime", Labels(At(ScriptKind.Template, "Ru|")));
        Assert.Contains("DateTime", Labels(At(ScriptKind.Template, "Date|")));
        Assert.Contains("foreach", Labels(At(ScriptKind.Template, "fore|")));
        Assert.Contains("snippet", At(ScriptKind.Template, "fore|")!.Items.Select(i => i.Kind));
        Assert.Null(At(ScriptKind.Template, "R|"));
        Assert.NotNull(At(ScriptKind.Template, "R|", forced: true));   // Ctrl+Space
    }

    [Fact]
    public void A_condition_offers_its_helpers_with_help_and_ignores_the_cs_prefix_and_references()
    {
        var s = At(ScriptKind.Condition, "cs: <From_response_Login.statusCode_r> == \"200\" && Ha|");
        Assert.Equal("Has", Labels(s).First());   // the helper first, then types such as HashSet
        Assert.Contains("Has(string key)", s!.Items.First().Detail);
        var all = Labels(At(ScriptKind.Condition, "cs: |", forced: true));
        foreach (var helper in new[] { "V", "Has", "Is", "True", "Num" }) Assert.Contains(helper, all);
        Assert.DoesNotContain("Runtime", all);
        Assert.Contains("Regex", Labels(At(ScriptKind.Condition, "cs: Rege|")));
        Assert.Contains("Length", Labels(At(ScriptKind.Condition, "cs: V(\"a\").|")));
    }

    [Fact]
    public void Inside_Is_the_verbs_are_offered_and_inside_Runtime_Get_the_variables()
    {
        var verbs = At(ScriptKind.Condition, "cs: Is(\"gt|");
        Assert.Equal(["gt", "gte"], Labels(verbs).Order().ToArray());
        Assert.Equal("cs: Is(\"".Length, verbs!.Start);
        Assert.Contains("startswith", Labels(At(ScriptKind.Condition, "cs: Is(\"|\", V(\"a\"), \"x\")")));
        var vars = At(ScriptKind.Template, "Runtime.Get(\"us|\")", variables: ["userid", "username", "env"]);
        Assert.Equal(["userid", "username"], Labels(vars));
        Assert.Null(At(ScriptKind.Template, "var s = \"Runtime.|\";"));      // an ordinary string offers nothing
        Assert.Null(At(ScriptKind.Template, "Runtime.Log(\"us|\")", variables: ["userid"]));
    }

    [Fact]
    public void Nothing_is_offered_in_comments_numbers_or_unknown_things()
    {
        Assert.Null(At(ScriptKind.Template, "// Runtime.|"));
        Assert.Null(At(ScriptKind.Template, "/* Runtime.Lo| */"));
        Assert.Null(At(ScriptKind.Template, "var n = 1.|"));
        Assert.Null(At(ScriptKind.Template, "NoSuchThing.|"));
    }

    [Fact]
    public void Signature_help_names_the_call_and_the_active_parameter()
    {
        var code = "cs: Is(\"gt\", V(\"a\"), ";
        var sig = ScriptCompletion.Signature(ScriptKind.Condition, code, code.Length);
        Assert.NotNull(sig);
        Assert.Equal(["verb", "actual", "expected"], sig!.Parameters);
        Assert.Equal(2, sig.Active);
        Assert.Contains("Is(string verb, string? actual, string expected = \"\")", sig.Text);
        var inner = "cs: Is(\"gt\", V(\"a|\"), \"1\")"; var caret = inner.IndexOf('|');
        var innerSig = ScriptCompletion.Signature(ScriptKind.Condition, inner.Remove(caret, 1), caret);
        Assert.Equal(["key"], innerSig!.Parameters);
        Assert.Null(ScriptCompletion.Signature(ScriptKind.Condition, "cs: 1 + 2", 5));
        var overloaded = "Runtime.Log(";
        Assert.Contains("Log(string message)", ScriptCompletion.Signature(ScriptKind.Template, overloaded, overloaded.Length)!.Text);
    }

    [Fact]
    public void Prepare_keeps_every_position_the_same()
    {
        var code = "cs: <From_response_A.b_r> == _i_ && V(\"x\") == \"y\"";
        var prepared = ScriptCompletion.Prepare(ScriptKind.Condition, code);
        Assert.Equal(code.Length, prepared.Length);
        Assert.DoesNotContain("<From", prepared); Assert.DoesNotContain("_i_", prepared); Assert.StartsWith("   ", prepared);
    }

    [Fact]
    public void Every_helper_of_the_script_hosts_is_offered_so_the_list_cannot_rot()
    {
        var condition = Labels(At(ScriptKind.Condition, "|", forced: true));
        foreach (var name in typeof(LoadTestingTool.Execution.ConditionGlobals).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Select(m => m.Name).Distinct())
            if (!name.StartsWith("get_") && !name.StartsWith("set_")) Assert.Contains(name, condition);
    }
}
