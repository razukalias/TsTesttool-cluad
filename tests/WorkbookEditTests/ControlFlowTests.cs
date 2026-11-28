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

    private static string Build(TempDir dir, string? dontrun, params Step[] steps) => Build(dir, dontrun, null, steps);

    private static string Build(TempDir dir, string? dontrun, Action<ClosedXML.Excel.IXLWorksheet>? requestSheet, params Step[] steps)
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
            requestSheet?.Invoke(request);
        });
    }

    private static async Task<(List<string> Steps, List<string> Paths, RunResult Run)> RunAsync(string path, Func<string, string> answer, Func<string, HttpStatusCode>? status = null)
    {
        var model = new WorkbookReader().Read(path);
        var server = new Server(answer, status);
        var runner = new TestRunner(new HttpClient(server), new AssertionEngine(), System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "Templates"), 30, features: new FeatureConfig { WorkspaceRoot = System.IO.Path.GetDirectoryName(path)! });
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        await runner.RunAsync(model.Testcases[0], model, run, CancellationToken.None, selectedDataIds: new HashSet<string> { "D1" }, executionMode: "sequential");
        return (run.Testcases.SelectMany(t => t.Requests).Select(r => r.StepName).ToList(), server.Paths, run);
    }

    private static string Ok(string path) => "{\"ok\":true,\"items\":[\"x\",\"y\"]}";

    [Fact]
    public async Task A_csharp_condition_can_use_references_variables_and_code()
    {
        async Task<List<string>> PathsFor(string condition)
        {
            var path = Build(new TempDir(),
                new Step("First", Url: "/first"),
                new Step("Check", "If", Condition: condition),
                new Step("Then1", Parent: "Check", Url: "/then"),
                new Step("Else1", Parent: "Check:else", Url: "/else"));
            return (await RunAsync(path, Ok)).Paths;
        }
        Assert.Equal(["/first", "/then"], await PathsFor("cs: <From_response_First.items_r>.Contains(\"y\") && !string.IsNullOrEmpty(<From_response_First.ok_r>)"));
        Assert.Equal(["/first", "/else"], await PathsFor("cs: <From_response_First.items_r>.Contains(\"zzz\")"));
        Assert.Equal(["/first", "/then"], await PathsFor("cs:\n var n = <From_response_First.items_r>.Length;\n return n > 3;"));
    }

    [Fact]
    public async Task A_csharp_loop_condition_stops_a_for_loop()
    {
        using var dir = new TempDir();
        var path = Build(dir,
            new Step("Loop", "For", Config: "from=1;to=9;stopwhen=cs: int.Parse(_i_) * 2 >= 6"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_"));
        var (_, paths, _) = await RunAsync(path, Ok);
        Assert.Equal(["/n/1", "/n/2", "/n/3"], paths);
    }

    [Fact]
    public async Task A_csharp_condition_that_does_not_compile_or_is_not_true_or_false_fails_only_that_step()
    {
        var bad = await RunAsync(Build(new TempDir(), new Step("Bad", "If", Condition: "cs: 1 +"), new Step("After", Url: "/after")), Ok);
        Assert.Empty(bad.Paths);
        Assert.Contains(bad.Run.Testcases.SelectMany(t => t.Requests), r => (r.ErrorMessage ?? "").Contains("does not compile"));
        var num = await RunAsync(Build(new TempDir(), new Step("Num", "If", Condition: "cs: 42"), new Step("After", Url: "/after")), Ok);
        Assert.Contains(num.Run.Testcases.SelectMany(t => t.Requests), r => (r.ErrorMessage ?? "").Contains("must give true or false"));
    }

    [Fact]
    public void Script_placeholders_are_found_outside_comments_and_replaced_by_their_text_in_strings()
    {
        var code = "// <From_response_A.x_r> in a comment\n_i_ == \"<From_response_A.y_r>\" && V(\"_i_\") != _j_";
        var found = ConditionScript.Find(code, ["i"]);
        Assert.Equal(["_i_", "<From_response_A.y_r>", "_i_"], found.Select(f => f.Text).ToList());   // _j_ is not a variable; the comment is skipped
        Assert.Equal([false, true, true], found.Select(f => f.InString).ToList());
        var rewritten = ConditionScript.Rewrite(code, found, p => p.Text == "_i_" ? "3" : "say \"hi\"", out var values);
        Assert.Contains("V(\"_i_\") == \"say \\\"hi\\\"\" && V(\"3\") != _j_", rewritten);
        Assert.Equal("3", values["_i_"]);
        Assert.Null(ConditionScript.Check("cs: V(\"a\") == \"b\""));
        Assert.Contains("line 1", ConditionScript.Check("cs: 1 +"));
    }

    private static string Json(string path) => "{\"id\":\"A7\",\"n\":3,\"v\":\"" + path + "\"}";

    [Fact]
    public async Task A_step_can_save_response_values_as_output_variables_for_conditions_loops_and_later_steps()
    {
        using var dir = new TempDir();
        var path = Build(dir,
            new Step("First", Url: "/first", Config: "outputs=orderId=id,code=statusCode,count=n"),
            new Step("Check", "If", Condition: "_orderId_ eq A7 and _code_ eq 200"),
            new Step("Then1", Parent: "Check", Url: "/then/_orderId_"),
            new Step("Loop", "For", Config: "from=1;to=9;stopwhen=_count_ eq _i_"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_"));
        var (_, paths, _) = await RunAsync(path, Json);
        Assert.Equal(["/first", "/then/A7", "/n/1", "/n/2", "/n/3"], paths);
    }

    [Fact]
    public async Task A_relative_reference_reads_the_step_before_and_the_previous_round_of_a_loop()
    {
        var before = await RunAsync(Build(new TempDir(),
            new Step("First", Url: "/first"),
            new Step("Second", Url: "/second"),
            new Step("Check", "If", Condition: "<From_response_@prev.id_r> eq A7"),
            new Step("Then1", Parent: "Check", Url: "/then")), Json);
        Assert.Equal(["/first", "/second", "/then"], before.Paths);

        var rounds = await RunAsync(Build(new TempDir(),
            new Step("Loop", "For", Config: "from=1;to=3"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_"),
            new Step("Later", "If", Parent: "Loop", Condition: "_i_ gt 1"),
            new Step("Echo", Parent: "Later", Url: "/echo<From_response_@lastround.Each.v_r>")), Json);
        Assert.Equal(["/n/1", "/n/2", "/echo/n/1", "/n/3", "/echo/n/2"], rounds.Paths);

        var none = await RunAsync(Build(new TempDir(), new Step("Only", Url: "/only"), new Step("Check", "If", Condition: "<From_response_@lastround.Only.id_r> eq A7")), Json);
        Assert.Contains(none.Run.Testcases.SelectMany(t => t.Requests), r => (r.ErrorMessage ?? "").Contains("no previous round"));
    }

    [Fact]
    public async Task A_loop_or_condition_can_read_a_cell_of_the_data_row_that_is_running()
    {
        var path = Build(new TempDir(), null, request => { request.Cell(1, 5).Value = "limit"; request.Cell(2, 5).Value = 2; },
            new Step("Loop", "For", Config: "from=1;to=<From_request_limit_r>"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_"),
            new Step("Check", "If", Condition: "<From_request_limit_r> eq 2"),
            new Step("Yes", Parent: "Check", Url: "/yes"));
        var (_, paths, _) = await RunAsync(path, Ok);
        Assert.Equal(["/n/1", "/n/2", "/yes"], paths);
    }

    [Fact]
    public void The_ControlFlowScenarios_instance_has_no_workbook_problems()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "Instances", "ControlFlowScenarios"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var path = System.IO.Path.Combine(dir!.FullName, "Instances", "ControlFlowScenarios", "ControlFlowScenarios.xlsx");
        var problems = LoadTestingTool.Editing.WorkbookValidator.Validate(LoadTestingTool.Editing.WorkbookDocument.Load(path));
        Assert.DoesNotContain(problems, p => p.Severity == LoadTestingTool.Editing.ProblemSeverity.Error);
        Assert.Equal(10, new LoadTestingTool.Excel.WorkbookReader().Read(path).Testcases.Count);
    }

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
        Assert.Equal(["First", "Check", "Then1", "After"], steps);   // the If writes a small result row (Check.output = true)

        var (_, otherPaths, _) = await RunAsync(Build(new TempDir(),
            new Step("First", Url: "/first"),
            new Step("Check", "If", Condition: "<From_response_First.ok_r> eq false"),
            new Step("Then1", Parent: "Check", Url: "/then"),
            new Step("Else1", Parent: "Check:else", Url: "/else")), Ok);
        Assert.Equal(["/first", "/else"], otherPaths);
    }

    [Fact]
    public async Task A_loop_and_an_if_publish_output_and_index_to_later_steps_and_can_be_checked()
    {
        using var dir = new TempDir();
        var path = Build(dir,
            new Step("Each", "ForEach", Config: "{\"items\":\"a,b,c\"}"),
            new Step("Call", Parent: "Each", Url: "/x/<From_response_Each.output_r>/<From_response_Each.index_r>"),
            new Step("Check", "If", Condition: "1 eq 1"),
            new Step("Then1", Parent: "Check", Url: "/after/<From_response_Check.output_r>"));
        var (steps, paths, _) = await RunAsync(path, Ok);
        Assert.Equal(["/x/a/0", "/x/b/1", "/x/c/2", "/after/true"], paths);
        Assert.Equal(["Each", "Call", "Each", "Call", "Each", "Call", "Check", "Then1"], steps);
    }

    [Fact]
    public async Task Loop_settings_and_conditions_can_come_from_the_request_columns_of_the_data_row()
    {
        var path = Build(new TempDir(), null, request => { request.Cell(1, 5).Value = "Loop.rounds"; request.Cell(2, 5).Value = 2; request.Cell(1, 6).Value = "Gate.who"; request.Cell(2, 6).Value = "yes"; },
            new Step("Loop", "For", Config: "from=1;count=_rounds_"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_"),
            new Step("Gate", "If", Condition: "_who_ eq yes"),
            new Step("Then1", Parent: "Gate", Url: "/then"));
        var (_, paths, _) = await RunAsync(path, Ok);
        Assert.Equal(["/n/1", "/n/2", "/then"], paths);
    }

    [Fact]
    public void A_reference_to_a_step_that_does_not_run_before_is_warned_about()
    {
        var path = Build(new TempDir(), null, request =>
            {
                request.Cell(1, 5).Value = "First.late"; request.Cell(2, 5).Value = "<From_response_Last.id_r>";
                request.Cell(1, 6).Value = "Last.bad"; request.Cell(2, 6).Value = "<From_response_First.output_r> <From_response_Ghost.id_r>";
            },
            new Step("First", Url: "/a"), new Step("Last", Url: "/b"));
        var messages = LoadTestingTool.Editing.WorkbookValidator.Validate(LoadTestingTool.Editing.WorkbookDocument.Load(path)).Where(p => p.Severity == LoadTestingTool.Editing.ProblemSeverity.Warning).Select(p => p.Message).ToList();
        Assert.Contains(messages, m => m.Contains("Last, which does not run before First"));
        Assert.Contains(messages, m => m.Contains("First.output exists only on File"));
        Assert.Contains(messages, m => m.Contains("'Ghost' that this testcase does not have"));
    }

    [Fact]
    public void Renaming_a_step_renames_its_output_index_and_file_columns_and_removing_it_reports_the_ones_with_data()
    {
        var path = Build(new TempDir(), null, request => { request.Cell(1, 5).Value = "Loop.to"; request.Cell(2, 5).Value = 3; },
            new Step("Loop", "For", Config: "from=1;to=_to_"), new Step("Each", Parent: "Loop", Url: "/n/_i_"));
        var doc = LoadTestingTool.Editing.WorkbookDocument.Load(path);
        doc.Response.EnsureColumn("{eq}Loop.output"); doc.Response.EnsureColumn("{eq}Loop.index");
        Assert.Equal(["Loop.to"], doc.StepColumnsWithData("Loop"));
        doc.RenameStep("Loop", "Outer");
        Assert.Contains("Outer.to", doc.Request.Headers);
        Assert.Contains("{eq}Outer.output", doc.Response.Headers); Assert.Contains("{eq}Outer.index", doc.Response.Headers);
    }

    [Fact]
    public async Task A_loop_with_rows_no_writes_no_result_rows_but_the_values_stay_available()
    {
        var path = Build(new TempDir(), new Step("Loop", "For", Config: "from=1;to=2;rows=no"), new Step("Each", Parent: "Loop", Url: "/n/<From_response_Loop.output_r>/<From_response_Loop.index_r>"));
        var (steps, paths, _) = await RunAsync(path, Ok);
        Assert.Equal(["Each", "Each"], steps);
        Assert.Equal(["/n/1/0", "/n/2/1"], paths);
    }

    [Fact]
    public void Inside_one_loop_a_step_may_read_a_later_step_of_the_same_loop_without_a_warning()
    {
        var path = Build(new TempDir(), null, request =>
            {
                request.Cell(1, 5).Value = "Check.prev"; request.Cell(2, 5).Value = "<From_response_Save.id_r>";
                request.Cell(1, 6).Value = "Outside.prev"; request.Cell(2, 6).Value = "<From_response_Save.id_r>";
            },
            new Step("Loop", "For", Config: "from=1;to=2"), new Step("Check", Parent: "Loop", Url: "/c"), new Step("Save", Parent: "Loop", Url: "/s"), new Step("Outside", Url: "/o"));
        var messages = LoadTestingTool.Editing.WorkbookValidator.Validate(LoadTestingTool.Editing.WorkbookDocument.Load(path)).Select(p => p.Message).ToList();
        Assert.DoesNotContain(messages, m => m.Contains("Check.prev") && m.Contains("does not run before"));
    }

    [Fact]
    public async Task A_C_sharp_condition_can_use_a_variable_line_from_a_request_column_and_an_if_can_switch_its_row_off()
    {
        var text = LoadTestingTool.Execution.ConditionScript.WithVariable("cs: who == \"yes\"", "who", "_who_");
        Assert.Equal("cs: var who = \"_who_\"; return who == \"yes\";", text);
        Assert.Equal("cs: var a = \"_a_\"; var b = \"_b_\"; return a == b;", LoadTestingTool.Execution.ConditionScript.WithVariable("cs: var b = \"_b_\"; return a == b;", "a", "_a_").Replace("cs: var a = \"_a_\"; var b", "cs: var a = \"_a_\"; var b"));
        var path = Build(new TempDir(), null, request => { request.Cell(1, 5).Value = "Gate.who"; request.Cell(2, 5).Value = "yes"; },
            new Step("Gate", "If", Condition: text, Config: "rows=no"), new Step("Then1", Parent: "Gate", Url: "/then"));
        var (steps, paths, _) = await RunAsync(path, Ok);
        Assert.Equal(["/then"], paths);
        Assert.Equal(["Then1"], steps);   // rows=no: the If wrote no result row
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
    public async Task For_can_stop_when_a_condition_holds_after_a_round_and_skip_rounds_before_they_start()
    {
        var stop = await RunAsync(Build(new TempDir(),
            new Step("Loop", "For", Config: "from=1;to=10;stopwhen=_i_ eq 3"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_"),
            new Step("After", Url: "/after")), Ok);
        Assert.Equal(["/n/1", "/n/2", "/n/3", "/after"], stop.Paths);

        var skip = await RunAsync(Build(new TempDir(),
            new Step("Loop", "For", Config: "from=1;to=5;skipwhen=_i_ eq 2 or _i_ eq 4"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_")), Ok);
        Assert.Equal(["/n/1", "/n/3", "/n/5"], skip.Paths);

        var both = await RunAsync(Build(new TempDir(),
            new Step("Loop", "For", Config: "{\"from\":1,\"to\":9,\"skipwhen\":\"_i_ lt 3\",\"stopwhen\":\"_i_ gte 5 and not _i_ eq 9\"}"),
            new Step("Each", Parent: "Loop", Url: "/n/_i_")), Ok);
        Assert.Equal(["/n/3", "/n/4", "/n/5"], both.Paths);
    }

    [Fact]
    public async Task ForEach_stop_and_skip_see_the_item_and_a_response_inside_the_loop()
    {
        var run = await RunAsync(Build(new TempDir(),
            new Step("Each", "ForEach", Config: "items=a,b,c,d;skipwhen=_item_ eq b;stopwhen=<From_response_Item.ok_r> eq true and _index_ gte 2"),
            new Step("Item", Parent: "Each", Url: "/item/_item_")), Ok);
        Assert.Equal(["/item/a", "/item/c"], run.Paths);   // b is skipped, the stop is met after c (index 2)
    }

    [Fact]
    public async Task An_if_and_a_while_use_and_or_not_and_brackets()
    {
        var run = await RunAsync(Build(new TempDir(),
            new Step("First", Url: "/first"),
            new Step("Check", "If", Condition: "(<From_response_First.ok_r> eq true and 1 eq 2) or not <From_response_First.ok_r> eq false"),
            new Step("Then1", Parent: "Check", Url: "/then"),
            new Step("Else1", Parent: "Check:else", Url: "/else")), Ok);
        Assert.Equal(["/first", "/then"], run.Paths);

        var bad = await RunAsync(Build(new TempDir(),
            new Step("Check", "If", Condition: "1 eq 1 and (2 eq 2"),
            new Step("Then1", Parent: "Check", Url: "/then")), Ok);
        Assert.DoesNotContain("/then", bad.Paths);
        Assert.Contains(bad.Run.Testcases.SelectMany(t => t.Requests), r => r.ErrorMessage.Contains("not closed"));
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
    public async Task ForEach_reads_the_rows_of_a_csv_file_with_item_column_variables()
    {
        var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Files"));
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Files"), "users.csv"), "id,email\n1,a@x.io\n2,\"b,b@x.io\"\n3,c@x.io\n");
        var result = await RunAsync(Build(dir, new Step("Each", "ForEach", Config: "file=users.csv;skip=1;take=2"), new Step("U", Parent: "Each", Url: "/u/_item.id_/_index_")), Ok);
        Assert.Equal(["/u/2/0", "/u/3/1"], result.Paths);
        var one = await RunAsync(Build(dir, new Step("Each", "ForEach", Config: "file=users.csv;column=id"), new Step("U", Parent: "Each", Url: "/n/_item_")), Ok);
        Assert.Equal(["/n/1", "/n/2", "/n/3"], one.Paths);
    }

    [Fact]
    public async Task ForEach_reads_an_excel_sheet_and_a_json_array_path_and_objects_of_a_response()
    {
        var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Files"));
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            book.AddWorksheet("Other").Cell(1, 1).Value = "x";
            var sheet = book.AddWorksheet("People");
            sheet.Cell(1, 1).Value = "name"; sheet.Cell(1, 2).Value = "age"; sheet.Cell(2, 1).Value = "Ann"; sheet.Cell(2, 2).Value = 31; sheet.Cell(3, 1).Value = "Bo"; sheet.Cell(3, 2).Value = 40;
            book.SaveAs(System.IO.Path.Combine(dir.Combine("Files"), "p.xlsx"));
        }
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Files"), "d.json"), "{\"data\":{\"users\":[{\"id\":7,\"n\":\"q\"},{\"id\":8,\"n\":\"r\"}]}}");
        var excel = await RunAsync(Build(dir, new Step("Each", "ForEach", Config: "file=p.xlsx;sheet=People"), new Step("U", Parent: "Each", Url: "/p/_item.name_/_item.age_")), Ok);
        Assert.Equal(["/p/Ann/31", "/p/Bo/40"], excel.Paths);
        var json = await RunAsync(Build(dir, new Step("Each", "ForEach", Config: "file=d.json;path=$.data.users"), new Step("U", Parent: "Each", Url: "/j/_item.id_/_item.n_")), Ok);
        Assert.Equal(["/j/7/q", "/j/8/r"], json.Paths);
        var objects = await RunAsync(Build(new TempDir(), new Step("List", Url: "/list"), new Step("Each", "ForEach", Config: "items=<From_response_List.people_r>"), new Step("U", Parent: "Each", Url: "/o/_item.id_")),
            p => p == "/list" ? "{\"people\":[{\"id\":5},{\"id\":6}]}" : "{}");
        Assert.Equal(["/list", "/o/5", "/o/6"], objects.Paths);
    }

    [Fact]
    public async Task ForEach_file_problems_are_said_in_plain_words()
    {
        var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Files"));
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Files"), "u.csv"), "id\n1\n");
        foreach (var (config, expect) in new[] { ("file=none.csv", "was not found"), ("file=u.csv;column=zip", "There is no column 'zip'") })
        {
            var result = await RunAsync(Build(dir, new Step("Each", "ForEach", Config: config), new Step("U", Parent: "Each", Url: "/u")), Ok);
            Assert.Contains(result.Run.Testcases.SelectMany(t => t.Requests), r => (r.ErrorMessage ?? "").Contains(expect));
        }
    }

    [Fact]
    public void RowSources_inspect_tells_sheets_columns_rows_and_json_lists_for_the_browse_preview()
    {
        var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Files"));
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Files"), "d.json"), "{\"a\":{\"list\":[{\"x\":1}]},\"b\":[{\"x\":2},{\"x\":3}]}");
        var info = RowSources.Inspect(dir.Path, "d.json", new RowSourceOptions { JsonPath = "$.b" });
        Assert.Equal(2, info.TotalRows);
        Assert.Equal(["x"], info.Columns);
        Assert.Contains("$.a.list", info.Arrays); Assert.Contains("$.b", info.Arrays);
        var bad = Assert.Throws<InvalidDataException>(() => RowSources.Inspect(dir.Path, "d.json", new RowSourceOptions { JsonPath = "$.nope" }));
        Assert.Contains("no array at $.nope", bad.Message);
    }

    [Fact]
    public async Task A_file_step_reading_excel_or_info_gives_values_later_steps_can_use()
    {
        var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Files"));
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = book.AddWorksheet("S"); sheet.Cell(1, 1).Value = "name"; sheet.Cell(2, 1).Value = "Ann"; sheet.Cell(3, 1).Value = "Bo";
            book.SaveAs(System.IO.Path.Combine(dir.Combine("Files"), "p.xlsx"));
        }
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Files"), "d.json"), "[{\"x\":1}]");
        var result = await RunAsync(Build(dir,
            new Step("Rd", "File", Config: "{\"Action\":\"Read\",\"Path\":\"p.xlsx\"}"),
            new Step("Inf", "File", Config: "{\"Action\":\"Info\",\"Path\":\"d.json\"}"),
            new Step("U", Url: "/u/<From_response_Rd.rows[1].name_r>/<From_response_Inf.rows_r>/<From_response_Inf.sizeBytes_r>")), Ok);
        Assert.True(result.Paths.Count > 0, string.Join(" | ", result.Run.Testcases.SelectMany(t => t.Requests).Select(r => r.StepName + ": " + r.ErrorMessage)));
        Assert.Equal(["/u/Bo/1/9"], result.Paths);
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
    public async Task A_guid_in_a_step_inside_a_loop_is_new_every_round()
    {
        var result = await RunAsync(Build(new TempDir(),
            new Step("Loop", "For", Config: "from=1;to=3"), new Step("Each", Parent: "Loop", Url: "/g/<guid>"), new Step("Once", Url: "/o/<guid>")), Ok);
        var guids = result.Paths.Where(p => p.StartsWith("/g/")).ToList();
        Assert.Equal(3, guids.Count);
        Assert.Equal(3, guids.Distinct().Count());
        Assert.Single(result.Paths, p => p.StartsWith("/o/"));
    }

    [Fact]
    public async Task A_loop_that_runs_longer_than_its_timeout_fails_between_rounds()
    {
        var result = await RunAsync(Build(new TempDir(),
            new Step("Wait", "While", Condition: "1 eq 1", Config: "max=1000;delayms=40;timeoutms=150"), new Step("Poll", Parent: "Wait", Url: "/poll"), new Step("After", Url: "/after")), Ok);
        Assert.DoesNotContain("/after", result.Paths);
        Assert.InRange(result.Paths.Count, 2, 8);
        var failure = result.Run.Testcases.SelectMany(t => t.Requests).Single(r => r.Result == "FAIL");
        Assert.Contains("longer than its timeout (150 ms)", failure.ErrorMessage);
    }

    [Fact]
    public async Task Results_inside_a_loop_say_which_round_made_them()
    {
        var result = await RunAsync(Build(new TempDir(),
            new Step("Loop", "For", Config: "from=1;to=2"), new Step("Each", Parent: "Loop", Url: "/e"), new Step("Once", Url: "/o")), Ok);
        var rows = result.Run.Testcases.SelectMany(t => t.Requests).ToList();
        Assert.Equal(new[] { "Loop #1", "Loop #2", "" }, rows.Where(r => r.StepName != "Loop").Select(r => r.LoopRound).ToArray());
    }

    [Fact]
    public async Task An_ActionManual_step_inside_a_loop_asks_once_per_round()
    {
        using var dir = new TempDir();
        var path = Build(dir, new Step("Loop", "For", Config: "from=1;to=3"), new Step("Ask", "ActionManual", Parent: "Loop"));
        var model = new WorkbookReader().Read(path);
        var control = dir.Combine("manual");
        Directory.CreateDirectory(control);
        var prompts = new List<string>();
        var runner = new TestRunner(new HttpClient(new Server(Ok)), new AssertionEngine(), dir.Combine("Templates"), 30, manualPending: (request, _, _) =>
        {
            prompts.Add(request.RequestId);
            LoadTestingTool.Reporting.ManualActionControl.WriteResponse(control, request.RequestId, "pass", "ok");
        }, manualControlFolder: control);
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        await runner.RunAsync(model.Testcases[0], model, run, CancellationToken.None, selectedDataIds: new HashSet<string> { "D1" }, executionMode: "sequential");
        Assert.Equal(3, prompts.Count);
        Assert.Equal(3, prompts.Distinct().Count());
        Assert.Equal(new[] { "Loop #1", "Loop #2", "Loop #3" }, run.Testcases.SelectMany(t => t.Requests).Where(r => r.StepName == "Ask").Select(r => r.LoopRound).ToArray());
    }

    [Fact]
    public async Task A_re_run_that_reuses_values_gets_the_same_guid_in_every_round_as_before()
    {
        using var dir = new TempDir();
        var path = Build(dir, new Step("Loop", "For", Config: "from=1;to=3"), new Step("Each", Parent: "Loop", Url: "/g/<guid>"));
        var store = new MemoryRunStateStore();
        async Task<List<string>> OnceAsync(bool reuse)
        {
            var memory = new RunValueMemory(store);
            RunValueMemory.Active = memory;
            try
            {
                memory.BeginRead(reuse);
                var result = await RunAsync(path, Ok);
                memory.Save();
                return result.Paths.Where(p => p.StartsWith("/g/")).ToList();
            }
            finally { RunValueMemory.Active = null; }
        }
        var first = await OnceAsync(false);
        var second = await OnceAsync(true);
        var third = await OnceAsync(false);
        Assert.Equal(3, first.Distinct().Count());
        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
    }

    [Fact]
    public async Task The_live_event_of_a_step_in_a_loop_names_its_round()
    {
        using var dir = new TempDir();
        var path = Build(dir, new Step("Loop", "For", Config: "from=1;to=2"), new Step("Each", Parent: "Loop", Url: "/e"));
        var result = await RunAsync(path, Ok);
        var folder = dir.Combine("events");
        string file;
        using (var writer = new LoadTestingTool.Reporting.LiveEventWriter(folder, "i", "r"))
        {
            foreach (var request in result.Run.Testcases.SelectMany(t => t.Requests).Where(r => r.StepName == "Each")) writer.WriteRequest(request);
            file = writer.EventPath;
        }
        var rounds = File.ReadAllLines(file).Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement)
            .Where(e => e.GetProperty("eventType").GetString() == "request-completed").Select(e => e.GetProperty("loopRound").GetString()).ToArray();
        Assert.Equal(new[] { "Loop #1", "Loop #2" }, rounds);
    }

    [Fact]
    public async Task A_condition_can_read_the_status_code_of_an_earlier_step()
    {
        var result = await RunAsync(Build(new TempDir(),
            new Step("Get", Url: "/get"), new Step("Check", "If", Condition: "<From_response_Get.statusCode_r> eq 202"),
            new Step("Yes", Parent: "Check", Url: "/yes"), new Step("No", Parent: "Check:else", Url: "/no")), Ok, path => HttpStatusCode.Accepted);
        Assert.Contains("/yes", result.Paths);
        Assert.DoesNotContain("/no", result.Paths);
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
    public void A_bad_output_name_is_reported_and_a_good_one_is_not()
    {
        var problems = Problems(("A", "HTTP", "", "", "outputs=1bad=id,good=id,my_name=id"));
        Assert.Contains(problems, m => m.Contains("Output name '1bad'"));
        Assert.Contains(problems, m => m.Contains("Output name 'my_name'"));
        Assert.DoesNotContain(problems, m => m.Contains("Output name 'good'"));
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

public class ControlFlowEditingTests
{
    private static LoadTestingTool.Editing.WorkbookDocument Open(TempDir dir, params (string Name, string Type, string Parent)[] steps)
    {
        var path = Workbooks.CreateSmall(dir, (config, request, response) =>
        {
            config.Cell(1, 9).Value = "parent";
            for (var i = 0; i < steps.Length; i++)
            {
                var row = i + 2;
                config.Cell(row, 1).Value = 1; config.Cell(row, 2).Value = "Flow"; config.Cell(row, 3).Value = steps[i].Name; config.Cell(row, 4).Value = steps[i].Type;
                config.Cell(row, 5).Value = "t.json"; config.Cell(row, 6).Value = "http://h/x"; config.Cell(row, 7).Value = "POST"; config.Cell(row, 8).Value = true;
                config.Cell(row, 9).Value = steps[i].Parent;
            }
        });
        return LoadTestingTool.Editing.WorkbookDocument.Load(path);
    }

    private static string Tree(LoadTestingTool.Editing.WorkbookDocument d) => string.Join(" ", d.GetNesting(1).Select(n => new string('.', n.Depth) + n.Name + (n.InElse ? "~" : "")));
    private static LoadTestingTool.Editing.TableRow Row(LoadTestingTool.Editing.WorkbookDocument d, string name) => d.Config.Rows.First(r => r.Get("stepname") == name);

    [Fact]
    public void File_and_script_steps_get_an_output_column_when_created_once()
    {
        using var dir = new TempDir();
        var d = Open(dir, ("ReadPdf", "File", ""), ("Calc", "Script", ""), ("Call", "HTTP", ""));
        d.Config.Rows.First(r => r.Get("stepname") == "Calc").Set("stepname", "Calc");
        d.Response.Rows.FirstOrDefault()?.Set("x", "");
        Assert.Equal(2, d.EnsureOutputColumns());
        Assert.Contains(d.Response.Headers, h => h == "{eq}ReadPdf.output");
        Assert.Contains(d.Response.Headers, h => h == "{eq}Calc.output");
        Assert.DoesNotContain(d.Response.Headers, h => h.Contains("Call.output"));
        Assert.Equal(0, d.EnsureOutputColumns());
    }

    [Fact]
    public void Steps_inside_a_disabled_control_step_are_marked_as_not_reached()
    {
        using var dir = new TempDir();
        var d = Open(dir, ("A", "HTTP", ""), ("If1", "If", ""), ("T", "HTTP", "If1"), ("L", "For", "If1"), ("Deep", "HTTP", "L"));
        d.Config.Rows.First(r => r.Get("stepname") == "If1").Set("enabled", "False");
        var nesting = d.GetNesting(1).ToDictionary(n => n.Name);
        Assert.True(nesting["If1"].IsDisabled); Assert.False(nesting["If1"].ParentDisabled);
        Assert.True(nesting["T"].ParentDisabled); Assert.True(nesting["Deep"].ParentDisabled); Assert.False(nesting["Deep"].IsDisabled);
        Assert.False(nesting["A"].ParentDisabled);
    }

    [Fact]
    public void Nesting_is_read_from_the_parent_column()
    {
        using var dir = new TempDir();
        var d = Open(dir, ("A", "HTTP", ""), ("If1", "If", ""), ("T", "HTTP", "If1"), ("E", "HTTP", "If1:else"), ("L", "For", "If1"), ("Deep", "HTTP", "L"));
        Assert.Equal("A If1 .T .E~ .L ..Deep", Tree(d));
        Assert.Equal(["", "If", "", "", "For", ""], d.GetNesting(1).Select(n => n.IsControl ? n.TypeName : "").ToArray());
    }

    [Fact]
    public void Putting_a_step_inside_a_control_step_makes_it_the_last_one_in_it_and_keeps_the_rows_in_tree_order()
    {
        using var dir = new TempDir();
        var d = Open(dir, ("L", "For", ""), ("Inner1", "HTTP", "L"), ("Other", "HTTP", ""), ("After", "HTTP", ""));
        d.SetStepParent(Row(d, "After"), "L");
        Assert.Equal("L .Inner1 .After Other", Tree(d));
        Assert.Equal(["L", "Inner1", "After", "Other"], d.GetTestcases()[0].Steps);   // run order of the rows follows the tree
        d.SetStepParent(Row(d, "Other"), "L");
        Assert.Equal("L .Inner1 .After .Other", Tree(d));
    }

    [Fact]
    public void A_control_step_takes_the_steps_inside_it_along_and_cannot_go_inside_itself()
    {
        using var dir = new TempDir();
        var d = Open(dir, ("Outer", "For", ""), ("Mid", "If", ""), ("Leaf", "HTTP", "Mid"), ("Z", "HTTP", ""));
        d.SetStepParent(Row(d, "Mid"), "Outer");
        Assert.Equal("Outer .Mid ..Leaf Z", Tree(d));
        Assert.Throws<InvalidOperationException>(() => d.SetStepParent(Row(d, "Outer"), "Mid"));
        Assert.Throws<InvalidOperationException>(() => d.SetStepParent(Row(d, "Z"), "Leaf"));            // not a control step
        Assert.Throws<InvalidOperationException>(() => d.SetStepParent(Row(d, "Z"), "Outer", elseBranch: true));   // only If has else
        d.SetStepParent(Row(d, "Z"), "Mid", elseBranch: true);
        Assert.Equal("Outer .Mid ..Leaf ..Z~", Tree(d));
    }

    [Fact]
    public void Moving_out_puts_the_step_right_after_the_control_step_it_left()
    {
        using var dir = new TempDir();
        var d = Open(dir, ("L", "For", ""), ("A", "HTTP", "L"), ("B", "HTTP", "L"), ("Next", "HTTP", ""));
        d.MoveStepOut(Row(d, "A"));
        Assert.Equal("L .B A Next", Tree(d));
        Assert.Throws<InvalidOperationException>(() => d.MoveStepOut(Row(d, "A")));
        d.SetStepParent(Row(d, "B"), null);
        Assert.Equal("L B A Next", Tree(d));
    }

    [Fact]
    public void The_arrows_swap_a_step_with_its_neighbour_inside_the_same_parent_and_take_its_children_along()
    {
        using var dir = new TempDir();
        var d = Open(dir, ("L", "For", ""), ("A", "If", "L"), ("A1", "HTTP", "A"), ("B", "HTTP", "L"), ("End", "HTTP", ""));
        Assert.True(d.MoveStep(Row(d, "B"), -1));
        Assert.Equal("L .B .A ..A1 End", Tree(d));
        Assert.True(d.MoveStep(Row(d, "B"), 1));
        Assert.Equal("L .A ..A1 .B End", Tree(d));
        Assert.False(d.MoveStep(Row(d, "B"), 1));   // last inside L
        Assert.True(d.MoveStep(Row(d, "L"), 1));
        Assert.Equal("End L .A ..A1 .B", Tree(d));
    }

    [Fact]
    public void Renaming_a_control_step_updates_its_children_and_removing_it_lifts_them_up()
    {
        using var dir = new TempDir();
        var d = Open(dir, ("Outer", "For", ""), ("If1", "If", "Outer"), ("T", "HTTP", "If1"), ("E", "HTTP", "If1:else"));
        d.RenameStep("If1", "Check");
        Assert.Equal("Outer .Check ..T ..E~", Tree(d));
        d.RemoveStep(Row(d, "Check"));
        Assert.Equal("Outer .T .E", Tree(d));   // they take the removed step's own parent
    }
}
