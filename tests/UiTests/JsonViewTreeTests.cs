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
