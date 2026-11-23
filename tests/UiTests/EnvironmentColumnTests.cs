using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class EnvironmentColumnTests
{
    private static DataEditorViewModel Open(TempDir dir, params object[] events)
    {
        var path = Samples.CreateLarge(dir, rows: 4);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson", events);
        var editor = new DataEditorViewModel(path, "Customers");
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        return editor;
    }

    // CUST001: ok in dev, name fails in test.  CUST002: name fails in both.  CUST003: ok in dev, the step times out in test.  CUST004: ok in both.
    private static object[] TwoEnvironments() =>
    [
        FakeRun.Request(1, "CUST001", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Customer 1", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", category: "Assertion", env: "test"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "x", env: "test"),
        FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", category: "Assertion", env: "dev"), FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "y", env: "dev"),
        FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", category: "Assertion", env: "test"), FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "y", env: "test"),
        FakeRun.Request(1, "CUST003", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST003", "CreateCustomer", "name", "eq", "Customer 3", "Customer 3", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST003", "CreateCustomer", result: "FAIL", status: 0, category: "Timeout", error: "The request timed out", env: "test"),
        FakeRun.Request(1, "CUST004", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST004", "CreateCustomer", "name", "eq", "Customer 4", "Customer 4", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST004", "CreateCustomer", env: "test"), FakeRun.Assertion(1, "CUST004", "CreateCustomer", "name", "eq", "Customer 4", "Customer 4", result: "PASS", env: "test"),
    ];

    private static DataRowViewModel Row(DataEditorViewModel editor, string id) => editor.VisibleRows.Single(r => r.CurrentDataId == id);

    [Fact]
    public void Each_row_shows_one_chip_per_environment()
    {
        using var dir = new TempDir();
        var editor = Open(dir, TwoEnvironments());
        Assert.True(editor.HasEnvColumn);
        Assert.Equal(["✓ dev", "✗ test"], Row(editor, "CUST001").EnvChips.Select(c => c.Text).ToArray());
        Assert.Equal(["✗ dev", "✗ test"], Row(editor, "CUST002").EnvChips.Select(c => c.Text).ToArray());
        Assert.Equal(["✓ dev", "⚠ test"], Row(editor, "CUST003").EnvChips.Select(c => c.Text).ToArray());
        Assert.Equal(["✓ dev", "✓ test"], Row(editor, "CUST004").EnvChips.Select(c => c.Text).ToArray());
        Assert.Contains("test: FAIL", Row(editor, "CUST001").EnvDetail);
    }

    [Fact]
    public void A_step_that_broke_is_an_error_not_a_failure()
    {
        using var dir = new TempDir();
        var editor = Open(dir, TwoEnvironments());
        Assert.Equal("ERROR", Row(editor, "CUST003").ResultText);
        Assert.Equal("FAIL", Row(editor, "CUST001").ResultText);
        Assert.Contains("1 error", editor.RunSummaryText);
        Assert.Contains("2 failed", editor.RunSummaryText);
        Assert.Contains("1 passed", editor.RunSummaryText);

        editor.ShowFailedOnly = true;   // an error is also a row that went wrong
        Assert.Equal(["CUST001", "CUST002", "CUST003"], editor.VisibleRows.Select(r => r.CurrentDataId).ToArray());

        editor.ShowFailedOnly = false;
        editor.EnvironmentChoice = "dev";   // the timeout happened in test only
        Assert.Equal("PASS", Row(editor, "CUST003").ResultText);
        editor.EnvironmentChoice = "test";
        Assert.Equal("ERROR", Row(editor, "CUST003").ResultText);
        Assert.Contains("1 error", editor.RunSummaryText);
    }

    [Fact]
    public void An_unexpected_http_status_or_a_wrong_value_stays_a_failure()
    {
        using var dir = new TempDir();
        var editor = Open(dir,
            FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", status: 500, category: "HttpStatus", error: "Status 500"),
            FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", status: 0, category: "Network", error: "connection refused"));
        Assert.Equal("FAIL", Row(editor, "CUST001").ResultText);
        Assert.Equal("ERROR", Row(editor, "CUST002").ResultText);
    }

    [Fact]
    public void The_comparison_sorts_the_columns_into_differs_same_and_passed()
    {
        using var dir = new TempDir();
        var editor = Open(dir, TwoEnvironments());

        var one = editor.CompareEnvironments(Row(editor, "CUST001"))!;
        var differs = Assert.Single(one.Differs);
        Assert.Equal("CreateCustomer.name", differs.Title);
        Assert.Equal("failed in test, ok in dev", differs.Summary);
        Assert.True(differs.CellIndex >= 1);
        Assert.Empty(one.SameFailing);

        var two = editor.CompareEnvironments(Row(editor, "CUST002"))!;
        Assert.Equal("CreateCustomer.name", Assert.Single(two.SameFailing).Title);
        Assert.Empty(two.Differs);

        var four = editor.CompareEnvironments(Row(editor, "CUST004"))!;
        Assert.Equal("CreateCustomer.name", Assert.Single(four.PassedEverywhere).Title);
        Assert.False(four.PassesUnknown);

        var three = editor.CompareEnvironments(Row(editor, "CUST003"))!;
        Assert.Contains(three.Differs.Concat(three.SameFailing), c => c.Title == "CreateCustomer (step)");
    }

    [Fact]
    public void Without_logged_passes_only_failed_columns_are_listed_and_the_note_says_so()
    {
        using var dir = new TempDir();
        var editor = Open(dir,
            FakeRun.Request(1, "CUST001", "CreateCustomer", env: "dev"),
            FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", category: "Assertion", env: "test"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "x", env: "test"));
        var comparison = editor.CompareEnvironments(Row(editor, "CUST001"))!;
        Assert.True(comparison.PassesUnknown);
        Assert.Equal("failed in test", Assert.Single(comparison.SameFailing).Summary);
    }

    [Fact]
    public void The_differing_rows_filter_keeps_rows_whose_status_differs()
    {
        using var dir = new TempDir();
        var editor = Open(dir, TwoEnvironments());
        editor.ShowDifferingOnly = true;
        Assert.Equal(["CUST001", "CUST003"], editor.VisibleRows.Select(r => r.CurrentDataId).ToArray());
        Assert.Contains("of", editor.RowCountText);
        Assert.Contains("Differing rows", editor.ShowOptionsTitle);
        editor.ShowDifferingOnly = false;
        Assert.Equal(4, editor.VisibleRows.Count);
    }

    [Fact]
    public void Many_environments_collapse_into_one_count_chip()
    {
        using var dir = new TempDir();
        var events = new List<object>();
        foreach (var env in new[] { "e1", "e2", "e3", "e4", "e5" })
            events.Add(env is "e4" or "e5" ? FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", category: "Assertion", env: env) : FakeRun.Request(1, "CUST001", "CreateCustomer", env: env));
        var editor = Open(dir, events.ToArray());
        var chip = Assert.Single(Row(editor, "CUST001").EnvChips);
        Assert.Equal("3/5 ✓", chip.Text);
        Assert.Equal("FAIL", chip.Status);
        Assert.Contains("e4: FAIL", Row(editor, "CUST001").EnvDetail);
    }

    [Fact]
    public void A_single_environment_run_has_no_envs_column()
    {
        using var dir = new TempDir();
        var editor = Open(dir, FakeRun.Request(1, "CUST001", "CreateCustomer", env: "dev"));
        Assert.False(editor.HasEnvColumn);
        Assert.Empty(Row(editor, "CUST001").EnvChips);
    }

    [Fact]
    public void Clicking_a_chip_in_the_comparison_switches_the_table_to_that_environment()
    {
        using var dir = new TempDir();
        var editor = Open(dir, TwoEnvironments());
        editor.ShowEnvironment("test");
        Assert.Equal("test", editor.EnvironmentChoice);
        Assert.Equal("ERROR", Row(editor, "CUST003").ResultText);
        Assert.Equal(["✓ dev", "⚠ test"], Row(editor, "CUST003").EnvChips.Select(c => c.Text).ToArray());   // the chips keep showing every environment
    }

    [AvaloniaFact]
    public void The_grid_shows_the_envs_column_only_for_a_multi_environment_run()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 4);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson", TwoEnvironments());
        var window = DataEditorWindow.Open(path, "Customers");
        window.Width = 1440; window.Height = 820; window.Show();
        var vm = window.ViewModel;
        var grid = window.FindControl<DataGrid>("Grid")!;
        Dispatcher.UIThread.RunJobs();

        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));
        Dispatcher.UIThread.RunJobs();
        Assert.True(grid.Columns.Single(c => c.Tag is "envs").IsVisible);
        Assert.NotEmpty(grid.GetVisualDescendants().OfType<Border>().Where(b => b.Tag is "env-chip"));
        Assert.NotEmpty(grid.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is "row-envs-button"));

        var button = grid.GetVisualDescendants().OfType<Button>().First(b => b.Tag is "row-envs-button");
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));   // opens the comparison without error
        Dispatcher.UIThread.RunJobs();
        window.Close();

        // a run with one environment has no use for the column
        using var other = new TempDir();
        var single = Samples.CreateLarge(other, rows: 2);
        FakeRun.Write(FakeRun.Folder(single), $"events_{FakeRun.RunId}.ndjson", FakeRun.Request(1, "CUST001", "CreateCustomer", env: "dev"));
        var second = DataEditorWindow.Open(single, "Customers");
        second.Width = 1440; second.Height = 820; second.Show();
        second.ViewModel.ApplyResults(RunResultsReader.ReadLatest(second.ViewModel.ResultsFolder));
        Dispatcher.UIThread.RunJobs();
        Assert.False(second.FindControl<DataGrid>("Grid")!.Columns.Single(c => c.Tag is "envs").IsVisible);
        second.Close();
    }
}
