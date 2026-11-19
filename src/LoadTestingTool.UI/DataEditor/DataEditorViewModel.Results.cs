using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One line of "what went wrong" for the selected row, with the grid column it relates to.</summary>
public sealed class FailureViewModel
{
    public FailureViewModel(FailureDetail detail, int cellIndex, string title, string text)
    {
        Detail = detail;
        CellIndex = cellIndex;
        Title = title;
        Text = text;
    }

    public FailureDetail Detail { get; }

    /// <summary>Grid position of the column to look at (0 is the data ID), or -1 when the column is not shown.</summary>
    public int CellIndex { get; }

    public string Title { get; }
    public string Text { get; }
    public string Kind => Detail.IsAssertion ? "ASSERT" : "STEP";
}

// Showing the latest run: a result per row, per testcase, a failed-only filter, and the failures behind a row.
public sealed partial class DataEditorViewModel
{
    private RunResults? _results;

    public ObservableCollection<FailureViewModel> RowFailures { get; } = [];

    /// <summary>Where the runner writes this instance's results.</summary>
    public string ResultsFolder => Path.Combine(Path.GetDirectoryName(WorkbookPath) ?? ".", "Results");

    [ObservableProperty] private bool _showFailedOnly;

    public const string AllEnvironments = "All environments";

    /// <summary>"All environments" plus every environment that has a failure in the shown run; empty when the run has no environment names.</summary>
    public ObservableCollection<string> EnvironmentOptions { get; } = [];

    /// <summary>Which environment's failures the failed rows / columns / cells views and the cell marks use; all of them mixed by default.</summary>
    [ObservableProperty] private string _environmentChoice = AllEnvironments;

    public bool HasEnvironmentChoice => EnvironmentOptions.Count > 2;

    partial void OnEnvironmentChoiceChanged(string value)
    {
        if (value is null) { EnvironmentChoice = AllEnvironments; return; }
        ApplyFilter();   // rebuilds the rows, so the cell marks are drawn again too
        RefreshRowFailures();
    }

    private bool EnvironmentPicked => EnvironmentChoice is { Length: > 0 } choice && choice != AllEnvironments;

    /// <summary>The failures of a row in the chosen environment (all of them when none is chosen).</summary>
    private IEnumerable<FailureDetail> FailuresOf(RowOutcome outcome)
        => EnvironmentPicked ? outcome.Failures.Where(f => string.Equals(f.Environment, EnvironmentChoice, StringComparison.OrdinalIgnoreCase)) : outcome.Failures;

    private bool RowFailed(DataRowViewModel row)
        => row.Outcome is { } outcome && (EnvironmentPicked ? FailuresOf(outcome).Any(f => !IsKnownBug(row, f)) : row.FailedForReal);

    private void RefreshEnvironmentOptions()
    {
        var names = _results?.Rows.Values.SelectMany(r => r.Failures).Select(f => f.Environment).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        var options = names.Count == 0 ? [] : new[] { AllEnvironments }.Concat(names).ToList();
        if (!options.SequenceEqual(EnvironmentOptions)) { EnvironmentOptions.Clear(); foreach (var o in options) EnvironmentOptions.Add(o); }
        if (!options.Contains(EnvironmentChoice)) EnvironmentChoice = AllEnvironments;
        OnPropertyChanged(nameof(HasEnvironmentChoice));
    }
    [ObservableProperty] private string _runSummaryText = "No results yet";
    [ObservableProperty] private FailureViewModel? _selectedFailure;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasRowResult))]
    private string _rowResultHeadline = string.Empty;

    [ObservableProperty] private string _rowResultColor = "#8EA4BC";

    public bool HasRowResult => RowResultHeadline.Length > 0;
    public bool HasResults => _results is not null;

    /// <summary>Asks the view to scroll a grid column into view (cell index as in <see cref="DataRowViewModel.Cells"/>).</summary>
    public event EventHandler<int>? RevealColumnRequested;

    /// <summary>Shows the outcome of a run (or clears it with null).</summary>
    public void ApplyResults(RunResults? results)
    {
        _results = results;
        OnPropertyChanged(nameof(HasResults));
        RefreshEnvironmentOptions();
        AssignOutcomes();
        UpdateTestcaseResults();
        if (ShowFailedOnly || EnvironmentPicked) ApplyFilter(); // otherwise the rows stay as they are, so selection and scroll position survive a refresh
        RefreshRowFailures();
        if (ShowFailedCells) RefreshFailedCells();
        if (ShowFailedColumns || ShowBugColumns) RefreshFailedColumns();
    }

    private void AssignOutcomes()
    {
        foreach (var row in _allRows)
        {
            row.KnownBugCheck = IsKnownBug;
            row.PathText = DisplayPath;
            row.Outcome = _results?.Find(row.TestcaseIndex, row.CurrentDataId);
        }
        UpdateRunSummary();   // also after Undo / Redo, which rebuild the rows
    }

    /// <summary>"Last run …: N passed, M failed, K known bugs": a row whose only failures are tagged known bugs counts as a known bug, not a failure.</summary>
    private void UpdateRunSummary()
    {
        if (_results is null) { RunSummaryText = "No results yet"; return; }
        var passed = 0; var failed = 0; var bugs = 0; var failedCells = 0; var bugCells = 0;
        var rows = new Dictionary<(int, string), DataRowViewModel>();
        foreach (var r in _allRows) rows[(r.TestcaseIndex, r.CurrentDataId.Trim().ToUpperInvariant())] = r;
        foreach (var (key, outcome) in _results.Rows)
        {
            if (outcome.Passed) { passed++; continue; }
            if (!rows.TryGetValue(key, out var row)) { failed++; continue; }
            if (row.OnlyKnownBugs) bugs++; else failed++;
            failedCells += row.FailedCount; bugCells += row.BugCount;
        }
        RunSummaryText = $"Last run {_results.WhenLocal.ToString("d MMM HH:mm", CultureInfo.CurrentCulture)}: {passed} passed, {failed} failed" + (bugs > 0 ? $", {bugs} known {(bugs == 1 ? "bug" : "bugs")}" : string.Empty)
            + (failedCells + bugCells > 0 ? $" ({failedCells} failed {(failedCells == 1 ? "cell" : "cells")}, {bugCells} bug {(bugCells == 1 ? "cell" : "cells")})" : string.Empty);
    }

    private void UpdateTestcaseResults()
    {
        foreach (var testcase in Testcases)
        {
            var rows = _results?.Rows.Where(r => r.Key.Testcase == testcase.Index).Select(r => r.Value).ToList() ?? [];
            testcase.SetResults(rows.Count(r => r.Passed), rows.Count(r => !r.Passed));
        }
    }

    partial void OnShowFailedOnlyChanged(bool value) => ApplyFilter();

    partial void OnSelectedRowChanged(DataRowViewModel? value) { RefreshRowFailures(); RefreshPreview(); if (ShowRecord) BuildRecord(); }

    partial void OnSelectedFailureChanged(FailureViewModel? value)
    {
        if (value is null) return;
        // Show the request that was sent for the failing step, so the cause can be read next to the failure.
        if (PreviewStepNames.FirstOrDefault(n => string.Equals(n, value.Detail.StepName, StringComparison.OrdinalIgnoreCase)) is { } step)
        {
            PreviewStepName = step;
            PreviewVisible = true;
        }
        if (value.CellIndex < 1) return;
        CurrentCellIndex = value.CellIndex;
        RevealColumnRequested?.Invoke(this, value.CellIndex);
    }

    private void RefreshRowFailures()
    {
        RowFailures.Clear();
        SelectedFailure = null;
        var outcome = SelectedRow?.Outcome;
        if (outcome is null) { RowResultHeadline = string.Empty; return; }

        foreach (var failure in FailuresOf(outcome).Where(f => !IsKnownBug(SelectedRow!, f))) RowFailures.Add(DescribeFailure(failure));
        var problems = outcome.Problems;
        if (SelectedRow!.OnlyKnownBugs)
        {
            RowResultHeadline = $"Known {(problems == 1 ? "bug" : "bugs")} · {problems} tagged, nothing else failed · {outcome.Requests} {(outcome.Requests == 1 ? "request" : "requests")}";
            RowResultColor = "#F6A623";
            return;
        }
        RowResultHeadline = outcome.Passed
            ? $"Passed · {outcome.Requests} {(outcome.Requests == 1 ? "request" : "requests")} · slowest {outcome.MaxDurationMs} ms"
            : $"Failed · {problems} {(problems == 1 ? "problem" : "problems")} in {outcome.Requests} {(outcome.Requests == 1 ? "request" : "requests")}";
        RowResultColor = outcome.Passed ? "#39D39B" : "#FF7588";
    }

    private FailureViewModel DescribeFailure(FailureDetail failure)
    {
        var environment = failure.Environment.Length > 0 ? $" [{failure.Environment}]" : string.Empty;
        if (!failure.IsAssertion)
            return new FailureViewModel(failure, FindStepColumn(failure.StepName), failure.StepName + environment, failure.Message);

        var text = $"{failure.Verb}: expected \"{failure.Expected}\", got \"{failure.Actual}\"";
        if (failure.Message.Length > 0 && !text.Contains(failure.Message, StringComparison.OrdinalIgnoreCase)) text += $" — {failure.Message}";
        var wanted = $"{failure.StepName}.{failure.ResponsePath}";
        var column = Columns.FirstOrDefault(c => c.Group == ColumnGroup.Response && string.Equals(StripVerb(c.Title), wanted, StringComparison.OrdinalIgnoreCase));
        return new FailureViewModel(failure, column?.CellIndex ?? -1, wanted + environment, text);
    }

    private int FindStepColumn(string step)
        => Columns.FirstOrDefault(c => c.Group == ColumnGroup.Request && string.Equals(RequestStepPrefix(c.Title), step, StringComparison.OrdinalIgnoreCase))?.CellIndex ?? -1;
}
