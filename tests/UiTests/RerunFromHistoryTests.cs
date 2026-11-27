using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LoadTestingTool.Domain;
using LoadTestingTool.Editing;
using LoadTestingTool.Reporting;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class RerunFromHistoryTests
{
    private static RunHistoryRow Row(string data, string result = "FAILED", int iteration = 1, string env = "qa")
        => new("runA", DateTimeOffset.Now, null, 1, "Order", data, iteration, env, result);

    private static readonly string[] Steps = ["Login", "Create", "Check"];

    [Fact]
    public void All_steps_with_old_data_is_a_plain_rerun_of_the_chosen_rows_linked_to_the_earlier_run()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2"), Row("d2", iteration: 2), Row("d3")], Steps,
            Steps.ToDictionary(s => s, _ => true));

        Assert.Equal([1], request.Testcases);
        Assert.Equal(["d2", "d3"], request.DataIds);          // one entry per data row, not per iteration
        Assert.Null(request.Steps);                           // every step
        Assert.True(request.Rerun);
        Assert.Null(request.RerunSteps);
        Assert.Equal("runA", request.RerunOf);
        Assert.Equal(["qa"], request.RowsByEnvironment!.Keys);   // each row runs in the environment it ran in
        Assert.Equal(["d2", "d3"], request.RowsByEnvironment["qa"]);
    }

    [Fact]
    public void Chosen_steps_and_a_mix_of_old_and_fresh_data_are_passed_per_step()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2")], Steps,
            new Dictionary<string, bool> { ["Create"] = true, ["Check"] = false });

        Assert.Equal(["Create", "Check"], request.Steps![1]);
        Assert.False(request.Rerun);
        Assert.Equal(["Create"], request.RerunSteps![1]);     // old data only where chosen; Check gets fresh values
    }

    [Fact]
    public void Fresh_data_for_every_step_asks_for_no_reuse_but_is_still_linked()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2")], Steps, Steps.ToDictionary(s => s, _ => false));

        Assert.False(request.Rerun);
        Assert.Null(request.RerunSteps);
        Assert.Equal("runA", request.RerunOf);
    }

    [Fact]
    public void Each_ticked_row_runs_only_in_the_environment_where_it_failed()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2", env: "qa"), Row("d5", env: "prod"), Row("d7", env: "qa")], Steps, Steps.ToDictionary(s => s, _ => true));

        Assert.Equal(["d2", "d7"], request.RowsByEnvironment!["qa"]);
        Assert.Equal(["d5"], request.RowsByEnvironment["prod"]);
        Assert.Equal(["qa", "prod"], request.Environments);
    }

    [AvaloniaFact]
    public void The_dialog_lists_the_runs_of_the_testcase_and_starts_with_its_failed_rows_ticked()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { HistoryFolder = folder };
            var run = new RunResult { RunId = "runA", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
            foreach (var (data, status) in new[] { ("d1", 200), ("d2", 500) })
            {
                var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = data, Iteration = 1, Environment = "qa", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
                testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Create", HttpStatus = status });
                run.Testcases.Add(testcase);
            }
            new HistoryWriter(config).Write(run, Path.Combine(folder, "runA"), "runA.xlsx");

            var window = new RerunWindow(new TestcaseItem(new TestcaseInfo(1, "Order", ["Create"]), 2), new RerunContext(folder, ["qa", "prod"]));
            window.Show();
            for (var wait = 0; !window.IsLoaded && wait < 500; wait++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }   // the history is read in the background
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var boxes = window.GetVisualDescendantsOf<CheckBox>().ToList();
            Assert.DoesNotContain(boxes, b => (string?)b.Tag == "row:d1");   // passed: not listed
            var d2 = boxes.Single(b => (string?)b.Tag == "row:d2");
            Assert.True(d2.IsChecked);                       // failed: ticked by default
            Assert.Contains(window.GetVisualDescendantsOf<ComboBox>(), c => c.SelectedItem is ComboBoxItem { Tag: "runA" });
            window.Close();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

internal static class VisualExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual root) where T : class
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}

public class RerunWindowPagingTests
{
    private static void WriteRun(AppConfig config, string folder, string runId, int rows, DateTimeOffset when)
    {
        var run = new RunResult { RunId = runId, StartedAt = when, EndedAt = when };
        for (var n = 1; n <= rows; n++)
        {
            var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = $"d{n:0000}", Iteration = 1, Environment = "qa", StartedAt = when, EndedAt = when };
            testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Create", HttpStatus = n % 2 == 0 ? 500 : 200 });
            run.Testcases.Add(testcase);
        }
        new HistoryWriter(config).Write(run, Path.Combine(folder, runId), runId + ".xlsx");
        File.SetLastWriteTimeUtc(Path.Combine(folder, runId, runId + ".xlsx"), when.UtcDateTime);
    }

    private static void Wait(RerunWindow window)
    {
        for (var wait = 0; !window.IsLoaded && wait < 1000; wait++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void With_the_database_as_storage_the_window_lists_the_runs_and_failed_rows_of_the_database()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = new RunResult { RunId = "runDb", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
            foreach (var (data, status) in new[] { ("d1", 200), ("d2", 500), ("d3", 200) })
            {
                var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = data, Iteration = 1, Environment = "qa", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
                testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Create", HttpStatus = status });
                run.Testcases.Add(testcase);
            }
            new LoadTestingTool.Reporting.SqliteHistoryStore(folder).SaveRun(run, null, null);   // no history workbook at all
            Assert.Empty(Directory.GetFiles(folder, "*.xlsx"));
            var window = new RerunWindow(new TestcaseItem(new TestcaseInfo(1, "Order", ["Create"]), 3), new RerunContext(folder, ["qa"], LoadTestingTool.Reporting.HistoryStorage.Database));
            window.Show();
            Wait(window);
            var list = window.GetVisualDescendantsOf<ListBox>().First(l => (string?)l.Tag == "rerun-rows");
            Assert.Equal(1, list.ItemCount);                 // only d2 failed
            window.Close();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void Steps_that_passed_in_the_failed_rows_are_listed_but_unticked()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { HistoryFolder = folder };
            var run = new RunResult { RunId = "runS", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
            foreach (var (data, createStatus, checkStatus) in new[] { ("d1", 200, 200), ("d2", 200, 500), ("d3", 500, 200) })
            {
                var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = data, Iteration = 1, Environment = "qa", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
                testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Create", HttpStatus = createStatus });
                testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Check", HttpStatus = checkStatus });
                testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Cleanup", HttpStatus = 200 });
                run.Testcases.Add(testcase);
            }
            new HistoryWriter(config).Write(run, Path.Combine(folder, "runS"), "runS.xlsx");
            var window = new RerunWindow(new TestcaseItem(new TestcaseInfo(1, "Order", ["Create", "Check", "Cleanup"]), 3), new RerunContext(folder, ["qa"]));
            window.Show();
            Wait(window);
            var steps = window.GetVisualDescendantsOf<CheckBox>().Where(b => b.Content?.ToString() is "Create" or "Check" or "Cleanup").ToDictionary(b => b.Content!.ToString()!, b => b.IsChecked);
            Assert.Equal(3, steps.Count);                       // every step is still listed
            Assert.True(steps["Create"]); Assert.True(steps["Check"]);   // failed in d3 and d2
            Assert.False(steps["Cleanup"]);                     // passed everywhere: unticked, can be ticked by hand
            window.Close();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void The_environment_filter_hides_unticked_environments_and_their_rows_and_passed_rows_are_never_listed()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { HistoryFolder = folder };
            var run = new RunResult { RunId = "runA", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
            foreach (var (data, env, status) in new[] { ("d1", "qa", 200), ("d2", "qa", 500), ("d2", "prod", 200), ("d3", "prod", 500), ("d4", "prod", 500) })
            {
                var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = data, Iteration = 1, Environment = env, StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
                testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Create", HttpStatus = status });
                run.Testcases.Add(testcase);
            }
            new HistoryWriter(config).Write(run, Path.Combine(folder, "runA"), "runA.xlsx");
            var window = new RerunWindow(new TestcaseItem(new TestcaseInfo(1, "Order", ["Create"]), 2), new RerunContext(folder, ["qa", "prod"]));
            window.Show();
            Wait(window);
            var list = window.GetVisualDescendantsOf<ListBox>().First(l => (string?)l.Tag == "rerun-rows");
            Assert.Equal(3, list.ItemCount);                 // d2/qa, d3/prod, d4/prod: d1 and d2/prod passed
            var envBoxes = window.GetVisualDescendantsOf<CheckBox>().Where(b => (string?)b.Tag == "rerun-env").ToList();
            Assert.Equal(["qa", "prod"], envBoxes.Select(b => b.Content?.ToString()).ToArray());
            envBoxes.Single(b => b.Content?.ToString() == "prod").IsChecked = false;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, list.ItemCount);                 // only d2/qa is left
            window.Close();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void Only_the_newest_history_files_are_read_at_first_and_older_ones_on_request()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { HistoryFolder = folder };
            var start = DateTimeOffset.Now.AddDays(-1);
            for (var n = 0; n < RerunWindow.PageSize + 5; n++) WriteRun(config, folder, $"run{n:000}", 2, start.AddMinutes(n));

            var window = new RerunWindow(new TestcaseItem(new TestcaseInfo(1, "Order", ["Create"]), 2), new RerunContext(folder, ["qa"]));
            window.Show();
            Wait(window);
            var combo = window.GetVisualDescendantsOf<ComboBox>().First(c => c.ItemsSource is not null && c.Items.OfType<ComboBoxItem>().Any(i => i.Tag is string));
            Assert.Equal(RerunWindow.PageSize, combo.Items.Count);
            var more = window.GetVisualDescendantsOf<Button>().First(b => (string?)b.Tag == "rerun-more");
            Assert.True(more.IsVisible);

            more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            for (var wait = 0; combo.Items.Count < RerunWindow.PageSize + 5 && wait < 1000; wait++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            Assert.Equal(RerunWindow.PageSize + 5, combo.Items.Count);
            Assert.False(more.IsVisible);   // nothing older is left
            window.Close();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void The_row_list_shows_every_row_of_the_run_without_scrolling()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { HistoryFolder = folder };
            WriteRun(config, folder, "mid", 144, DateTimeOffset.Now);   // 144 rows, 72 of them failed (only those are listed)
            var window = new RerunWindow(new TestcaseItem(new TestcaseInfo(1, "Order", ["Create"]), 2), new RerunContext(folder, ["qa"]));
            window.Show();
            Wait(window);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendantsOf<ListBox>().First(l => (string?)l.Tag == "rerun-rows");
            Assert.Equal(72, list.ItemCount); Assert.True(list.Height >= 72 * 26, $"the list is {list.Height} high");
            var built = window.GetVisualDescendantsOf<CheckBox>().Count(b => ((string?)b.Tag)?.StartsWith("row:") == true);
            Assert.InRange(built, 3, 72);   // the lines in view are built; the rest come as the window is scrolled (the list itself no longer scrolls)
            window.Close();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public void A_run_with_thousands_of_rows_builds_only_the_lines_in_view()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { HistoryFolder = folder };
            WriteRun(config, folder, "big", 3000, DateTimeOffset.Now);
            var window = new RerunWindow(new TestcaseItem(new TestcaseInfo(1, "Order", ["Create"]), 2), new RerunContext(folder, ["qa"]));
            window.Show();
            Wait(window);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendantsOf<ListBox>().First(l => (string?)l.Tag == "rerun-rows");
            Assert.Equal(1500, list.ItemCount);   // the failed half
            var built = window.GetVisualDescendantsOf<CheckBox>().Count(b => ((string?)b.Tag)?.StartsWith("row:") == true);
            Assert.InRange(built, 1, 100);   // not 3000
            var count = window.GetVisualDescendantsOf<TextBlock>().First(t => t.Text?.EndsWith("ticked") == true).Text;
            Assert.Equal("1500 of 1500 ticked", count);
            window.Close();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

public class RerunWindowNotModalTests
{
    [AvaloniaFact]
    public void The_rerun_window_does_not_block_the_editor_and_opens_only_once()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var main = DataEditorWindow.Open(path);
        main.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var editor = main.Shell.Current!;
        editor.RerunContextProvider = () => new RerunContext(Path.Combine(dir.Path, "history"), []);
        editor.SelectedTestcase = editor.Testcases.First();

        main.OnRerunClick(null, new Avalonia.Interactivity.RoutedEventArgs());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var dialog = main.OpenRerunWindow;
        Assert.NotNull(dialog);
        Assert.True(dialog!.IsVisible);
        Assert.True(main.IsEnabled);               // a modal dialog would disable it
        editor.ViewModeIndex = 3;                  // and the editor still reacts
        Assert.Equal(3, editor.ViewModeIndex);

        main.OnRerunClick(null, new Avalonia.Interactivity.RoutedEventArgs());
        Assert.Same(dialog, main.OpenRerunWindow);   // a second click brings the same window forward

        dialog.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Null(main.OpenRerunWindow);
    }
}

public class RunHistoryIndexCacheTests
{
    [Fact]
    public void A_history_workbook_is_opened_once_and_its_run_is_kept_in_a_small_index_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rerun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new AppConfig { HistoryFolder = folder };
            var run = new RunResult { RunId = "runC", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
            var testcase = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = "d1", Iteration = 1, Environment = "qa", StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now };
            testcase.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Create", HttpStatus = 500 });
            run.Testcases.Add(testcase);
            new HistoryWriter(config).Write(run, Path.Combine(folder, "runC"), "runC.xlsx");
            var workbook = new FileInfo(Path.Combine(folder, "runC", "runC.xlsx"));

            var first = RunHistoryIndex.Scan(folder);
            Assert.Single(first);
            Assert.True(File.Exists(workbook.FullName + ".runindex.json"));

            // damage the workbook but keep its size and time: only the index can still answer
            var length = workbook.Length; var time = workbook.LastWriteTimeUtc;
            File.WriteAllBytes(workbook.FullName, new byte[length]);
            File.SetLastWriteTimeUtc(workbook.FullName, time);
            var second = RunHistoryIndex.Scan(folder);
            Assert.Equal(first.Select(r => (r.RunId, r.DataId, r.Result)), second.Select(r => (r.RunId, r.DataId, r.Result)));

            // a changed workbook is read again
            File.WriteAllBytes(workbook.FullName, [1, 2, 3]);
            Assert.Empty(RunHistoryIndex.Scan(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
