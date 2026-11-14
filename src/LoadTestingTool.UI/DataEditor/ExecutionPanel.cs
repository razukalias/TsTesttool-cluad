using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The row-execution boxes for one scope (global or one instance's overrides). Built in code so the same panel serves both flyouts.
/// Boxes carry Tag "exec" so the tree's rename handlers leave them alone.
/// </summary>
public sealed class ExecutionPanel : UserControl
{
    public ExecutionPanel() { DataContextChanged += (_, _) => Build(); }

    public TextBox? MaxRowsBox { get; private set; }
    public TextBox? IterationsBox { get; private set; }
    public TextBox? StartIntervalBox { get; private set; }
    public TextBox? IterationIntervalBox { get; private set; }
    public ComboBox? ModeBox { get; private set; }

    private void Build()
    {
        if (DataContext is not ExecutionSettings vm) { Content = null; return; }
        var global = vm.IsGlobal;
        var stack = new StackPanel { Spacing = 6, Width = 300 };
        stack.Children.Add(new TextBlock { Text = global ? "ROW EXECUTION · ALL INSTANCES" : $"ROW EXECUTION · {vm.Instance}", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#8EA4BC")) });

        ModeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "exec" };
        var items = global ? new[] { "parallel", "sequential" } : new[] { "(use global)", "parallel", "sequential" };
        foreach (var item in items) ModeBox.Items.Add(item);
        ModeBox.SelectedItem = vm.Mode.Length == 0 ? "(use global)" : vm.Mode;
        ModeBox.SelectionChanged += (_, _) =>
        {
            var picked = ModeBox.SelectedItem as string ?? string.Empty;
            var text = picked == "(use global)" ? string.Empty : picked;
            if (text == vm.Mode) return;
            vm.Mode = text;
            vm.Commit(ExecutionKeys.Mode);
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExecutionSettings.Mode)) ModeBox.SelectedItem = vm.Mode.Length == 0 ? "(use global)" : vm.Mode;
        };
        stack.Children.Add(Row("Rows run", ModeBox, global ? null : () => vm.InheritedMode, "parallel = several rows/iterations at once; sequential = one after another"));

        MaxRowsBox = Box(vm, ExecutionKeys.MaxRows, nameof(ExecutionSettings.MaxRows), nameof(ExecutionSettings.ParallelFieldsEnabled), true, global ? null : vm.InheritedMaxRows);
        stack.Children.Add(Row("Max parallel rows", MaxRowsBox, null, "How many row/iteration executions run at the same time (1–100). Only used in parallel mode; it is not multiplied by Iterations."));

        IterationsBox = Box(vm, ExecutionKeys.Iterations, nameof(ExecutionSettings.Iterations), null, false, global ? null : vm.InheritedIterations);
        stack.Children.Add(Row("Iterations", IterationsBox, null, "How many times each selected row runs. Adds work, not concurrency."));

        StartIntervalBox = Box(vm, ExecutionKeys.StartInterval, nameof(ExecutionSettings.StartInterval), nameof(ExecutionSettings.ParallelFieldsEnabled), true, global ? null : vm.InheritedStartInterval);
        stack.Children.Add(Row("Row start interval (ms)", StartIntervalBox, null, "Staggers the first start of each parallel worker (worker 2 starts one interval after worker 1, …). Only used in parallel mode."));

        IterationIntervalBox = Box(vm, ExecutionKeys.IterationInterval, nameof(ExecutionSettings.IterationInterval), nameof(ExecutionSettings.IterationIntervalEnabled), true, global ? null : vm.InheritedIterationInterval);
        stack.Children.Add(Row("Iteration interval (ms)", IterationIntervalBox, null, "Wait between the iterations of a row. Only used in sequential mode."));

        stack.Children.Add(new TextBlock { Text = vm.Hint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#6F87A1")) });
        Content = stack;
    }

    private static TextBox Box(ExecutionSettings vm, string key, string property, string? enabledProperty, bool numeric, string? inherited)
    {
        var box = new TextBox { Tag = "exec", MinHeight = 28, Padding = new Thickness(6, 3), Watermark = string.IsNullOrEmpty(inherited) ? null : $"{inherited} (global)" };
        box.Bind(TextBox.TextProperty, new Binding(property, BindingMode.TwoWay) { Source = vm, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        if (enabledProperty is not null) box.Bind(IsEnabledProperty, new Binding(enabledProperty) { Source = vm });
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { vm.Commit(key); e.Handled = true; } };
        box.LostFocus += (_, _) => vm.Commit(key);
        return box;
    }

    private static Control Row(string label, Control input, Func<string>? _, string tip)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        ToolTip.SetTip(grid, tip);
        return grid;
    }
}
