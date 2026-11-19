using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

// Tagging an expected-value cell as a known bug from the table, whether or not it failed in the latest run.
public sealed partial class DataEditorViewModel
{
    /// <summary>True for an expected-value column: only there does a known-bug tag mean something.</summary>
    public bool IsExpectedCell(int cell) => cell >= 1 && cell <= Columns.Count && Columns[cell - 1].Group == ColumnGroup.Response;

    /// <summary>The environments the "Tag as known bug" menu offers: every environment the shown run has, or none when it has none.</summary>
    public IReadOnlyList<string> BugEnvironmentChoices => EnvironmentOptions.Where(e => e != AllEnvironments).ToList();

    /// <summary>
    /// Writes <c>&lt;bug: environments; description&gt;</c> into the expected cell of each row (an existing tag is replaced). Without a description, a cell
    /// that failed in the latest run gets what the run got, so the Bugs tab says something. One undo step.
    /// </summary>
    public void TagAsBug(IReadOnlyList<DataRowViewModel> rows, int cell, string environments, string? description = null)
    {
        if (!IsExpectedCell(cell)) { ShowStatus("A bug tag only works on an expected-value cell. Click one in a response column.", isError: false); return; }
        if (rows.Count == 0) { ShowStatus("Click a cell or select rows first.", isError: false); return; }
        var wanted = string.IsNullOrWhiteSpace(environments) ? "all" : environments.Trim();
        Record($"tag {rows.Count} {(rows.Count == 1 ? "cell" : "cells")} as known bug");
        var changed = 0;
        foreach (var row in rows)
        {
            var current = BugTag.Without(row.Cells[cell] ?? string.Empty);
            var text = description ?? DescribeBug(row, cell);
            var tagged = (current.Length > 0 ? current + " " : string.Empty) + $"<bug: {wanted}" + (text.Length > 0 ? $"; {text}" : string.Empty) + ">";
            if (ApplyCell(row, cell, tagged) == CellResult.Changed) changed++;
        }
        foreach (var row in rows) row.RefreshStanding();   // a tag decides whether a failed row is a failure or a known bug
        UpdateRunSummary();
        FinishBulk(changed, $"Tagged {changed} {(changed == 1 ? "cell" : "cells")} as known bug ({wanted})", 0, 0);
        if (ShowFailedCells) RefreshFailedCells();
        if (ShowFailedColumns || ShowBugColumns) RefreshFailedColumns();
    }

    /// <summary>Takes the bug tag out of the expected cell of each row.</summary>
    public void RemoveBugTag(IReadOnlyList<DataRowViewModel> rows, int cell)
    {
        if (!IsExpectedCell(cell) || rows.Count == 0) return;
        Record($"remove bug tag from {rows.Count} {(rows.Count == 1 ? "cell" : "cells")}");
        var changed = rows.Count(row => ApplyCell(row, cell, BugTag.Without(row.Cells[cell] ?? string.Empty)) == CellResult.Changed);
        foreach (var row in rows) row.RefreshStanding();
        UpdateRunSummary();
        FinishBulk(changed, $"Removed the bug tag from {changed} {(changed == 1 ? "cell" : "cells")}", 0, 0);
    }

    private string DescribeBug(DataRowViewModel row, int cell)
    {
        if (row.Outcome is not { } outcome) return string.Empty;
        var failure = outcome.Failures.FirstOrDefault(f => f.IsAssertion && CellOfFailure(f) == cell);
        return failure is null ? string.Empty : $"got {failure.Actual}".Replace(';', ',').Replace('>', ')');
    }
}
