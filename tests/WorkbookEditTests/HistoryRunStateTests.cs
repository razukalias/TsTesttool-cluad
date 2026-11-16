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

    private static void WriteResults(AppConfig config, string runId, DateTimeOffset started, string? rerunOf, params (string DataId, int Iteration, string Result)[] rows)
    {
        var run = new RunResult { RunId = runId, StartedAt = started, EndedAt = started };
        foreach (var (dataId, iteration, result) in rows)
        {
            var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = dataId, Iteration = iteration, Environment = "qa", StartedAt = started, EndedAt = started };
            testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "S", HttpStatus = result == "FAILED" ? 500 : 200 });
            run.Testcases.Add(testcase);
        }
        new HistoryWriter(config) { RerunOf = rerunOf }.Write(run, Path.Combine(config.HistoryFolder, runId), $"{runId}.xlsx");
    }

    [Fact]
    public void The_index_lists_runs_with_their_failed_rows_and_the_run_each_one_re_ran()
    {
        using var dir = new TempDir();
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        var t0 = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        WriteResults(config, "runA", t0, null, ("d1", 1, "PASSED"), ("d2", 1, "FAILED"), ("d3", 2, "FAILED"));
        WriteResults(config, "runB", t0.AddHours(1), "runA", ("d2", 1, "PASSED"));

        var rows = RunHistoryIndex.Scan(config.HistoryFolder);
        var runs = RunHistoryIndex.Runs(rows);

        Assert.Equal(["runB", "runA"], runs.Select(r => r.RunId));          // newest first
        Assert.Equal(("runA", 3, 2, (string?)null), (runs[1].RunId, runs[1].Rows, runs[1].Failed, runs[1].RerunOf));
        Assert.Equal("runA", runs[0].RerunOf);
        var failed = rows.Where(r => r.RunId == "runA" && r.Failed).Select(r => (r.DataId, r.Iteration, r.Environment)).ToList();
        Assert.Equal([("d2", 1, "qa"), ("d3", 2, "qa")], failed);
    }

    [Fact]
    public void Old_data_can_be_taken_from_one_chosen_run_instead_of_the_newest()
    {
        using var dir = new TempDir();
        var config = new AppConfig { HistoryFolder = dir.Combine("history") };
        WriteRun(config, State("d1", "from-A", "tokA"), "runA", ("d1", 1));
        File.SetLastWriteTimeUtc(Path.Combine(config.HistoryFolder, "runA", "runA.xlsx"), DateTime.UtcNow.AddMinutes(-10));
        WriteRun(config, State("d1", "from-B", "tokB"), "runB", ("d1", 1));

        var newest = new HistoryRunStateStore(config.HistoryFolder).Load()!;
        var chosen = new HistoryRunStateStore(config.HistoryFolder, "runA").Load()!;
        var unknown = new HistoryRunStateStore(config.HistoryFolder, "nope").Load()!;

        Assert.Equal("tokB", newest.Rows[Key()]["token"]);
        Assert.Equal("tokA", chosen.Rows[Key()]["token"]);
        Assert.Equal("from-A", chosen.Generated[$"{Key()}|sheet|2|abc|def|0"]);
        Assert.Empty(unknown.Rows);
    }
}
