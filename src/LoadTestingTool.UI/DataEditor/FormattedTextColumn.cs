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
        var grid = new Grid { Children = { content, overlay } };
        if (OpenWizard is not null && cell is not null) grid.Children.Add(WizardButton(cell));
        return grid;
    }

    /// <summary>True for an expected-value column: only there does a known-bug tag mean something.</summary>
    public bool ExpectedColumn { get; set; }

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
