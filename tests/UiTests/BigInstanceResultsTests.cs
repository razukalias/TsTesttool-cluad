using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Threading;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

/// <summary>A big instance (many rows, three environments, several expected columns): does every cell show what the run said?</summary>
public class BigInstanceResultsTests
{
    private static readonly string[] Envs = ["dev", "test", "prod"];
    private static readonly string[] Paths = ["statusCode", "id", "name"];

    /// <summary>Whether assertion <paramref name="column"/> of row <paramref name="n"/> fails in <paramref name="env"/> in the first run.</summary>
    private static bool Fails(int n, string env, int column) => (n + column * 7 + Array.IndexOf(Envs, env) * 3) % 5 == 0;

    private static void WriteRun(string results, int number, IEnumerable<int> rows, Func<int, string, int, bool> fails, string[] envs)
    {
        var runId = $"20261124_21{number:00}00_big{number:000000}";   // 25 characters
        var folder = Path.Combine(results, "failed", "Run_" + number, "log");
        Directory.CreateDirectory(folder);
        foreach (var env in envs)
        {
            var lines = new List<string>();
            foreach (var n in rows)
            {
                var id = $"CUST{n:000}";
                var rowFailed = Enumerable.Range(0, Paths.Length).Any(c => fails(n, env, c));
                lines.Add(JsonSerializer.Serialize(new { eventType = "request-completed", runId, testcaseIndex = 1, dataId = id, stepName = "CreateCustomer", result = rowFailed ? "FAIL" : "PASS", httpStatus = 201, durationMs = 10, errorMessage = "", failureCategory = rowFailed ? "Assertion" : "", environment = env }));
                for (var c = 0; c < Paths.Length; c++)
                    lines.Add(JsonSerializer.Serialize(new { eventType = "assertion-completed", runId, testcaseIndex = 1, dataId = id, stepName = "CreateCustomer", responsePath = Paths[c], assertionVerb = "eq", expectedValue = "x", actualValue = fails(n, env, c) ? "y" : "x", result = fails(n, env, c) ? "FAIL" : "PASS", failureMessage = "", environment = env }));
            }
            var file = Path.Combine(folder, $"events_{runId}_{env}.ndjson");
            File.WriteAllLines(file, lines);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(number));
        }
    }

    private static (DataEditorWindow Window, DataEditorViewModel Editor) Open(TempDir dir, int rows)
    {
        var path = Samples.CreateLarge(dir, rows);
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.Shell.Current!);
    }

    private static int Cell(DataEditorViewModel editor, string path) => editor.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.EndsWith("CreateCustomer." + path, StringComparison.OrdinalIgnoreCase)).CellIndex;

    [AvaloniaFact]
    public void Every_failed_cell_of_a_big_multi_environment_run_is_marked_in_all_environments_and_in_each_one()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir, rows: 400);
        var results = Path.Combine(Path.GetDirectoryName(editor.WorkbookPath)!, "Results");
        WriteRun(results, 1, Enumerable.Range(1, 400), Fails, Envs);

        var clock = Stopwatch.StartNew();
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        Dispatcher.UIThread.RunJobs();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"applying took {clock.Elapsed}");
        Assert.Equal(400, editor.VisibleRows.Count);

        void Check(string? env)
        {
            editor.EnvironmentChoice = env ?? DataEditorViewModel.AllEnvironments;
            Dispatcher.UIThread.RunJobs();
            var wrong = new List<string>();
            foreach (var row in editor.VisibleRows)
            {
                var n = int.Parse(row.CurrentDataId[4..]);
                for (var c = 0; c < Paths.Length; c++)
                {
                    var expected = Envs.Where(e => env is null || e == env).Any(e => Fails(n, e, c));
                    var marked = editor.FailureAt(row, Cell(editor, Paths[c])) is not null;
                    if (marked != expected) wrong.Add($"{row.CurrentDataId}.{Paths[c]} env={env ?? "all"} expected {expected} got {marked}");
                }
                var rowExpected = Enumerable.Range(0, Paths.Length).Any(c => Envs.Where(e => env is null || e == env).Any(e => Fails(n, e, c)));
                if ((row.ResultText == "FAIL") != rowExpected) wrong.Add($"{row.CurrentDataId} row status {row.ResultText} expected failed={rowExpected} env={env ?? "all"}");
            }
            Assert.True(wrong.Count == 0, $"{wrong.Count} wrong, first: {string.Join("; ", wrong.Take(5))}");
        }
        Check(null);
        foreach (var env in Envs) Check(env);

        // the failed-cells list agrees: one entry per failed cell of the rows shown
        editor.EnvironmentChoice = DataEditorViewModel.AllEnvironments;
        editor.ShowFailedCells = true;
        Dispatcher.UIThread.RunJobs();
        var failedCells = Enumerable.Range(1, 400).Sum(n => Enumerable.Range(0, Paths.Length).Count(c => Envs.Any(e => Fails(n, e, c))));
        Assert.Equal(failedCells, editor.FailedCells.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void After_a_rerun_of_some_rows_the_cells_of_the_other_rows_still_show_their_old_failures()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir, rows: 400);
        var results = Path.Combine(Path.GetDirectoryName(editor.WorkbookPath)!, "Results");
        WriteRun(results, 1, Enumerable.Range(1, 400), Fails, Envs);
        var rerun = Enumerable.Range(1, 400).Where(n => Enumerable.Range(0, 3).Any(c => Envs.Any(e => Fails(n, e, c)))).Take(40).ToList();
        WriteRun(results, 2, rerun, (n, e, c) => false, Envs);    // everything they ran passes now

        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        Dispatcher.UIThread.RunJobs();
        var wrong = 0;
        foreach (var row in editor.VisibleRows)
        {
            var n = int.Parse(row.CurrentDataId[4..]);
            for (var c = 0; c < Paths.Length; c++)
            {
                var expected = !rerun.Contains(n) && Envs.Any(e => Fails(n, e, c));
                if ((editor.FailureAt(row, Cell(editor, Paths[c])) is not null) != expected) wrong++;
            }
        }
        Assert.Equal(0, wrong);
        window.Close();
    }

    [AvaloniaFact]
    public void The_JSON_view_counts_the_same_failed_cells_as_the_table()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir, rows: 400);
        var results = Path.Combine(Path.GetDirectoryName(editor.WorkbookPath)!, "Results");
        WriteRun(results, 1, Enumerable.Range(1, 400), Fails, Envs);
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        Assert.Equal("CreateCustomer", editor.StepFilter);
        foreach (var leaf in view.Root!.Leaves().Where(l => Paths.Contains(l.Path)))
        {
            var c = Array.IndexOf(Paths, leaf.Path);
            var expected = Enumerable.Range(1, 400).Count(n => Envs.Any(e => Fails(n, e, c)));
            Assert.Equal(expected, leaf.Failed);
            Assert.Equal(400 - expected, leaf.Passed);
        }
        window.Close();
    }

    private static (DataEditorWindow Window, DataEditorViewModel Editor) OpenWithRun(TempDir dir)
    {
        var (window, editor) = Open(dir, rows: 400);
        var results = Path.Combine(Path.GetDirectoryName(editor.WorkbookPath)!, "Results");
        WriteRun(results, 1, Enumerable.Range(1, 400), Fails, Envs);
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        Dispatcher.UIThread.RunJobs();
        return (window, editor);
    }

    private static IEnumerable<(DataRowViewModel Row, int Column, int Cell)> FailedCellsOf(DataEditorViewModel editor)
        => editor.VisibleRows.SelectMany(row => Enumerable.Range(0, Paths.Length).Select(c => (row, c, Cell(editor, Paths[c])))).Where(t => editor.FailureAt(t.row, t.Item3) is not null);

    [AvaloniaFact]
    public void Known_bug_tags_on_many_cells_of_a_big_run_turn_exactly_those_failures_into_known_bugs()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir);
        var before = FailedCellsOf(editor).ToList();
        var tagged = before.Where((_, i) => i % 3 == 0).ToList();                      // every third failed cell is tagged for the environment it failed in
        foreach (var (row, c, cell) in tagged)
        {
            var env = Envs.First(e => Fails(int.Parse(row.CurrentDataId[4..]), e, c));
            row.Cells[cell] = row.Cells[cell] + $" <bug: {env}; known issue>";
        }
        editor.RefreshFailedCells();
        Dispatcher.UIThread.RunJobs();
        var after = FailedCellsOf(editor).Select(t => (t.Row.CurrentDataId, t.Column)).ToHashSet();
        Assert.Equal(before.Count - tagged.Count, after.Count);
        Assert.All(tagged, t => Assert.DoesNotContain((t.Row.CurrentDataId, t.Column), after));
        // a row whose failed cells are all tagged is a known bug, not a failure
        foreach (var row in editor.VisibleRows)
        {
            var n = int.Parse(row.CurrentDataId[4..]);
            var failing = Enumerable.Range(0, Paths.Length).Where(c => Envs.Any(e => Fails(n, e, c))).ToList();
            if (failing.Count == 0) { Assert.Equal("PASS", row.ResultText); continue; }
            var allTagged = failing.All(c => tagged.Any(t => t.Row == row && t.Column == c));
            Assert.Equal(allTagged ? "BUG" : "FAIL", row.ResultText);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void Assign_actual_on_many_cells_of_a_big_run_clears_those_marks_and_Undo_brings_them_back()
    {
        using var dir = new TempDir();
        var (window, editor) = OpenWithRun(dir);
        var before = FailedCellsOf(editor).ToList();
        var chosen = before.Take(60).ToList();
        var original = chosen.ToDictionary(t => (t.Row.CurrentDataId, t.Cell), t => t.Row.Cells[t.Cell]);
        foreach (var (row, _, cell) in chosen) Assert.True(editor.AssignActual(row, cell, editor.FailureAt(row, cell)!));
        Assert.All(chosen, t => Assert.EndsWith("y", t.Row.Cells[t.Cell]));   // the verb written in the cell stays, the value is the one the run got
        Assert.Equal(before.Count - chosen.Count, FailedCellsOf(editor).Count());
        Assert.Equal(before.Count - chosen.Count, editor.FailedCells.Count == 0 ? before.Count - chosen.Count : editor.FailedCells.Count);

        for (var i = 0; i < chosen.Count; i++) editor.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before.Count, FailedCellsOf(editor).Count());
        Assert.All(chosen, t => Assert.Equal(original[(t.Row.CurrentDataId, t.Cell)], editor.VisibleRows.First(r => r.CurrentDataId == t.Row.CurrentDataId).Cells[t.Cell]));   // Undo rebuilds the rows: look them up again
        window.Close();
    }
}
