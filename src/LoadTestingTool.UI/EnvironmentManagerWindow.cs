using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

public sealed class EnvironmentManagerWindow : Window
{
    private readonly StackPanel _items = new() { Spacing = 6 };
    private readonly TextBox _name = new() { Watermark = "Environment name" };
    private readonly TextBlock _status = DashboardStyle.Text("Changes are saved to central appsettings.json.", 12, false, DashboardStyle.Muted);
    private readonly CheckBox _parallel = new() { Content = "Run environments in parallel", VerticalAlignment = VerticalAlignment.Center };
    private readonly List<string> _environments;
    private readonly Action<IReadOnlyList<string>, bool> _save;
    private readonly Action<IReadOnlyList<string>> _applyToAll;
    private readonly Dictionary<string, CheckBox> _checks = new(StringComparer.OrdinalIgnoreCase);

    public EnvironmentManagerWindow(
        IEnumerable<string> environments,
        bool runEnvironmentsParallel,
        Action<IReadOnlyList<string>, bool> save,
        Action<IReadOnlyList<string>> applyToAll)
    {
        _environments = environments
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _parallel.IsChecked = runEnvironmentsParallel;
        _save = save;
        _applyToAll = applyToAll;

        Title = "Manage global environments";
        Width = 620;
        Height = 680;
        MinWidth = 520;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var add = DashboardStyle.Action("＋ Add", Add, "primary");
        _name.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Add(); };
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _name, add } };
        add.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(add, 1);

        var selectAll = DashboardStyle.Action("Select all", () => SetAll(true));
        var clearAll = DashboardStyle.Action("Clear all", () => SetAll(false));
        var delete = DashboardStyle.Action("− Delete selected", DeleteSelected, "danger");
        var listActions = new StackPanel { Orientation = Orientation.Horizontal, Children = { selectAll, clearAll, delete } };

        var apply = DashboardStyle.Action("Apply checked environments to all instances", ApplyToAll, "primary");
        var saveButton = DashboardStyle.Action("Save changes", Save, "primary");
        var close = DashboardStyle.Action("Close", Close);
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { close, saveButton } };

        var description = DashboardStyle.Text(
            "Checked environments are used only when you apply them to all instances. Each instance can still change its own environment selection later.",
            12, false, DashboardStyle.Muted);
        description.TextWrapping = TextWrapping.Wrap;

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto"), Margin = new Thickness(20) };
        Grid.SetRow(description, 0); root.Children.Add(description);
        Grid.SetRow(addRow, 1); root.Children.Add(addRow);
        var environmentScroll = new ScrollViewer
        {
            Content = _items,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Grid.SetRow(environmentScroll, 2); root.Children.Add(environmentScroll);
        Grid.SetRow(listActions, 3); root.Children.Add(listActions);
        Grid.SetRow(_parallel, 4); root.Children.Add(_parallel);
        var bulk = new StackPanel { Spacing = 6, Children = { apply, _status } };
        Grid.SetRow(bulk, 5); root.Children.Add(bulk);
        Grid.SetRow(footerButtons, 6);
        root.Children.Add(footerButtons);
        Content = root;
        Refresh();
    }

    private IReadOnlyList<string> Checked()
        => _checks.Where(x => x.Value.IsChecked == true).Select(x => x.Key).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    private void Refresh()
    {
        _items.Children.Clear();
        _checks.Clear();
        foreach (var environment in _environments)
        {
            var check = new CheckBox
            {
                Content = DashboardStyle.Text(environment, 13),
                IsChecked = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = environment
            };
            _checks[environment] = check;
            _items.Children.Add(check);
        }
        _status.Text = $"{_environments.Count} global environment(s). Check environments to bulk-apply.";
        _status.Foreground = DashboardStyle.Muted;
    }

    private void SetAll(bool value)
    {
        foreach (var check in _checks.Values) check.IsChecked = value;
    }

    private void Add()
    {
        var name = _name.Text?.Trim() ?? string.Empty;
        if (name.Length == 0) { SetError("Enter an environment name."); return; }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(',', StringComparison.Ordinal) || name.Contains('|', StringComparison.Ordinal))
        { SetError("Use a simple name without path or selection separators."); return; }
        if (_environments.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)))
        { SetError($"Environment '{name}' already exists."); return; }
        _environments.Add(name);
        _environments.Sort(StringComparer.OrdinalIgnoreCase);
        _name.Text = string.Empty;
        Refresh();
        Save();
    }

    private void DeleteSelected()
    {
        var selected = _checks.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToList();
        if (selected.Count == 0) { SetError("Check one or more environments first."); return; }
        _environments.RemoveAll(x => selected.Contains(x, StringComparer.OrdinalIgnoreCase));
        Refresh();
        Save();
    }

    private void ApplyToAll()
    {
        var selected = Checked();
        if (selected.Count == 0) { SetError("Check at least one environment before applying to all instances."); return; }
        try
        {
            _applyToAll(selected);
            _status.Text = $"Applied {selected.Count} environment(s) to all instances.";
            _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { SetError($"Could not apply environments: {ex.Message}"); }
    }

    private void Save()
    {
        try
        {
            _save(_environments, _parallel.IsChecked == true);
            _status.Text = "Saved global environments and parallel execution setting.";
            _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { SetError($"Could not save global settings: {ex.Message}"); }
    }

    private void SetError(string message)
    {
        _status.Text = message;
        _status.Foreground = DashboardStyle.Red;
    }
}
