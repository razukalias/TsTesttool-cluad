using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace LoadTestingTool.UI.DataEditor;

public sealed partial class DataEditorWindow
{
    /// <summary>
    /// The small button on a table cell: shows the Record view's line for this cell (chips, the + menu, the dontrun step picker, help and warnings)
    /// in a popup next to it. The value is written the same way the Record view writes it; when rows are selected, the result goes to all of them.
    /// </summary>
    private void ShowCellWizard(DataGridCell cell, Control anchor, GridColumn column)
    {
        if (_shell.Current is not { } editor || cell.DataContext is not DataRowViewModel row) return;
        var expected = column.Group == ColumnGroup.Response;
        var dontRun = !expected && column.Title.Equals("dontrun", StringComparison.OrdinalIgnoreCase);
        var others = this.FindControl<DataGrid>("Grid")?.SelectedItems.OfType<DataRowViewModel>().Where(r => !ReferenceEquals(r, row)).Cast<ICellRow>().ToList() ?? [];
        Flyout? flyout = null;
        var extra = expected ? BugTagBar(editor, [row, .. others.OfType<DataRowViewModel>()], column.CellIndex, () => flyout?.Hide()) : null;
        flyout = ShowLineWizard(editor, anchor, row, others, column.CellIndex, column.Title, expected, editor.WarningsFor(expected, dontRun), "row", extra);
    }

    /// <summary>The ✦ of an entry in the "Failed cells only" list: the same wizard and bug bar as on the table cell, writing to the same cell.</summary>
    private void OpenFailedCellWizard(FailedCell failed, Control anchor)
    {
        if (_shell.Current is not { } editor) return;
        var column = editor.Columns.FirstOrDefault(c => c.CellIndex == failed.CellIndex);
        if (column is null) return;
        Flyout? flyout = null;
        var extra = BugTagBar(editor, [failed.Row], failed.CellIndex, () => flyout?.Hide());
        flyout = ShowLineWizard(editor, anchor, failed.Row, [], failed.CellIndex, column.Title, expected: true, editor.WarningsFor(true, false), "row", extra);
    }

    /// <summary>
    /// "Tag as known bug" for an expected-value cell, in the wizard and in the failed-cell popup, so a cell can be marked whether or not it failed.
    /// Environments: all, or one of the run's environments.
    /// </summary>
    private static Control BugTagBar(DataEditorViewModel editor, IReadOnlyList<DataRowViewModel> rows, int cell, Action close)
    {
        var choices = new List<string> { "all" };
        choices.AddRange(editor.BugEnvironmentChoices);
        var environment = new ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 110, Tag = "bug-environment" };
        var tag = new Button { Content = "⚑  Tag as known bug", Tag = "bug-tag" };
        ToolTip.SetTip(tag, "Writes <bug: environment; description> into this cell (and the other selected rows). A failing assertion of the cell then counts as a known bug in that environment, not as a failure");
        tag.Click += (_, _) => { close(); editor.TagAsBug(rows, cell, environment.SelectedItem as string ?? "all"); };
        var remove = new Button { Content = "Remove tag", Tag = "bug-untag", IsEnabled = rows.Any(r => (r.Cells[cell] ?? string.Empty).Contains("<bug", StringComparison.OrdinalIgnoreCase)) };
        remove.Click += (_, _) => { close(); editor.RemoveBugTag(rows, cell); };
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Tag = "bug-bar",
            Children = { new TextBlock { Text = "Known bug in", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC") }, environment, tag, remove },
        };
    }

    /// <summary>The same popup for a cell of one of the other sheets: the value as chips, and which request cells refer to it.</summary>
    private void ShowSheetCellWizard(DataGridCell cell, Control anchor, int cellIndex)
    {
        if (_shell.Current is not { IsOtherSheet: true } editor || cell.DataContext is not SheetRowViewModel row) return;
        var others = this.FindControl<DataGrid>("SheetGrid")?.SelectedItems.OfType<SheetRowViewModel>().Where(r => !ReferenceEquals(r, row) && r.Editable).Cast<ICellRow>().ToList() ?? [];
        var used = editor.CellUsedBy(editor.SheetHeaders[cellIndex], row);
        var note = new TextBlock
        {
            Tag = "cell-used-by", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC"), TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = used.Count == 0 ? "No request cell refers to this cell (as of the last save)." : $"Used by {used.Count} {(used.Count == 1 ? "cell" : "cells")} (as of the last save): " + string.Join(", ", used),
        };
        // no warnings: a bug tag means nothing on these sheets
        ShowLineWizard(editor, anchor, row, others, cellIndex, editor.SheetHeaders[cellIndex], expected: false, warn: null, "row", note);
    }

    /// <summary>The same popup for a cell of the step grid (a field of the step in the config sheet).</summary>
    private void ShowStepCellWizard(DataGridCell cell, Control anchor, GridColumn column)
    {
        if (_shell.Current is not { } editor || cell.DataContext is not StepRowViewModel row) return;
        var others = this.FindControl<DataGrid>("StepsGrid")?.SelectedItems.OfType<StepRowViewModel>().Where(r => !ReferenceEquals(r, row)).Cast<ICellRow>().ToList() ?? [];
        ShowLineWizard(editor, anchor, row, others, column.CellIndex, column.Title, expected: false, editor.LineWarnings, "step", extra: null);
    }

    private static Flyout ShowLineWizard(DataEditorViewModel editor, Control anchor, ICellRow row, List<ICellRow> others, int cellIndex, string title, bool expected,
        Func<IEnumerable<LoadTestingTool.Editing.ValuePart>, string?, List<string>>? warn, string noun, Control? extra,
        IReadOnlyList<(ICellRow Row, int Cell)>? targets = null)
    {
        var line = new RecordLineViewModel(row, cellIndex, title, expected, warn);
        var before = row.Cells[cellIndex] ?? string.Empty;
        var content = new StackPanel { Spacing = 6, MinWidth = 520, MaxWidth = 720 };
        var flyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedRight };
        var cancelled = false;
        if (targets is { Count: > 0 })
        {
            // Wizard for several selected cells: a form with a title, ✕ and Apply, so it is clear what it does and how to leave it
            content.MinWidth = 640; content.MaxWidth = 900;
            flyout.Placement = PlacementMode.Bottom;
            var close = new Button { Content = "✕", Tag = "cell-wizard-close", Padding = new Avalonia.Thickness(8, 2) };
            close.Click += (_, _) => { cancelled = true; row.Cells[cellIndex] = before; flyout.Hide(); };
            var header = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(new TextBlock { Text = $"Wizard for {targets.Count + 1} cells · {title}", FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            Avalonia.Controls.Grid.SetColumn(close, 1); header.Children.Add(close);
            content.Children.Add(header);
            content.Children.Add(new TextBlock { Text = $"Build the value once; Apply writes it to all {targets.Count + 1} selected cells.", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC"), Tag = "cell-wizard-note" });
        }
        if (others.Count > 0)
            content.Children.Add(new TextBlock { Text = $"The result is written to this {noun} and the {others.Count} other selected {(others.Count == 1 ? noun : noun + "s")}.", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC"), Tag = "cell-wizard-note" });
        content.Children.Add(new RecordLineView(line, editor));
        if (extra is not null) content.Children.Add(extra);
        if (targets is { Count: > 0 })
        {
            var cancel = new Button { Content = "Cancel", Tag = "cell-wizard-cancel" };
            cancel.Click += (_, _) => { cancelled = true; row.Cells[cellIndex] = before; flyout.Hide(); };
            var apply = new Button { Content = $"Apply to {targets.Count + 1} cells", Classes = { "primary" }, Tag = "cell-wizard-apply" };
            apply.Click += (_, _) => flyout.Hide();
            content.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, apply } });
        }
        flyout.Closed += (_, _) => CrashGuard.Guard(() =>
        {
            CrashGuard.Trace($"wizard closed (cell {cellIndex}{(cancelled ? ", cancelled" : "")})");
            if (cancelled) return;
            var after = row.Cells[cellIndex] ?? string.Empty;
            if (after == before) return;
            foreach (var other in others) if ((other.Cells[cellIndex] ?? string.Empty) != after) other.Cells[cellIndex] = after;
            if (targets is not null) foreach (var (target, cell) in targets) if ((target.Cells[cell] ?? string.Empty) != after) target.Cells[cell] = after;
        }, "the cell wizard");
        CrashGuard.Trace($"wizard opened (cell {cellIndex}, {title})");
        _openWizard?.Hide();
        _openWizard = flyout;
        flyout.Closed += (_, _) => { if (ReferenceEquals(_openWizard, flyout)) _openWizard = null; };
        flyout.ShowAt(anchor);
        return flyout;
    }

    /// <summary>The wizard that is open now. Moving to another cell closes it, and closing applies what it built (the same as clicking away).</summary>
    private static Flyout? _openWizard;

    /// <summary>The red mark on a failed expected-value cell: what the run expected and got, and a button that writes the actual value into the cell.</summary>
    private void ShowFailurePopup(DataGridCell cell, Control anchor, DataRowViewModel row, FailureDetail failure)
    {
        if (_shell.Current is not { } editor) return;
        var column = editor.Columns.FirstOrDefault(c => c.Group == ColumnGroup.Response && string.Equals(editor.HeaderWithoutVerb(c.Title), $"{failure.StepName}.{failure.ResponsePath}", StringComparison.OrdinalIgnoreCase));
        if (column is null) return;
        var small = Avalonia.Media.Brush.Parse("#8EA4BC");
        var content = new StackPanel { Spacing = 6, MinWidth = 300, MaxWidth = 480 };
        content.Children.Add(new TextBlock { Text = $"{column.Title} · Data ID {row.CurrentDataId}" + (failure.Environment.Length > 0 ? $" · {failure.Environment}" : string.Empty), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = $"{failure.Verb}: expected \"{failure.Expected}\"", Tag = "failure-expected" });
        content.Children.Add(new TextBlock { Text = $"the run got \"{failure.Actual}\"", Tag = "failure-actual" });
        if (failure.Message.Length > 0) content.Children.Add(new TextBlock { Text = failure.Message, FontSize = 11, Foreground = small, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var can = editor.CanAssign(row, column.CellIndex, failure);
        if (!can) content.Children.Add(new TextBlock { Text = failure.Actual is null or "<missing>" ? "The run returned no value for this field, so there is nothing to assign." : "This cell holds a value from another cell or a generated value, so it is not overwritten.", FontSize = 11, Foreground = small, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Tag = "failure-note" });
        var flyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedRight };
        var assign = new Button { Content = $"Assign {failure.Actual}", Classes = { "primary" }, IsEnabled = can, Tag = "failure-assign", MaxWidth = 260 };
        assign.Click += (_, _) => { flyout.Hide(); editor.AssignActual(row, column.CellIndex, failure); };
        var close = new Button { Content = "Close", Tag = "failure-close" };
        close.Click += (_, _) => flyout.Hide();
        content.Children.Add(BugTagBar(editor, [row], column.CellIndex, flyout.Hide));
        content.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { close, assign } });
        flyout.ShowAt(anchor);
    }

    /// <summary>Click on a ⚑ flag: which environments the bug tag covers and its comment; the comment can be changed here and the tag removed.</summary>
    private void ShowBugPopup(DataGridCell cell, Control anchor)
    {
        if (_shell.Current is not { } editor || cell.DataContext is not DataRowViewModel row || cell.Tag is not int index) return;
        var tag = LoadTestingTool.Execution.BugTag.Parse(row.Cells[index] ?? string.Empty).Tag;
        if (tag is null) return;
        var title = editor.Columns.FirstOrDefault(c => c.CellIndex == index)?.Title ?? string.Empty;
        var environments = string.Join(", ", tag.Environments);
        var comment = new TextBox { Text = tag.Description, Watermark = "What is the bug? (a ticket number, what is wrong)", AcceptsReturn = false, MinWidth = 320, Tag = "bug-comment" };
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var save = new Button { Content = "Save comment", Classes = { "primary" }, Tag = "bug-comment-save" };
        save.Click += (_, _) =>
        {
            var text = (comment.Text ?? string.Empty).Replace(';', ',').Replace('>', ')').Replace('<', '(').Trim();
            flyout.Hide();
            if (text != tag.Description) editor.TagCellsAsBug([(row, index)], environments, text);
        };
        var remove = new Button { Content = "Remove the bug tag", Tag = "bug-remove" };
        remove.Click += (_, _) => { flyout.Hide(); editor.RemoveBugTags([(row, index)]); };
        var content = new StackPanel { Spacing = 6, MinWidth = 340, MaxWidth = 520 };
        content.Children.Add(new TextBlock { Text = $"Known bug · {title} · Data ID {row.CurrentDataId}", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = $"Environments: {environments}", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC"), Tag = "bug-environments" });
        content.Children.Add(new TextBlock { Text = "A failing assertion of this cell does not fail the test; it is listed in the Bugs tab.", FontSize = 11, Foreground = Avalonia.Media.Brush.Parse("#8EA4BC"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        content.Children.Add(comment);
        content.Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { remove, save } });
        flyout.Content = content;
        flyout.ShowAt(anchor);
    }
}
