using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using LoadTestingTool.UI.CentralRerun;
using Xunit;
using static UiTests.CentralRerunTests;

namespace UiTests;

public class CentralRerunWindowTests
{
    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); await Task.Delay(25); }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(done(), "timed out");
    }

    private static T Find<T>(Window window, string tag) where T : Control => window.GetVisualDescendantsOf<T>().First(c => (string?)c.Tag == tag);
    private static void Click(Window window, string tag) { Find<Button>(window, tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }

    private static (FakeHost Host, string A, string B) Setup(TempDir dir)
    {
        var a = Path.Combine(dir.Path, "A", "Results"); var b = Path.Combine(dir.Path, "B", "Results");
        string[] envs = ["dev", "prod"];
        WriteRun(a, 1, Enumerable.Range(1, 4).SelectMany(n => envs.Select(e => new Ev(1, "Orders", $"ROW{n:000}", e, n <= 2))));       // 4 failed pairs... ROW001, ROW002 in dev and prod
        WriteRun(b, 1, Enumerable.Range(1, 3).SelectMany(n => envs.Select(e => new Ev(1, "Users", $"ROW{n:000}", e, n == 3 && e == "prod"))));   // 1 failed pair
        var run = 2;
        var instances = new[] { new CentralInstance("A", a, Catalog((1, "Orders")), (_, id) => id), new CentralInstance("B", b, Catalog((1, "Users")), (_, id) => id) };
        var host = new FakeHost
        {
            Instances = instances,
            ReportsFolder = Path.Combine(dir.Path, "Reports"),
            OnStart = (instance, request) =>
            {
                var (results, name) = instance == "A" ? (a, "Orders") : (b, "Users");
                WriteRun(results, Interlocked.Increment(ref run), CentralRerunPlanner.Expand(request).Select(t => new Ev(1, name, t.DataId, t.Environment.ToLowerInvariant(), instance == "A" && t.DataId == "ROW001" && t.Environment == "PROD")));
                return Task.FromResult<string?>(null);
            }
        };
        return (host, a, b);
    }

    [AvaloniaFact]
    public void The_re_run_center_opens_when_the_dashboard_window_is_hidden()
    {
        // The data editor is the only visible window; the dashboard that owns the button is never shown (it used to throw "non-visible owner").
        var main = new LoadTestingTool.UI.MainWindow();
        Assert.False(main.IsVisible);
        var window = main.OpenRerunCenter();
        try { Assert.True(window.IsVisible); Assert.Same(window, main.OpenRerunCenter()); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task The_window_lists_the_failed_rows_of_all_instances_and_the_filters_narrow_the_list()
    {
        using var dir = new TempDir();
        var (host, _, _) = Setup(dir);
        var window = new CentralRerunWindow(host);
        window.Show();
        await WaitFor(() => window.IsLoaded);

        var list = Find<ListBox>(window, "central-rows");
        Assert.Equal(5, list.ItemCount);                                  // 4 in A, 1 in B
        Assert.Equal(["A", "B"], window.Failed.Select(p => p.Instance).Distinct());

        Find<ComboBox>(window, "central-instance").SelectedItem = "B";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, list.ItemCount);
        Find<ComboBox>(window, "central-instance").SelectedItem = "All instances";
        Find<ComboBox>(window, "central-environment").SelectedItem = "dev";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, list.ItemCount);                                  // ROW001 and ROW002 of A in dev
        Find<ComboBox>(window, "central-environment").SelectedItem = "All environments";
        Find<TextBox>(window, "central-text").Text = "row001";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, list.ItemCount);                                  // ROW001 in dev and prod
        window.Close();
    }

    [AvaloniaFact]
    public async Task Every_row_has_its_steps_the_failed_one_with_old_data_and_later_ones_fresh_a_hand_change_is_kept_then_the_run_makes_a_report()
    {
        using var dir = new TempDir();
        var (host, _, _) = Setup(dir);
        var window = new CentralRerunWindow(host);
        window.Show();
        await WaitFor(() => window.IsLoaded);

        Assert.False(Find<Button>(window, "central-run").IsEnabled);       // nothing ticked
        Click(window, "central-tick-shown");
        Assert.Equal(5, window.Ticked.Count);
        Assert.True(Find<Button>(window, "central-run").IsEnabled);

        // the defaults of every row: the step that failed (Create) runs again with its old data, the step after it (Verify) with fresh data
        foreach (var pair in window.Ticked)
            Assert.Equal([new RowStep("Create", true, true), new RowStep("Verify", true, false)], window.StepsOf(pair));
        var boxes = window.GetVisualDescendantsOf<CheckBox>().Where(b => ((string?)b.Tag)?.StartsWith("step:") == true).ToList();
        Assert.True(boxes.Count >= 4);
        Assert.All(boxes, b => Assert.True(b.IsChecked));                  // both steps are ticked by default
        var data = window.GetVisualDescendantsOf<ComboBox>().Where(c => ((string?)c.Tag)?.StartsWith("data:") == true).ToList();
        Assert.All(data.Where(c => (string?)c.Tag == "data:Create"), c => Assert.Equal(0, c.SelectedIndex));   // Old data
        Assert.All(data.Where(c => (string?)c.Tag == "data:Verify"), c => Assert.Equal(1, c.SelectedIndex));   // Fresh data

        // by hand, on the first card (ROW001 of A in dev): no Verify
        var first = window.Ticked[0];
        boxes.First(b => (string?)b.Tag == "step:Verify").IsChecked = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal([new RowStep("Create", true, true), new RowStep("Verify", false, false)], window.StepsOf(first));
        Click(window, "central-untick-shown");
        Click(window, "central-tick-shown");                                // ticking again must not undo the hand change
        Assert.Equal([new RowStep("Create", true, true), new RowStep("Verify", false, false)], window.StepsOf(first));

        Click(window, "central-run");
        await WaitFor(() => window.Report is not null);

        var forA = host.Started.Where(s => s.Instance == "A").Select(s => s.Request).ToList();
        Assert.Equal(2, forA.Count);                                       // A's rows have two different step choices: two runs
        Assert.Contains(forA, r => r.Steps is { } steps && steps[1].SequenceEqual(new[] { "Create" }));   // the row without Verify
        Assert.Contains(forA, r => r.Steps is null);                       // the others run every step
        Assert.All(forA, r => Assert.Equal(["Create"], r.RerunSteps![1])); // only the failed step takes the old data
        var forB = host.Started.Single(s => s.Instance == "B").Request;
        Assert.Null(forB.Steps); Assert.Equal(["Create"], forB.RerunSteps![1]);
        Assert.Contains("Create (old data) › Verify (fresh data)", window.LastBatch!.Rows.Select(r => r.StepsText));
        Assert.Contains("Create (old data)", window.LastBatch.Rows.Select(r => r.StepsText));
        Assert.True(Find<StackPanel>(window, "central-report-ready").IsVisible);
        Assert.True(File.Exists(window.Report!.Value.Html)); Assert.True(File.Exists(window.Report.Value.Excel));
        Assert.Equal(5, window.LastBatch.Rows.Count);
        Assert.Equal(1, window.LastBatch.Rows.Count(r => r.IsStillFailed));

        Click(window, "central-report-html"); Click(window, "central-report-excel"); Click(window, "central-report-folder");
        Assert.Equal([window.Report.Value.Html, window.Report.Value.Excel, Path.GetDirectoryName(window.Report.Value.Html)], host.Opened);   // a link, never opened by itself before

        await WaitFor(() => window.IsLoaded);
        Assert.Equal(1, Find<ListBox>(window, "central-rows").ItemCount); // the list shows the new status: only ROW001 in prod of A is still failed
        window.Close();
    }
}
