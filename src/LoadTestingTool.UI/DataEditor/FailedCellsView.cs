using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>"Failed cells only": one line per failed expected-value cell of the rows shown, with its header, what was expected and what the run got.</summary>
public sealed class FailedCellsView : UserControl
{
    private readonly DataEditorViewModel _editor;
    private readonly StackPanel _body = new() { Spacing = 4, Margin = new Thickness(12) };

    public FailedCellsView(DataEditorViewModel editor)
    {
        _editor = editor;
        Content = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        editor.FailedCells.CollectionChanged += (_, _) => Draw();
        Draw();
    }

    private void Draw()
    {
        _body.Children.Clear();
        var muted = Brush.Parse("#8EA4BC");
        _body.Children.Add(new TextBlock
        {
            Tag = "failed-cells-summary", Foreground = muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 6),
            Text = !_editor.HasResults ? "There are no results yet. Run the rows first."
                : _editor.FailedCells.Count == 0 ? "No failed cells in the rows shown."
                : $"{_editor.FailedCells.Count} failed {(_editor.FailedCells.Count == 1 ? "cell" : "cells")} in {_editor.FailedCells.Select(c => c.Row).Distinct().Count()} {(_editor.FailedCells.Select(c => c.Row).Distinct().Count() == 1 ? "row" : "rows")} (latest run).",
        });
        foreach (var cell in _editor.FailedCells)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,180,*,*,76"), Margin = new Thickness(0, 2) };
            void Put(Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }
            Put(new TextBlock { Text = cell.DataId, VerticalAlignment = VerticalAlignment.Center }, 0);
            Put(new TextBlock { Text = cell.Header + (cell.Environment.Length > 0 ? $" [{cell.Environment}]" : string.Empty), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, 1);
            Put(new TextBlock { Text = $"expected {cell.Expected}", Foreground = Brush.Parse("#FF7588"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, 2);
            Put(new TextBlock { Text = $"got {cell.Actual}", Foreground = Brush.Parse("#39D39B"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, 3);
            var assign = new Button { Content = "Assign", Tag = "failed-cell-assign", IsEnabled = cell.CanAssign, MinHeight = 28, Padding = new Thickness(12, 0) };
            ToolTip.SetTip(assign, cell.CanAssign ? $"Write {cell.Actual} into this cell (can be undone)" : "Nothing to assign: the run returned no value, or the cell holds a value from another cell or a generated value");
            var captured = cell;
            assign.Click += (_, _) => { var failure = _editor.FailureAt(captured.Row, captured.CellIndex); if (failure is not null) _editor.AssignActual(captured.Row, captured.CellIndex, failure); };
            Put(assign, 4);
            _body.Children.Add(grid);
        }
    }
}
