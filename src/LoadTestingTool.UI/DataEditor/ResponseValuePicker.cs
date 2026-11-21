using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// A small dialog that lists what other steps give (status code, response fields, headers) and returns the
/// <c>&lt;From_response_Step.path_r&gt;</c> text for the chosen one, for a condition or a loop's items.
/// </summary>
public sealed class ResponseValuePicker : Window
{
    private readonly TextBox _filter = new() { Watermark = "Filter (step or field name)", Tag = "picker-filter" };
    private readonly ListBox _list = new() { Tag = "picker-list", MinHeight = 200 };
    private readonly IReadOnlyList<string> _targets;
    private readonly TextBlock _note = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "picker-note" };

    public string? Chosen { get; private set; }

    public static string ReferenceFor(string target) => $"<From_response_{target}_r>";

    private Func<string, string> _format = ReferenceFor;

    public ResponseValuePicker(DataEditorViewModel editor, string? exceptStep) : this(editor.ReferenceTargets(exceptStep)) { }

    /// <summary>A picker over any list of names (data columns, loop variables…): <paramref name="format"/> turns the chosen name into the text that is inserted.</summary>
    public ResponseValuePicker(IReadOnlyList<string> targets, Func<string, string> format, string title, string note) : this(targets)
    {
        _format = format;
        Title = title;
        _note.Text = targets.Count == 0 ? "Nothing to choose from here yet." : note;
    }

    public ResponseValuePicker(IReadOnlyList<string> targets)
    {
        _targets = targets;
        Title = "Insert a value from another step";
        Width = 520;
        Height = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var insert = new Button { Content = "Insert", Classes = { "accent" }, IsEnabled = false, Tag = "picker-insert" };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        void Choose() { if (_list.SelectedItem is string target) { Chosen = _format(target); Close(Chosen); } }
        insert.Click += (_, _) => Choose();
        cancel.Click += (_, _) => Close(null);
        _list.SelectionChanged += (_, _) => insert.IsEnabled = _list.SelectedItem is string;
        _list.DoubleTapped += (_, _) => Choose();
        _filter.TextChanged += (_, _) => Refill();
        _note.Text = _targets.Count == 0
            ? "No other steps in this testcase yet."
            : "Status codes and the fields of each step's expected columns are always listed. Fields and headers of the last stored response are listed when the testcase has been run with response history on.";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, insert } };
        var layout = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_filter, Dock.Top);
        DockPanel.SetDock(_note, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(_note);
        layout.Children.Add(_filter);
        layout.Children.Add(_list);
        Content = layout;
        Refill();
    }

    /// <summary>The list as it stands for a filter text (for tests).</summary>
    public IReadOnlyList<string> Shown => _list.ItemsSource as IReadOnlyList<string> ?? [];

    private void Refill()
    {
        var text = _filter.Text?.Trim() ?? string.Empty;
        _list.ItemsSource = _targets.Where(t => text.Length == 0 || t.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
