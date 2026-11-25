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
    private readonly List<(WeakReference<DataGridCell> Cell, Border Mark)> _selectionMarks = [];

    /// <summary>The cell selection of the table; cells of this column that belong to it get a blue frame.</summary>
    public CellSelection? Selection { get; set; }

    /// <summary>Redraws the frame of every cell of this column that is on screen.</summary>
    public void RefreshSelection()
    {
        _selectionMarks.RemoveAll(m => !m.Cell.TryGetTarget(out _));
        foreach (var (weak, mark) in _selectionMarks) if (weak.TryGetTarget(out var cell)) mark.IsVisible = IsSelected(cell);
    }

    private bool IsSelected(DataGridCell cell) => Selection is not null && cell.DataContext is DataRowViewModel row && Selection.Contains(row, _cell);

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
    private Control BugMarked(TextBlock text, DataGridCell? cell = null)
    {
        var flag = new TextBlock { Text = "⚑", FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(4, 0, 0, 0), IsVisible = false, Tag = "bug-mark", Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        flag.PointerPressed += (_, e) => { if (cell is not null && OpenBug is not null) { e.Handled = true; OpenBug(cell, flag); } };   // click the flag: what the bug is
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
        if (cell is not null)
        {
            var frame = new Border { BorderBrush = Avalonia.Media.Brush.Parse("#3291FF"), BorderThickness = new Avalonia.Thickness(2), Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A3291FF")), IsHitTestVisible = false, IsVisible = IsSelected(cell), Tag = "cell-selected" };
            _selectionMarks.Add((new WeakReference<DataGridCell>(cell), frame));
            cell.DataContextChanged += (_, _) => frame.IsVisible = IsSelected(cell);
            grid.Children.Add(frame);
        }
        if (OpenWizard is not null && cell is not null) grid.Children.Add(WizardButton(cell));
        return grid;
    }

    /// <summary>True for an expected-value column: only there does a known-bug tag mean something.</summary>
    public bool ExpectedColumn { get; set; }

    /// <summary>Finds the failed assertion behind a cell of an expected-value column; set by the window together with <see cref="OpenFailure"/>.</summary>
    public Func<DataRowViewModel, int, FailureDetail?>? FailureAt { get; set; }

    /// <summary>What the latest run saw behind a cell that passed (the value, or "not in the response"); set by the window.</summary>
    public Func<DataRowViewModel, int, CellPass?>? PassAt { get; set; }

    /// <summary>Called with the cell, the mark that was clicked, the row and the failure.</summary>
    public Action<DataGridCell, Control, DataRowViewModel, FailureDetail>? OpenFailure { get; set; }

    /// <summary>How wide the text of a cell may get before it wraps; the window raises it when it fits the column to its longest value.</summary>
    public double ContentMaxWidth { get; set; } = 300;

    /// <summary>Opens the popup of a ⚑ known-bug flag (environments, comment, edit and remove); set by the window.</summary>
    public Action<DataGridCell, Control>? OpenBug { get; set; }

    /// <summary>A red tint and a small red mark on a cell whose assertion failed in the latest run; it goes once a value is assigned or a new run passes.</summary>
    private Control FailureMarked(Control content, DataGridCell cell)
    {
        // The red tint and the "!" button are built only for a cell that really failed; most cells never get them.
        var host = new Grid { Children = { content } };
        Border? tint = null;
        Button? mark = null;
        FailureDetail? current = null;
        DataRowViewModel? hooked = null;
        Border? passTint = null;
        Border? pill = null;
        TextBlock? pillText = null;
        // green: the cell passed and the response had this value; yellow: the field was not in the response (notexists, empty, or a blank optional cell)
        void ShowPass(CellPass? pass)
        {
            if (pass is null && pill is null) return;
            if (pill is null)
            {
                passTint = new Border { IsHitTestVisible = false };
                pillText = new TextBlock { FontSize = 11, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, MaxWidth = 140 };
                pill = new Border { Child = pillText, Tag = "pass-mark", CornerRadius = new Avalonia.CornerRadius(9), Padding = new Avalonia.Thickness(7, 1), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 24, 0) };
                host.Children.Insert(0, passTint);
                host.Children.Add(pill);
            }
            passTint!.IsVisible = pill.IsVisible = pass is not null;
            if (pass is null) return;
            passTint.Background = Avalonia.Media.Brush.Parse(pass.Absent ? "#30F6C76B" : "#2639D39B");
            pill.Background = Avalonia.Media.Brush.Parse(pass.Absent ? "#4A3F1E" : "#17422F");
            pillText!.Foreground = Avalonia.Media.Brush.Parse(pass.Absent ? "#F6C76B" : "#39D39B");
            pillText.Text = pass.Absent ? "∅ " + DataEditorViewModel.MissingText : "✓ " + (pass.Value.Length == 0 ? "(empty)" : pass.Value);
            ToolTip.SetTip(pill, (pass.Absent ? "The field was not in the response of the latest run." : "Passed in the latest run; the response had this value.") + (pass.Detail.Length > 0 ? "\n" + pass.Detail : string.Empty));
        }
        void Refresh()
        {
            var row = cell.DataContext as DataRowViewModel;
            current = row is not null ? FailureAt?.Invoke(row, _cell) : null;
            ShowPass(current is null && row is not null ? PassAt?.Invoke(row, _cell) : null);
            if (current is null && mark is null) return;
            if (mark is null)
            {
                tint = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#40FF5A6E")), IsHitTestVisible = false };
                mark = new Button
                {
                    Content = "!", Tag = "failed-cell", Width = 18, Height = 18, Padding = new Avalonia.Thickness(0), FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold, CornerRadius = new Avalonia.CornerRadius(9),
                    Background = Avalonia.Media.Brush.Parse("#E5484D"), Foreground = Avalonia.Media.Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 24, 0),
                };
                ToolTip.SetTip(mark, "This expected value failed in the latest run. Click to see what the run got");
                mark.Click += (_, e) => { e.Handled = true; if (current is not null && hooked is not null) OpenFailure?.Invoke(cell, mark, hooked, current); };
                mark.PointerPressed += (_, e) => e.Handled = true;
                host.Children.Insert(0, tint);
                host.Children.Add(mark);
            }
            tint!.IsVisible = mark.IsVisible = current is not null;
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
        Hook();
        return host;
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
        cell.Tag = _cell;   // lets the window tell which column a cell under the mouse belongs to (drag selection)
        cell.Bind(DataGridCell.BackgroundProperty, new Binding($"Formats[{_cell}].Background"));
        if (element is TextBlock text)
        {
            text.MaxWidth = ContentMaxWidth;
            text.Bind(TextBlock.TextWrappingProperty, new Binding($"Formats[{_cell}].Wrapping"));
            text.Bind(TextBlock.ForegroundProperty, new Binding($"Formats[{_cell}].Foreground"));
        }
        if (element is TextBlock plain && _rule is null) return Tinted(BugMarked(plain, cell), cell);
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
