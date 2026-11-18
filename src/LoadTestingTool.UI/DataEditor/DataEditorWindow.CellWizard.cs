using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace LoadTestingTool.UI.DataEditor;

public sealed partial class DataEditorWindow
{
    /// <summary>
    /// The small button on the current table cell: shows the Record view's line for this cell (chips, the + menu, the dontrun step picker, help and warnings)
    /// in a popup next to it. The value is written the same way the Record view writes it; when rows are selected, the result goes to all of them.
    /// </summary>
    private void ShowCellWizard(DataGridCell cell, Control anchor, GridColumn column)
    {
        if (_shell.Current is not { } editor || cell.DataContext is not DataRowViewModel row) return;
        var grid = this.FindControl<DataGrid>("Grid");
        var expected = column.Group == ColumnGroup.Response;
        var dontRun = !expected && column.Title.Equals("dontrun", StringComparison.OrdinalIgnoreCase);
        var line = new RecordLineViewModel(row, column.CellIndex, column.Title, expected, dontRun ? editor.DontRunWarnings : editor.LineWarnings);
        var before = row.Cells[column.CellIndex] ?? string.Empty;
        var others = grid?.SelectedItems.OfType<DataRowViewModel>().Where(r => !ReferenceEquals(r, row)).ToList() ?? [];
        var content = new StackPanel { Spacing = 6, MinWidth = 520, MaxWidth = 720 };
        if (others.Count > 0)
            content.Children.Add(new TextBlock { Text = $"The result is written to this row and the {others.Count} other selected {(others.Count == 1 ? "row" : "rows")}.", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC"), Tag = "cell-wizard-note" });
        content.Children.Add(new RecordLineView(line, editor));
        var flyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Closed += (_, _) =>
        {
            var after = row.Cells[column.CellIndex] ?? string.Empty;
            if (after == before) return;
            foreach (var other in others) if ((other.Cells[column.CellIndex] ?? string.Empty) != after) other.Cells[column.CellIndex] = after;
        };
        flyout.ShowAt(anchor);
    }

    /// <summary>The same popup for a cell of one of the other sheets: the value as chips, and which request cells refer to it.</summary>
    private void ShowSheetCellWizard(DataGridCell cell, Control anchor, int cellIndex)
    {
        if (_shell.Current is not { IsOtherSheet: true } editor || cell.DataContext is not SheetRowViewModel row) return;
        var grid = this.FindControl<DataGrid>("SheetGrid");
        var line = new RecordLineViewModel(row, cellIndex, editor.SheetHeaders[cellIndex], expected: false);
        var before = row.Cells[cellIndex] ?? string.Empty;
        var others = grid?.SelectedItems.OfType<SheetRowViewModel>().Where(r => !ReferenceEquals(r, row) && r.Editable).ToList() ?? [];
        var content = new StackPanel { Spacing = 6, MinWidth = 520, MaxWidth = 720 };
        if (others.Count > 0)
            content.Children.Add(new TextBlock { Text = $"The result is written to this row and the {others.Count} other selected {(others.Count == 1 ? "row" : "rows")}.", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC"), Tag = "cell-wizard-note" });
        content.Children.Add(new RecordLineView(line, editor));
        var used = editor.CellUsedBy(editor.SheetHeaders[cellIndex], row);
        content.Children.Add(new TextBlock
        {
            Tag = "cell-used-by", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC"), TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = used.Count == 0 ? "No request cell refers to this cell (as of the last save)." : $"Used by {used.Count} {(used.Count == 1 ? "cell" : "cells")} (as of the last save): " + string.Join(", ", used),
        });
        var flyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Closed += (_, _) =>
        {
            var after = row.Cells[cellIndex] ?? string.Empty;
            if (after == before) return;
            foreach (var other in others) if ((other.Cells[cellIndex] ?? string.Empty) != after) other.Cells[cellIndex] = after;
        };
        flyout.ShowAt(anchor);
    }
}
