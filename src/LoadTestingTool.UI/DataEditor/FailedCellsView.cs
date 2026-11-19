using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// "Failed cells only": one line per failed expected-value cell of the rows shown, with its header, what was expected and what the run got.
/// The lines are virtualized, so a run with thousands of failed cells opens at once.
/// </summary>
public sealed class FailedCellsView : UserControl
{
    private readonly DataEditorViewModel _editor;
    private readonly TextBlock _summary = new() { Tag = "failed-cells-summary", Foreground = Brush.Parse("#8EA4BC"), FontSize = 12, Margin = new Thickness(12, 12, 12, 6) };
    private readonly ListBox _list;

    /// <summary>Opens the ✦ wizard for a failed cell (set by the window, which owns the wizard).</summary>
    public Action<FailedCell, Control>? OpenWizard { get; set; }

    public FailedCellsView(DataEditorViewModel editor)
    {
        _editor = editor;
        _list = new ListBox
        {
            Tag = "failed-cells-list", Margin = new Thickness(12, 0), Background = Brushes.Transparent, SelectionMode = SelectionMode.Single,
            ItemsSource = editor.FailedCells, ItemTemplate = new FuncDataTemplate<FailedCell>((cell, _) => cell is null ? null : Line(cell)),
        };
        var panel = new DockPanel();
        DockPanel.SetDock(_summary, Dock.Top);
        panel.Children.Add(_summary);
        panel.Children.Add(_list);
        Content = panel;
        editor.FailedCells.CollectionChanged += (_, _) => Summarize();
        Summarize();
    }

    private void Summarize()
    {
        var cells = _editor.FailedCells;
        _summary.Text = !_editor.HasResults ? "There are no results yet. Run the rows first."
            : cells.Count == 0 ? "No failed cells in the rows shown."
            : $"{cells.Count} failed {(cells.Count == 1 ? "cell" : "cells")} in {cells.Select(c => c.Row).Distinct().Count()} {(cells.Select(c => c.Row).Distinct().Count() == 1 ? "row" : "rows")}. (latest run).";
    }

    private Control Line(FailedCell cell)
    {
        var stack = new StackPanel();
        if (cell.Environment.Length > 0) stack.Children.Add(new TextBlock { Text = $"Failed in: {cell.Environment}", FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), Margin = new Thickness(80, 4, 0, 0), Tag = "failed-cell-environments" });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,*,*,76"), Margin = new Thickness(0, 1) };
        void Put(Control control, int column) { Grid.SetColumn(control, column); grid.Children.Add(control); }
        Put(new TextBlock { Text = cell.DataId, VerticalAlignment = VerticalAlignment.Center }, 0);
        Put(new TextBlock { Text = cell.Header, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, 1);
        // The expected value is edited in place: the box writes to the same cell as the table (so Undo works), the ✦ opens the wizard
        var value = new TextBox { Tag = "failed-cell-value", MinHeight = 28, Padding = new Thickness(6, 2), VerticalContentAlignment = VerticalAlignment.Center, DataContext = cell.Row, Foreground = Brush.Parse("#FF7588") };
        value.Bind(TextBox.TextProperty, new Avalonia.Data.Binding($"Cells[{cell.CellIndex}]") { Mode = Avalonia.Data.BindingMode.TwoWay });
        var wizard = new Button { Content = "✦", Tag = "failed-cell-wizard", Width = 28, Height = 28, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0) };
        ToolTip.SetTip(wizard, "Open the wizard for this cell: verbs, value parts, known-bug tag");
        wizard.Click += (_, _) => OpenWizard?.Invoke(cell, wizard);
        var expected = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 8, 0) };
        expected.Children.Add(value);
        Grid.SetColumn(wizard, 1); expected.Children.Add(wizard);
        Put(expected, 2);
        Put(new TextBlock { Text = $"got {cell.Actual}", Foreground = Brush.Parse("#39D39B"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, 3);
        var assign = new Button { Content = "Assign", Tag = "failed-cell-assign", IsEnabled = cell.CanAssign, MinHeight = 28, Padding = new Thickness(12, 0) };
        ToolTip.SetTip(assign, cell.CanAssign ? $"Write {cell.Actual} into this cell (can be undone)" : "Nothing to assign: the run returned no value, or the cell holds a value from another cell or a generated value");
        assign.Click += (_, _) => { var failure = _editor.FailureAt(cell.Row, cell.CellIndex); if (failure is not null) _editor.AssignActual(cell.Row, cell.CellIndex, failure); };
        Put(assign, 4);
        stack.Children.Add(grid);
        return stack;
    }
}
