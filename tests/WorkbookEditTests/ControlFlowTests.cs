using System.Net;
using System.Text;
using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

public class ConditionTests
{
    [Theory]
    [InlineData("200 eq 200", true)]
    [InlineData("200 ne 200", false)]
    [InlineData("5 gt 3", true)]
    [InlineData("2 gte 3", false)]
    [InlineData("\"hello world\" contains world", true)]
    [InlineData("DONE eq done", true)]
    [InlineData("'' empty", true)]
    [InlineData("abc notempty", true)]
    [InlineData("abc regex ^a.c$", true)]
    [InlineData("b in a|b|c", true)]
    [InlineData("true", true)]
    [InlineData("no", false)]
    [InlineData("", false)]
    public void A_condition_is_left_verb_right_with_the_assertion_verbs(string text, bool expected)
        => Assert.Equal(expected, ConditionEvaluator.Evaluate(text));

    [Fact]
    public void An_unknown_verb_is_explained()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ConditionEvaluator.Evaluate("1 equals 1"));
        Assert.Contains("is not a comparison", ex.Message);
    }

    [Fact]
    public void Step_config_is_read_as_json_or_key_value_text()
    {
        Assert.Equal("3", ControlConfig.Parse("{\"to\":3,\"var\":\"n\"}")["to"]);
        Assert.Equal("n", ControlConfig.Parse("{\"to\":3,\"var\":\"n\"}")["var"]);
        var text = ControlConfig.Parse("from=1; to=5;onfailure=break");
        Assert.Equal(["1", "5", "break"], [text["from"], text["to"], text["onfailure"]]);
    }
}

public class ControlFlowTests
{
    private sealed record Step(string Name, string Type = "HTTP", string Parent = "", string Url = "", string Config = "", string Condition = "");

    private sealed class Server(Func<string, string> answer, Func<string, HttpStatusCode>? status = null) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(path);
            return Task.FromResult(new HttpResponseMessage(status?.Invoke(path) ?? HttpStatusCode.OK) { Content = new StringContent(answer(path), Encoding.UTF8, "application/json") });
        }
    }

    private static string Build(TempDir dir, params Step[] steps) => Build(dir, null, steps);

    private static string Build(TempDir dir, string? dontrun, params Step[] steps)
    {
        Directory.CreateDirectory(dir.Combine("Templates"));
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Templates"), "t.json"), "{}");
        return Workbooks.CreateSmall(dir, (config, request, response) =>
        {
            config.Cell(1, 9).Value = "parent"; config.Cell(1, 10).Value = "condition"; config.Cell(1, 11).Value = "stepconfig";
            for (var i = 0; i < steps.Length; i++)
            {
                var row = i + 2;
                var s = steps[i];
                var http = s.Type == "HTTP";
                config.Cell(row, 1).Value = 1; config.Cell(row, 2).Value = "Flow"; config.Cell(row, 3).Value = s.Name; config.Cell(row, 4).Value = s.Type;
                config.Cell(row, 5).Value = http ? "t.json" : ""; config.Cell(row, 6).Value = http ? "http://127.0.0.1:1" + s.Url : ""; config.Cell(row, 7).Value = "POST"; config.Cell(row, 8).Value = true;
                config.Cell(row, 9).Value = s.Parent; config.Cell(row, 10).Value = s.Condition; config.Cell(row, 11).Value = s.Config;
            }
            if (dontrun is not null) request.Cell(2, 4).Value = dontrun;   // row of D1
        });
    }

    private static async Task<(List<string> Steps, List<string> Paths, RunResult Run)> RunAsync(string path, Func<string, string> answer, Func<string, HttpStatusCode>? status = null)
    {
        var model = new WorkbookReader().Read(path);
        var server = new Server(answer, status);
        var runner = new TestRunner(new HttpClient(server), new AssertionEngine(), System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "Templates"), 30);
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        await runner.RunAsync(model.Testcases[0], model, run, CancellationToken.None, selectedDataIds: new HashSet<string> { "D1" }, executionMode: "sequential");
        return (run.Testcases.SelectMany(t => t.Requests).Select(r => r.StepName).ToList(), server.Paths, run);
    }

    private static string Ok(string path) => "{\"ok\":true,\"items\":[\"x\",\"y\"]}";

    [Fact]
    public async Task If_runs_its_steps_when_the_condition_holds_and_the_else_steps_when_it_does_not()
    {
        using var dir = new TempDir();
        var path = Build(dir,
            new Step("First", Url: "/first"),
            new Step("Check", "If", Condition: "<From_response_First.ok_r> eq true"),
            new Step("Then1", Parent: "Check", Url: "/then"),
            new Step("Else1", Parent: "Check:else", Url: "/else"),
            new Step("After", Url: "/after"));
        var (steps, paths, _) = await RunAsync(path, Ok);
        Assert.Equal(["/first", "/then", "/after"], paths);
        Assert.Equal(["First", "Then1", "After"], steps);

        var (_, otherPaths, _) = await RunAsync(Build(new TempDir(),
            new Step("First", Url: "/first"),
            new Step("Check", "If", Condition: "<From_response_First.ok_r> eq false"),
            new Step("Then1", Parent: "Check", Url: "/then"),
            new Step("Else1", Parent: "Check:else", Url: "/else")), Ok);
        Assert.Equal(["/first", "/else"], otherPaths);
    }

    [Fact]
    public async Task For_counts_from_to_and_exposes_the_counter_only_inside_the_loop()
    {
        using var dir = new TempDir();
        var path = Build(dir,
            new Step("Loop", "For", Config: "from=1;to=3"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_"),
            new Step("After", Url: "/after"));
        var (_, paths, _) = await RunAsync(path, Ok);
        Assert.Equal(["/n/1", "/n/2", "/n/3", "/after"], paths);

        var counted = await RunAsync(Build(new TempDir(), new Step("Loop", "For", Config: "{\"from\":10,\"count\":3,\"step\":-5,\"var\":\"k\"}"), new Step("Each", Parent: "Loop", Url: "/k/_k_")), Ok);
        Assert.Equal(["/k/10", "/k/5", "/k/0"], counted.Paths);
    }

    [Fact]
    public async Task ForEach_walks_a_json_array_from_a_response_or_a_comma_list_with_item_and_index()
    {
        var fromResponse = await RunAsync(Build(new TempDir(),
            new Step("List", Url: "/list"),
            new Step("Each", "ForEach", Config: "items=<From_response_List.items_r>"),
            new Step("Item", Parent: "Each", Url: "/item/_item_/_index_")), Ok);
        Assert.Equal(["/list", "/item/x/0", "/item/y/1"], fromResponse.Paths);

        var fromText = await RunAsync(Build(new TempDir(), new Step("Each", "ForEach", Config: "items=a, b ,c;var=name"), new Step("Item", Parent: "Each", Url: "/n/_name_")), Ok);
        Assert.Equal(["/n/a", "/n/b", "/n/c"], fromText.Paths);
    }

    [Fact]
    public async Task While_repeats_until_the_condition_is_false_and_waits_for_a_step_inside_it_the_first_time()
    {
        var polls = 0;
        var result = await RunAsync(Build(new TempDir(),
            new Step("Wait", "While", Condition: "<From_response_Poll.state_r> ne DONE", Config: "max=10"),
            new Step("Poll", Parent: "Wait", Url: "/poll"),
            new Step("After", Url: "/after")), p => p == "/poll" ? $"{{\"state\":\"{(++polls >= 3 ? "DONE" : "WAIT")}\"}}" : "{}");
        Assert.Equal(["/poll", "/poll", "/poll", "/after"], result.Paths);
    }

    [Fact]
    public async Task A_while_loop_that_never_ends_fails_at_the_limit_or_carries_on_with_onlimit_continue()
    {
        var failing = await RunAsync(Build(new TempDir(),
            new Step("Wait", "While", Condition: "1 eq 1", Config: "max=3"), new Step("Poll", Parent: "Wait", Url: "/poll"), new Step("After", Url: "/after")), Ok);
        Assert.Equal(["/poll", "/poll", "/poll"], failing.Paths);   // the row stops, After does not run
        var failure = failing.Run.Testcases.SelectMany(t => t.Requests).Single(r => r.Result == "FAIL");
        Assert.Equal("Wait", failure.StepName);
        Assert.Equal(FailureCategories.Control, failure.FailureCategory);
        Assert.Contains("still true after 3 rounds", failure.ErrorMessage);

        var carrying = await RunAsync(Build(new TempDir(),
            new Step("Wait", "While", Condition: "1 eq 1", Config: "max=2;onlimit=continue"), new Step("Poll", Parent: "Wait", Url: "/poll"), new Step("After", Url: "/after")), Ok);
        Assert.Equal(["/poll", "/poll", "/after"], carrying.Paths);
    }

    [Fact]
    public async Task Loops_nest_and_each_level_keeps_its_own_variable()
    {
        var result = await RunAsync(Build(new TempDir(),
            new Step("Outer", "For", Config: "from=1;to=2;var=a"),
            new Step("Inner", "For", Parent: "Outer", Config: "from=1;to=2;var=b"),
            new Step("Call", Parent: "Inner", Url: "/c/_a_/_b_")), Ok);
        Assert.Equal(["/c/1/1", "/c/1/2", "/c/2/1", "/c/2/2"], result.Paths);
    }

    [Theory]
    [InlineData("", new[] { "/n/1", "/n/2" })]                                   // default: the row stops
    [InlineData("onfailure=stop", new[] { "/n/1", "/n/2" })]
    [InlineData("onfailure=break", new[] { "/n/1", "/n/2", "/after" })]          // leave the loop, carry on after it
    [InlineData("onfailure=continue", new[] { "/n/1", "/n/2", "/n/3", "/after" })] // next round
    public async Task A_failed_step_inside_a_loop_follows_onfailure(string onFailure, string[] expected)
    {
        var result = await RunAsync(Build(new TempDir(),
            new Step("Loop", "For", Config: "from=1;to=3" + (onFailure.Length > 0 ? ";" + onFailure : "")), new Step("Each", Parent: "Loop", Url: "/n/_i_"), new Step("After", Url: "/after")),
            Ok, p => p.EndsWith("/2") ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
        Assert.Equal(expected, result.Paths);
        Assert.Contains(result.Run.Testcases.SelectMany(t => t.Requests), r => r.StepName == "Each" && r.Result == "FAIL");
    }

    [Fact]
    public async Task Dontrun_and_enabled_on_a_control_step_skip_everything_inside()
    {
        var skipped = await RunAsync(Build(new TempDir(), "Loop",
            new Step("Loop", "For", Config: "from=1;to=3"), new Step("Each", Parent: "Loop", Url: "/n/_i_"), new Step("After", Url: "/after")), Ok);
        Assert.Equal(["/after"], skipped.Paths);
    }

    [Fact]
    public void A_parent_must_be_a_control_step_of_the_same_testcase_and_nesting_is_limited()
    {
        using var dir = new TempDir();
        string Read(params Step[] steps) { try { new WorkbookReader().Read(Build(new TempDir(), steps)); return "ok"; } catch (InvalidDataException ex) { return ex.Message; } }

        Assert.Contains("which is not a step", Read(new Step("A", Parent: "Nope", Url: "/a")));
        Assert.Contains("must be If, While, For or ForEach", Read(new Step("P", Url: "/p"), new Step("A", Parent: "P", Url: "/a")));
        Assert.Contains("':else' only belongs to an If", Read(new Step("L", "For", Config: "to=1"), new Step("A", Parent: "L:else", Url: "/a")));
        Assert.Contains("inside itself", Read(new Step("X", "For", Parent: "Y", Config: "to=1"), new Step("Y", "For", Parent: "X", Config: "to=1")));
        var deep = Enumerable.Range(0, 7).Select(i => new Step("L" + i, "For", Parent: i == 0 ? "" : "L" + (i - 1), Config: "to=1")).Append(new Step("Leaf", Parent: "L6", Url: "/a")).ToArray();
        Assert.Contains("more than 5 levels", Read(deep));
        Assert.Equal("ok", Read(new Step("L", "For", Config: "to=1"), new Step("A", Parent: "L", Url: "/a")));
    }
}

public class ControlFlowValidationTests
{
    private static List<string> Problems(params (string Name, string Type, string Parent, string Condition, string Config)[] steps)
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (config, request, response) =>
        {
            config.Cell(1, 9).Value = "parent"; config.Cell(1, 10).Value = "condition"; config.Cell(1, 11).Value = "stepconfig";
            for (var i = 0; i < steps.Length; i++)
            {
                var row = i + 3;   // row 2 is the sample CreatePost step
                var (name, type, parent, condition, stepConfig) = steps[i];
                config.Cell(row, 1).Value = 1; config.Cell(row, 2).Value = "Flow"; config.Cell(row, 3).Value = name; config.Cell(row, 4).Value = type;
                config.Cell(row, 5).Value = type == "HTTP" ? "t.json" : ""; config.Cell(row, 6).Value = type == "HTTP" ? "http://h/x" : ""; config.Cell(row, 7).Value = "POST"; config.Cell(row, 8).Value = true;
                config.Cell(row, 9).Value = parent; config.Cell(row, 10).Value = condition; config.Cell(row, 11).Value = stepConfig;
            }
        });
        return LoadTestingTool.Editing.WorkbookValidator.Validate(LoadTestingTool.Editing.WorkbookDocument.Load(path)).Select(p => p.Message).ToList();
    }

    [Fact]
    public void Each_control_step_says_what_it_is_missing()
    {
        var problems = Problems(("C1", "If", "", "", ""), ("C2", "For", "", "", ""), ("C3", "ForEach", "", "", ""), ("C4", "While", "", "1 eq 1", ""), ("C5", "For", "", "", "to=2;onfailure=explode"));
        Assert.Contains(problems, m => m.Contains("If steps need a condition"));
        Assert.Contains(problems, m => m.Contains("For step needs 'to' or 'count'"));
        Assert.Contains(problems, m => m.Contains("ForEach step needs 'items'"));
        Assert.Contains(problems, m => m.Contains("While step without 'max'"));
        Assert.Contains(problems, m => m.Contains("onfailure 'explode'"));
        Assert.DoesNotContain(problems, m => m.Contains("need a StepConfig"));   // control steps do not need one
    }

    [Fact]
    public void A_parent_that_is_unknown_not_a_control_step_or_has_a_wrong_else_is_reported()
    {
        var problems = Problems(("L", "For", "", "", "to=2"), ("Bad1", "HTTP", "Nope", "", ""), ("Bad2", "HTTP", "CreatePost", "", ""), ("Bad3", "HTTP", "L:else", "", ""), ("Good", "HTTP", "L", "", ""));
        Assert.Contains(problems, m => m.Contains("Parent 'Nope' is not a step"));
        Assert.Contains(problems, m => m.Contains("Parent 'CreatePost' is a HTTP step"));
        Assert.Contains(problems, m => m.Contains("':else' only belongs to an If"));
        Assert.DoesNotContain(problems, m => m.Contains("'Good'"));
    }

    [Fact]
    public void A_correct_if_with_else_and_a_loop_with_a_range_has_no_control_problems()
    {
        var problems = Problems(("Check", "If", "", "_status_ eq 200", ""), ("T", "HTTP", "Check", "", ""), ("E", "HTTP", "Check:else", "", ""), ("Loop", "For", "", "", "from=1;to=3"), ("Each", "HTTP", "Loop", "", ""));
        Assert.DoesNotContain(problems, m => m.Contains("Parent") || m.Contains("condition") || m.Contains("needs 'to'") || m.Contains("':else'"));
    }
}
