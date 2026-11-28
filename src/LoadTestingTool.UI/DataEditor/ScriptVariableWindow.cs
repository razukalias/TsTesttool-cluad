using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>"Add variable…" of a script: the C# name, and where the value comes from (a request column or a value of an earlier step). Result: the name and the placeholder.</summary>
public sealed class ScriptVariableWindow : Window
{
    private readonly TextBox _name = new() { Watermark = "C# name, e.g. orderId", Tag = "script-variable-name" };
    private readonly ComboBox _source = new() { ItemsSource = new[] { "Request column", "Value from a step" }, SelectedIndex = 0, Tag = "script-variable-source" };
    private readonly TextBox _filter = new() { Watermark = "Filter", Tag = "script-variable-filter" };
    private readonly ListBox _list = new() { MinHeight = 180, Tag = "script-variable-list" };
    private readonly TextBlock _error = new() { Foreground = Brush.Parse("#E5484D"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = "script-variable-error" };
    private readonly IReadOnlyList<string> _columns, _steps;
    private readonly string _code;

    public ScriptVariableWindow(IReadOnlyList<string> requestColumns, IReadOnlyList<string> stepValues, string code)
    {
        _columns = requestColumns; _steps = stepValues; _code = code;
        Title = "Add variable to the script"; Width = 480; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var add = new Button { Content = "Add", Classes = { "accent" }, Tag = "script-variable-add" };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => Choose();
        cancel.Click += (_, _) => Close(null);
        _list.DoubleTapped += (_, _) => Choose();
        _filter.TextChanged += (_, _) => Refill();
        _source.SelectionChanged += (_, _) => Refill();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, add } };
        var top = new StackPanel { Spacing = 6, Children =
        {
            new TextBlock { Text = "One line is added at the top of the script, e.g.  var orderId = \"_orderId_\";  (always a string; the run fills in the value).", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush.Parse("#8EA4BC") },
            new TextBlock { Text = "Name in the script" }, _name, new TextBlock { Text = "Value from" }, _source, _filter,
        } };
        var layout = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom); DockPanel.SetDock(_error, Dock.Bottom); DockPanel.SetDock(top, Dock.Top);
        layout.Children.Add(buttons); layout.Children.Add(_error); layout.Children.Add(top); layout.Children.Add(_list);
        Content = layout;
        Refill();
    }

    private void Refill()
    {
        var source = _source.SelectedIndex == 0 ? _columns : _steps;
        var filter = _filter.Text?.Trim() ?? string.Empty;
        _list.ItemsSource = source.Where(s => filter.Length == 0 || s.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void Choose()
    {
        _error.Text = ScriptVariableLines.CheckName(_name.Text, _code) ?? string.Empty;
        if (_error.Text.Length > 0) return;
        var placeholder = ScriptVariableLines.Placeholder(_list.SelectedItem as string, out var problem);
        if (placeholder is null) { _error.Text = problem ?? string.Empty; return; }
        Close((_name.Text!.Trim(), placeholder));
    }
}
