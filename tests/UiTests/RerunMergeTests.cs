using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

/// <summary>A rerun writes a new run that holds only the rows it ran; the rows it did not run must keep their last result.</summary>
public class RerunMergeTests
{
    internal static readonly string[] Envs = ["dev", "test", "prod"];

    private static string RunIdOf(int n) => $"20261124_20{n:00}00_abcde{n:0000}";   // 25 characters

    /// <summary>One run: for each environment its own event file, like the runner writes them.</summary>
    internal static void WriteRun(string results, int number, IEnumerable<int> rows, Func<int, string, bool> fails, string[]? environments = null, DateTime? written = null, string prefix = "ROW")
    {
        var runId = RunIdOf(number);
        Assert.Equal(25, runId.Length);
        var folder = Path.Combine(results, "failed", "Run_" + number, "log");
        Directory.CreateDirectory(folder);
        foreach (var env in environments ?? Envs)
        {
            var lines = new List<string>();
            foreach (var n in rows)
            {
                var id = $"{prefix}{n:000}";
                var failed = fails(n, env);
                lines.Add(JsonSerializer.Serialize(new { eventType = "request-completed", runId, testcaseIndex = 1, dataId = id, stepName = "Create", result = failed ? "FAIL" : "PASS", httpStatus = 200, durationMs = 10, errorMessage = "", failureCategory = failed ? "Assertion" : "", environment = env }));
                for (var c = 0; c < 6; c++)   // many columns
                    lines.Add(JsonSerializer.Serialize(new { eventType = "assertion-completed", runId, testcaseIndex = 1, dataId = id, stepName = "Create", responsePath = "col" + c, assertionVerb = "eq", expectedValue = "a", actualValue = failed && c == 2 ? "b" : "a", result = failed && c == 2 ? "FAIL" : "PASS", failureMessage = "", environment = env }));
            }
            var file = Path.Combine(folder, $"events_{runId}_{env}.ndjson");
            File.WriteAllLines(file, lines);
            File.SetLastWriteTimeUtc(file, written ?? DateTime.UtcNow.AddMinutes(number));
        }
    }

    [Fact]
    public void After_a_rerun_of_some_failed_rows_every_row_that_is_still_failed_is_listed()
    {
        using var dir = new TempDir();
        var results = Path.Combine(dir.Path, "Results");
        // run 1: 100 rows x 3 environments; every third row fails in "test", every 7th in "prod"
        bool First(int n, string env) => env == "test" && n % 3 == 0 || env == "prod" && n % 7 == 0;
        WriteRun(results, 1, Enumerable.Range(1, 100), First);
        var failedBefore = Enumerable.Range(1, 100).Where(n => First(n, "test") || First(n, "prod")).ToList();
        Assert.Equal(failedBefore.Count, RunResultsReader.ReadLatest(results)!.FailedRows);

        // run 2 (the rerun): only 10 of the failed rows, all environments; they pass now except the multiples of 9, which still fail in "test"
        var rerun = failedBefore.Take(10).ToList();
        bool Second(int n, string env) => env == "test" && n % 9 == 0;
        WriteRun(results, 2, rerun, Second);

        var merged = RunResultsReader.ReadLatest(results)!;
        var expected = failedBefore.Except(rerun).Concat(rerun.Where(n => Second(n, "test"))).OrderBy(n => n).ToList();
        var actual = merged.Rows.Where(r => !r.Value.Passed).Select(r => int.Parse(r.Key.DataId[3..])).OrderBy(n => n).ToList();
        Assert.Equal(expected, actual);
        Assert.Equal(100, merged.Rows.Count);                       // rows the rerun did not run keep their last result
        Assert.Equal(RunIdOf(2), merged.RunId);                     // the latest run is still the run shown
        Assert.Equal(["dev", "prod", "test"], merged.Environments);
    }

    [Fact]
    public void A_rerun_of_one_environment_keeps_the_other_environments_of_the_same_row()
    {
        using var dir = new TempDir();
        var results = Path.Combine(dir.Path, "Results");
        WriteRun(results, 1, Enumerable.Range(1, 20), (n, env) => env == "prod" && n <= 5 || env == "test" && n <= 3);
        WriteRun(results, 2, Enumerable.Range(1, 5), (n, env) => false, environments: ["prod"]);   // prod rerun, now passing

        var merged = RunResultsReader.ReadLatest(results)!;
        Assert.True(merged.Find(1, "ROW004")!.In("prod")!.Passed);    // the rerun's result
        Assert.False(merged.Find(1, "ROW002")!.In("test")!.Passed);   // test was not rerun: still failed
        Assert.NotNull(merged.Find(1, "ROW002")!.In("dev"));
        Assert.False(merged.Find(1, "ROW002")!.Passed);               // so the row as a whole is still failed
        Assert.True(merged.Find(1, "ROW004")!.Passed);                // ROW004 only failed in prod: passes now
    }

    [Fact]
    public void A_complete_new_run_replaces_everything_and_old_runs_are_not_mixed_in()
    {
        using var dir = new TempDir();
        var results = Path.Combine(dir.Path, "Results");
        WriteRun(results, 1, Enumerable.Range(1, 30), (n, env) => n % 2 == 0);
        WriteRun(results, 2, Enumerable.Range(1, 30), (n, env) => false);
        var merged = RunResultsReader.ReadLatest(results)!;
        Assert.Equal(30, merged.Rows.Count);
        Assert.Equal(0, merged.FailedRows);
    }
}

public class RerunEditorTests
{
    [AvaloniaFact]
    public void The_failed_rows_filter_lists_every_failed_row_after_a_rerun_of_a_few()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 100);
        var results = Path.Combine(Path.GetDirectoryName(path)!, "Results");
        bool First(int n, string env) => env == "test" && n % 4 == 0 || env == "prod" && n % 9 == 0;
        RerunMergeTests.WriteRun(results, 1, Enumerable.Range(1, 100), First, prefix: "CUST");
        var failed = Enumerable.Range(1, 100).Where(n => First(n, "test") || First(n, "prod")).ToList();
        var rerun = failed.Take(8).ToList();
        RerunMergeTests.WriteRun(results, 2, rerun, (n, env) => false, prefix: "CUST");   // the 8 rows pass now

        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        editor.ShowFailedOnly = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(failed.Count - rerun.Count, editor.VisibleRows.Count);
        Assert.DoesNotContain(editor.VisibleRows, r => rerun.Contains(int.Parse(r.CurrentDataId[4..])));
        Assert.Contains("keep their result from an earlier run", editor.RunSummaryText);
    }
}
