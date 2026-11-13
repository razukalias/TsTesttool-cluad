using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

/// <summary>Writes event files in the shape the runner's LiveEventWriter produces.</summary>
internal static class FakeRun
{
    public const string RunId = "20261113_201500_abcdef123"; // 25 characters, like the runner's

    public static string Folder(string workbookPath, string bucket = "failed", string runName = "Run_1")
    {
        var folder = Path.Combine(Path.GetDirectoryName(workbookPath)!, "Results", bucket, runName, "log");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static object Request(int testcase, string dataId, string step, string result = "PASS", int status = 200, long ms = 120, string error = "", string category = "", string env = "")
        => new { eventType = "request-completed", runId = RunId, testcaseIndex = testcase, dataId, stepName = step, result, httpStatus = status, durationMs = ms, errorMessage = error, failureCategory = category, environment = env };

    public static object Assertion(int testcase, string dataId, string step, string path, string verb, string expected, string actual, string result = "FAIL", string message = "", string env = "")
        => new { eventType = "assertion-completed", runId = RunId, testcaseIndex = testcase, dataId, stepName = step, responsePath = path, assertionVerb = verb, expectedValue = expected, actualValue = actual, result, failureMessage = message, environment = env };

    public static string Write(string folder, string fileName, params object[] events)
    {
        var path = Path.Combine(folder, fileName);
        File.WriteAllLines(path, events.Select(e => JsonSerializer.Serialize(e)));
        return path;
    }
}

public class ResultsTests
{
    private static int CellOf(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    private static string LargeWithRun(TempDir dir, int rows = 6)
    {
        var path = Samples.CreateLarge(dir, rows);
        var folder = FakeRun.Folder(path);
        var events = new List<object>();
        for (var n = 1; n <= rows; n++)
        {
            var id = $"CUST{n:000}";
            events.Add(FakeRun.Request(1, id, "CreateCustomer", status: 201, ms: 100 + n));
            events.Add(FakeRun.Assertion(1, id, "CreateCustomer", "statusCode", "eq", "201", "201", result: "PASS"));
        }
        // CUST002: wrong assertion. CUST004: the step itself failed.
        events.RemoveAll(e => JsonSerializer.Serialize(e).Contains("\"CUST002\"") || JsonSerializer.Serialize(e).Contains("\"CUST004\""));
        events.Add(FakeRun.Request(1, "CUST002", "CreateCustomer", result: "FAIL", status: 201));
        events.Add(FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Kund 2", message: "Values differ"));
        events.Add(FakeRun.Request(1, "CUST004", "CreateCustomer", result: "FAIL", status: 500, category: "HTTP", error: "Internal Server Error"));
        FakeRun.Write(folder, $"events_{FakeRun.RunId}.ndjson", [.. events]);
        return path;
    }

    // ---- Reader --------------------------------------------------------------------------

    [Fact]
    public void Reads_pass_and_failure_details_per_data_row()
    {
        using var dir = new TempDir();
        var path = LargeWithRun(dir);

        var results = RunResultsReader.ReadLatest(Path.Combine(Path.GetDirectoryName(path)!, "Results"))!;

        Assert.Equal(FakeRun.RunId, results.RunId);
        Assert.Equal(4, results.PassedRows);
        Assert.Equal(2, results.FailedRows);
        Assert.True(results.Find(1, "cust001")!.Passed); // case-insensitive
        var assertionFailure = Assert.Single(results.Find(1, "CUST002")!.Failures, f => f.IsAssertion);
        Assert.Equal(("name", "Customer 2", "Kund 2"), (assertionFailure.ResponsePath, assertionFailure.Expected, assertionFailure.Actual));
        var stepFailure = Assert.Single(results.Find(1, "CUST004")!.Failures);
        Assert.Equal("HTTP 500 — HTTP — Internal Server Error", stepFailure.Message);
        Assert.Null(results.Find(1, "CUST999"));
    }

    [Fact]
    public void A_row_fails_if_any_iteration_or_environment_failed()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, 2);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}_test.ndjson",
            FakeRun.Request(1, "CUST001", "CreateCustomer", env: "test"), FakeRun.Request(1, "CUST001", "CreateCustomer", env: "test"));
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}_prod.ndjson",
            FakeRun.Request(1, "CUST001", "CreateCustomer", env: "prod"), FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", status: 503, env: "prod"));

        var results = RunResultsReader.ReadLatest(Path.Combine(Path.GetDirectoryName(path)!, "Results"))!;

        var outcome = results.Find(1, "CUST001")!;
        Assert.False(outcome.Passed);
        Assert.Equal(4, outcome.Requests);
        Assert.Equal("prod", Assert.Single(outcome.Failures).Environment);
    }

    [Fact]
    public void Picks_the_latest_run_ignores_aggregate_files_and_bad_lines()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, 2);
        var oldFile = FakeRun.Write(FakeRun.Folder(path, "failed", "Run_old"), "events_20260101_100000_oldrun123.ndjson",
            new { eventType = "request-completed", runId = "20260101_100000_oldrun123", testcaseIndex = 1, dataId = "CUST001", stepName = "S", result = "FAIL" });
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-3));
        var folder = FakeRun.Folder(path, "passed", "Run_new");
        File.WriteAllLines(Path.Combine(folder, $"events_{FakeRun.RunId}.ndjson"),
        [
            "this is not json",
            JsonSerializer.Serialize(FakeRun.Request(1, "CUST001", "CreateCustomer")),
            "{\"eventType\":\"request-completed\",\"dataId\":\"CUS"  // cut off mid-write
        ]);
        FakeRun.Write(folder, $"events_{FakeRun.RunId}_aggregate.ndjson", new { eventType = "run-completed", runId = FakeRun.RunId });

        var results = RunResultsReader.ReadLatest(Path.Combine(Path.GetDirectoryName(path)!, "Results"))!;

        Assert.Equal(FakeRun.RunId, results.RunId);
        Assert.Equal(1, results.PassedRows);
        Assert.Equal(0, results.FailedRows);
    }

    [Fact]
    public void Reads_a_file_the_runner_still_has_open_and_returns_null_without_results()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, 2);
        var resultsFolder = Path.Combine(Path.GetDirectoryName(path)!, "Results");
        Assert.Null(RunResultsReader.ReadLatest(resultsFolder));

        var file = Path.Combine(FakeRun.Folder(path, "running"), $"events_{FakeRun.RunId}.ndjson");
        using var writer = new StreamWriter(new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(FakeRun.Request(1, "CUST001", "CreateCustomer")));

        Assert.Equal(1, RunResultsReader.ReadLatest(resultsFolder)!.PassedRows);
    }

    // ---- In the editor -------------------------------------------------------------------

    [Fact]
    public void Rows_testcases_and_summary_show_the_run()
    {
        using var dir = new TempDir();
        var path = LargeWithRun(dir);
        var vm = new DataEditorViewModel(path);
        Assert.Equal("No results yet", vm.RunSummaryText);
        Assert.All(vm.VisibleRows, r => Assert.Equal("—", r.ResultText));

        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));

        Assert.Equal(["PASS", "FAIL", "PASS", "FAIL", "PASS", "PASS"], vm.VisibleRows.Select(r => r.ResultText));
        Assert.Equal("2 failed · 4 passed", vm.SelectedTestcase!.ResultText);
        Assert.Contains("4 passed, 2 failed", vm.RunSummaryText);
        Assert.True(vm.HasResults);
    }

    [Fact]
    public void Failed_rows_only_filters_and_combines_with_the_text_filter()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(LargeWithRun(dir));
        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));

        vm.ShowFailedOnly = true;
        Assert.Equal(["CUST002", "CUST004"], vm.VisibleRows.Select(r => r.Cells[0]));
        Assert.Equal("2 rows", vm.RowCountText);

        vm.FilterText = "CUST004";
        Assert.Equal(["CUST004"], vm.VisibleRows.Select(r => r.Cells[0]));
        vm.FilterText = "";
        vm.ShowFailedOnly = false;
        Assert.Equal(6, vm.VisibleRows.Count);
    }

    [Fact]
    public void Selecting_a_failed_row_lists_its_failures_and_selecting_one_reveals_its_column()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(LargeWithRun(dir));
        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));
        var revealed = new List<int>();
        vm.RevealColumnRequested += (_, cell) => revealed.Add(cell);

        vm.SelectedRow = vm.VisibleRows[1]; // CUST002

        Assert.True(vm.HasRowResult);
        Assert.StartsWith("Failed", vm.RowResultHeadline);
        var failure = Assert.Single(vm.RowFailures);
        Assert.Equal("CreateCustomer.name", failure.Title);
        Assert.Contains("expected \"Customer 2\", got \"Kund 2\"", failure.Text);
        Assert.Equal(vm.Columns.First(c => c.Group == ColumnGroup.Response && c.Title == "CreateCustomer.name").CellIndex, failure.CellIndex); // the expected-value column, not the request one

        vm.SelectedFailure = failure;
        Assert.Equal([failure.CellIndex], revealed);
        Assert.Equal(failure.CellIndex, vm.CurrentCellIndex);

        vm.SelectedRow = vm.VisibleRows[3]; // CUST004: the step failed
        Assert.Equal("Failed · 1 problem in 1 request", vm.RowResultHeadline);
        Assert.Equal("CreateCustomer", vm.RowFailures[0].Title);
        Assert.Contains("HTTP 500", vm.RowFailures[0].Text);

        vm.SelectedRow = vm.VisibleRows[0]; // passed
        Assert.StartsWith("Passed", vm.RowResultHeadline);
        Assert.Empty(vm.RowFailures);
    }

    [Fact]
    public void Results_survive_switching_testcase_and_come_back()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Request(1, "HTTP001", "FetchUser"), FakeRun.Request(1, "HTTP001", "CreatePost", result: "FAIL", status: 400),
            FakeRun.Request(2, "GQL001", "QueryCountry"));
        var vm = new DataEditorViewModel(path);
        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));

        Assert.Equal("1 failed · 0 passed", vm.Testcases[0].ResultText);
        Assert.Equal("1 passed", vm.Testcases[1].ResultText);
        Assert.Equal("", vm.Testcases[2].ResultText);

        vm.SelectedTestcase = vm.Testcases[1];
        Assert.Equal("PASS", vm.VisibleRows[0].ResultText);
        vm.SelectedTestcase = vm.Testcases[0];
        Assert.Equal("FAIL", vm.VisibleRows[0].ResultText);
    }

    [Fact]
    public async Task The_shell_loads_the_results_of_every_instance()
    {
        using var dir = new TempDir();
        var a = LargeWithRun(dir);
        var other = new TempDir();
        using var _ = other;
        var b = Samples.CreateLarge(other, 3);
        var shell = new EditorShellViewModel([("A", a), ("B", b)], a);

        await shell.RefreshResultsAsync();

        Assert.Contains("4 passed, 2 failed", shell.Instances[0].RunSummary);
        Assert.Equal("No results yet", shell.Instances[1].RunSummary);
        Assert.False(shell.IsLoadingResults);
        Assert.Equal("FAIL", shell.Instances[0].Editor!.VisibleRows[1].ResultText);
    }

    [AvaloniaFact]
    public void Renders_results_in_the_tree_the_grid_and_the_detail_panel()
    {
        using var dir = new TempDir();
        var path = LargeWithRun(dir, rows: 12);
        var window = DataEditorWindow.Open(path, "Customers");
        window.Width = 1440;
        window.Height = 860;
        window.Show();
        var vm = window.ViewModel;
        vm.ApplyResults(RunResultsReader.ReadLatest(vm.ResultsFolder));
        vm.SelectedRow = vm.VisibleRows[1];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(1 + 1 + vm.Columns.Count, window.FindControl<DataGrid>("Grid")!.Columns.Count);
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.GetTempPath();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-results.png"));
        window.Close();
    }
}
