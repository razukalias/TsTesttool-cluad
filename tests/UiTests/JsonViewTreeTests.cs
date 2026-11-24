using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class JsonViewTreeTests
{
    private static DataEditorViewModel Open(TempDir dir, params object[] events)
    {
        var path = Samples.CreateLarge(dir, rows: 4);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson", events);
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        return editor;
    }

    private static object[] TwoEnvironments() =>
    [
        FakeRun.Request(1, "CUST001", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Customer 1", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", category: "Assertion", env: "test"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "x", env: "test"),
        FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", category: "Assertion", env: "dev"), FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "y", env: "dev"),
        FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", category: "Assertion", env: "test"), FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "y", env: "test"),
        FakeRun.Request(1, "CUST003", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST003", "CreateCustomer", "name", "eq", "Customer 3", "Customer 3", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST003", "CreateCustomer", result: "FAIL", status: 0, category: "Timeout", error: "The request timed out", env: "test"),
        FakeRun.Request(1, "CUST004", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST004", "CreateCustomer", "name", "eq", "Customer 4", "Customer 4", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST004", "CreateCustomer", env: "test"), FakeRun.Assertion(1, "CUST004", "CreateCustomer", "name", "eq", "Customer 4", "Customer 4", result: "PASS", env: "test"),
    ];

    [Fact]
    public void The_response_tree_holds_the_expected_columns_of_the_step_with_counts_and_environment_differences()
    {
        using var dir = new TempDir();
        var editor = Open(dir, TwoEnvironments());
        editor.StepFilter = "CreateCustomer";
        var root = editor.BuildJsonTree(JsonTab.Response, "CreateCustomer");
        var name = root.Leaves().Single(l => l.Path == "name");
        Assert.NotNull(name.Column);
        editor.TallyJsonTree(root, "CreateCustomer");
        Assert.Equal((1, 2, 1), (name.Passed, name.Failed, name.Errors));   // CUST004 passes; CUST001 and CUST002 fail; CUST003's step broke
        Assert.True(name.EnvDiffers);                                       // CUST001 passes in dev and fails in test
        Assert.Equal(name.Failed, root.Failed);                             // the step node adds its items up
    }

    [Fact]
    public void A_cell_reports_each_environment_and_what_the_run_got()
    {
        using var dir = new TempDir();
        var editor = Open(dir, TwoEnvironments());
        editor.StepFilter = "CreateCustomer";
        var root = editor.BuildJsonTree(JsonTab.Response, "CreateCustomer");
        var name = root.Leaves().Single(l => l.Path == "name");
        var results = editor.JsonCellResults(editor.VisibleRows.Single(r => r.CurrentDataId == "CUST001"), "CreateCustomer", name);
        Assert.Equal([("dev", "PASS"), ("test", "FAIL")], results.Select(r => (r.Environment, r.State)).ToArray());
        Assert.Equal("x", results[1].Actual);
        Assert.Equal("ERROR", editor.JsonCellResults(editor.VisibleRows.Single(r => r.CurrentDataId == "CUST003"), "CreateCustomer", name)[1].State);
    }

    [Fact]
    public void Filters_decide_which_rows_are_counted()
    {
        using var dir = new TempDir();
        var editor = Open(dir, TwoEnvironments());
        editor.StepFilter = "CreateCustomer";
        editor.ShowFailedOnly = true;
        var root = editor.BuildJsonTree(JsonTab.Response, "CreateCustomer");
        editor.TallyJsonTree(root, "CreateCustomer");
        var name = root.Leaves().Single(l => l.Path == "name");
        Assert.Equal(0, name.Passed);           // CUST004 is filtered out
        Assert.Equal(3, name.Failed + name.Errors);
    }

    [Fact]
    public void Request_tabs_split_the_variables_between_the_template_and_the_rest_and_show_shared_ones()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.StepFilter = "CreateCustomer";
        var template = editor.BuildJsonTree(JsonTab.Template, "CreateCustomer");
        var config = editor.BuildJsonTree(JsonTab.Config, "CreateCustomer");
        var all = template.Leaves().Concat(config.Leaves()).Where(l => l.Column is not null).Select(l => l.Column!.Title).ToList();
        Assert.Contains(all, t => t.StartsWith("CreateCustomer.name") || t == "name");
        Assert.Contains("dontrun", all);                                       // a shared column shows up in one of the two tabs
        Assert.DoesNotContain(template.Leaves().Select(l => l.Name), n => config.Leaves().Any(c => c.Name == n && c.Column is not null && template.Leaves().Any(t => t.Name == n && t.Column is not null)));   // no item in both
    }

    [Fact]
    public void Fields_of_the_last_response_without_a_column_are_extra_and_columns_the_response_lacks_are_missing()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        Directory.CreateDirectory(dir.Combine("History"));
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer";
            sheet.Cell(2, 4).Value = "{\"name\":\"Customer 1\",\"phone\":\"123\",\"address\":{\"city\":\"Oslo\"}}";   // no "id"; has phone and address.city
            book.SaveAs(System.IO.Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        var editor = new DataEditorViewModel(path, "Customers");
        editor.StepFilter = "CreateCustomer";
        var root = editor.BuildJsonTree(JsonTab.Response, "CreateCustomer");
        Assert.True(root.Leaves().Single(l => l.Path == "phone").Extra);
        Assert.Equal("123", root.Leaves().Single(l => l.Path == "phone").Sample);
        Assert.True(root.Leaves().Single(l => l.Path == "address.city").Extra);
        Assert.Equal("address", root.Children.Single(c => c.Name == "address").Name);          // nested by the dots
        Assert.True(root.Leaves().Single(l => l.Path == "id").Missing);                         // a column, but the response has no id
        Assert.False(root.Leaves().Single(l => l.Path == "name").Missing || root.Leaves().Single(l => l.Path == "name").Extra);
        Assert.False(root.Leaves().Single(l => l.Path == "statusCode").Missing);               // the status is not in the body, and is not "missing"
    }
}

public class JsonViewXmlTests
{
    [Fact]
    public void An_xml_response_and_an_xml_template_give_trees_like_json_does()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        Directory.CreateDirectory(dir.Combine("History"));
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer";
            sheet.Cell(2, 4).Value = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><CreateResponse xmlns=\"http://t/\"><Id>7</Id><Lines><Line sku=\"a\">1</Line><Line sku=\"b\">2</Line></Lines></CreateResponse></s:Body></s:Envelope>";
            book.SaveAs(System.IO.Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        var templates = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "Templates");
        Directory.CreateDirectory(templates);
        File.WriteAllText(System.IO.Path.Combine(templates, "create.json"), "<Create xmlns=\"http://t/\"><Name>_name_</Name><Mail a=\"_email_\"/><Extra>_note_</Extra></Create>");
        var editor = new DataEditorViewModel(path, "Customers");
        editor.StepFilter = "CreateCustomer";

        var response = editor.BuildJsonTree(JsonTab.Response, "CreateCustomer");
        var paths = response.Leaves().Where(l => l.Extra).Select(l => l.Path).OrderBy(p => p).ToList();
        Assert.Contains("CreateResponse.Id", paths);
        Assert.Contains(paths, p => p.StartsWith("CreateResponse.Lines.Line[0]") && p.EndsWith("@sku"));

        var template = editor.BuildJsonTree(JsonTab.Template, "CreateCustomer");
        Assert.Equal(["email", "name", "note"], template.Leaves().Select(l => l.Variable).OrderBy(n => n).ToArray());
        var create = template.Children.Single(c => c.Name == "Create");
        Assert.Equal(["Name", "Mail", "Extra"], create.Children.Select(c => c.Name).ToArray());   // the structure of the template
        Assert.Equal("name", create.Children.Single(c => c.Name == "Name").Variable);
        Assert.Equal("email", create.Children.Single(c => c.Name == "Mail").Children.Single(c => c.Name == "@a").Variable);   // an attribute is @a below its element
        Assert.Equal(["note"], template.Leaves().Where(l => l.Extra).Select(l => l.Variable).ToArray());   // name and email have columns, note has not
        Assert.Equal("CreateCustomer.name", template.Leaves().Single(l => l.Variable == "name").Column!.Title);

        // repeated elements are one node with [0] and [1], and the paths are still the runner's
        var lines = response.Children.Single(c => c.Name == "CreateResponse").Children.Single(c => c.Name == "Lines").Children.Single(c => c.Name == "Line");
        Assert.Equal(["[0]", "[1]"], lines.Children.Select(c => c.Name).ToArray());
        Assert.Equal("CreateResponse.Lines.Line[1]", lines.Children[1].Path);
        Assert.Contains(lines.Children[0].Leaves(), l => l.Path == "CreateResponse.Lines.Line[0].@sku");
    }

    [Fact]
    public void A_json_template_with_bare_placeholders_becomes_a_tree_with_arrays_and_fixed_text()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var templates = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "Templates");
        Directory.CreateDirectory(templates);
        File.WriteAllText(System.IO.Path.Combine(templates, "create.json"), """
            {
              "metadata": { "serie": "_serie_", "title": "Hello _first_ _last_" },
              "counterparts": [
                { "persOrgNumber": "_counterpart[0]_", "isPrimary": _isPrimary[0]_ },
                { "persOrgNumber": "_counterpart[1]_", "isPrimary": _isPrimary[1]_ }
              ],
              "userId": "ttttt"
            }
            """);
        var editor = new DataEditorViewModel(path, "Customers");
        editor.StepFilter = "CreateCustomer";
        var tree = editor.BuildJsonTree(JsonTab.Template, "CreateCustomer");

        Assert.Equal(["metadata", "counterparts", "userId"], tree.Children.Select(c => c.Name).ToArray());
        var counterparts = tree.Children.Single(c => c.Name == "counterparts");
        Assert.Equal(["[0]", "[1]"], counterparts.Children.Select(c => c.Name).ToArray());
        var first = counterparts.Children[0];
        Assert.Equal("counterparts[0].persOrgNumber", first.Children.Single(c => c.Name == "persOrgNumber").Path);
        Assert.Equal("counterpart[0]", first.Children.Single(c => c.Name == "persOrgNumber").Variable);
        Assert.Equal("isPrimary[1]", counterparts.Children[1].Children.Single(c => c.Name == "isPrimary").Variable);   // a bare placeholder is a variable too
        var title = tree.Children[0].Children.Single(c => c.Name == "title");
        Assert.Equal(["first", "last"], title.Children.Select(c => c.Variable).ToArray());                         // two variables in one text
        var user = tree.Children.Single(c => c.Name == "userId");
        Assert.True(user.Constant); Assert.Equal("ttttt", user.Sample); Assert.Null(user.Column);
        Assert.Equal(new[] { "first", "isPrimary[0]", "isPrimary[1]", "last", "counterpart[0]", "counterpart[1]", "serie" }.OrderBy(x => x), tree.Leaves().Where(l => !l.Constant).Select(l => l.Variable).OrderBy(x => x));
        Assert.Equal(7, DataEditorViewModel.JsonExtras(tree).Count);   // none of them has a column yet
    }

    [Fact]
    public void The_raw_text_is_the_stored_response_or_the_template_with_the_rows_values()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        Directory.CreateDirectory(dir.Combine("History"));
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer"; sheet.Cell(2, 4).Value = "{\"a\":1}";
            book.SaveAs(System.IO.Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        var templates = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "Templates");
        Directory.CreateDirectory(templates);
        File.WriteAllText(System.IO.Path.Combine(templates, "create.json"), "{\"name\":\"_name_\",\"other\":\"_other_\"}");
        var editor = new DataEditorViewModel(path, "Customers");
        editor.StepFilter = "CreateCustomer";
        var first = editor.VisibleRows[0];
        Assert.Contains("\"a\": 1", editor.JsonRawText(JsonTab.Response, "CreateCustomer", first));          // laid out for reading
        Assert.Contains("No stored response for CUST002", editor.JsonRawText(JsonTab.Response, "CreateCustomer", editor.VisibleRows[1]));
        var template = editor.JsonRawText(JsonTab.Template, "CreateCustomer", first);
        Assert.Contains("\"name\":\"Customer 1\"", template);   // the row's value in place of _name_
        Assert.Contains("_other_", template);                      // no column: stays as written
    }
}
