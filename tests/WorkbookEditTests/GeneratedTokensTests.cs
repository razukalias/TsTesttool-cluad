using ClosedXML.Excel;
using System.Globalization;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

/// <summary>Random numbers, GUIDs and dates are made when a step needs them: fresh per cell, per token and per iteration; a reference shares its cell's value.</summary>
[Collection("RunValueMemory")]
public class GeneratedTokensTests
{
    /// <summary>Runs <paramref name="work"/> as one data row and iteration: every Resolve inside it shares the row's scope.</summary>
    private static T Row<T>(int iteration, Func<T> work, string dataId = "D1")
    {
        var previous = GeneratedTokens.Scope.Value;
        GeneratedTokens.Scope.Value = new GeneratedTokens.RowScope(RunValueMemory.RowKey(1, dataId, iteration, ""));
        try { return work(); }
        finally { GeneratedTokens.Scope.Value = previous; }
    }

    private static string Resolve(string text, int iteration = 1) => Row(iteration, () => GeneratedTokens.Resolve(text));

    [Fact]
    public void The_reader_leaves_markers_instead_of_values_and_the_text_around_them_is_untouched()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) => request.Cell(2, 5).Value = "id-<randomnumber:000000>-<guid:N>-end");
        var raw = new WorkbookReader().Read(path).RequestData[(1, "D1")].Variables["title"];

        Assert.True(GeneratedTokens.HasMarkers(raw));
        Assert.Equal("id-<randomnumber:000000>-<guid:N>-end", GeneratedTokens.ToDisplay(raw));
        Assert.Matches(@"^id-\d{6}-[0-9a-f]{32}-end$", Resolve(raw));
    }

    [Fact]
    public void Every_cell_and_every_token_gets_its_own_value_but_one_scope_keeps_them_steady()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) =>
        {
            request.Cell(1, 6).Value = "other";
            request.Cell(2, 5).Value = "<randomnumber:000000>/<randomnumber:000000>";
            request.Cell(2, 6).Value = "<randomnumber:000000>/<randomnumber:000000>";   // identical text in another cell
        });
        var vars = new WorkbookReader().Read(path).RequestData[(1, "D1")].Variables;

        var (title, other, titleAgain) = Row(1, () => (GeneratedTokens.Resolve(vars["title"]), GeneratedTokens.Resolve(vars["other"]), GeneratedTokens.Resolve(vars["title"])));
        var parts = title.Split('/').Concat(other.Split('/')).ToList();
        Assert.Equal(4, parts.Distinct().Count());                 // four tokens, four different values (a repeat is a one in a million chance per pair)
        Assert.Equal(title, titleAgain);                           // inside one row and iteration a cell keeps its value
    }

    [Fact]
    public void A_new_iteration_gets_new_values_and_a_reference_gets_the_value_that_was_sent()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, response) =>
        {
            request.Cell(2, 5).Value = "order-<randomnumber:000000>";
            response.Cell(2, 4).Value = "{eq}<From_request_title_r>";
        });
        var model = new WorkbookReader().Read(path);
        var sent = model.RequestData[(1, "D1")].Variables["title"];
        var expected = model.Assertions[(1, "D1", "CreatePost")][0].ExpectedValue;

        var (first, firstExpected) = Row(1, () => (GeneratedTokens.Resolve(sent), GeneratedTokens.Resolve(expected)));
        var (second, secondExpected) = Row(2, () => (GeneratedTokens.Resolve(sent), GeneratedTokens.Resolve(expected)));
        Assert.Equal(first, firstExpected);                            // what the expected cell refers to is what was sent
        Assert.NotEqual(first, second);                                // the next iteration is fresh
        Assert.Equal(second, secondExpected);
    }

    [Fact]
    public void A_date_is_made_when_it_is_needed_not_when_the_workbook_is_read()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) => request.Cell(2, 5).Value = "<currentdatetime:yyyyMMddHHmmssfff>");
        var raw = new WorkbookReader().Read(path).RequestData[(1, "D1")].Variables["title"];
        var afterRead = DateTime.Now;
        Thread.Sleep(60);

        var made = DateTime.ParseExact(Resolve(raw), "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

        Assert.True(made >= afterRead.AddMilliseconds(40), $"made {made:O}, read at {afterRead:O}");
    }

    [Fact]
    public void Without_a_scope_every_use_is_fresh()
    {
        Assert.NotEqual(GeneratedTokens.Make("randomnumber", "000000000"), GeneratedTokens.Make("randomnumber", "000000000"));
        Assert.Equal(36, GeneratedTokens.Make("guid", null).Length);
    }
}
