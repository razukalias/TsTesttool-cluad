using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using Xunit;

namespace WorkbookEditTests;

public class ResponseTreeTests
{
    private const string Json = """
        { "total": 2, "ok": true, "none": null,
          "items": [ {"id": 7, "name": "A", "tags": ["x","y"]}, {"id": 8, "name": "B", "tags": []}, {"id": 9, "name": "C"} ],
          "customer": { "id": 5, "address": { "city": "Oslo", "geo": { "lat": 59.9 } } },
          "odd.name": 1 }
        """;

    private static ResponseNode Tree() => ResponseTree.Parse(Json, out _)!;

    private static string[] Paths(IEnumerable<GeneratedHeader> headers) => headers.Select(h => h.Path).ToArray();

    private static List<GeneratedHeader> Gen(params (string Path, NodeChoice Choice)[] choices)
        => ResponseTree.Generate(Tree(), choices.ToDictionary(c => c.Path, c => c.Choice), out _, out _);

    [Fact]
    public void The_tree_has_paths_kinds_counts_and_the_values_of_leaves()
    {
        var root = Tree();
        Assert.Equal(ResponseNodeKind.Object, root.Kind);
        var items = ResponseTree.Find(root, "items")!;
        Assert.Equal((ResponseNodeKind.Array, 3), (items.Kind, items.Count));
        Assert.Equal("items[1].name", items.Children[1].Children[1].Path);
        Assert.Equal("B", ResponseTree.Find(root, "items[1].name")!.Value);
        Assert.Equal(string.Empty, ResponseTree.Find(root, "none")!.Value);
        Assert.Equal("true", ResponseTree.Find(root, "ok")!.Value);
        Assert.True(ResponseTree.Find(root, "odd.name")!.OddName);
    }

    [Fact]
    public void A_response_that_is_not_json_says_so()
    {
        Assert.Null(ResponseTree.Parse("<a/>", out var note));
        Assert.Contains("not JSON", note);
        Assert.Null(ResponseTree.Parse("", out var empty));
        Assert.Contains("empty", empty);
    }

    [Fact]
    public void A_ticked_value_is_one_header_with_its_value()
    {
        var headers = Gen(("total", new NodeChoice(NodeMode.AsOne)), ("items[0].id", new NodeChoice(NodeMode.AsOne)));
        Assert.Equal(["total", "items[0].id"], Paths(headers));
        Assert.Equal(["2", "7"], headers.Select(h => h.Value).ToArray());
    }

    [Fact]
    public void An_array_or_object_taken_as_one_header_is_its_count()
    {
        var headers = Gen(("items", new NodeChoice(NodeMode.AsOne)), ("customer", new NodeChoice(NodeMode.AsOne)), ("items[0].tags", new NodeChoice(NodeMode.AsOne)));
        Assert.Equal(["items.#count", "items[0].tags.#count", "customer.#count"], Paths(headers));
        Assert.Equal("3", headers.First(h => h.Path == "items.#count").Value);
        Assert.Equal("2", headers.First(h => h.Path == "customer.#count").Value);
        Assert.Equal("2", headers.First(h => h.Path == "items[0].tags.#count").Value);
        Assert.All(headers, h => Assert.Equal(GeneratedKind.Count, h.Kind));
    }

    [Fact]
    public void Flatten_an_object_to_a_depth_leaves_deeper_containers_out_and_counts_them()
    {
        var one = ResponseTree.Generate(Tree(), new Dictionary<string, NodeChoice> { ["customer"] = new(NodeMode.Flatten, Depth: 1) }, out _, out var skipped);
        Assert.Equal(["customer.id"], Paths(one));
        Assert.Equal(1, skipped);
        Assert.Equal(["customer.id", "customer.address.city"], Paths(Gen(("customer", new NodeChoice(NodeMode.Flatten, Depth: 2)))));
        Assert.Equal(["customer.id", "customer.address.city", "customer.address.geo.lat"], Paths(Gen(("customer", new NodeChoice(NodeMode.Flatten)))));
    }

    [Fact]
    public void Flatten_an_array_takes_the_first_element_by_default_and_N_or_all_on_request()
    {
        Assert.Equal(["items[0].id", "items[0].name", "items[0].tags[0]"], Paths(Gen(("items", new NodeChoice(NodeMode.Flatten)))));   // the first element of every array on the way
        var two = Paths(Gen(("items", new NodeChoice(NodeMode.Flatten, Elements: 2))));
        Assert.Contains("items[1].name", two);
        Assert.Contains("items[0].tags[1]", two);
        Assert.DoesNotContain("items[2].id", two);
        Assert.Contains("items[2].id", Paths(Gen(("items", new NodeChoice(NodeMode.Flatten, Elements: ResponseTree.MaxElements)))));
    }

    [Fact]
    public void A_choice_on_a_node_below_wins_over_the_flatten_that_reaches_it_and_duplicates_are_dropped()
    {
        var headers = Gen(("customer", new NodeChoice(NodeMode.Flatten)), ("customer.address", new NodeChoice(NodeMode.AsOne)), ("customer.id", new NodeChoice(NodeMode.AsOne)));
        Assert.Equal(["customer.id", "customer.address.#count"], Paths(headers));
    }

    [Fact]
    public void The_root_can_be_flattened_and_nothing_chosen_gives_nothing()
    {
        Assert.Empty(Gen());
        var all = Gen(("", new NodeChoice(NodeMode.Flatten, Depth: 1)));
        Assert.Equal(["total", "ok", "none", "odd.name"], Paths(all));
        Assert.Equal(["#count"], Paths(Gen(("", new NodeChoice(NodeMode.AsOne)))));
    }

    [Fact]
    public void More_headers_than_the_limit_are_cut_and_say_so()
    {
        var big = "[" + string.Join(",", Enumerable.Range(0, 60).Select(i => $"{{\"a\":{i},\"b\":{i},\"c\":{i}}}")) + "]";
        var root = ResponseTree.Parse(big, out _)!;
        var headers = ResponseTree.Generate(root, new Dictionary<string, NodeChoice> { [""] = new(NodeMode.Flatten, Elements: 60) }, out var truncated, out _, maxHeaders: 50);
        Assert.Equal(50, headers.Count);
        Assert.True(truncated);
    }

    [Fact]
    public void A_big_array_keeps_only_the_first_elements_but_knows_its_length()
    {
        var root = ResponseTree.Parse("{\"a\":[" + string.Join(",", Enumerable.Range(0, 300)) + "]}", out _)!;
        var array = ResponseTree.Find(root, "a")!;
        Assert.Equal(300, array.Count);
        Assert.Equal(ResponseTree.MaxElements, array.Children.Count);
        Assert.True(array.Cut);
    }

    [Fact]
    public void Values_for_a_header_come_from_any_response_and_missing_paths_are_null()
    {
        var other = ResponseTree.Parse("{\"items\":[{\"id\":99}],\"total\":1}", out _)!;
        Assert.Equal("99", ResponseTree.ValueAt(other, "items[0].id"));
        Assert.Equal("1", ResponseTree.ValueAt(other, "items.#count"));
        Assert.Null(ResponseTree.ValueAt(other, "items[1].id"));
        Assert.Null(ResponseTree.ValueAt(other, "customer.#count"));
        Assert.Null(ResponseTree.ValueAt(other, "items"));              // a container is not a value
    }

    // ---- The runner understands the count path ----------------------------------------------------------------

    private static AssertionDefinition Def(string path, string expected, string verb = "eq") => new()
    {
        TestcaseIndex = 1, Testcase = "t", DataId = "d", StepName = "s", ResponsePath = path, ExpectedValue = expected, AssertionVerb = verb, ExcelRowNumber = 2, ExpectedValueColumn = 5,
    };

    [Theory]
    [InlineData("items.#count", "3", "eq", "PASS")]
    [InlineData("items.#count", "3", "gte", "PASS")]
    [InlineData("items.#count", "4", "gte", "FAIL")]
    [InlineData("items.#count", "0", "gt", "PASS")]
    [InlineData("customer.#count", "2", "eq", "PASS")]
    [InlineData("items[0].tags.#count", "2", "eq", "PASS")]
    [InlineData("items[1].tags.#count", "0", "eq", "PASS")]
    [InlineData("#count", "6", "eq", "PASS")]
    public void The_count_path_gives_the_number_of_elements_or_properties(string path, string expected, string verb, string result)
    {
        var results = new AssertionEngine().Evaluate(Json, "json", [Def(path, expected, verb)], "r", "q");
        Assert.Equal(result, Assert.Single(results.Where(r => r.ResponsePath == path)).Result);
    }

    [Fact]
    public void A_count_header_does_not_make_the_other_fields_unexpected_and_a_count_of_a_value_is_missing()
    {
        var engine = new AssertionEngine();
        var only = engine.Evaluate("{\"a\":[1,2],\"b\":3}", "json", [Def("a.#count", "2"), Def("b", "3")], "r", "q");
        Assert.All(only.Where(r => r.ResponsePath is "a.#count" or "b"), r => Assert.Equal("PASS", r.Result));
        Assert.DoesNotContain(only, r => r.ResponsePath == "a.#count" && r.AssertionVerb == "contract");   // the count itself is never "unexpected"
        Assert.Equal(["a[0]", "a[1]"], only.Where(r => r.AssertionVerb == "contract").Select(r => r.ResponsePath).OrderBy(p => p).ToArray());   // the leaves not asserted are reported as before
    }
}

public class CountHeaderWorkbookTests
{
    [Fact]
    public void A_count_header_written_to_the_response_sheet_is_read_as_an_assertion_on_the_count_path_with_its_verb()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (config, request, response) => { });
        var doc = LoadTestingTool.Editing.WorkbookDocument.Load(path);
        var id = doc.Response.Rows.First(r => r.Get("dataid").Length > 0).Get("dataid");
        doc.Response.EnsureColumn("{gte}Step.items.#count");
        doc.Response.Rows.First(r => r.Get("dataid") == id).Set("{gte}Step.items.#count", "2");
        doc.Save();

        var model = new LoadTestingTool.Excel.WorkbookReader().Read(path);
        var count = model.Assertions.Values.SelectMany(a => a).Single(a => a.ResponsePath == "items.#count");
        Assert.Equal("gte", count.AssertionVerb);
        Assert.Equal("2", count.ExpectedValue);
        Assert.Equal("step", count.StepName.ToLowerInvariant());
        var result = new AssertionEngine().Evaluate("{\"items\":[1,2,3]}", "json", [count], "r", "q", assertOnlyResponse: true);
        Assert.Equal("PASS", Assert.Single(result).Result);
    }
}
