using Avalonia.Controls;
using Avalonia.Interactivity;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

public partial class DataEditorWindow
{
    /// <summary>Format menu: a colour, "Clear highlight", "Wrap" or "No wrap", applied to the chosen scope.</summary>
    private void OnFormatClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action } || _shell.Current is not { } editor) return;
        var targets = FormatTargets(editor);
        switch (action.Split(':'))
        {
            case ["fill", "none"]: editor.FormatCells(targets, f => f with { Fill = null }, "Cleared highlight"); break;
            case ["fill", var hex]: editor.FormatCells(targets, f => f with { Fill = hex }, "Highlighted"); break;
            case ["wrap", "on"]: editor.FormatCells(targets, f => f with { Wrap = true }, "Wrapped text"); break;
            case ["wrap", "off"]: editor.FormatCells(targets, f => f with { Wrap = false }, "Unwrapped text"); break;
        }
    }

    private List<(IFormattedRow Row, int Cell)> FormatTargets(DataEditorViewModel editor)
    {
        CommitPendingEdit();
        SyncCurrentColumn();
        var result = new List<(IFormattedRow, int)>();
        IReadOnlyList<IFormattedRow> all;
        IReadOnlyList<IFormattedRow> selected;
        IFormattedRow? current;
        int cellCount;
        if (editor.ShowSteps)
        {
            var grid = this.FindControl<DataGrid>("StepsGrid");
            all = editor.StepRows;
            selected = grid?.SelectedItems.OfType<StepRowViewModel>().ToList<IFormattedRow>() ?? [];
            current = editor.SelectedStep;
            cellCount = editor.StepColumns.Count;
        }
        else
        {
            var grid = this.FindControl<DataGrid>("Grid");
            all = editor.VisibleRows;
            selected = grid?.SelectedItems.OfType<DataRowViewModel>().ToList<IFormattedRow>() ?? [];
            current = editor.SelectedRow;
            cellCount = editor.Columns.Count;
        }
        var cell = editor.CurrentCellIndex;
        switch (editor.FormatScope)
        {
            case "Selected rows":
                foreach (var row in selected.Count > 0 ? selected : current is null ? [] : [current])
                    for (var c = 0; c <= cellCount; c++) result.Add((row, c));
                break;
            case "This column":
                if (cell >= 0) foreach (var row in all) result.Add((row, cell));
                break;
            default:
                if (cell >= 0 && current is not null) result.Add((current, cell));
                break;
        }
        return result;
    }

    /// <summary>Opens the large text box for the current cell.</summary>
    private void OnEditCellClick(object? sender, RoutedEventArgs e)
    {
        if (_shell.Current is not { } editor) return;
        CommitPendingEdit();
        SyncCurrentColumn();
        var cell = editor.CurrentCellIndex;
        if (cell < 0) { editor.ReportStatus("Click a cell first.", isError: false); return; }
        if (editor.ShowSteps)
        {
            if (editor.SelectedStep is not { } step) return;
            var title = cell == 0 ? "stepname" : editor.StepColumns[cell - 1].Title;
            editor.OpenCellEditor(step, cell, $"{step.CurrentName} · {title}", step.Cells[cell]);
        }
        else
        {
            if (editor.SelectedRow is not { } row) return;
            var title = cell == 0 ? "Data ID" : editor.Columns[cell - 1].Title;
            editor.OpenCellEditor(row, cell, $"{row.CurrentDataId} · {title}", row.Cells[cell]);
        }
    }
}
