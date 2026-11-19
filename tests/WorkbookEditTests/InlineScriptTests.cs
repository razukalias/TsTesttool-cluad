using System.Reflection;
using LoadTestingTool.Domain;
using LoadTestingTool.Reporting;
using Microsoft.CodeAnalysis.Scripting;
using Xunit;
using RunnerExecutionContext = LoadTestingTool.Domain.ExecutionContext;

namespace WorkbookEditTests;

// <runscript(template:s)> in a cell: the environment script from the editor chat, run through the runner's own inline-script resolver.
public class InlineScriptTests
{
    private const string EnvironmentScript = """
        var environment = (Runtime.Get("env")?.ToString() ?? "")
            .Trim()
            .ToLowerInvariant();

        if (environment == "development")
        {
            return "500";
        }
        else if (environment == "test")
        {
            return "500";
        }
        else if (environment == "staging")
        {
            return "300";
        }
        else if (environment == "production")
        {
            return "200";   // the original had  return "200;  here: a missing closing quote
        }
        else
        {
            return "200";
        }
        """;

    private static async Task<string> Resolve(string cell, string? environment, string script)
    {
        var root = Path.Combine(Path.GetTempPath(), "inline_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Templates"));
        File.WriteAllText(Path.Combine(root, "Templates", "s.csx"), script);
        try
        {
            var context = new RunnerExecutionContext();
            if (environment is not null) context.Set("env", environment, VariableScope.Iteration);
            var features = new FeatureConfig { WorkspaceRoot = root, TemplatesFolder = "Templates" };
            using var logger = new InternalLogger(Path.Combine(root, "log"), "i", "r", enabled: false);
            var runner = typeof(FeatureConfig).Assembly.GetType("LoadTestingTool.Execution.InlineScriptRunner")!;
            var method = runner.GetMethod("ResolveTokensAsync", BindingFlags.Public | BindingFlags.Static)!;
            try { return await (Task<string>)method.Invoke(null, [cell, context, root, features, logger, CancellationToken.None])!; }
            catch (TargetInvocationException ex) { throw ex.InnerException!; }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("development", "500")]
    [InlineData("test", "500")]
    [InlineData("staging", "300")]
    [InlineData("production", "200")]
    [InlineData("Production ", "200")]   // trimmed and lower-cased by the script
    [InlineData("somethingelse", "200")]
    public async Task The_script_gives_the_value_of_the_environment(string environment, string expected)
        => Assert.Equal(expected, await Resolve("<runscript(template:s)>", environment, EnvironmentScript));

    [Fact]
    public async Task Without_an_env_variable_the_script_falls_to_the_default()
        => Assert.Equal("200", await Resolve("<runscript(template:s)>", null, EnvironmentScript));

    [Fact]
    public async Task The_token_works_inside_a_longer_cell_value()
        => Assert.Equal("status 300 expected", await Resolve("status <runscript(template:s)> expected", "staging", EnvironmentScript));

    [Fact]
    public async Task The_script_with_the_missing_quote_does_not_compile()
    {
        var broken = EnvironmentScript.Replace("return \"200\";   // the original had  return \"200;  here: a missing closing quote", "return \"200;");
        Assert.Contains("200;", broken);
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Resolve("<runscript(template:s)>", "production", broken));
        Assert.Contains("does not compile", ex.Message);
        Assert.Contains("line ", ex.Message);   // the line inside the .csx, not only the exception text
        Assert.IsType<CompilationErrorException>(ex.InnerException);
    }

    [Fact]
    public async Task A_missing_template_says_where_it_looked()
    {
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(() => Resolve("<runscript(template:nope)>", "test", EnvironmentScript));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public async Task The_log_names_the_template_parameters_origin_result_and_time()
    {
        var root = Path.Combine(Path.GetTempPath(), "inlinelog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Templates"));
        File.WriteAllText(Path.Combine(root, "Templates", "greet.csx"), "return \"hello \" + Runtime.Get(\"who\");");
        try
        {
            var features = new FeatureConfig { WorkspaceRoot = root, TemplatesFolder = "Templates" };
            string log;
            using (var logger = new InternalLogger(Path.Combine(root, "log"), "i", "r", enabled: true))
            {
                var runner = typeof(FeatureConfig).Assembly.GetType("LoadTestingTool.Execution.InlineScriptRunner")!;
                runner.GetProperty("Origin", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, "testcase=T1; dataid=D7; step=Login; environment=staging");
                var method = runner.GetMethod("ResolveTokensAsync", BindingFlags.Public | BindingFlags.Static)!;
                var result = await (Task<string>)method.Invoke(null, ["<runscript(template:greet, who:Ann)>", new RunnerExecutionContext(), root, features, logger, CancellationToken.None])!;
                Assert.Equal("hello Ann", result);
            }
            log = string.Join("\n", Directory.GetFiles(Path.Combine(root, "log"), "*.log", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.Contains("template=greet", log);
            Assert.Contains("who=Ann", log);
            Assert.Contains("dataid=D7", log);
            Assert.Contains("step=Login", log);
            Assert.Contains("result=hello Ann", log);
            Assert.Contains("elapsed=", log);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
