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
            Steps.ToDictionary(s => s, _ => true), ["qa", "prod"], ["qa", "prod"]);

        Assert.Equal([1], request.Testcases);
        Assert.Equal(["d2", "d3"], request.DataIds);          // one entry per data row, not per iteration
        Assert.Null(request.Steps);                           // every step
        Assert.True(request.Rerun);
        Assert.Null(request.RerunSteps);
        Assert.Equal("runA", request.RerunOf);
        Assert.Null(request.Environments);                    // all environments: the instance decides
    }

    [Fact]
    public void Chosen_steps_and_a_mix_of_old_and_fresh_data_are_passed_per_step()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2")], Steps,
            new Dictionary<string, bool> { ["Create"] = true, ["Check"] = false }, ["qa", "prod"], ["prod"]);

        Assert.Equal(["Create", "Check"], request.Steps![1]);
        Assert.False(request.Rerun);
        Assert.Equal(["Create"], request.RerunSteps![1]);     // old data only where chosen; Check gets fresh values
        Assert.Equal(["prod"], request.Environments);
    }

    [Fact]
    public void Fresh_data_for_every_step_asks_for_no_reuse_but_is_still_linked()
    {
        var request = RerunPlanner.Build(1, "runA", [Row("d2")], Steps, Steps.ToDictionary(s => s, _ => false), ["qa"], ["qa"]);

        Assert.False(request.Rerun);
        Assert.Null(request.RerunSteps);
        Assert.Equal("runA", request.RerunOf);
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
            var d1 = boxes.Single(b => b.Content?.ToString()!.StartsWith("d1") == true);
            var d2 = boxes.Single(b => b.Content?.ToString()!.StartsWith("d2") == true);
            Assert.False(d1.IsChecked);                      // passed
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
            Assert.Equal(3000, list.ItemCount);
            var built = window.GetVisualDescendantsOf<CheckBox>().Count(b => b.Content?.ToString()?.StartsWith('d') == true);
            Assert.InRange(built, 1, 100);   // not 3000
            var count = window.GetVisualDescendantsOf<TextBlock>().First(t => t.Text?.EndsWith("ticked") == true).Text;
            Assert.Equal("1500 of 3000 ticked", count);   // the failed half
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
