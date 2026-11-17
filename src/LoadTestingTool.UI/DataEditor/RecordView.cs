using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Record view: one data row, one line per column, the value as coloured chips with a [+] menu after the last one.</summary>
public sealed class RecordView : UserControl
{
    private readonly DataEditorViewModel _editor;
    private readonly StackPanel _body = new() { Spacing = 6, Margin = new Thickness(14) };

    public RecordView(DataEditorViewModel editor)
    {
        _editor = editor;
        Content = new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        editor.RequestLines.CollectionChanged += (_, _) => Rebuild();
        editor.ExpectedLines.CollectionChanged += (_, _) => Rebuild();
        editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DataEditorViewModel.RecordTitle)) Rebuild(); };
        Rebuild();
    }

    private bool _pending;

    private void Rebuild()
    {
        if (_pending) return;   // a rebuild fills the collections one line at a time; draw once afterwards
        _pending = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _pending = false; Draw(); });
    }

    public void Draw()
    {
        _body.Children.Clear();
        _body.Children.Add(new TextBlock { Text = _editor.RecordTitle, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        if (_editor.SelectedRow is null) return;
        Section("REQUEST", _editor.RequestLines);
        Section("EXPECTED RESPONSE", _editor.ExpectedLines);
    }

    private void Section(string title, IEnumerable<RecordLineViewModel> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        _body.Children.Add(new TextBlock { Text = title, Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 10, 0, 2) });
        foreach (var line in list) _body.Children.Add(new RecordLineView(line, _editor));
    }
}

/// <summary>One column of the row: header on the left, the value parts (with condition and [+]) on the right, raw text and an example below.</summary>
public sealed class RecordLineView : Border
{
    private static readonly IReadOnlyDictionary<ValuePartKind, (string Back, string Fore)> Colors = new Dictionary<ValuePartKind, (string, string)>
    {
        [ValuePartKind.Text] = ("#1C2E47", "#D6E2F2"), [ValuePartKind.RandomNumber] = ("#3A2A55", "#D9C4FF"), [ValuePartKind.Guid] = ("#4A3A1F", "#FFD58A"),
        [ValuePartKind.DateTime] = ("#1F4A47", "#8FE8DD"), [ValuePartKind.Reference] = ("#16335A", "#8CC1FF"), [ValuePartKind.File] = ("#46283A", "#FFA6D0"),
    };

    private readonly RecordLineViewModel _line;
    private readonly DataEditorViewModel _editor;
    private readonly WrapPanel _chips = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _raw = new() { FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 11, Foreground = Brush.Parse("#6F87A3"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _example = new() { FontSize = 11, Foreground = Brush.Parse("#6F87A3"), TextWrapping = TextWrapping.Wrap };

    public RecordLineView(RecordLineViewModel line, DataEditorViewModel editor)
    {
        _line = line;
        _editor = editor;
        Background = Brush.Parse("#13233A");
        BorderBrush = Brush.Parse("#22364F");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(12, 8);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*") };
        grid.Children.Add(new TextBlock { Text = line.Title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var right = new StackPanel { Spacing = 3 };
        Grid.SetColumn(right, 1);
        right.Children.Add(_chips);
        right.Children.Add(_raw);
        right.Children.Add(_example);
        grid.Children.Add(right);
        Child = grid;
        line.Parts.CollectionChanged += (_, _) => Render();
        line.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        _chips.Children.Clear();
        if (_line.HasCondition)
        {
            var box = new ComboBox { ItemsSource = RecordLineViewModel.ConditionChoices, SelectedItem = _line.ConditionWords, MinWidth = 150, Margin = new Thickness(0, 0, 8, 4), Tag = "condition" };
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is string words && words != _line.ConditionWords) _line.ConditionWords = words; };
            _chips.Children.Add(box);
        }
        if (_line.ShowsValue)
        {
            for (var i = 0; i < _line.Parts.Count; i++) _chips.Children.Add(Chip(i));
            _chips.Children.Add(AddButton());
        }
        var generated = _line.Parts.Any(p => p.Kind != ValuePartKind.Text);
        _raw.Text = _line.Raw.Length == 0 ? "(empty)" : "stored as: " + _line.Raw;
        _raw.IsVisible = generated || _line.Raw.Length == 0 || _line.HasCondition && _line.Raw.StartsWith('{');   // plain text needs no second copy
        _example.Text = generated ? "e.g. " + _line.Example : string.Empty;
        _example.IsVisible = generated;
    }

    private Control Chip(int index)
    {
        var part = _line.Parts[index];
        var (back, fore) = Colors[part.Kind];
        var button = new Button
        {
            Content = new TextBlock { Text = part.Kind == ValuePartKind.Text ? "“" + part.Label + "”" : part.Label, Foreground = Brush.Parse(fore), MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis },
            Background = Brush.Parse(back), Padding = new Thickness(10, 3), Margin = new Thickness(0, 0, 6, 4), MinHeight = 28, CornerRadius = new CornerRadius(14), Tag = "chip",
        };
        ToolTip.SetTip(button, part.Kind + ": click to change");
        button.Flyout = ChipFlyout(index, part);
        return button;
    }

    private Flyout ChipFlyout(int index, ValuePart part)
    {
        var box = new TextBox { Text = part.Raw, MinWidth = 320, AcceptsReturn = part.Kind == ValuePartKind.Text, Tag = "chip-edit" };
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        void Apply()
        {
            flyout.Hide();
            var text = box.Text ?? string.Empty;
            if (text == part.Raw) return;
            if (text.Length == 0) { _line.Remove(index); return; }
            var parsed = ValueParts.Parse(text);   // a changed chip may turn into several parts
            _line.Remove(index);
            for (var k = 0; k < parsed.Count; k++) InsertAt(index + k, parsed[k]);
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && !box.AcceptsReturn) { Apply(); e.Handled = true; } };
        var left = new Button { Content = "◀", Tag = "chip-left", IsEnabled = index > 0 };
        var right = new Button { Content = "▶", Tag = "chip-right", IsEnabled = index < _line.Parts.Count - 1 };
        var remove = new Button { Content = "✕ Remove", Tag = "chip-remove" };
        var ok = new Button { Content = "OK", Classes = { "primary" }, Tag = "chip-ok" };
        left.Click += (_, _) => { flyout.Hide(); _line.Move(index, -1); };
        right.Click += (_, _) => { flyout.Hide(); _line.Move(index, 1); };
        remove.Click += (_, _) => { flyout.Hide(); _line.Remove(index); };
        ok.Click += (_, _) => Apply();
        flyout.Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = part.Kind == ValuePartKind.Text ? "Text" : "As stored in the workbook", Foreground = Brush.Parse("#8EA4BC"), FontSize = 11 }, box, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { left, right, remove, ok } } } };
        flyout.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        return flyout;
    }

    private void InsertAt(int index, ValuePart part)
    {
        _line.Add(part);
        _line.Move(_line.Parts.Count - 1, index - (_line.Parts.Count - 1));
    }

    private Control AddButton()
    {
        var plus = new Button { Content = "＋", Tag = "add-part", MinHeight = 28, MinWidth = 32, Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 0) };
        ToolTip.SetTip(plus, "Add a part: text, a value from another cell, a random number, a GUID, a date or a file");
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var menu = new DrillDownMenu(RecordMenu.Root(_editor.MenuSources()), part => { flyout.Hide(); _line.Add(part); });
        flyout.Content = menu;
        flyout.Opened += (_, _) => menu.Reset();
        plus.Flyout = flyout;
        return plus;
    }
}

/// <summary>The [+] menu: one list at a time with a breadcrumb to go up and a filter box on every level.</summary>
public sealed class DrillDownMenu : UserControl
{
    private readonly IReadOnlyList<MenuNode> _root;
    private readonly Action<ValuePart> _picked;
    private readonly List<MenuNode> _path = [];
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly TextBox _filter = new() { Watermark = "Type to filter…", Tag = "menu-filter" };
    private readonly ListBox _list = new() { MaxHeight = 280, Tag = "menu-list" };
    private readonly TextBox _input = new() { Tag = "menu-input" };
    private readonly TextBlock _prompt = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11 };
    private readonly StackPanel _promptPanel = new() { Spacing = 6, IsVisible = false };
    private MenuNode? _asking;
    private IReadOnlyList<MenuNode> _level;

    public DrillDownMenu(IReadOnlyList<MenuNode> root, Action<ValuePart> picked)
    {
        _root = root;
        _level = root;
        _picked = picked;
        _list.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(MenuNode.Display));
        _list.DoubleTapped += (_, _) => Choose();
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Choose(); e.Handled = true; } };
        _filter.GetObservable(TextBox.TextProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<string?>(_ => Fill()));
        _filter.KeyDown += OnFilterKey;
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        var ok = new Button { Content = "Add", Classes = { "primary" }, Tag = "menu-input-ok" };
        ok.Click += (_, _) => Accept();
        _promptPanel.Children.Add(_prompt);
        _promptPanel.Children.Add(_input);
        _promptPanel.Children.Add(ok);
        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(_crumbs);
        panel.Children.Add(_filter);
        panel.Children.Add(_list);
        panel.Children.Add(_promptPanel);
        Content = panel;
        Reset();
    }

    /// <summary>The names of the lists from the first to the current, for the breadcrumb.</summary>
    public IReadOnlyList<string> Path => _path.Select(n => n.Title).ToList();
    public IReadOnlyList<MenuNode> Shown => (_list.ItemsSource as IEnumerable<MenuNode>)?.ToList() ?? [];

    public void Reset()
    {
        _path.Clear();
        _asking = null;
        _level = _root;
        _filter.Text = string.Empty;
        Fill();
        _filter.Focus();
    }

    private void Fill()
    {
        _asking = null;
        _promptPanel.IsVisible = false;
        _list.IsVisible = true; _filter.IsVisible = true;
        var shown = RecordMenu.Filter(_level, _filter.Text ?? string.Empty);
        _list.ItemsSource = shown;
        if (shown.Count > 0) _list.SelectedIndex = 0;
        _crumbs.Children.Clear();
        var home = new Button { Content = _path.Count == 0 ? "Add part" : "‹ Add part", Padding = new Thickness(4, 0), Background = Brushes.Transparent, Tag = "crumb" };
        home.Click += (_, _) => GoTo(0);
        _crumbs.Children.Add(home);
        for (var i = 0; i < _path.Count; i++)
        {
            var depth = i + 1;
            _crumbs.Children.Add(new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush.Parse("#8EA4BC") });
            var crumb = new Button { Content = _path[i].Title, Padding = new Thickness(4, 0), Background = Brushes.Transparent, Tag = "crumb" };
            crumb.Click += (_, _) => GoTo(depth);
            _crumbs.Children.Add(crumb);
        }
    }

    private void GoTo(int depth)
    {
        while (_path.Count > depth) _path.RemoveAt(_path.Count - 1);
        _level = _path.Count == 0 ? _root : _path[^1].Children!();
        _filter.Text = string.Empty;
        Fill();
        _filter.Focus();
    }

    private void OnFilterKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1); e.Handled = true; break;
            case Key.Up: _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = true; break;
            case Key.Enter: Choose(); e.Handled = true; break;
            case Key.Back when string.IsNullOrEmpty(_filter.Text) && _path.Count > 0: GoTo(_path.Count - 1); e.Handled = true; break;
        }
    }

    /// <summary>Picks the highlighted item: opens a folder, asks for text, or adds the part.</summary>
    public void Choose()
    {
        if (_list.SelectedItem is not MenuNode node) return;
        if (node.IsFolder) { _path.Add(node); _level = node.Children!(); _filter.Text = string.Empty; Fill(); _filter.Focus(); return; }
        if (node.Prompt is not null)
        {
            _asking = node;
            _prompt.Text = node.Prompt;
            _input.Text = string.Empty;
            _list.IsVisible = false; _filter.IsVisible = false; _promptPanel.IsVisible = true;
            _input.Focus();
            return;
        }
        if (node.Result is { } part) _picked(part);
    }

    private void Accept()
    {
        if (_asking?.FromInput?.Invoke(_input.Text ?? string.Empty) is { } part) _picked(part);
    }
}
