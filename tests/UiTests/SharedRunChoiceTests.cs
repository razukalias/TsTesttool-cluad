using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

/// <summary>One earlier-run choice per instance: the table and the dashboard show the same run.</summary>
public class SharedRunChoiceTests
{
    private static string RunId(int n) => $"20261124_20{n:00}00_abcde{n:0000}";

    [Fact]
    public void The_reader_shows_an_earlier_run_as_if_it_were_the_newest_and_lists_every_run()
    {
        using var dir = new TempDir();
        var results = Path.Combine(dir.Path, "Results");
        RerunMergeTests.WriteRun(results, 1, Enumerable.Range(1, 5), (n, env) => n == 2);              // run 1: ROW002 fails everywhere
        RerunMergeTests.WriteRun(results, 2, Enumerable.Range(1, 5), (n, env) => n == 4);              // run 2: ROW004 fails everywhere

        var latest = RunResultsReader.ReadLatest(results)!;
        Assert.Equal(RunId(2), latest.RunId);
        Assert.False(latest.IsEarlierRun);
        Assert.Equal([RunId(2), RunId(1)], latest.Runs.Select(r => r.RunId));
        Assert.True(latest.Find(1, "ROW004")!.Passed == false && latest.Find(1, "ROW002")!.Passed);

        var earlier = RunResultsReader.ReadLatest(results, RunId(1))!;
        Assert.Equal(RunId(1), earlier.RunId);
        Assert.True(earlier.IsEarlierRun);
        Assert.Equal(RunId(2), earlier.LatestRunId);
        Assert.False(earlier.Find(1, "ROW002")!.Passed);   // as run 1 saw it
        Assert.True(earlier.Find(1, "ROW004")!.Passed);
        Assert.Equal(2, earlier.Runs.Count);               // the list still offers every run
    }

    [Fact]
    public void An_earlier_run_does_not_pick_up_rows_from_runs_after_it()
    {
        using var dir = new TempDir();
        var results = Path.Combine(dir.Path, "Results");
        RerunMergeTests.WriteRun(results, 1, Enumerable.Range(1, 4), (n, env) => n == 1);
        RerunMergeTests.WriteRun(results, 2, [3], (n, env) => true);                                   // a re-run of ROW003 only, failed
        var at1 = RunResultsReader.ReadLatest(results, RunId(1))!;
        Assert.True(at1.Find(1, "ROW003")!.Passed);   // run 2 is newer than the shown run: not used
        var at2 = RunResultsReader.ReadLatest(results)!;
        Assert.False(at2.Find(1, "ROW003")!.Passed);
        Assert.True(at2.Find(1, "ROW001")!.FromRunId is not null);   // ROW001 keeps its result from run 1
    }

    [Fact]
    public void The_choice_is_kept_per_folder_and_listeners_hear_about_changes_only()
    {
        using var dir = new TempDir();
        var a = Path.Combine(dir.Path, "A", "Results"); var b = Path.Combine(dir.Path, "B", "Results");
        var heard = new List<string?>();
        var owner = new object();
        ShownRunChoice.Subscribe(owner, (_, key, run) => { if (ShownRunChoice.Same(key, a)) heard.Add(run); });
        Assert.Null(ShownRunChoice.Get(a));
        ShownRunChoice.Set(a, RunId(1));
        ShownRunChoice.Set(a, RunId(1));            // same again: nobody is told
        Assert.Equal(RunId(1), ShownRunChoice.Get(a));
        Assert.Null(ShownRunChoice.Get(b));         // another instance is not affected
        ShownRunChoice.Set(a + Path.DirectorySeparatorChar, null);   // the same folder written differently
        Assert.Null(ShownRunChoice.Get(a));
        Assert.Equal([RunId(1), null], heard);
        GC.KeepAlive(owner);
    }

    [AvaloniaFact]
    public async Task The_table_drop_down_and_the_dashboard_share_the_choice()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, 3);
        var results = Path.Combine(Path.GetDirectoryName(path)!, "Results");
        RerunMergeTests.WriteRun(results, 1, [1, 2, 3], (n, env) => n == 2, ["dev"], prefix: "CUST");
        RerunMergeTests.WriteRun(results, 2, [1, 2, 3], (n, env) => n == 3, ["dev"], prefix: "CUST");

        var shell = new EditorShellViewModel([("Demo", path)], path);
        await shell.RefreshResultsAsync();
        var vm = shell.Instances[0].Editor!;
        Assert.Equal(2, vm.RunOptions.Count);
        Assert.False(vm.ShowingEarlierRun);

        vm.RunChoice = vm.RunOptions[1];                                  // pick the older run in the table
        Assert.Equal(RunId(1), ShownRunChoice.Get(vm.ResultsFolder));      // the shared choice, which the dashboard follows
        for (var i = 0; i < 100 && !vm.ShowingEarlierRun; i++) await Task.Delay(50);
        Assert.True(vm.ShowingEarlierRun);
        Assert.Contains("not the latest", vm.EarlierRunText);
        Assert.StartsWith("Earlier run", vm.RunSummaryText);
        Assert.Equal(vm.RunOptions[1], vm.RunChoice);   // the drop-down shows the earlier run

        // the dashboard picks the newest again: the table follows
        ShownRunChoice.Set(vm.ResultsFolder, null);
        for (var i = 0; i < 100 && vm.ShowingEarlierRun; i++) await Task.Delay(50);
        Assert.False(vm.ShowingEarlierRun);
        Assert.StartsWith("Last run", vm.RunSummaryText);
    }

    [AvaloniaFact]
    public void The_dashboard_follows_a_choice_made_in_the_table()
    {
        var root = Path.Combine(Path.GetTempPath(), "shared_" + Guid.NewGuid().ToString("N")[..8]);
        var inst = Path.Combine(root, "Instances", "Demo");
        Directory.CreateDirectory(inst);
        using (var dir = new TempDir()) File.Copy(Samples.CreateLarge(dir, rows: 3), Path.Combine(inst, "DataEngine.xlsx"));
        File.WriteAllText(Path.Combine(root, "appsettings.json"), "{\"InstancesRoot\":\"Instances\",\"WorkbookName\":\"DataEngine.xlsx\"}");
        var workbook = Path.Combine(inst, "DataEngine.xlsx");
        const string old = "20261110_090000_aaaaaaaaa";
        FakeRun.Write(FakeRun.Folder(workbook, "failed", "Run_old"), $"events_{old}.ndjson",
            new { eventType = "request-completed", runId = old, testcaseIndex = 1, dataId = "CUST001", stepName = "CreateCustomer", result = "FAIL", httpStatus = 500, durationMs = 90L, errorMessage = "boom", failureCategory = "HTTP", environment = "" });
        FakeRun.Write(FakeRun.Folder(workbook, "passed", "Run_new"), $"events_{FakeRun.RunId}.ndjson", FakeRun.Request(1, "CUST001", "CreateCustomer"), FakeRun.Request(1, "CUST002", "CreateCustomer"));
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(root);
        try
        {
            var dashboard = new LoadTestingTool.UI.MainWindow();
            var item = Activator.CreateInstance(typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
            item.GetType().GetProperty("FolderPath")!.SetValue(item, inst);
            var list = typeof(LoadTestingTool.UI.MainWindow).GetField("_instances", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dashboard)!;
            list.GetType().GetMethod("Add")!.Invoke(list, [item]);
            string Current() => (string?)item.GetType().GetProperty("CurrentRunId")!.GetValue(item) ?? string.Empty;
            var results = (string)item.GetType().GetProperty("ResultsFolder")!.GetValue(item)!;

            ShownRunChoice.Set(results, old);                       // chosen in the table
            Assert.Equal(old, Current());                           // the dashboard replays that run
            ShownRunChoice.Set(results, null);                      // "Show the latest run" in the table
            Assert.Equal(FakeRun.RunId, Current());
        }
        finally { Directory.SetCurrentDirectory(previous); }
    }
}
