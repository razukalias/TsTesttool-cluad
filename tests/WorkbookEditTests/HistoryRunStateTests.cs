using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using Xunit;

namespace WorkbookEditTests;

public class HistoryRunStateTests
{
    private static string Key(string dataId = "d1", int iteration = 1) => RunValueMemory.RowKey(1, dataId, iteration, "qa");

    private static void WriteRun(AppConfig config, RunValueMemory.Snapshot state, string runId, params (string DataId, int Iteration)[] rows)
    {
        var run = new RunResult { RunId = runId, StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
        foreach (var (dataId, iteration) in rows)
            run.Testcases.Add(new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = dataId, Iteration = iteration, Environment = "qa", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now });
        new HistoryWriter(config) { RunState = state }.Write(run, Path.Combine(config.HistoryFolder, runId), $"{runId}.xlsx");
    }

    private static RunValueMemory.Snapshot State(string dataId, string generated, string extracted)
    {
        var state = new RunValueMemory.Snapshot();
        state.Generated[$"{Key(dataId)}|sheet|2|abc|def|0"] = generated;
        state.Rows[Key(dataId)] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["token"] = extracted };
        state.Steps[$"{Key(dataId)}|CreatePost|HASH|0"] = "resolved text";
        return state;
    }

    [Fact]
    public void A_later_run_finds_the_generated_values_extracted_values_and_step_text_in_the_history_it_left()
    {
        using var dir = new TempDir();
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        WriteRun(config, State("d1", "order-111", "tok-1"), "run1", ("d1", 1));

        var loaded = new HistoryRunStateStore(config.HistoryFolder).Load()!;

        Assert.Equal("order-111", loaded.Generated[$"{Key()}|sheet|2|abc|def|0"]);
        Assert.Equal("tok-1", loaded.Rows[Key()]["token"]);
        Assert.Equal("resolved text", loaded.Steps[$"{Key()}|CreatePost|HASH|0"]);
    }

    [Fact]
    public void Each_data_row_comes_from_the_newest_history_file_that_has_it_and_rows_of_other_runs_are_kept()
    {
        using var dir = new TempDir();
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        WriteRun(config, State("d1", "old-1", "old-tok"), "run1", ("d1", 1));
        WriteRun(config, State("d2", "old-2", "tok-2"), "run2", ("d2", 1));
        File.SetLastWriteTimeUtc(Path.Combine(config.HistoryFolder, "run1", "run1.xlsx"), DateTime.UtcNow.AddMinutes(-10));
        WriteRun(config, State("d1", "new-1", "new-tok"), "run3", ("d1", 1));              // newer: d1 only

        var store = new HistoryRunStateStore(config.HistoryFolder);
        var loaded = store.Load()!;

        Assert.Equal("new-1", loaded.Generated[$"{Key("d1")}|sheet|2|abc|def|0"]);
        Assert.Equal("new-tok", loaded.Rows[Key("d1")]["token"]);
        Assert.Equal("old-2", loaded.Generated[$"{Key("d2")}|sheet|2|abc|def|0"]);       // not in the newest run, still found
        Assert.Equal(2, store.RowsFound);
    }

    [Fact]
    public void Very_long_values_survive_the_cell_size_limit_of_excel()
    {
        using var dir = new TempDir();
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        var big = new string('x', 100_000) + "END";
        WriteRun(config, State("d1", big, "t"), "run1", ("d1", 1));

        var loaded = new HistoryRunStateStore(config.HistoryFolder).Load()!;

        Assert.Equal(big, loaded.Generated[$"{Key()}|sheet|2|abc|def|0"]);
    }

    [Fact]
    public void No_history_means_nothing_to_reuse_and_a_damaged_file_is_skipped()
    {
        using var dir = new TempDir();
        var folder = dir.Combine("history");
        Assert.Empty(new HistoryRunStateStore(folder).Load()!.Rows);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "broken.xlsx"), "not a workbook");
        Assert.Empty(new HistoryRunStateStore(folder).Load()!.Rows);
    }

    [Fact]
    public void A_memory_built_on_history_reuses_the_old_values_for_a_rerun()
    {
        using var dir = new TempDir();
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        var first = new RunValueMemory(new HistoryRunStateStore(config.HistoryFolder));
        first.BeginRead(reuse: false);
        Assert.Equal("made-1", first.GeneratedFor(Key(), "tok", null, () => "made-1"));
        first.SetRow(Key(), new Dictionary<string, string> { ["token"] = "abc" });
        WriteRun(config, first.Export(), "run1", ("d1", 1));

        var rerun = new RunValueMemory(new HistoryRunStateStore(config.HistoryFolder));
        Assert.True(rerun.HasPrevious);
        rerun.BeginRead(reuse: true);
        Assert.Equal("made-1", rerun.GeneratedFor(Key(), "tok", null, () => "made-2"));
        Assert.Equal("abc", rerun.PreviousRow(Key())["token"]);
    }
}
