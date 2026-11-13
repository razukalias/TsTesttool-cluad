using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class LazyLoadingTests
{
    private static (EditorShellViewModel Shell, string[] Paths) Open(TempDir dir, bool lazy, string? start = null)
    {
        var a = Samples.Copy(dir, Samples.IntegrationScenarios);
        var b = Samples.CreateLarge(dir, rows: 30);
        var c = Samples.Copy(dir, Samples.FileHttpScenarios);
        return (new EditorShellViewModel([("A", a), ("B", b), ("C", c)], start ?? a, lazy), [a, b, c]);
    }

    [Fact]
    public void Without_lazy_loading_every_instance_is_read_up_front()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir, lazy: false);

        Assert.All(shell.Instances, n => Assert.True(n.IsLoaded));
        Assert.Equal("4 testcases", shell.Instances[0].Subtitle);
        Assert.Equal("5 testcases", shell.Instances[2].Subtitle);
    }

    [Fact]
    public void With_lazy_loading_only_the_starting_instance_is_read()
    {
        using var dir = new TempDir();
        var (shell, paths) = Open(dir, lazy: true, start: null);

        Assert.True(shell.Instances[0].IsLoaded);
        Assert.False(shell.Instances[1].IsLoaded);
        Assert.False(shell.Instances[2].IsLoaded);
        Assert.Equal("Not loaded — click to open", shell.Instances[1].Subtitle);
        Assert.Empty(shell.Instances[1].Testcases);
        Assert.Equal(paths[0], shell.Current!.WorkbookPath);
    }

    [Fact]
    public void Starting_on_a_later_instance_loads_that_one_not_the_first()
    {
        using var dir = new TempDir();
        var a = Samples.Copy(dir, Samples.IntegrationScenarios);
        var b = Samples.CreateLarge(dir, rows: 5);
        var shell = new EditorShellViewModel([("A", a), ("B", b)], b, lazyLoading: true);

        Assert.False(shell.Instances[0].IsLoaded);
        Assert.True(shell.Instances[1].IsLoaded);
        Assert.Equal(b, shell.Current!.WorkbookPath);
    }

    [Fact]
    public async Task Opening_an_unloaded_instance_loads_it_and_shows_its_first_testcase()
    {
        using var dir = new TempDir();
        var (shell, paths) = Open(dir, lazy: true);
        var node = shell.Instances[1];
        var changes = new List<string?>();
        node.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        shell.SelectedNode = node;
        await shell.WhenIdle;

        Assert.True(node.IsLoaded);
        Assert.False(node.IsLoading);
        Assert.Equal(paths[1], shell.Current!.WorkbookPath);
        Assert.IsType<TestcaseItem>(shell.SelectedNode);
        Assert.Equal(30, shell.Current.VisibleRows.Count);
        Assert.Equal("1 testcase", node.Subtitle);
        Assert.Contains(nameof(InstanceNode.Testcases), changes);
        Assert.False(shell.Instances[2].IsLoaded); // others still waiting
    }

    [Fact]
    public async Task A_lazily_loaded_instance_gets_its_results_too()
    {
        using var dir = new TempDir();
        var a = Samples.Copy(dir, Samples.IntegrationScenarios);
        var other = new TempDir();
        using var _ = other;
        var b = Samples.CreateLarge(other, rows: 3);
        FakeRun.Write(FakeRun.Folder(b), $"events_{FakeRun.RunId}.ndjson", FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", status: 500, error: "x"));
        var shell = new EditorShellViewModel([("A", a), ("B", b)], a, lazyLoading: true);

        shell.SelectedNode = shell.Instances[1];
        await shell.WhenIdle;

        Assert.Equal("FAIL", shell.Instances[1].Editor!.VisibleRows[0].ResultText);
        Assert.Contains("1 failed", shell.Instances[1].RunSummary);
        Assert.False(await shell.RefreshResultsIfChangedAsync()); // already up to date, no needless re-read
    }

    [Fact]
    public async Task An_unloaded_instance_that_cannot_be_opened_shows_the_reason_and_is_not_retried()
    {
        using var dir = new TempDir();
        var good = Samples.CreateLarge(dir, rows: 3);
        var shell = new EditorShellViewModel([("Good", good), ("Broken", dir.Combine("missing.xlsx"))], good, lazyLoading: true);
        var broken = shell.Instances[1];

        shell.SelectedNode = broken;
        await shell.WhenIdle;

        Assert.True(broken.HasError);
        Assert.Contains("Cannot open", broken.Subtitle);
        Assert.False(broken.CanLoad);
        Assert.Equal(good, shell.Current!.WorkbookPath); // stays where it was
    }

    [Fact]
    public async Task Turning_lazy_loading_off_reads_the_instances_still_waiting()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir, lazy: true);

        shell.LazyLoading = false;
        await shell.WhenIdle;

        Assert.All(shell.Instances, n => Assert.True(n.IsLoaded));
    }

    [Fact]
    public async Task Edits_stay_per_instance_when_instances_load_one_after_another()
    {
        using var dir = new TempDir();
        var (shell, _) = Open(dir, lazy: true);
        var first = shell.Current!;
        first.VisibleRows[0].Cells[first.Columns.First(c => c.Title == "CreatePost.title").CellIndex] = "Edited";

        shell.SelectedNode = shell.Instances[1];
        await shell.WhenIdle;

        Assert.True(shell.Instances[0].IsDirty);
        Assert.False(shell.Instances[1].IsDirty);
        Assert.False(shell.RequestClose()); // the unloaded third instance does not block, the dirty first one does
        Assert.Same(first, shell.Current);
    }

    [Fact]
    public void The_choice_is_remembered_between_sessions()
    {
        Assert.False(EditorSettings.Load().LazyLoading);

        new EditorSettings { LazyLoading = true }.Save();
        Assert.True(EditorSettings.Load().LazyLoading);

        File.WriteAllText(EditorSettings.FilePath, "{ not json");
        Assert.False(EditorSettings.Load().LazyLoading); // a damaged file is ignored, not fatal
    }

    [AvaloniaFact]
    public void The_window_applies_and_saves_the_setting_and_renders_unloaded_instances()
    {
        using var dir = new TempDir();
        new EditorSettings { LazyLoading = true }.Save();
        var a = Samples.Copy(dir, Samples.IntegrationScenarios);
        var b = Samples.CreateLarge(dir, rows: 5);
        var c = Samples.Copy(dir, Samples.FileHttpScenarios);

        var window = DataEditorWindow.Open([("DataEngine_IntegrationScenarios", a), ("Customers", b), ("FileHttpScenarios", c)], a);
        window.Width = 1440;
        window.Height = 860;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(window.Shell.LazyLoading);
        Assert.False(window.Shell.Instances[1].IsLoaded);
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.GetTempPath();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-lazy.png"));

        window.Shell.LazyLoading = false;
        Assert.False(EditorSettings.Load().LazyLoading);
        window.Close();
    }
}
