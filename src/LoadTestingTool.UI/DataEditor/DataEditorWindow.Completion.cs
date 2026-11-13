using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

// Autocomplete while a cell is being edited: a list under the cell offers <functions>, <From_ sheets and headers>, and {assertion verbs}.
public partial class DataEditorWindow
{
    private Border? _completionPanel;
    private ListBox? _completionList;
    private TextBox? _completionBox;
    private Completion? _completionState;
    private CellCompletion? _completionEngine;
    private bool _completionVerbs;
    private bool _applyingCompletion;

    private void InitCompletion()
    {
        _completionList = new ListBox
        {
            MaxHeight = 220,
            MinWidth = 280,
            Focusable = false,
            Background = Brushes.Transparent,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<CompletionItem>((item, _) =>
            {
                var row = new StackPanel { Spacing = 1 };
                if (item is null) return row;
                row.Children.Add(new TextBlock { Text = item.Label, FontWeight = FontWeight.SemiBold, FontSize = 13 });
                row.Children.Add(new TextBlock { Text = item.Detail, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#8EA4BC")), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 });
                return row;
            }),
        };
        _completionList.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel); // keep focus in the cell
        _completionList.DoubleTapped += (_, _) => AcceptCompletion();
        _completionList.Tapped += (_, _) => AcceptCompletion();
        _completionPanel = new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.Parse("#12233A")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2F5F9B")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(2),
            ZIndex = 100,
            Child = _completionList,
        };
        if (Content is Grid root)
        {
            Avalonia.Controls.Grid.SetColumnSpan(_completionPanel, Math.Max(1, root.ColumnDefinitions.Count));
            root.Children.Add(_completionPanel);
        }
        if (this.FindControl<DataGrid>("Grid") is { } data) data.PreparingCellForEdit += (_, e) => AttachCompletion(e.EditingElement, allowVerbs: IsExpectedColumn(e.Column));
        if (this.FindControl<DataGrid>("StepsGrid") is { } steps) steps.PreparingCellForEdit += (_, e) => AttachCompletion(e.EditingElement, allowVerbs: false);
    }

    private bool IsExpectedColumn(DataGridColumn column)
        => column.Tag is int cell && cell > 0 && _shell.Current is { } editor && cell <= editor.Columns.Count && editor.Columns[cell - 1].Group == ColumnGroup.Response;

    private void AttachCompletion(Control? element, bool allowVerbs)
    {
        if (element is not TextBox box || _shell.Current is not { } editor) return;
        _completionEngine = editor.Completion;
        _completionVerbs = allowVerbs;
        box.TextChanged += (_, _) => ShowCompletion(box, force: false);
        box.AddHandler(KeyDownEvent, OnCompletionKeyDown, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) => HideCompletion();
        box.DetachedFromVisualTree += (_, _) => HideCompletion();
    }

    private void ShowCompletion(TextBox box, bool force)
    {
        if (_applyingCompletion || _completionEngine is null || _completionPanel is null || _completionList is null) return;
        var text = box.Text ?? string.Empty;
        var completion = _completionEngine.Suggest(text, box.CaretIndex, _completionVerbs);
        // An exact, finished match (the user typed "{eq" and nothing else can follow) still shows, so Enter can close the brace.
        if (completion is null) { HideCompletion(); return; }
        _completionBox = box;
        _completionState = completion;
        _completionList.ItemsSource = completion.Items;
        _completionList.SelectedIndex = 0;
        var point = box.TranslatePoint(new Point(0, box.Bounds.Height), this);
        if (point is null) { HideCompletion(); return; }
        _completionPanel.Margin = new Thickness(point.Value.X, point.Value.Y + 2, 0, 0);
        _completionPanel.IsVisible = true;
    }

    private void HideCompletion()
    {
        if (_completionPanel is not null) _completionPanel.IsVisible = false;
        _completionState = null;
    }

    private void OnCompletionKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { ShowCompletion(box, force: true); e.Handled = true; return; }
        if (_completionState is null || _completionList is null || _completionPanel is not { IsVisible: true }) return;
        var count = _completionState.Items.Count;
        switch (e.Key)
        {
            case Key.Down: _completionList.SelectedIndex = Math.Min(count - 1, _completionList.SelectedIndex + 1); e.Handled = true; break;
            case Key.Up: _completionList.SelectedIndex = Math.Max(0, _completionList.SelectedIndex - 1); e.Handled = true; break;
            case Key.Enter or Key.Tab: AcceptCompletion(); e.Handled = true; break;
            case Key.Escape: HideCompletion(); e.Handled = true; break; // closes the list; a second Escape cancels the edit
        }
    }

    private void AcceptCompletion()
    {
        if (_completionBox is not { } box || _completionState is not { } state || _completionList?.SelectedItem is not CompletionItem item) return;
        var (text, caret) = CellCompletion.Apply(box.Text ?? string.Empty, box.CaretIndex, state, item);
        _applyingCompletion = true;
        try
        {
            box.Text = text;
            box.CaretIndex = caret;
        }
        finally { _applyingCompletion = false; }
        box.Focus();
        ShowCompletion(box, force: false); // chain: "<From_" -> sheet -> header -> r
    }
}
