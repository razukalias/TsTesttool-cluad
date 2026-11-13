using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

internal sealed class DataIdSelectionWindow : Window
{
    private readonly Dictionary<int, List<CheckBox>> _checks = [];
    private readonly Action<string>? _log;

    public DataIdSelectionWindow(IReadOnlyDictionary<int, List<string>> options, string selection, Action<string>? log = null)
    {
        _log = log;
        Title = "Configure request data IDs";
        Width = 520;
        Height = 620;
        CanResize = true;
        var selected = Parse(selection);
        var content = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        content.Children.Add(new TextBlock { Text = "Choose complete request-sheet rows by testcase. If no IDs are selected, all data IDs will run.", TextWrapping = TextWrapping.Wrap });
        var groups = new StackPanel { Spacing = 10 };
        foreach (var group in options.OrderBy(x => x.Key))
        {
            var checks = new List<CheckBox>();
            var panel = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var id in group.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var check = new CheckBox { Content = id, IsChecked = selected.TryGetValue(group.Key, out var ids) && ids.Contains(id), Margin = new Thickness(4, 2) };
                checks.Add(check); panel.Children.Add(check);
            }
            _checks[group.Key] = checks;
            groups.Children.Add(new StackPanel { Children = { new TextBlock { Text = $"Testcase {group.Key}", FontWeight = Avalonia.Media.FontWeight.Bold }, panel } });
        }
        content.Children.Add(new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 470 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var all = new Button { Content = "Select all" }; all.Click += (_, _) => { _log?.Invoke("Data-ID dialog action: select all."); SetAll(true); };
        var none = new Button { Content = "Clear (run all)" }; none.Click += (_, _) => { _log?.Invoke("Data-ID dialog action: clear selection (run all)."); SetAll(false); };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => { _log?.Invoke("Data-ID dialog action: cancel."); Close(null); };
        var apply = new Button { Content = "Apply", IsDefault = true }; apply.Click += (_, _) => { var value = Serialize(); _log?.Invoke($"Data-ID dialog action: apply. selection={value}"); Close(value); };
        buttons.Children.Add(all); buttons.Children.Add(none); buttons.Children.Add(cancel); buttons.Children.Add(apply);
        content.Children.Add(buttons);
        Content = content;
    }

    private void SetAll(bool value) { foreach (var check in _checks.Values.SelectMany(x => x)) check.IsChecked = value; }
    private string Serialize() => string.Join(";", _checks.OrderBy(x => x.Key).Select(x => $"{x.Key}:{string.Join(',', x.Value.Where(c => c.IsChecked == true).Select(c => c.Content?.ToString()))}").Where(x => !x.EndsWith(":")));
    private static Dictionary<int, HashSet<string>> Parse(string value)
    {
        var result = new Dictionary<int, HashSet<string>>();
        foreach (var group in (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = group.Split(':', 2); if (parts.Length != 2 || !int.TryParse(parts[0], out var index)) continue;
            result[index] = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return result;
    }
}
