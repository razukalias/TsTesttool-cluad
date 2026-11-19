using LoadTestingTool.Editing;
using Xunit;

namespace WorkbookEditTests;

// Script templates in the wizard: the <runscript(...)> part, the template list, parameters and the compile-only check.
public class ScriptTemplateTests
{
    [Fact]
    public void A_runscript_token_is_one_part_even_when_a_parameter_holds_a_From_reference()
    {
        var parts = ValueParts.Parse("id-<runscript(template:s, env:<From_request_env_r>, n:3)>-<randomnumber>");
        Assert.Equal([ValuePartKind.Text, ValuePartKind.Script, ValuePartKind.Text, ValuePartKind.RandomNumber], parts.Select(p => p.Kind));
        Assert.Equal("<runscript(template:s, env:<From_request_env_r>, n:3)>", parts[1].Raw);
        Assert.Equal("id-<runscript(template:s, env:<From_request_env_r>, n:3)>-<randomnumber>", ValueParts.Format(parts));
        Assert.Equal("⚙ s(env=<From_request_env_r>, n=3)", parts[1].Label);
    }

    [Fact]
    public void A_token_is_built_and_read_back_with_its_parameters()
    {
        var token = ScriptTemplates.Token("greet", [("who", "Ann"), ("when", "<datum>")]);
        Assert.Equal("<runscript(template:greet, who:Ann, when:<datum>)>", token);
        var back = ScriptTemplates.ParseToken(token)!.Value;
        Assert.Equal("greet", back.Template);
        Assert.Equal([("who", "Ann"), ("when", "<datum>")], back.Parameters);
        Assert.Null(ScriptTemplates.ParseToken("<guid>"));
    }

    [Fact]
    public void Templates_are_listed_from_the_workspace_Templates_and_the_configured_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "tpl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Templates")); Directory.CreateDirectory(Path.Combine(root, "extra"));
        File.WriteAllText(Path.Combine(root, "a.csx"), "return 1;");
        File.WriteAllText(Path.Combine(root, "Templates", "b.csx"), "return 2;");
        File.WriteAllText(Path.Combine(root, "extra", "c.csx"), "return 3;");
        try
        {
            Assert.Equal(["a", "b", "c"], ScriptTemplates.List(root, "extra"));
            Assert.EndsWith("c.csx", ScriptTemplates.PathOf("c", root, "extra"));
            Assert.Null(ScriptTemplates.PathOf("zzz", root, "extra"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Validate_reports_the_line_and_column_of_a_compile_error_and_nothing_for_good_code()
    {
        Assert.Empty(ScriptTemplates.Validate("var x = 1;\nreturn x.ToString();"));
        var errors = ScriptTemplates.Validate("var x = 1;\nreturn \"200;");
        var first = errors.First(d => d.IsError && d.Line == 2);   // the compiler may report several problems on the broken line
        Assert.True(first.Column >= 1);
        Assert.NotEmpty(ScriptTemplates.Validate("return Runtime.Nope();"));   // an unknown member is found without running
        Assert.Empty(ScriptTemplates.Validate("return _name_;"));            // the _name_ short form is expanded first
    }

    [Fact]
    public void The_parameters_a_template_reads_are_found_for_both_forms()
    {
        var used = ScriptTemplates.ParametersUsed("var a = Runtime.Get(\"env\"); return _first_ + _last_;");
        Assert.Equal(["first", "last", "env"], used.OrderBy(u => u == "env" ? 2 : u == "first" ? 0 : 1));
        Assert.True(ScriptTemplates.IsValidName("my-script_1"));
        Assert.False(ScriptTemplates.IsValidName("../x"));
        Assert.Empty(ScriptTemplates.Validate(ScriptTemplates.Starter("demo")));
    }
}
