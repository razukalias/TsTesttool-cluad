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
        RunSummaryText = results is null
            ? "No results yet"
            : $"Last run {results.WhenLocal.ToString("d MMM HH:mm", CultureInfo.CurrentCulture)}: {results.PassedRows} passed, {results.FailedRows} failed";
        OnPropertyChanged(nameof(HasResults));
        AssignOutcomes();
        UpdateTestcaseResults();
        if (ShowFailedOnly) ApplyFilter(); // otherwise the rows stay as they are, so selection and scroll position survive a refresh
        RefreshRowFailures();
    }

    private void AssignOutcomes()
    {
        foreach (var row in _allRows) row.Outcome = _results?.Find(row.TestcaseIndex, row.CurrentDataId);
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

    partial void OnSelectedRowChanged(DataRowViewModel? value) { RefreshRowFailures(); RefreshPreview(); }

    partial void OnSelectedFailureChanged(FailureViewModel? value)
    {
        if (value is null || value.CellIndex < 1) return;
        CurrentCellIndex = value.CellIndex;
        RevealColumnRequested?.Invoke(this, value.CellIndex);
    }

    private void RefreshRowFailures()
    {
        RowFailures.Clear();
        SelectedFailure = null;
        var outcome = SelectedRow?.Outcome;
        if (outcome is null) { RowResultHeadline = string.Empty; return; }

        foreach (var failure in outcome.Failures) RowFailures.Add(DescribeFailure(failure));
        var problems = outcome.Problems;
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
