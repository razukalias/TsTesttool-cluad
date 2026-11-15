using ClosedXML.Excel;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

[Collection("RunValueMemory")]
public class RunValueMemoryTests
{
    private static string Workbook(TempDir dir) => Workbooks.CreateSmall(dir, (_, request, _) =>
    {
        request.Cell(2, 5).Value = "order-<randomnumber:000000>-<guid>";
        request.Cell(3, 5).Value = "second-<randomnumber:000000>";
    });

    private static string Title(string path, string dataId = "D1")
        => new WorkbookReader().Read(path).RequestData[(1, dataId)].Variables.First(v => v.Key.Equals("title", StringComparison.OrdinalIgnoreCase)).Value;

    private static string ReadWith(string path, bool reuse, bool save = true, string dataId = "D1")
    {
        var memory = new RunValueMemory(path);
        RunValueMemory.Active = memory;
        try { memory.BeginRead(reuse); var value = Title(path, dataId); if (save) memory.Save(); return value; }
        finally { RunValueMemory.Active = null; }
    }

    [Fact]
    public void A_partial_re_run_gets_the_same_random_number_and_guid_back()
    {
        using var dir = new TempDir();
        var path = Workbook(dir);

        var firstRun = ReadWith(path, reuse: false);
        var again = ReadWith(path, reuse: true);
        var freshRun = ReadWith(path, reuse: false);

        Assert.Equal(firstRun, again);
        Assert.NotEqual(firstRun, freshRun);              // a normal run still generates new values
        Assert.Matches(@"^order-\d{6}-[0-9a-f-]{36}$", firstRun);
    }

    [Fact]
    public void An_edited_cell_gets_a_new_value_while_untouched_cells_keep_theirs()
    {
        using var dir = new TempDir();
        var path = Workbook(dir);
        var first1 = ReadWith(path, reuse: false);
        var first2 = ReadWith(path, reuse: true, dataId: "D2");

        using (var book = new XLWorkbook(path))
        {
            book.Worksheet("request").Cell(2, 5).Value = "changed-<randomnumber:000000>";
            book.Save();
        }

        Assert.Equal(first2, ReadWith(path, reuse: true, dataId: "D2"));
        var edited = ReadWith(path, reuse: true);
        Assert.StartsWith("changed-", edited);
        Assert.NotEqual(first1, edited);
    }

    [Fact]
    public void Values_extracted_by_earlier_steps_are_kept_per_row_and_iteration()
    {
        using var dir = new TempDir();
        var path = Workbook(dir);
        var memory = new RunValueMemory(path);
        memory.SetRow(RunValueMemory.RowKey(1, "D1", 1, ""), new Dictionary<string, string> { ["token"] = "abc", ["orderId"] = "42" });
        memory.Save();

        var next = new RunValueMemory(path);
        Assert.True(next.HasPrevious);
        Assert.Equal("abc", next.PreviousRow(RunValueMemory.RowKey(1, "D1", 1, ""))["token"]);
        Assert.Empty(next.PreviousRow(RunValueMemory.RowKey(1, "D1", 2, "")));   // another iteration
        Assert.Empty(next.PreviousRow(RunValueMemory.RowKey(1, "D2", 1, "")));   // another data row
    }

    [Fact]
    public void A_reusing_save_keeps_rows_it_did_not_run()
    {
        using var dir = new TempDir();
        var path = Workbook(dir);
        var first = new RunValueMemory(path);
        first.SetRow("1|D1|1|default", new Dictionary<string, string> { ["a"] = "1" });
        first.SetRow("1|D2|1|default", new Dictionary<string, string> { ["a"] = "2" });
        first.Save();

        var partial = new RunValueMemory(path);
        partial.BeginRead(reuse: true);
        partial.SetRow("1|D1|1|default", new Dictionary<string, string> { ["a"] = "1", ["b"] = "new" });
        partial.Save();

        var check = new RunValueMemory(path);
        Assert.Equal("new", check.PreviousRow("1|D1|1|default")["b"]);
        Assert.Equal("2", check.PreviousRow("1|D2|1|default")["a"]);
    }

    [Fact]
    public void Date_and_time_tokens_are_remembered_too()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) => request.Cell(2, 5).Value = "at-<currentdatetime:yyyyMMddHHmmssfff>");

        var first = ReadWith(path, reuse: false);
        Thread.Sleep(30);
        Assert.Equal(first, ReadWith(path, reuse: true));
        Assert.NotEqual(first, ReadWith(path, reuse: false));
    }

    [Fact]
    public async Task Resolved_step_text_is_replayed_only_for_the_same_step_and_text()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var calls = 0;
        Task<string> Resolve() => Task.FromResult($"value-{++calls}");

        var first = new RunValueMemory(path);
        first.BeginRead(reuse: false);
        RunValueMemory.StepScope.Value = "1|D1|1|default|CreatePost";
        Assert.Equal("value-1", await first.StepTextAsync("body <script>", Resolve));
        Assert.Equal("plain", await first.StepTextAsync("plain", () => Task.FromResult("plain")));   // unchanged text is not stored
        first.Save();

        var rerun = new RunValueMemory(path);
        rerun.BeginRead(reuse: true);
        Assert.Equal("value-1", await rerun.StepTextAsync("body <script>", Resolve));          // replayed, resolver not called
        Assert.Equal(1, calls);
        Assert.Equal("value-2", await rerun.StepTextAsync("body <script> changed", Resolve));  // edited text resolves anew
        RunValueMemory.StepScope.Value = "1|D1|1|default|OtherStep";
        Assert.Equal("value-3", await rerun.StepTextAsync("body <script>", Resolve));          // another step does not borrow it
        RunValueMemory.StepScope.Value = null;
    }

    [Fact]
    public void Only_the_chosen_steps_get_their_old_values_back()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (config, request, _) =>
        {
            config.Cell(3, 1).Value = 1; config.Cell(3, 2).Value = "Flow"; config.Cell(3, 3).Value = "Second"; config.Cell(3, 4).Value = "HTTP";
            config.Cell(3, 5).Value = "create.json"; config.Cell(3, 6).Value = "http://host/x"; config.Cell(3, 7).Value = "POST"; config.Cell(3, 8).Value = true;
            request.Cell(1, 6).Value = "CreatePost.code"; request.Cell(1, 7).Value = "Second.code";
            request.Cell(2, 6).Value = "<randomnumber:000000>"; request.Cell(2, 7).Value = "<randomnumber:000000>";
        });
        (string First, string Second) Read(bool reuse, string[]? only)
        {
            var memory = new RunValueMemory(path);
            memory.ReuseOnly(only);
            RunValueMemory.Active = memory;
            try
            {
                memory.BeginRead(reuse);
                var vars = new WorkbookReader().Read(path).RequestData[(1, "D1")].Variables;
                memory.Save();
                return (vars["CreatePost.code"], vars["Second.code"]);
            }
            finally { RunValueMemory.Active = null; }
        }

        var original = Read(false, null);
        var rerun = Read(true, ["CreatePost"]);

        Assert.Equal(original.First, rerun.First);       // chosen: same as last run
        Assert.NotEqual(original.Second, rerun.Second);  // not chosen: new
    }
}
