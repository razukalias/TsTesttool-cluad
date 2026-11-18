using Avalonia.Controls;
using Avalonia.Data;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>A text column whose cells show the fill colour and wrapping the workbook holds for them. Editing is unchanged.</summary>
public sealed class FormattedTextColumn : DataGridTextColumn
{
    private readonly int _cell;
    private readonly string? _rule;
    private readonly List<WeakReference<Border>> _overlays = [];
    private bool _selected;

    /// <summary>True while the user has this column picked by clicking its header: its cells and header are tinted.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            _overlays.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var weak in _overlays) if (weak.TryGetTarget(out var overlay)) overlay.IsVisible = value;
            UpdateHeader();
        }
    }

    private void UpdateHeader()
    {
        if (Header is not Avalonia.Visual visual) return;
        var header = Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(visual).OfType<DataGridColumnHeader>().FirstOrDefault();
        if (header is null) return;
        if (_selected) { if (!header.Classes.Contains("colsel")) header.Classes.Add("colsel"); }
        else header.Classes.Remove("colsel");
    }

    /// <summary>A small ⚑ in front of a cell that holds a &lt;bug: …&gt; tag; its tooltip says what the tag does, or what is wrong with it.</summary>
    private Control BugMarked(TextBlock text)
    {
        var flag = new TextBlock { Text = "⚑", FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(4, 0, 0, 0), IsVisible = false, Tag = "bug-mark" };
        var holder = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { flag, text } };
        void Refresh()
        {
            var value = text.Text ?? string.Empty;
            flag.IsVisible = value.Contains("<bug", StringComparison.OrdinalIgnoreCase);
            if (!flag.IsVisible) { ToolTip.SetTip(holder, null); return; }
            var problems = DataEditorViewModel.BugTagWarnings(value, ExpectedColumn);
            flag.Foreground = Avalonia.Media.Brush.Parse(problems.Count == 0 ? "#8CC1FF" : "#F6C76B");
            var tag = LoadTestingTool.Execution.BugTag.Parse(value).Tag;
            ToolTip.SetTip(holder, problems.Count > 0 ? string.Join(" ", problems)
                : $"Known bug in {string.Join(", ", tag!.Environments)}: a failing assertion of this cell does not fail the test; it is listed in the Bugs tab. {tag.Description}");
        }
        text.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) Refresh(); };
        Refresh();
        return holder;
    }

    /// <summary>Lays a translucent tint over the cell while the column is selected; it never takes mouse input.</summary>
    private Control Tinted(Control content, DataGridCell? cell = null)
    {
        var overlay = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#383291FF")), IsHitTestVisible = false, IsVisible = _selected };
        _overlays.Add(new WeakReference<Border>(overlay));
        Control inner = content;
        if (ExpectedColumn && FailureAt is not null && cell is not null) inner = FailureMarked(content, cell);
        var grid = new Grid { Children = { inner, overlay } };
        if (OpenWizard is not null && cell is not null) grid.Children.Add(WizardButton(cell));
        return grid;
    }

    /// <summary>True for an expected-value column: only there does a known-bug tag mean something.</summary>
    public bool ExpectedColumn { get; set; }

    /// <summary>Finds the failed assertion behind a cell of an expected-value column; set by the window together with <see cref="OpenFailure"/>.</summary>
    public Func<DataRowViewModel, int, FailureDetail?>? FailureAt { get; set; }

    /// <summary>Called with the cell, the mark that was clicked, the row and the failure.</summary>
    public Action<DataGridCell, Control, DataRowViewModel, FailureDetail>? OpenFailure { get; set; }

    /// <summary>A red tint and a small red mark on a cell whose assertion failed in the latest run; it goes once a value is assigned or a new run passes.</summary>
    private Control FailureMarked(Control content, DataGridCell cell)
    {
        var tint = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#40FF5A6E")), IsHitTestVisible = false, IsVisible = false };
        var mark = new Button
        {
            Content = "!", Tag = "failed-cell", Width = 18, Height = 18, Padding = new Avalonia.Thickness(0), FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold, CornerRadius = new Avalonia.CornerRadius(9),
            Background = Avalonia.Media.Brush.Parse("#E5484D"), Foreground = Avalonia.Media.Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 24, 0), IsVisible = false,
        };
        ToolTip.SetTip(mark, "This expected value failed in the latest run. Click to see what the run got");
        FailureDetail? current = null;
        DataRowViewModel? hooked = null;
        void Refresh()
        {
            current = cell.DataContext is DataRowViewModel row ? FailureAt?.Invoke(row, _cell) : null;
            tint.IsVisible = mark.IsVisible = current is not null;
        }
        void Hook()
        {
            if (hooked is not null) { hooked.PropertyChanged -= OnRowChanged; hooked.Cells.CollectionChanged -= OnCellsChanged; }
            hooked = cell.DataContext as DataRowViewModel;
            if (hooked is not null) { hooked.PropertyChanged += OnRowChanged; hooked.Cells.CollectionChanged += OnCellsChanged; }
            Refresh();
        }
        void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(DataRowViewModel.Outcome)) Refresh(); }
        void OnCellsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Refresh();
        cell.DataContextChanged += (_, _) => Hook();
        cell.DetachedFromVisualTree += (_, _) => { if (hooked is not null) { hooked.PropertyChanged -= OnRowChanged; hooked.Cells.CollectionChanged -= OnCellsChanged; hooked = null; } };
        cell.AttachedToVisualTree += (_, _) => Hook();
        mark.Click += (_, e) => { e.Handled = true; if (current is not null && hooked is not null) OpenFailure?.Invoke(cell, mark, hooked, current); };
        mark.PointerPressed += (_, e) => e.Handled = true;
        Hook();
        return new Grid { Children = { tint, content, mark } };
    }

    /// <summary>When set, the current cell shows a small button on its border that calls this with the cell and the button (the Record view's wizard).</summary>
    public Action<DataGridCell, Control>? OpenWizard { get; set; }

    /// <summary>The small wizard button; it shows only while its cell is the grid's current cell.</summary>
    private Control WizardButton(DataGridCell cell) => WizardButtons.Create(cell, (c, b) => OpenWizard?.Invoke(c, b));

    /// <param name="cell">Index of the cell in the row.</param>
    /// <param name="ruleHeader">The config header whose rules mark a questionable value with a ⚠, or null for none.</param>
    public FormattedTextColumn(int cell, string? ruleHeader = null)
    {
        _cell = cell;
        _rule = ruleHeader is not null && LoadTestingTool.Editing.StepFieldRules.Covers(ruleHeader) ? ruleHeader : null;
    }

    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        var element = base.GenerateElement(cell, dataItem);
        cell.Bind(DataGridCell.BackgroundProperty, new Binding($"Formats[{_cell}].Background"));
        if (element is TextBlock text)
        {
            text.MaxWidth = 300;
            text.Bind(TextBlock.TextWrappingProperty, new Binding($"Formats[{_cell}].Wrapping"));
            text.Bind(TextBlock.ForegroundProperty, new Binding($"Formats[{_cell}].Foreground"));
        }
        if (element is TextBlock plain && _rule is null) return Tinted(BugMarked(plain), cell);
        if (_rule is null || element is not TextBlock shown) return Tinted(element, cell);

        // A ⚠ beside a value the runner would misread, with the reason as its tooltip; an ⓘ where it only deserves a note.
        var mark = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(4, 0, 6, 0), IsVisible = false };
        var holder = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { shown, mark } };
        void Refresh()
        {
            var check = LoadTestingTool.Editing.StepFieldRules.Check(_rule, shown.Text);
            mark.IsVisible = check.State != LoadTestingTool.Editing.FieldState.Ok;
            mark.Text = check.State == LoadTestingTool.Editing.FieldState.Warning ? "⚠" : "ⓘ";
            mark.Foreground = Avalonia.Media.Brush.Parse(check.State == LoadTestingTool.Editing.FieldState.Warning ? "#F6C76B" : "#8FA3BF");
            ToolTip.SetTip(holder, mark.IsVisible ? check.Message : null);
        }
        shown.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) Refresh(); };
        Refresh();
        return Tinted(holder, cell);
    }
}
