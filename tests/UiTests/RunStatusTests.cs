using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class RunStatusTests
{
    private sealed class Details : IRunDetails
    {
        public RunStatusSnapshot Snapshot { get; set; } = RunStatusSnapshot.Empty;
        public List<(string Instance, RunFocus Focus)> Focused { get; } = [];
        public List<string> Stopped { get; } = [];
        public TabControl TakeTabs() => new() { ItemsSource = new[] { new TabItem { Header = "Overview" }, new TabItem { Header = "Assertions" } } };
        public List<(string Instance, RunScope? Scope)> Selected { get; } = [];
        public void Show(string instanceName) { }
        public void Select(string instanceName, RunScope? scope) => Selected.Add((instanceName, scope));
        public RunStatusSnapshot GetStatus(string instanceName) => Snapshot;
        public void Focus(string instanceName, RunFocus focus) => Focused.Add((instanceName, focus));
        public void Stop(string instanceName) => Stopped.Add(instanceName);
    }

    private static (EditorShellViewModel Shell, Details Details, InstanceNode Instance, TestcaseItem Testcase, StepNode Step) Open(TempDir dir)
    {
        var shell = new EditorShellViewModel([("Sample", Samples.CreateLarge(dir, rows: 3))]);
        var details = new Details();
        shell.RunDetails = details;
        var instance = shell.Instances[0];
        var testcase = instance.Testcases[0];
        return (shell, details, instance, testcase, testcase.StepNodes[0]);
    }

    [Theory]
    [InlineData("PASS", "", "", RunState.Passed)]
    [InlineData("FAIL", "Assertion", "", RunState.AssertionFailed)]
    [InlineData("FAIL", "HttpStatus", "", RunState.AssertionFailed)]
    [InlineData("FAIL", "", "", RunState.AssertionFailed)]
    [InlineData("FAIL", "Template", "missing", RunState.ValidationError)]
    [InlineData("FAIL", "WorkbookValidation", "", RunState.ValidationError)]
    [InlineData("FAIL", "RuntimeReference", "x", RunState.VariableError)]
    [InlineData("FAIL", "VariableResolution", "x", RunState.VariableError)]
    [InlineData("FAIL", "Timeout", "timed out", RunState.UnexpectedError)]
    [InlineData("FAIL", "Network", "refused", RunState.UnexpectedError)]
    [InlineData("FAIL", "", "boom", RunState.UnexpectedError)]
    [InlineData("FAIL", "Cancelled", "", RunState.Cancelled)]
    public void Results_are_grouped_by_where_the_fix_is(string result, string category, string error, RunState expected)
        => Assert.Equal(expected, RunClassifier.Classify(result, category, error));

    [Fact]
    public void Skipped_requests_are_not_counted()
        => Assert.Null(RunClassifier.Classify("SKIPPED", "", ""));

    [Fact]
    public void Worst_result_wins_and_counts_are_shown()
    {
        var badge = new RunBadge();
        StepOutcome O(RunState s, string env = "dev") => new("T", "S", "D", env, s, s == RunState.Passed ? "" : "boom");
        badge.Apply([O(RunState.Passed), O(RunState.Passed), O(RunState.Passed), O(RunState.Passed), O(RunState.AssertionFailed, "test")], false);
        Assert.Equal(RunState.AssertionFailed, badge.State);
        Assert.Equal("✗", badge.Icon);
        Assert.Equal("4✓ 1✗", badge.Counts);
        Assert.Contains("4 passed · 1 assertion failed", badge.Tooltip);
        Assert.Contains("dev ✓ · test ✗", badge.Tooltip);
        badge.Apply([O(RunState.AssertionFailed), O(RunState.UnexpectedError), O(RunState.VariableError)], false);
        Assert.Equal(RunState.UnexpectedError, badge.State);
        Assert.Equal("‼", badge.Icon);
        badge.Apply([O(RunState.Passed)], false);
        Assert.Equal("✓", badge.Icon);
        Assert.Equal(string.Empty, badge.Counts);   // a single result needs no counts
        badge.Apply([], false);
        Assert.False(badge.HasStatus);
    }

    [Fact]
    public void Run_status_rolls_up_from_steps_to_testcase_and_instance()
    {
        using var dir = new TempDir();
        var (shell, details, instance, testcase, step) = Open(dir);
        details.Snapshot = new RunStatusSnapshot(false,
        [
            new(testcase.Name, step.Name, "D1", "", RunState.Passed, ""),
            new(testcase.Name, step.Name, "D2", "", RunState.AssertionFailed, "expected 1"),
            new(testcase.Name, "OtherStep", "D1", "", RunState.Passed, "")
        ]);
        shell.RefreshRunStatus();
        Assert.Equal(RunState.AssertionFailed, step.Run.State);
        Assert.Equal("1✓ 1✗", step.Run.Counts);
        Assert.Equal(RunState.AssertionFailed, testcase.Run.State);
        Assert.Equal(RunState.AssertionFailed, instance.Run.State);
        Assert.False(instance.Run.IsRunning);
    }

    [Fact]
    public void Run_buttons_are_disabled_while_the_instance_runs_and_come_back_afterwards()
    {
        using var dir = new TempDir();
        var (shell, details, instance, testcase, _) = Open(dir);
        details.Snapshot = new RunStatusSnapshot(true, []);
        shell.RefreshRunStatus();
        Assert.True(instance.Run.IsRunning);
        Assert.False(instance.Run.NotRunning);
        Assert.False(testcase.InstanceRun!.NotRunning);
        Assert.False(instance.Editor!.CanRun);
        Assert.True(shell.AnyRunning);
        details.Snapshot = new RunStatusSnapshot(false, [new(testcase.Name, testcase.StepNodes[0].Name, "D", "", RunState.Passed, "")]);
        shell.RefreshRunStatus();
        Assert.False(instance.Run.IsRunning);
        Assert.True(testcase.InstanceRun!.NotRunning);
        Assert.Equal("✓", instance.Run.Icon);
    }

    [Fact]
    public async Task A_second_run_is_refused_while_running()
    {
        using var dir = new TempDir();
        var (shell, details, instance, testcase, _) = Open(dir);
        var started = 0;
        shell.RunHandler = (_, _) => { started++; return Task.FromResult<string?>(null); };
        details.Snapshot = new RunStatusSnapshot(true, []);
        shell.RefreshRunStatus();
        await shell.RunNodeAsync(testcase);
        Assert.Equal(0, started);
    }

    [Fact]
    public void Editing_a_step_greys_its_status_until_the_next_run()
    {
        using var dir = new TempDir();
        var (shell, details, instance, testcase, step) = Open(dir);
        details.Snapshot = new RunStatusSnapshot(false, [new(testcase.Name, step.Name, "D", "", RunState.Passed, "")]);
        shell.RefreshRunStatus();
        Assert.False(step.Run.Stale);
        instance.Editor!.SelectedTestcase = testcase;
        instance.Editor.StepRows[0].Write("timeout", "42");
        shell.RefreshRunStatus();
        Assert.True(step.Run.Stale);
        Assert.StartsWith("Edited since the last run", step.Run.Tooltip);
        details.Snapshot = new RunStatusSnapshot(false, [new(testcase.Name, step.Name, "D", "", RunState.AssertionFailed, "x")]);
        shell.RefreshRunStatus();
        Assert.False(step.Run.Stale);
        Assert.Equal(RunState.AssertionFailed, step.Run.State);
    }

    [Fact]
    public void Clicking_a_status_icon_opens_the_matching_tab()
    {
        using var dir = new TempDir();
        var (shell, details, instance, testcase, step) = Open(dir);
        RunStatusSnapshot Only(RunState state) => new(false, [new(testcase.Name, step.Name, "D", "", state, "x")]);
        details.Snapshot = Only(RunState.AssertionFailed);
        shell.RefreshRunStatus();
        Assert.Equal("Assertions", shell.OpenStatus(step));
        Assert.Equal(new RunFocus(testcase.Name, testcase.Index, step.Name, RunState.AssertionFailed), details.Focused[^1].Focus);
        details.Snapshot = Only(RunState.ValidationError);
        shell.RefreshRunStatus();
        Assert.Equal("Workbook", shell.OpenStatus(step));
        details.Snapshot = Only(RunState.UnexpectedError);
        shell.RefreshRunStatus();
        Assert.Equal("Overview", shell.OpenStatus(testcase));
        Assert.Equal("Overview", shell.OpenStatus(instance));
        Assert.Equal(new RunFocus(string.Empty, 0, null, RunState.UnexpectedError), details.Focused[^1].Focus);
        shell.StopInstance(instance);
        Assert.Equal(["Sample"], details.Stopped);
    }

    [AvaloniaFact]
    public void Tree_shows_status_icons_and_the_stop_button()
    {
        using var dir = new TempDir();
        var (shell, details, instance, testcase, step) = Open(dir);
        var window = new DataEditorWindow(shell);
        window.Width = 1200; window.Height = 800;
        window.Show();
        details.Snapshot = new RunStatusSnapshot(true,
        [
            new(testcase.Name, step.Name, "D1", "dev", RunState.Passed, ""),
            new(testcase.Name, step.Name, "D2", "dev", RunState.AssertionFailed, "expected 1"),
            new(testcase.Name, "OtherStep", "D1", "dev", RunState.UnexpectedError, "boom")
        ]);
        shell.RefreshRunStatus();
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-run-status.png"));
        window.Close();
    }

    [Fact]
    public void Selecting_a_node_narrows_the_run_views_to_it()
    {
        using var dir = new TempDir();
        var (shell, details, instance, testcase, step) = Open(dir);
        shell.SelectedNode = step;
        Assert.Equal(("Sample", new RunScope(testcase.Name, testcase.Index, step.Name)), details.Selected[^1]);
        shell.SelectedNode = testcase;
        Assert.Equal(("Sample", new RunScope(testcase.Name, testcase.Index, null)), details.Selected[^1]);
        shell.SelectedNode = instance;
        Assert.Equal(("Sample", (RunScope?)null), details.Selected[^1]);
    }
}
