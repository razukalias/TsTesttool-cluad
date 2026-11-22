using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using LoadTestingTool.UI.DataEditor;

namespace LoadTestingTool.UI;

/// <summary>The page state of a list: which page of how many items. Sort and filter work on the whole list; only the slice shown changes.</summary>
public sealed class Pager
{
    public static readonly int[] PageSizes = [50, 100, 250];
    public const int DefaultPageSize = 100;

    private int _pageSize = DefaultPageSize;
    public int PageSize { get => _pageSize; set { var v = PageSizes.Contains(value) ? value : DefaultPageSize; if (v == _pageSize) return; _pageSize = v; Page = 1; Changed?.Invoke(); } }
    /// <summary>Sets the size without raising <see cref="Changed"/> (used while a bar is being built).</summary>
    public void SetPageSizeSilently(int value) { _pageSize = PageSizes.Contains(value) ? value : DefaultPageSize; Page = 1; }

    public int Page { get; private set; } = 1;
    public int Total { get; private set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public int FirstItem => Total == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItem => Math.Min(Total, Page * PageSize);

    /// <summary>Raised when the page or the page size changed; the list shows its slice again.</summary>
    public event Action? Changed;

    public void GoTo(int page)
    {
        var clamped = Math.Clamp(page, 1, PageCount);
        if (clamped == Page) return;
        Page = clamped; Changed?.Invoke();
    }

    /// <summary>A new filter or search means the first page of the new result.</summary>
    public void Reset() => Page = 1;

    /// <summary>The slice of the (already filtered and sorted) list for the current page; also sets the total and keeps the page inside the new total.</summary>
    public IReadOnlyList<T> Slice<T>(IReadOnlyList<T> all)
    {
        Total = all.Count;
        Page = Math.Clamp(Page, 1, PageCount);
        return all.Skip((Page - 1) * PageSize).Take(PageSize).ToList();
    }

    public string Summary => Total == 0 ? "No items" : $"{FirstItem:N0}–{LastItem:N0} of {Total:N0}";
}

/// <summary>First / previous / next / last, "page 3 of 17", jump to a page, the total, and the page size (50 / 100 / 250, remembered).</summary>
public sealed class PagerBar : StackPanel
{
    private readonly Pager _pager;
    private readonly Button _first = new() { Content = "⏮", Tag = "page-first", MinWidth = 32 };
    private readonly Button _previous = new() { Content = "◀", Tag = "page-previous", MinWidth = 32 };
    private readonly Button _next = new() { Content = "▶", Tag = "page-next", MinWidth = 32 };
    private readonly Button _last = new() { Content = "⏭", Tag = "page-last", MinWidth = 32 };
    private readonly TextBox _jump = new() { Width = 56, Tag = "page-jump", TextAlignment = Avalonia.Media.TextAlignment.Center };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center, Tag = "page-label" };
    private readonly ComboBox _size = new() { ItemsSource = Pager.PageSizes, Width = 80, Tag = "page-size" };
    private bool _updating;

    public PagerBar(Pager pager, bool rememberSize = true)
    {
        _pager = pager;
        Orientation = Orientation.Horizontal; Spacing = 6; Margin = new Thickness(0, 4);
        if (rememberSize) pager.SetPageSizeSilently(EditorSettings.Load().HistoryPageSize);
        _first.Click += (_, _) => pager.GoTo(1);
        _previous.Click += (_, _) => pager.GoTo(pager.Page - 1);
        _next.Click += (_, _) => pager.GoTo(pager.Page + 1);
        _last.Click += (_, _) => pager.GoTo(pager.PageCount);
        _jump.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { if (int.TryParse(_jump.Text?.Trim(), out var page)) pager.GoTo(page); Update(); e.Handled = true; } };
        _jump.LostFocus += (_, _) => { if (int.TryParse(_jump.Text?.Trim(), out var page)) pager.GoTo(page); Update(); };
        _size.SelectionChanged += (_, _) =>
        {
            if (_updating || _size.SelectedItem is not int size) return;
            pager.PageSize = size;
            if (rememberSize) { var settings = EditorSettings.Load(); settings.HistoryPageSize = size; settings.Save(); }
        };
        pager.Changed += Update;
        DetachedFromVisualTree += (_, _) => pager.Changed -= Update;   // bars rebuilt on every refresh must not pile up on the pager
        AttachedToVisualTree += (_, _) => { pager.Changed -= Update; pager.Changed += Update; Update(); };
        foreach (var control in new Control[] { _first, _previous, new TextBlock { Text = "Page", VerticalAlignment = VerticalAlignment.Center }, _jump, _label, _next, _last,
                     new TextBlock { Text = "per page", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }, _size })
            Children.Add(control);
        Update();
    }

    /// <summary>Call after the list was sliced (the total may have changed).</summary>
    public void Update()
    {
        _updating = true;
        _jump.Text = _pager.Page.ToString();
        _label.Text = $"of {_pager.PageCount:N0}  ·  {_pager.Summary}";
        _size.SelectedItem = _pager.PageSize;
        _first.IsEnabled = _previous.IsEnabled = _pager.Page > 1;
        _next.IsEnabled = _last.IsEnabled = _pager.Page < _pager.PageCount;
        IsVisible = true;
        _updating = false;
    }
}
