namespace LoadTestingTool.UI.DataEditor;

// Paste, fill down and find & replace on the Steps tab. Step names are skipped: a rename is deliberate (it renames data
// columns and references too), so it only happens by editing the name cell itself.
public sealed partial class DataEditorViewModel
{
    private CellResult ApplyStepCell(StepRowViewModel step, int cellIndex, string value)
    {
        if (cellIndex < 1 || cellIndex > StepColumns.Count) return CellResult.Rejected;
        var column = StepColumns[cellIndex - 1];
        if (step.Row.GetAt(column.SheetColumn) == value) return CellResult.Unchanged;
        step.Row.SetAt(column.SheetColumn, value);
        step.SetCellSilently(cellIndex, value);
        return CellResult.Changed;
    }

    /// <summary>Pastes a block of text cells into the steps grid with its top-left corner at the given step and cell.</summary>
    public void PasteStepBlock(int startRow, int startCell, string[][] block)
    {
        if (block.Length == 0 || startRow < 0 || startRow >= StepRows.Count) return;
        Record($"paste {block.Length} {(block.Length == 1 ? "row" : "rows")} into steps");
        var changed = 0;
        var skipped = 0;
        for (var r = 0; r < block.Length; r++)
        {
            if (startRow + r >= StepRows.Count) { skipped += block[r].Length; continue; }
            var step = StepRows[startRow + r];
            for (var c = 0; c < block[r].Length; c++)
            {
                var cell = startCell + c;
                if (cell == 0 || cell > StepColumns.Count) { skipped++; continue; }
                switch (ApplyStepCell(step, cell, block[r][c]))
                {
                    case CellResult.Changed: changed++; break;
                    case CellResult.Rejected: skipped++; break;
                }
            }
        }
        FinishStepBulk(changed, $"Pasted {changed} {(changed == 1 ? "cell" : "cells")}", skipped, "skipped (step names, cells past the last column or the last step)");
    }

    /// <summary>Copies the first step's value in a column into the other selected steps, which must be in grid order.</summary>
    public void FillDownSteps(IReadOnlyList<StepRowViewModel> steps, int cellIndex)
    {
        if (steps.Count < 2) { ShowStatus("Select two or more steps (Ctrl+click or Shift+click), click the column, then press Ctrl+D.", isError: false); return; }
        if (cellIndex < 1 || cellIndex > StepColumns.Count) { ShowStatus("Click a cell in a step setting first. Step names cannot be filled.", isError: false); return; }
        Record($"fill down {StepColumns[cellIndex - 1].Title}");
        var value = steps[0].Cells[cellIndex];
        var changed = steps.Skip(1).Count(s => ApplyStepCell(s, cellIndex, value) == CellResult.Changed);
        FinishStepBulk(changed, $"Filled {StepColumns[cellIndex - 1].Title} down {changed} {(changed == 1 ? "step" : "steps")}", 0, string.Empty);
    }

    /// <summary>Sets the enabled cell of the named steps. A disabled step stays in the workbook and the runner skips it. One undo step.</summary>
    public void SetStepsEnabled(IReadOnlyList<string> names, bool enabled)
    {
        var steps = names.Select(n => StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, n, StringComparison.OrdinalIgnoreCase))).Where(r => r is not null).Select(r => r!).ToList();
        if (steps.Count == 0) return;
        var value = enabled ? "True" : "False";
        var cell = StepColumns.ToList().FindIndex(c => c.Title.Equals("Enabled", StringComparison.OrdinalIgnoreCase));
        Record($"{(enabled ? "enable" : "disable")} {(steps.Count == 1 ? "step " + steps[0].CurrentName : steps.Count + " steps")}");
        var changed = 0;
        foreach (var step in steps)
        {
            if (cell >= 0) { if (ApplyStepCell(step, cell + 1, value) == CellResult.Changed) changed++; }
            else { step.Row.Set("enabled", value); changed++; }
        }
        FinishStepBulk(changed, $"{(enabled ? "Enabled" : "Disabled")} {(changed == 1 ? "step " + steps[0].CurrentName : changed + " steps")}" + (enabled ? string.Empty : ". The runner skips it; Undo or Enable brings it back"), 0, string.Empty);
    }

    private void FinishStepBulk(int changed, string message, int skipped, string skippedNote)
    {
        if (changed == 0)
        {
            DiscardLastRecord();
            ShowStatus(skipped > 0 ? $"Nothing changed; {skipped} {skippedNote}." : "Nothing changed.", isError: skipped > 0);
            return;
        }
        StepsChanged();
        ShowStatus(skipped > 0 ? $"{message}; {skipped} {skippedNote}." : message + ".", isError: false);
    }
}
