using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The ✦ wizard for a column that does not exist yet (in the "Template and response" window): the same value line the table cells open
/// (condition, chips, the + menu, OR / AND, warnings), writing to a one-cell stand-in. The text it builds becomes the cell of the new column.
/// </summary>
public static class NewColumnWizard
{
    /// <summary>A one-cell row: the expected value a new column will get for the row shown.</summary>
    public sealed class Cell(string value) : ICellRow
    {
        public string Initial { get; } = value;
        public System.Collections.ObjectModel.ObservableCollection<string> Cells { get; } = [value];

        /// <summary>The value was changed in the wizard, so it is written instead of the plain response value.</summary>
        public bool Edited => !string.Equals(Cells[0], Initial, StringComparison.Ordinal);
    }

    /// <param name="title">The header the column will have, with its verb: <c>{eq}Step.path</c>.</param>
    public static Flyout Open(DataEditorViewModel editor, Control anchor, Cell cell, string title, Action? changed = null)
    {
        var line = new RecordLineViewModel(cell, 0, title, expected: true, editor.WarningsFor(true, false));
        var before = cell.Cells[0];
        var content = new StackPanel { Spacing = 6, MinWidth = 520, MaxWidth = 720, Tag = "new-column-wizard" };
        var flyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedRight };
        var close = new Button { Content = "✕", Tag = "cell-wizard-close", Padding = new Avalonia.Thickness(8, 2), HorizontalAlignment = HorizontalAlignment.Right };
        ToolTip.SetTip(close, "Close without keeping the change");
        close.Click += (_, _) => { cell.Cells[0] = before; flyout.Hide(); };
        content.Children.Add(close);
        content.Children.Add(new TextBlock { Text = "The value the new column gets for the row shown (the other rows follow the \"Cells of the new columns\" choice).", FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), TextWrapping = TextWrapping.Wrap, Tag = "cell-wizard-note" });
        content.Children.Add(new RecordLineView(line, editor));
        var ok = new Button { Content = "OK", Classes = { "primary" }, Tag = "cell-wizard-ok", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => flyout.Hide();
        content.Children.Add(ok);
        flyout.Closed += (_, _) => changed?.Invoke();
        flyout.ShowAt(anchor);
        return flyout;
    }

    public static Button Button(Action<Control> open)
    {
        var button = new Button { Content = "✦", Tag = "result-wizard", Width = 24, Height = 24, Padding = new Avalonia.Thickness(0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, "Open the value wizard for this column (condition, chips, + menu, bug and alternatives), the same as on a table cell");
        button.Click += (_, _) => open(button);
        return button;
    }
}
