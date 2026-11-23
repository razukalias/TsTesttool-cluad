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
        content.Children.Add(BugBar(editor, cell));
        var ok = new Button { Content = "OK", Classes = { "primary" }, Tag = "cell-wizard-ok", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => flyout.Hide();
        content.Children.Add(ok);
        flyout.Closed += (_, _) => changed?.Invoke();
        flyout.ShowAt(anchor);
        return flyout;
    }

    /// <summary>"Known bug in …": writes (or removes) the <c>&lt;bug: environment; description&gt;</c> tag in the new column's cell text, like the bar on a table cell.</summary>
    private static Control BugBar(DataEditorViewModel editor, Cell cell)
    {
        var choices = new List<string> { "all" };
        choices.AddRange(editor.BugEnvironmentChoices);
        var environment = new ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 110, Tag = "bug-environment" };
        var description = new TextBox { Watermark = "what is wrong (optional)", MinWidth = 180, Tag = "bug-description" };
        var tag = new Button { Content = "⚑  Tag as known bug", Tag = "bug-tag" };
        ToolTip.SetTip(tag, "Writes <bug: environment; description> into this cell. A failing assertion of the column then counts as a known bug in that environment, not as a failure");
        var remove = new Button { Content = "Remove tag", Tag = "bug-untag", IsEnabled = HasTag(cell) };
        tag.Click += (_, _) =>
        {
            var wanted = environment.SelectedItem as string ?? "all";
            var text = (description.Text ?? string.Empty).Trim().Replace(';', ',').Replace('>', ')');
            var current = LoadTestingTool.Execution.BugTag.Without(cell.Cells[0] ?? string.Empty);
            cell.Cells[0] = (current.Length > 0 ? current + " " : string.Empty) + $"<bug: {wanted}" + (text.Length > 0 ? $"; {text}" : string.Empty) + ">";
            remove.IsEnabled = true;
        };
        remove.Click += (_, _) => { cell.Cells[0] = LoadTestingTool.Execution.BugTag.Without(cell.Cells[0] ?? string.Empty); remove.IsEnabled = false; };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Tag = "bug-bar",
            Children = { new TextBlock { Text = "Known bug in", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush.Parse("#8EA4BC") }, environment, description, tag, remove },
        };
    }

    private static bool HasTag(Cell cell) => (cell.Cells[0] ?? string.Empty).Contains("<bug", StringComparison.OrdinalIgnoreCase);

    public static Button Button(Action<Control> open)
    {
        var button = new Button { Content = "✦", Tag = "result-wizard", Width = 24, Height = 24, Padding = new Avalonia.Thickness(0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(button, "Open the value wizard for this column (condition, chips, + menu, bug and alternatives), the same as on a table cell");
        button.Click += (_, _) => open(button);
        return button;
    }
}
