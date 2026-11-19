using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

/// <summary>
/// A filter button ("Environment ▾") that opens a checklist with a search box. Nothing ticked means everything is shown.
/// The label says what is chosen; the choice is applied when the list closes, so a refresh of the view never closes the list while you tick.
/// </summary>
internal sealed class MultiFilter : Button
{
    private readonly string _name;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _options = [];
    private bool _pending;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly TextBox _search = new() { Watermark = "Search…", MinWidth = 220, Margin = new Thickness(0, 0, 0, 6) };

    public MultiFilter(string name)
    {
        _name = name;
        Tag = "filter-" + name.ToLowerInvariant();
        MinHeight = 32; Padding = new Thickness(10, 2);
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = Panel() };
        flyout.Closed += (_, _) => { if (_pending) { _pending = false; Changed?.Invoke(this, EventArgs.Empty); } };
        Flyout = flyout;
        _search.TextChanged += (_, _) => Rebuild();
        UpdateLabel();
    }

    /// <summary>What is ticked; empty means no restriction.</summary>
    public IReadOnlyCollection<string> Selected => _selected;
    public IReadOnlyList<string> Options => _options;
    public event EventHandler? Changed;

    public bool Matches(string value) => _selected.Count == 0 || _selected.Contains(value);

    /// <summary>Replaces the offered values; ticked values that are no longer offered are dropped.</summary>
    public void SetOptions(IEnumerable<string> options)
    {
        _options = options.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList();
        _selected.RemoveWhere(s => !_options.Contains(s, StringComparer.OrdinalIgnoreCase));
        Rebuild(); UpdateLabel();
    }

    /// <summary>Takes the restriction away (everything shown) and reports it.</summary>
    public void Clear(bool raise = true)
    {
        if (_selected.Count == 0) return;
        _selected.Clear(); Rebuild(); UpdateLabel();
        if (raise) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ticks exactly these values (used when another view hands its choice over).</summary>
    public void Set(IEnumerable<string> values)
    {
        _selected.Clear();
        foreach (var value in values) _selected.Add(value);
        Rebuild(); UpdateLabel();
    }

    public string Summary => _selected.Count == 0 ? string.Empty : $"{_name}: {string.Join(", ", _selected.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))}";

    private Control Panel()
    {
        var clear = new Button { Content = "Show all", Padding = new Thickness(8, 2), Tag = "filter-clear" };
        clear.Click += (_, _) => { if (_selected.Count > 0) { _selected.Clear(); _pending = true; Rebuild(); UpdateLabel(); } };
        var scroll = new ScrollViewer { Content = _list, MaxHeight = 260, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        return new StackPanel { Spacing = 4, MinWidth = 240, Children = { _search, scroll, clear } };
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var term = _search.Text?.Trim() ?? string.Empty;
        foreach (var option in _options.Where(o => term.Length == 0 || o.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            var value = option;
            var box = new CheckBox { Content = value, IsChecked = _selected.Contains(value), Margin = new Thickness(0, 1) };
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true) _selected.Add(value); else _selected.Remove(value);
                _pending = true; UpdateLabel();
            };
            _list.Children.Add(box);
        }
        if (_list.Children.Count == 0) _list.Children.Add(new TextBlock { Text = "Nothing to choose yet", Foreground = Brush.Parse("#8EA4BC"), FontSize = 12 });
    }

    private void UpdateLabel()
    {
        Content = _selected.Count switch { 0 => $"{_name} ▾", 1 => $"{_name}: {_selected.First()} ▾", var n => $"{_name} ▾ {n}" };
        FontWeight = _selected.Count > 0 ? FontWeight.SemiBold : FontWeight.Normal;
    }
}

/// <summary>The line of removable chips under a filter bar; empty (null) when no filter is active.</summary>
internal static class FilterChips
{
    public static Control? Build(IEnumerable<(string Text, Action Remove)> chips, Action clearAll)
    {
        var list = chips.ToList();
        if (list.Count == 0) return null;
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6), Tag = "filter-chips" };
        foreach (var (text, remove) in list)
        {
            var chip = new Button { Content = $"{(text.Length > 60 ? text[..57] + "…" : text)}  ✕", FontSize = 11, Padding = new Thickness(8, 1), MinHeight = 24, Margin = new Thickness(0, 0, 6, 4), Tag = "filter-chip" };
            chip.Click += (_, _) => remove();
            panel.Children.Add(chip);
        }
        var all = new Button { Content = "Clear all", FontSize = 11, Padding = new Thickness(8, 1), MinHeight = 24, Margin = new Thickness(0, 0, 6, 4), Tag = "filter-clear-all" };
        all.Click += (_, _) => clearAll();
        panel.Children.Add(all);
        return panel;
    }
}
