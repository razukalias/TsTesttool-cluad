using ClosedXML.Excel;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

// The Bugs tab's statistics: bug tags counted per instance, testcase and step, read from the workbook without a run.
public class BugStatisticsTests
{
    private static string Workbook(TempDir dir, params (int Case, string Id, string Header, string Value)[] cells)
    {
        var path = dir.Combine("wb.xlsx");
        using var wb = new XLWorkbook();
        var config = wb.AddWorksheet("config");
        string[] head = ["testcaseindex", "testcase", "stepname"];
        for (var i = 0; i < head.Length; i++) config.Cell(1, i + 1).Value = head[i];
        (int, string, string)[] steps = [(1, "Customers", "Create"), (1, "Customers", "Get"), (1, "Customers", "Delete"), (2, "Orders", "Place")];
        for (var r = 0; r < steps.Length; r++) { config.Cell(r + 2, 1).Value = steps[r].Item1; config.Cell(r + 2, 2).Value = steps[r].Item2; config.Cell(r + 2, 3).Value = steps[r].Item3; }
        var response = wb.AddWorksheet("response");
        response.Cell(1, 1).Value = "testcaseindex"; response.Cell(1, 2).Value = "dataid";
        var headers = cells.Select(c => c.Header).Distinct().ToList();
        for (var h = 0; h < headers.Count; h++) response.Cell(1, h + 3).Value = headers[h];
        var row = 2;
        foreach (var group in cells.GroupBy(c => (c.Case, c.Id)))
        {
            response.Cell(row, 1).Value = group.Key.Case; response.Cell(row, 2).Value = group.Key.Id;
            foreach (var c in group) response.Cell(row, 3 + headers.IndexOf(c.Header)).Value = c.Value;
            row++;
        }
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public void Counts_add_up_from_steps_to_testcases_to_the_instance_without_any_run()
    {
        using var dir = new TempDir();
        var path = Workbook(dir,
            (1, "A", "Create.name", "Bo <bug: all; x>"), (1, "B", "Create.name", "Cy <bug: dev>"), (1, "A", "Get.age", "3 <bug: all>"),
            (1, "A", "Delete.ok", "true"), (2, "A", "Place.total", "9 <bug: all>"));
        var root = BugStatistics.Build("large", BugStatistics.Scan(path));
        Assert.Equal("Instance", root.Kind);
        Assert.Equal(4, root.Count);
        var customers = root.Children[0];
        Assert.Equal("Customers", customers.Name);
        Assert.Equal(3, customers.Count);
        Assert.Equal(new[] { 2, 1, 0 }, customers.Children.Select(s => s.Count).ToArray());   // Delete stays listed with 0
        Assert.Equal(1, root.Children[1].Count);
    }

    [Fact]
    public void A_workbook_without_tags_or_a_missing_file_counts_zero_and_a_new_tag_shows_after_a_rescan()
    {
        using var dir = new TempDir();
        var path = Workbook(dir, (1, "A", "Create.name", "Bo"));
        Assert.Equal(0, BugStatistics.Build("x", BugStatistics.Scan(path)).Count);
        Assert.Equal(0, BugStatistics.Build("x", BugStatistics.Scan(dir.Combine("missing.xlsx"))).Count);
        using (var wb = new XLWorkbook(path)) { wb.Worksheet("response").Cell(2, 3).Value = "Bo <bug: all; new>"; wb.Save(); }
        Assert.Equal(1, BugStatistics.Build("x", BugStatistics.Scan(path)).Count);
    }

    [Fact]
    public void Nodes_know_their_instance_testcase_and_step()
    {
        using var dir = new TempDir();
        var root = BugStatistics.Build("inst", BugStatistics.Scan(Workbook(dir, (2, "A", "Place.total", "9 <bug: all>"))));
        var step = root.Children[1].Children[0];
        Assert.Equal(("inst", "Orders", "Place"), (step.Instance, step.Testcase, step.Step));
        Assert.Null(root.Testcase);
    }
}
