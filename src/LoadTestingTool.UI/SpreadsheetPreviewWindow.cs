using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using Spreadalonia;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI;

/// <summary>
/// Experimental spreadsheet editor. Changes are saved to the source workbook after a backup is created.
/// </summary>
public sealed class SpreadsheetPreviewWindow : Window
{
    private sealed class SheetView
    {
        public sealed class CellStyle
        {
            public bool Bold { get; set; }
            public bool Italic { get; set; }
            public string? TextColor { get; set; }
            public string? Highlight { get; set; }
        }

        public required string Name { get; set; }
        public string? OriginalName { get; init; }
        public required Dictionary<(int Col, int Row), string> Cells { get; init; }
        public required Dictionary<(int Col, int Row), string> OriginalCells { get; init; }
        public Dictionary<(int Col, int Row), CellStyle> OriginalStyles { get; } = [];
        public string HeaderFilter { get; set; } = string.Empty;
        public string RowFilter { get; set; } = string.Empty;
        public string ValueFilter { get; set; } = string.Empty;
        public int OriginalRowCount { get; set; }
        public int OriginalColumnCount { get; set; }
        public Spreadsheet Grid { get; set; } = null!;
        public bool GridDataLoaded { get; set; }
        public Canvas HighlightOverlay { get; set; } = null!;
        public required TabItem Tab { get; init; }
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public bool StructureDirty { get; set; }
        public Dictionary<int, int> DisplayRows { get; set; } = [];
        public Dictionary<int, int> DisplayColumns { get; set; } = [];
        public Dictionary<int, double> FilterColumnWidths { get; } = [];
        public Dictionary<int, double> FilterRowHeights { get; } = [];
        public HashSet<int> HiddenColumns { get; } = [];
        public HashSet<int> HiddenRows { get; } = [];
        public Dictionary<(int Col, int Row), CellStyle> Styles { get; } = [];
    }

    private readonly string _sourcePath;
    private readonly Action<string>? _log;
    private readonly TabControl _tabs = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Dictionary<string, SheetView> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedSheetNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _headerFilter = new() { Watermark = "Headers: header1 & header2…", MinWidth = 230 };
    private readonly TextBox _rowFilter = new() { Watermark = "Rows: 0,1,2…", MinWidth = 150 };
    private readonly TextBox _valueFilter = new() { Watermark = "Cell values: term1 & term2…", MinWidth = 230 };
    private readonly TextBlock _status = DashboardStyle.Text("Edit cells here and save the original workbook.", 12, false, DashboardStyle.Muted);
    private readonly ComboBox _textColor = new() { Width = 120, ItemsSource = new[] { "Text color", "White", "Yellow", "Cyan", "Red", "Green" }, SelectedIndex = 0 };
    private readonly ComboBox _highlightColor = new() { Width = 130, ItemsSource = new[] { "Highlight", "Blue", "Yellow", "Green", "Red" }, SelectedIndex = 0 };
    private readonly Popup _suggestionPopup = new() { PlacementMode = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = false };
    private readonly ListBox _suggestions = new() { MaxHeight = 240, MinWidth = 330 };
    private SheetView? _suggestionSheet;
    private Spreadsheet? _suggestionGrid;
    private bool _refreshing;
    private bool _loadingSheetFilters;

    public SpreadsheetPreviewWindow(string workbookPath, Action<string>? log = null)
    {
        _sourcePath = workbookPath;
        _log = log;
        Title = $"Spreadsheet editor — {Path.GetFileName(workbookPath)}";
        Width = 1320; Height = 900; MinWidth = 900; MinHeight = 600;
        WindowState = WindowState.Maximized;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        try
        {
            using var workbookStream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(workbookStream);
            foreach (var worksheet in workbook.Worksheets)
            {
                var used = worksheet.RangeUsed();
                var cells = ReadCells(worksheet)
                    .GroupBy(x => (x.Key.Item1, x.Key.Item2))
                    .ToDictionary(group => group.Key, group => group.Last().Value);
                var tab = new TabItem { Header = worksheet.Name };
                var view = new SheetView { Name = worksheet.Name, OriginalName = worksheet.Name, Cells = cells, OriginalCells = new Dictionary<(int Col, int Row), string>(cells), Tab = tab,
                    RowCount = Math.Max(1, used?.RangeAddress.LastAddress.RowNumber ?? 1),
                    ColumnCount = Math.Max(1, used?.RangeAddress.LastAddress.ColumnNumber ?? 1),
                    OriginalRowCount = Math.Max(1, used?.RangeAddress.LastAddress.RowNumber ?? 1),
                    OriginalColumnCount = Math.Max(1, used?.RangeAddress.LastAddress.ColumnNumber ?? 1) };
                foreach (var cell in worksheet.CellsUsed())
                {
                    var style = new SheetView.CellStyle
                    {
                        Bold = cell.Style.Font.Bold,
                        Italic = cell.Style.Font.Italic,
                        TextColor = cell.Style.Font.FontColor.ColorType == XLColorType.Color ? $"#{cell.Style.Font.FontColor.Color.R:X2}{cell.Style.Font.FontColor.Color.G:X2}{cell.Style.Font.FontColor.Color.B:X2}" : null,
                        Highlight = cell.Style.Fill.BackgroundColor.ColorType == XLColorType.Color ? $"#{cell.Style.Fill.BackgroundColor.Color.R:X2}{cell.Style.Fill.BackgroundColor.Color.G:X2}{cell.Style.Fill.BackgroundColor.Color.B:X2}" : null
                    };
                    var key = (cell.Address.ColumnNumber - 1, cell.Address.RowNumber - 1);
                    if (style.Bold || style.Italic || style.TextColor is not null || style.Highlight is not null)
                    {
                        view.Styles[key] = style;
                        view.OriginalStyles[key] = new SheetView.CellStyle { Bold = style.Bold, Italic = style.Italic, TextColor = style.TextColor, Highlight = style.Highlight };
                    }
                }
                tab.Header = BuildSheetTabHeader(view);
                _sheets[worksheet.Name] = view;
                _tabs.Items.Add(tab);
            }
            if (_tabs.ItemCount == 0) _status.Text = "The workbook contains no worksheets.";
            Log($"Spreadsheet editor opened. workbook={_sourcePath}; sheets={_sheets.Count}");
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not load workbook: {ex.Message}";
            _status.Foreground = DashboardStyle.Red;
            Log($"Spreadsheet editor open failed. workbook={_sourcePath}; error={ex}");
        }

        _headerFilter.TextChanged += (_, _) => SheetFilterChanged();
        _rowFilter.TextChanged += (_, _) => SheetFilterChanged();
        _valueFilter.TextChanged += (_, _) => SheetFilterChanged();
        _suggestions.PointerReleased += (_, _) => { if (_suggestions.SelectedItem is string value) AcceptSuggestion(value); };
        _suggestionPopup.Child = _suggestions;
        _textColor.SelectionChanged += (_, _) => ApplyTextColor();
        _highlightColor.SelectionChanged += (_, _) => ApplyHighlight();
        AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control && e.Key == Key.S)
            {
                SaveWorkbook();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        var heading = new StackPanel { Spacing = 4, Children =
        {
            DashboardStyle.Text("Spreadsheet editor", 21, true),
            DashboardStyle.Text("Edits save to the original DataEngine workbook after creating a .bak.xlsx backup. Filters are visual only and do not delete workbook data.", 12, false, DashboardStyle.Muted)
        }};
        var filterBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 8), Children =
        {
            Labeled("Header filter", _headerFilter), Labeled("Row filter", _rowFilter), Labeled("Cell-value filter", _valueFilter),
            DashboardStyle.Action("Clear filters", ClearFilters)
        }};
        var formatBar = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN, ItemHeight = double.NaN, Margin = new Thickness(0, 0, 0, 8), Children =
        {
            DashboardStyle.Text("Format", 10, false, DashboardStyle.Muted),
            DashboardStyle.Action("Bold", () => ApplyTypeface(FontWeight.Bold)),
            DashboardStyle.Action("Italic", () => ApplyTypeface(FontWeight.Normal)),
            _textColor, _highlightColor,
            DashboardStyle.Action("Undo", () => ApplyFormatCommand("Undo", g => g.Undo())),
            DashboardStyle.Action("Redo", () => ApplyFormatCommand("Redo", g => g.Redo())),
            DashboardStyle.Action("Auto-fit all columns", AutoFitSelectedColumns),
            DashboardStyle.Action("Reset format", ResetSelectedFormat),
            DashboardStyle.Text("Use right-click menus for cell, row, and column operations.", 10, false, DashboardStyle.Muted)
        }};
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children =
        {
            DashboardStyle.Action("Save workbook", SaveWorkbook, "primary"),
            DashboardStyle.Action("Reload workbook", () => _ = ReloadWorkbookAsync()),
            DashboardStyle.Action("Open source workbook", () => SystemDefaultFileOpener.Open(_sourcePath)),
            DashboardStyle.Action("Close", Close)
        }};
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(18) };
        root.Children.Add(heading);
        Grid.SetRow(filterBar, 1); root.Children.Add(filterBar);
        Grid.SetRow(formatBar, 2); root.Children.Add(formatBar);
        var tabsBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        tabsBar.Children.Add(_tabs);
        var addSheet = DashboardStyle.Action("+  Add sheet", AddSheet, "primary"); addSheet.Margin = new Thickness(6, 0, 0, 0); addSheet.MinWidth = 110;
        Grid.SetColumn(addSheet, 1); tabsBar.Children.Add(addSheet);
        Grid.SetRow(tabsBar, 3); root.Children.Add(tabsBar);
        Grid.SetRow(_status, 4); root.Children.Add(_status);
        Grid.SetRow(buttons, 5); root.Children.Add(buttons);
        Content = new Grid { Children = { root, _suggestionPopup } };
        _tabs.SelectionChanged += (_, _) => { LoadSelectedSheetFilters(); RefreshFilteredViews(SelectedSheet()); RefreshStatus(); };
        _tabs.SelectedIndex = 0;
        Dispatcher.UIThread.Post(() =>
        {
            LoadSelectedSheetFilters();
            RefreshFilteredViews();
            ScheduleInitialAutoFit();
        }, DispatcherPriority.Loaded);
    }

    private void ScheduleInitialAutoFit()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var attempts = 0;
        timer.Tick += (_, _) =>
        {
            var view = SelectedSheet();
            if (view?.GridDataLoaded == true)
            {
                AutoFitSelectedColumns();
                timer.Stop();
            }
            else if (++attempts >= 50) timer.Stop();
        };
        timer.Start();
    }

    private static Control Labeled(string label, Control field) => new StackPanel { Spacing = 3, Children = { DashboardStyle.Text(label, 10, false, DashboardStyle.Muted), field } };

    private void ClearFilters()
    {
        _headerFilter.Text = ""; _rowFilter.Text = ""; _valueFilter.Text = "";
    }

    private async Task ReloadWorkbookAsync()
    {
        foreach (var view in _sheets.Values) SyncView(view);
        if (_sheets.Values.Any(HasChanges) || _removedSheetNames.Count > 0)
        {
            var dialog = new Window { Title = "Reload workbook", Width = 500, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var cancel = DashboardStyle.Action("Cancel", () => dialog.Close(false));
            var reload = DashboardStyle.Action("Reload and discard", () => dialog.Close(true), "primary");
            dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { DashboardStyle.Text("Unsaved changes will be discarded.", 15, true), DashboardStyle.Text("Reload the complete workbook from disk?", 12, false, DashboardStyle.Muted), new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, reload } } } };
            if (await dialog.ShowDialog<bool>(this) != true) return;
        }
        var owner = Owner;
        var replacement = new SpreadsheetPreviewWindow(_sourcePath, _log);
        if (owner is Window windowOwner)
        {
            replacement.Show(windowOwner);
            Close();
        }
        else if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = replacement;
            replacement.Show();
            Close();
        }
        else
        {
            replacement.Show();
            Close();
        }
        _log?.Invoke($"Spreadsheet workbook reloaded. workbook={_sourcePath}");
    }

    private void SheetFilterChanged()
    {
        if (_loadingSheetFilters) return;
        var view = SelectedSheet();
        if (view is null) return;
        view.HeaderFilter = _headerFilter.Text ?? string.Empty;
        view.RowFilter = _rowFilter.Text ?? string.Empty;
        view.ValueFilter = _valueFilter.Text ?? string.Empty;
        RefreshFilteredViews(view);
    }

    private void LoadSelectedSheetFilters()
    {
        var view = SelectedSheet();
        if (view is null) return;
        _loadingSheetFilters = true;
        _headerFilter.Text = view.HeaderFilter;
        _rowFilter.Text = view.RowFilter;
        _valueFilter.Text = view.ValueFilter;
        _loadingSheetFilters = false;
    }

    private static IEnumerable<KeyValuePair<(int, int), string>> ReadCells(IXLWorksheet worksheet)
    {
        var used = worksheet.RangeUsed();
        if (used is null) yield break;
        foreach (var cell in used.Cells())
            yield return new KeyValuePair<(int, int), string>((cell.Address.ColumnNumber - 1, cell.Address.RowNumber - 1), cell.GetString());
    }

    private void RefreshFilteredViews(SheetView? only = null, bool syncExisting = true)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            IEnumerable<SheetView> targetViews = only is null ? _sheets.Values : new[] { only };
            foreach (var view in targetViews)
            {
                if (syncExisting) SyncView(view);
                var rows = Enumerable.Range(0, Math.Max(1, view.RowCount)).ToArray();
                var columns = Enumerable.Range(0, Math.Max(1, view.ColumnCount)).ToArray();
                var visibleRows = ParseRows(view.RowFilter, rows);
                var headerTerms = Terms(view.HeaderFilter);
                var valueTerms = Terms(view.ValueFilter);
                var hiddenRows = rows.Where(row => !visibleRows.Contains(row)).ToHashSet();
                var hiddenColumns = columns.Where(col =>
                    (headerTerms.Length > 0 && !headerTerms.Any(term => Cell(view.Cells, col, 0).Contains(term, StringComparison.OrdinalIgnoreCase)))
                    || (valueTerms.Length > 0 && !valueTerms.Any(term => Enumerable.Range(1, Math.Max(0, view.RowCount - 1)).Any(row => Cell(view.Cells, col, row).Contains(term, StringComparison.OrdinalIgnoreCase))))).ToHashSet();

                // Keep the workbook coordinates unchanged. The grid contains
                // the complete sheet, while non-matching rows/columns are
                // hidden by height/width. This prevents filtered display
                // indexes from ever being mistaken for Excel indexes.
                view.DisplayRows = rows.ToDictionary(row => row, row => row);
                view.DisplayColumns = columns.ToDictionary(column => column, column => column);
                var projected = new Dictionary<(int, int), string>(view.Cells);
                ReplaceGrid(view, projected, hiddenRows, hiddenColumns, syncExisting);
            }
            RefreshStatus();
        }
        finally { _refreshing = false; }
    }

    private static string[] Terms(string? text) => (text ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int[] ParseRows(string? text, int[] allRows)
    {
        var filter = (text ?? "").Trim();
        if (filter.Length == 0) return allRows;

        var requested = new HashSet<int>();
        foreach (var part in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 2 && int.TryParse(range[0], out var first) && int.TryParse(range[1], out var last))
            {
                var lower = Math.Max(0, Math.Min(first, last));
                var upper = Math.Min(allRows.Length - 1, Math.Max(first, last));
                for (var number = lower; number <= upper; number++) requested.Add(number);
            }
            else if (range.Length == 1 && int.TryParse(part, out var number) && number >= 0)
            {
                requested.Add(number);
            }
        }

        // Row 0 is the workbook header and must stay visible for every row filter.
        return requested.Count == 0
            ? allRows
            : allRows.Where(row => row == 0 || requested.Contains(row)).ToArray();
    }

    private static string Cell(Dictionary<(int Col, int Row), string> cells, int col, int row) => cells.TryGetValue((col, row), out var value) ? value : "";
    private static string RowText(Dictionary<(int Col, int Row), string> cells, int row) => string.Join(" ", cells.Where(x => x.Key.Row == row).OrderBy(x => x.Key.Col).Select(x => x.Value));

    private void ReplaceGrid(SheetView view, Dictionary<(int, int), string> projected, HashSet<int> hiddenRows, HashSet<int> hiddenColumns, bool syncExisting = true)
    {
        if (syncExisting && view.Grid is not null) SyncView(view);
        if (view.Grid is not null && view.GridDataLoaded)
        {
            // Keep the same control when filtering. Replacing a TabItem's
            // content can leave a new Spreadalonia control uninitialized;
            // more importantly, reusing it guarantees that grid coordinates
            // remain the original workbook coordinates.
            CaptureFilterDimensions(view);
            // Spreadalonia keeps values for coordinates omitted from SetData.
            // Supply explicit empty values for old coordinates that disappeared
            // from the model, otherwise deleted rows/columns appear duplicated
            // and empty cells inherit stale neighbouring values.
            var refreshed = new Dictionary<(int, int), string>(projected);
            if (view.Grid.Data is not null)
                foreach (var key in view.Grid.Data.Keys)
                    if (!refreshed.ContainsKey(key)) refreshed[key] = string.Empty;
            view.Grid.SetData(refreshed);
            ApplyChangedHiddenDimensions(view, hiddenRows, hiddenColumns);
            Dispatcher.UIThread.Post(() => RefreshHighlightOverlay(view), DispatcherPriority.Render);
            return;
        }
        var grid = new Spreadsheet
        {
            Foreground = Brushes.Black,
            SpreadsheetBackground = new SolidColorBrush(Colors.White),
            HeaderForeground = Brushes.Black,
            HeaderBackground = Color.Parse("#E6E6E6"),
            SelectionAccent = new SolidColorBrush(Color.Parse("#9CC7F5"))
        };
        view.GridDataLoaded = false;
        view.Grid = grid;
        var host = new Grid { ClipToBounds = true };
        var overlay = new Canvas { IsHitTestVisible = false };
        view.HighlightOverlay = overlay;
        host.Children.Add(grid);
        host.Children.Add(overlay);
        view.Tab.Content = new Border { Padding = new Thickness(6), Child = host };
        WireAutocomplete(grid, view);
        grid.PropertyChanged += (_, e) =>
        {
            if (e.Property == Spreadsheet.SelectionProperty)
                Dispatcher.UIThread.Post(() => RefreshHighlightOverlay(view), DispatcherPriority.Render);
        };
        ScheduleGridLoad(view, grid, projected, hiddenRows, hiddenColumns);
        Dispatcher.UIThread.Post(() => RefreshHighlightOverlay(view), DispatcherPriority.Render);
    }

    private static void ApplyChangedHiddenDimensions(SheetView view, HashSet<int> hiddenRows, HashSet<int> hiddenColumns)
    {
        if (view.Grid is null) return;
        var changedColumns = view.HiddenColumns.Union(hiddenColumns).Except(view.HiddenColumns.Intersect(hiddenColumns))
            .ToDictionary(column => column, column => hiddenColumns.Contains(column) ? 0d : view.FilterColumnWidths.GetValueOrDefault(column, 100d));
        var changedRows = view.HiddenRows.Union(hiddenRows).Except(view.HiddenRows.Intersect(hiddenRows))
            .ToDictionary(row => row, row => hiddenRows.Contains(row) ? 0d : view.FilterRowHeights.GetValueOrDefault(row, 22d));
        if (changedColumns.Count > 0) view.Grid.SetWidth(changedColumns);
        if (changedRows.Count > 0) view.Grid.SetHeight(changedRows);
        view.HiddenColumns.Clear();
        view.HiddenColumns.UnionWith(hiddenColumns);
        view.HiddenRows.Clear();
        view.HiddenRows.UnionWith(hiddenRows);
    }

    private void WireAutocomplete(Spreadsheet grid, SheetView view)
    {
        grid.ContextMenu = BuildGridMenu(grid);
        grid.AddHandler(InputElement.TextInputEvent, (_, _) => Dispatcher.UIThread.Post(() => UpdateSuggestions(grid, view)), RoutingStrategies.Bubble);
        grid.AddHandler(InputElement.KeyDownEvent, (_, e) => HandleClipboardKey(grid, view, e), RoutingStrategies.Tunnel);
        grid.AddHandler(InputElement.KeyDownEvent, (_, e) => HandleAutocompleteKey(grid, view, e), RoutingStrategies.Tunnel);
        grid.PropertyChanged += (_, e) =>
        {
            if (e.Property == Spreadsheet.IsEditingProperty && grid.IsEditing)
            {
                Dispatcher.UIThread.Post(() => AttachEditingAutocomplete(grid, view), DispatcherPriority.Background);
                Dispatcher.UIThread.Post(() => AttachEditingAutocomplete(grid, view), DispatcherPriority.Render);
            }
        };
        grid.Loaded += (_, _) => Dispatcher.UIThread.Post(() => AttachEditingAutocomplete(grid, view), DispatcherPriority.Loaded);
    }

    private void HandleClipboardKey(Spreadsheet grid, SheetView view, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control || grid.Selection.Count == 0) return;
        var editor = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox" && x.IsVisible && x.IsFocused);
        if (editor is not null && e.Key == Key.C && editor.SelectionStart != editor.SelectionEnd)
        {
            _ = CopyTextSelectionAsync(editor); e.Handled = true; return;
        }
        if (editor is not null && e.Key == Key.V)
        {
            _ = PasteTextSelectionAsync(editor, grid, view); e.Handled = true; return;
        }
        switch (e.Key)
        {
            case Key.C:
                grid.Copy();
                Log($"Spreadsheet copy. selection={DescribeSelection(grid)}; gridCells={grid.Data?.Count ?? 0}; modelCells={view.Cells.Count}");
                e.Handled = true;
                break;
            case Key.X:
                grid.Cut();
                SyncView(view);
                Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
                Log($"Spreadsheet cut. selection={DescribeSelection(grid)}; gridCells={grid.Data?.Count ?? 0}; modelCells={view.Cells.Count}");
                e.Handled = true;
                break;
            case Key.V:
                _ = PasteFromKeyboardAsync(grid, view);
                Log($"Spreadsheet paste requested. selection={DescribeSelection(grid)}; modelCellsBefore={view.Cells.Count}");
                e.Handled = true;
                break;
        }
    }

    private async Task PasteFromKeyboardAsync(Spreadsheet grid, SheetView view)
    {
        await grid.Paste(false);
        SyncView(view);
        Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
        Log($"Spreadsheet paste completed. selection={DescribeSelection(grid)}; gridCells={grid.Data?.Count ?? 0}; modelCells={view.Cells.Count}");
    }

    private async Task CopyTextSelectionAsync(TextBox editor)
    {
        var clipboard = TopLevel.GetTopLevel(editor)?.Clipboard;
        if (clipboard is not null && editor.SelectionStart != editor.SelectionEnd)
            await clipboard.SetTextAsync(editor.SelectedText ?? string.Empty);
    }

    private async Task PasteTextSelectionAsync(TextBox editor, Spreadsheet grid, SheetView view)
    {
        var clipboard = TopLevel.GetTopLevel(editor)?.Clipboard;
        var pasted = clipboard is null ? null : await clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(pasted)) return;
        var text = editor.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(editor.SelectionStart, editor.SelectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(editor.SelectionStart, editor.SelectionEnd), start, text.Length);
        editor.Text = text[..start] + pasted + text[end..];
        editor.CaretIndex = start + pasted.Length;
        editor.SelectionStart = editor.CaretIndex;
        editor.SelectionEnd = editor.CaretIndex;
        SyncView(view);
        Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
    }

    private void AttachEditingAutocomplete(Spreadsheet grid, SheetView view)
    {
        var editor = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox");
        if (editor is null || editor.Tag is not null) return;
        editor.Tag = "autocomplete-attached";
        editor.Foreground = Brushes.Black;
        editor.Background = Brushes.White;
        editor.CaretBrush = Brushes.Black;
        editor.Opacity = 1;
        editor.TextChanged += (_, _) => Dispatcher.UIThread.Post(() => UpdateSuggestions(grid, view, liveText: editor.Text), DispatcherPriority.Input);
        editor.TextChanged += (_, _) => SyncEditingCell(grid, view, editor.Text ?? string.Empty);
    }

    private ContextMenu BuildGridMenu(Spreadsheet grid)
    {
        var menu = new ContextMenu();
        void RefreshMenu()
        {
            menu.Items.Clear();
            var hasSelection = grid.Selection.Count > 0;
            var selection = hasSelection ? grid.Selection[0] : default;
            var fullRows = hasSelection && selection.Width > 256;
            var fullColumns = hasSelection && selection.Height > 256;
            var items = fullRows ? RowMenu(grid) : fullColumns ? ColumnMenu(grid) : CellMenu(grid);
            foreach (var item in items) menu.Items.Add(item);
        }
        menu.Opening += (_, _) => RefreshMenu();
        RefreshMenu();
        return menu;
    }

    private Control[] CellMenu(Spreadsheet grid) =>
    [
        Menu("Copy", () => grid.Copy()), Menu("Cut", () => ApplyDataCommand(grid, g => g.Cut())), Menu("Paste", () => _ = PasteAndSync(grid)),
        new Separator(), Menu("Bold", () => ApplyFormatCommand("Bold", g => { g.SetTypeface(new Typeface("Arial", FontStyle.Normal, FontWeight.Bold)); CaptureSelectionStyle(g, bold: true); })),
        Menu("Italic", () => ApplyFormatCommand("Italic", g => { g.SetTypeface(new Typeface("Arial", FontStyle.Italic, FontWeight.Normal)); CaptureSelectionStyle(g, italic: true); })),
        Menu("Reset format", ResetSelectedFormat), new Separator(), Menu("Undo", () => grid.Undo()), Menu("Redo", () => grid.Redo())
    ];

    private Control[] RowMenu(Spreadsheet grid)
    {
        var items = new List<Control>
        {
            Menu("Insert empty row", () => ApplyStructuralCommand("Inserted empty row", true, 1)),
            Menu("Duplicate row", () => DuplicateSelection(true)),
            Menu("Delete row", () => ApplyStructuralCommand("Deleted row", true, -1))
        };
        if (SelectedSheet()?.Name.Equals("config", StringComparison.OrdinalIgnoreCase) == true)
        {
            items.Add(new Separator());
            items.Add(Menu("Open template", () => OpenTemplateForSelectedConfigRow(grid)));
        }
        return items.ToArray();
    }

    private Control[] ColumnMenu(Spreadsheet grid) =>
    [
        Menu("Insert empty column", () => ApplyStructuralCommand("Inserted empty column", false, 1)),
        Menu("Duplicate column", () => DuplicateSelection(false)),
        Menu("Delete column", () => ApplyStructuralCommand("Deleted column", false, -1)),
        new Separator(), Menu("Auto-fit all columns", AutoFitSelectedColumns)
    ];

    private static MenuItem Menu(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    private void OpenTemplateForSelectedConfigRow(Spreadsheet grid)
    {
        var view = SelectedSheet();
        if (view is null || grid.Selection.Count == 0) return;
        var row = OriginalRowAt(view, grid.Selection[0].Top);
        if (row <= 0)
        {
            _status.Text = "Select a config data row, not the header, to open its template.";
            _status.Foreground = DashboardStyle.Red;
            return;
        }

        var templateColumn = HeaderColumn(view, "templetsource");
        if (templateColumn < 0)
        {
            _status.Text = "The config sheet has no templetsource column.";
            _status.Foreground = DashboardStyle.Red;
            return;
        }
        var source = Cell(view.Cells, templateColumn, row).Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            _status.Text = "The selected config row has no templetsource value.";
            _status.Foreground = DashboardStyle.Red;
            return;
        }

        var templatesFolder = Path.Combine(Path.GetDirectoryName(_sourcePath)!, "Templates");
        var path = Path.GetFullPath(Path.Combine(templatesFolder, source.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(templatesFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            _status.Text = $"Template not found: {source}";
            _status.Foreground = DashboardStyle.Red;
            return;
        }

        var testcase = Cell(view.Cells, HeaderColumn(view, "testcase"), row);
        var stepName = Cell(view.Cells, HeaderColumn(view, "stepname"), row);
        var stepType = Cell(view.Cells, HeaderColumn(view, "steptype"), row);
        var instanceName = Path.GetFileName(Path.GetDirectoryName(_sourcePath) ?? "");
        var editor = new TemplateEditorWindow(templatesFolder, source, string.IsNullOrWhiteSpace(stepType) ? "HTTP" : stepType,
            value => { view.Cells[(templateColumn, row)] = value; _status.Text = $"Template saved: {value}"; _status.Foreground = DashboardStyle.Green; },
            instanceName, testcase, stepName);
        editor.Show(this);
    }

    private static int HeaderColumn(SheetView view, string name)
        => view.Cells.Where(x => x.Key.Row == 0 && x.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key.Col).DefaultIfEmpty(-1).First();

    private void Log(string message) => _log?.Invoke(message);

    private static string DescribeSelection(Spreadsheet grid) => grid.Selection.Count == 0
        ? "none"
        : string.Join("|", grid.Selection.Select(range => $"L{range.Left + 1}:T{range.Top + 1}-R{range.Right + 1}:B{range.Bottom + 1}"));

    private Spreadsheet? ActiveGrid() => _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex)?.Grid;

    private void ApplyFormatCommand(string message, Action<Spreadsheet> action)
    {
        var grid = ActiveGrid();
        if (grid is null) return;
        try
        {
            action(grid);
            var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
            if (view is not null) Dispatcher.UIThread.Post(() => RefreshHighlightOverlay(view), DispatcherPriority.Render);
            _status.Text = message + "."; _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { _status.Text = $"{message} failed: {ex.Message}"; _status.Foreground = DashboardStyle.Red; }
    }

    private void ApplyDataCommand(Spreadsheet grid, Action<Spreadsheet> action)
    {
        try
        {
            action(grid);
            var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
            if (view is not null)
            {
                SyncView(view);
                Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
            }
            _status.Foreground = DashboardStyle.Green;
            _status.Text = "Cell changes applied. Save workbook to write them to the original file.";
            Log($"Spreadsheet clipboard command completed. selection={DescribeSelection(grid)}; gridCells={grid.Data?.Count ?? 0}; modelCells={view?.Cells.Count ?? 0}");
        }
        catch (Exception ex)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Cell operation failed: {ex.Message}";
            Log($"Spreadsheet cell operation failed. error={ex}");
        }
    }

    private async Task PasteAndSync(Spreadsheet grid)
    {
        try
        {
            await grid.Paste(false);
            var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
            if (view is not null)
            {
                SyncView(view);
                Dispatcher.UIThread.Post(() => SyncView(view), DispatcherPriority.Background);
            }
            _status.Foreground = DashboardStyle.Green;
            _status.Text = "Pasted cells applied. Save workbook to write them to the original file.";
            Log($"Spreadsheet paste completed. selection={DescribeSelection(grid)}; gridCells={grid.Data?.Count ?? 0}; modelCells={view?.Cells.Count ?? 0}");
        }
        catch (Exception ex)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Paste failed: {ex.Message}";
            Log($"Spreadsheet paste failed. error={ex}");
        }
    }

    private void DuplicateSelection(bool rows)
    {
        var view = SelectedSheet();
        var grid = view?.Grid;
        if (view is null || grid is null || grid.Selection.Count == 0) return;
        try
        {
            if (!view.StructureDirty) SyncView(view);
            var range = grid.Selection[0];
            var start = rows ? OriginalRowAt(view, range.Top) : OriginalColumnAt(view, range.Left);
            var count = rows ? Math.Max(1, Math.Min(range.Height, Math.Max(1, view.RowCount - start))) : Math.Max(1, Math.Min(range.Width, Math.Max(1, view.ColumnCount - start)));
            var snapshot = view.Cells.Where(x => rows ? x.Key.Row >= start && x.Key.Row < start + count : x.Key.Col >= start && x.Key.Col < start + count).ToArray();
            if (rows)
            {
                ShiftRows(view, start + count, count); view.RowCount += count;
                foreach (var cell in snapshot) view.Cells[(cell.Key.Col, cell.Key.Row + count)] = cell.Value;
            }
            else
            {
                ShiftColumns(view, start + count, count); view.ColumnCount += count;
                foreach (var cell in snapshot) view.Cells[(cell.Key.Col + count, cell.Key.Row)] = cell.Value;
            }
            view.StructureDirty = true;
            RefreshFilteredViews(view, syncExisting: false);
            _status.Foreground = DashboardStyle.Green;
            _status.Text = $"Duplicated {(rows ? "row" : "column")} selection. Changes will be written when saved.";
        }
        catch (Exception ex) { _status.Text = $"Duplicate failed: {ex.Message}"; _status.Foreground = DashboardStyle.Red; }
    }

    private void ApplyStructuralCommand(string message, bool rows, int direction)
    {
        var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
        var grid = view?.Grid;
        if (view is null || grid is null || grid.Selection.Count == 0) return;
        try
        {
            if (!view.StructureDirty) SyncView(view);
            var range = grid.Selection[0];
            var start = rows ? OriginalRowAt(view, range.Top) : OriginalColumnAt(view, range.Left);
            var reportedCount = rows ? range.Height : range.Width;
            // Spreadalonia reports a whole row/column header selection as the
            // full Excel grid (over a million rows or thousands of columns).
            // Structural editing must operate on the selected workbook area,
            // never on the virtual grid dimensions.
            var workbookCount = rows ? view.RowCount : view.ColumnCount;
            var count = reportedCount is > 0 and <= 256
                ? Math.Min(reportedCount, Math.Max(1, workbookCount - start))
                : 1;
            if (rows)
            {
                ShiftRows(view, start, direction * count);
                view.RowCount = Math.Max(1, view.RowCount + direction * count);
            }
            else
            {
                ShiftColumns(view, start, direction * count);
                view.ColumnCount = Math.Max(1, view.ColumnCount + direction * count);
            }
            view.StructureDirty = true;
            RefreshFilteredViews(view, syncExisting: false);
            _status.Foreground = DashboardStyle.Green;
            _status.Text = $"{message} at {(rows ? $"row {start + 1}" : $"column {start + 1}")}. Changes will be written to the original workbook when saved.";
        }
        catch (Exception ex)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"{message} failed: {ex.Message}";
        }
    }

    private void AutoFitSelectedColumns()
    {
        var view = SelectedSheet();
        var grid = view?.Grid;
        if (view is null || grid is null) return;
        try
        {
            var widths = new Dictionary<int, double>();
            for (var column = 0; column < view.ColumnCount; column++)
            {
                var longest = Enumerable.Range(0, Math.Min(view.RowCount, 10000)).Select(row => Cell(view.Cells, column, row).Length).DefaultIfEmpty(0).Max();
                widths[column] = Math.Clamp(24d + longest * 7.2d, 48d, 640d);
            }
            if (widths.Count > 0) grid.SetWidth(widths);
            _status.Text = $"Auto-fit all {widths.Count} column(s)."; _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { _status.Text = $"Auto-fit columns failed: {ex.Message}"; _status.Foreground = DashboardStyle.Red; Log($"Spreadsheet auto-fit columns failed. error={ex}"); }
    }

    private void AutoFitSelectedRows()
    {
        var view = SelectedSheet();
        var grid = view?.Grid;
        if (view is null || grid is null || grid.Selection.Count == 0) return;
        try
        {
            var heights = new Dictionary<int, double>();
            foreach (var range in grid.Selection)
            {
                var top = Math.Clamp(range.Top, 0, Math.Max(0, view.RowCount - 1));
                var bottom = Math.Clamp(range.Bottom, top, Math.Max(0, view.RowCount - 1));
                for (var row = top; row <= bottom; row++) heights[row] = Math.Clamp(22d + Math.Min(6, Cell(view.Cells, 0, row).Length / 80) * 15d, 22d, 112d);
            }
            if (heights.Count > 0) grid.SetHeight(heights);
            _status.Text = $"Auto-fit {heights.Count} row(s)."; _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { _status.Text = $"Auto-fit rows failed: {ex.Message}"; _status.Foreground = DashboardStyle.Red; Log($"Spreadsheet auto-fit rows failed. error={ex}"); }
    }

    private static void ShiftRows(SheetView view, int start, int delta)
    {
        var cells = view.Cells.ToArray();
        view.Cells.Clear();
        foreach (var cell in cells)
        {
            if (delta < 0 && cell.Key.Row >= start && cell.Key.Row < start - delta) continue;
            var row = cell.Key.Row >= start + (delta < 0 ? -delta : 0) ? cell.Key.Row + delta : cell.Key.Row;
            view.Cells[(cell.Key.Col, row)] = cell.Value;
        }
        ShiftStyles(view, start, delta, true);
    }

    private static void ShiftColumns(SheetView view, int start, int delta)
    {
        var cells = view.Cells.ToArray();
        view.Cells.Clear();
        foreach (var cell in cells)
        {
            if (delta < 0 && cell.Key.Col >= start && cell.Key.Col < start - delta) continue;
            var column = cell.Key.Col >= start + (delta < 0 ? -delta : 0) ? cell.Key.Col + delta : cell.Key.Col;
            view.Cells[(column, cell.Key.Row)] = cell.Value;
        }
        ShiftStyles(view, start, delta, false);
    }

    private static void ShiftStyles(SheetView view, int start, int delta, bool rows)
    {
        var styles = view.Styles.ToArray();
        view.Styles.Clear();
        foreach (var style in styles)
        {
            var coordinate = rows ? style.Key.Row : style.Key.Col;
            if (delta < 0 && coordinate >= start && coordinate < start - delta) continue;
            coordinate = coordinate >= start + (delta < 0 ? -delta : 0) ? coordinate + delta : coordinate;
            view.Styles[rows ? (style.Key.Col, coordinate) : (coordinate, style.Key.Row)] = style.Value;
        }
    }

    private static int OriginalRowAt(SheetView view, int displayRow)
    {
        foreach (var pair in view.DisplayRows)
            if (pair.Value == displayRow) return pair.Key;
        return Math.Clamp(displayRow, 0, Math.Max(0, view.RowCount - 1));
    }

    private static int OriginalColumnAt(SheetView view, int displayColumn)
    {
        foreach (var pair in view.DisplayColumns)
            if (pair.Value == displayColumn) return pair.Key;
        return Math.Clamp(displayColumn, 0, Math.Max(0, view.ColumnCount - 1));
    }

    private void ApplyTypeface(FontWeight weight)
    {
        ApplyFormatCommand(weight == FontWeight.Bold ? "Bold applied" : "Italic applied", grid =>
        {
            grid.SetTypeface(new Typeface("Arial", weight == FontWeight.Bold ? FontStyle.Normal : FontStyle.Italic, weight));
            CaptureSelectionStyle(grid, bold: weight == FontWeight.Bold, italic: weight != FontWeight.Bold);
        });
    }

    private void ApplyTextColor()
    {
        var color = _textColor.SelectedItem?.ToString() switch { "White" => "#FFFFFF", "Yellow" => "#FFFF00", "Cyan" => "#00FFFF", "Red" => "#FF0000", "Green" => "#32CD32", _ => null };
        if (color is not null)
            ApplyFormatCommand("Text color applied", grid => { grid.SetForeground(new SolidColorBrush(Color.Parse(color))); CaptureSelectionStyle(grid, textColor: color); });
        _textColor.SelectedIndex = 0;
    }

    private void ApplyHighlight()
    {
        var color = _highlightColor.SelectedItem?.ToString() switch { "Blue" => "#3B6EA8", "Yellow" => "#806B20", "Green" => "#276144", "Red" => "#74353F", _ => null };
        if (color is not null)
            ApplyFormatCommand("Selection highlight applied", grid => { grid.SelectionAccent = new SolidColorBrush(Color.Parse(color)); CaptureSelectionStyle(grid, highlight: color); });
        _highlightColor.SelectedIndex = 0;
    }

    private void CaptureSelectionStyle(Spreadsheet grid, bool? bold = null, bool? italic = null, string? textColor = null, string? highlight = null)
    {
        var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
        if (view is null || grid.Selection.Count == 0) return;
        var maxColumn = Math.Max(0, view.DisplayColumns.Count - 1);
        var maxRow = Math.Max(0, view.DisplayRows.Count - 1);
        foreach (var range in grid.Selection)
        {
            var left = Math.Clamp(range.Left, 0, maxColumn);
            var right = Math.Clamp(range.Right, 0, maxColumn);
            var top = Math.Clamp(range.Top, 0, maxRow);
            var bottom = Math.Clamp(range.Bottom, 0, maxRow);
            for (var row = top; row <= bottom; row++)
                for (var column = left; column <= right; column++)
                {
                    if (!view.DisplayRows.TryGetValue(row, out var originalRow) || !view.DisplayColumns.TryGetValue(column, out var originalColumn)) continue;
                    var key = (originalColumn, originalRow);
                    if (!view.Styles.TryGetValue(key, out var style)) view.Styles[key] = style = new SheetView.CellStyle();
                    if (bold.HasValue) style.Bold = bold.Value;
                    if (italic.HasValue) style.Italic = italic.Value;
                    if (textColor is not null) style.TextColor = textColor;
                    if (highlight is not null) style.Highlight = highlight;
                }
        }
    }

    private void ResetSelectedFormat()
    {
        var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
        var grid = ActiveGrid();
        if (view is null || grid is null) return;
        ApplyFormatCommand("Reset format", g => g.ResetFormat());
        var maxColumn = Math.Max(0, view.DisplayColumns.Count - 1);
        var maxRow = Math.Max(0, view.DisplayRows.Count - 1);
        foreach (var range in grid.Selection)
        {
            var left = Math.Clamp(range.Left, 0, maxColumn);
            var right = Math.Clamp(range.Right, 0, maxColumn);
            var top = Math.Clamp(range.Top, 0, maxRow);
            var bottom = Math.Clamp(range.Bottom, 0, maxRow);
            for (var row = top; row <= bottom; row++)
                for (var column = left; column <= right; column++)
                    if (view.DisplayRows.TryGetValue(row, out var originalRow) && view.DisplayColumns.TryGetValue(column, out var originalColumn))
                        view.Styles.Remove((originalColumn, originalRow));
        }
    }

    private void HandleAutocompleteKey(Spreadsheet grid, SheetView view, KeyEventArgs e)
    {
        if (_suggestionPopup.IsOpen && e.Key == Key.Up)
        {
            _suggestions.SelectedIndex = Math.Max(0, _suggestions.SelectedIndex - 1); e.Handled = true; return;
        }
        if (_suggestionPopup.IsOpen && e.Key == Key.Down)
        {
            _suggestions.SelectedIndex = Math.Min(_suggestions.ItemCount - 1, _suggestions.SelectedIndex + 1); e.Handled = true; return;
        }
        if (_suggestionPopup.IsOpen && (e.Key == Key.Enter || e.Key == Key.Tab) && _suggestions.SelectedItem is string selected)
        {
            AcceptSuggestion(selected); e.Handled = true; return;
        }
        if (e.Key == Key.Escape && _suggestionPopup.IsOpen) { _suggestionPopup.IsOpen = false; e.Handled = true; return; }
        if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            UpdateSuggestions(grid, view, true); e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            Dispatcher.UIThread.Post(() => UpdateSuggestions(grid, view), DispatcherPriority.Background);
        }
    }

    private void UpdateSuggestions(Spreadsheet grid, SheetView view, bool force = false, string? liveText = null)
    {
        AttachEditingAutocomplete(grid, view);
        var data = grid.Data;
        if (grid.Selection.Count == 0 || data is null) { _suggestionPopup.IsOpen = false; return; }
        var selection = grid.Selection[0];
        var editor = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox" && x.IsVisible);
        var text = liveText ?? (grid.IsEditing && editor is not null ? editor.Text ?? "" : data.TryGetValue((selection.Left, selection.Top), out var value) ? value ?? "" : "");
        var caret = text.Length;
        var before = text[..caret];
        var lt = before.LastIndexOf('<'); var brace = before.LastIndexOf('{');
        var start = Math.Max(lt, brace);
        var token = start >= 0 ? before[start..] : "";
        var suggestions = new List<string>();
        if (brace > lt && !before[(brace + 1)..].Contains('}'))
            suggestions.AddRange(new[] { "eq", "ne", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "gt", "gte", "lt", "lte", "in", "size" }.Select(x => "{" + x + "}"));
        else if (lt >= 0 && !before[(lt + 1)..].Contains('>'))
        {
            var typed = before[(lt + 1)..];
            var from = typed.StartsWith("From_", StringComparison.OrdinalIgnoreCase);
            if (typed.Equals("from", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("<From_");
            }
            else if (from)
            {
                var parts = typed[5..].Split('_', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) suggestions.AddRange(_sheets.Keys.Select(x => $"<From_{x}_"));
                else if (parts.Length == 1)
                {
                    var source = _sheets.Values.FirstOrDefault(x => x.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    if (source is not null) suggestions.AddRange(source.Cells.Where(x => x.Key.Row == 0).Select(x => CleanAutocompleteHeader(x.Value)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Select(x => $"<From_{source.Name}_{x}_r>"));
                }
                else
                {
                    var source = _sheets.Values.FirstOrDefault(x => x.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                    var prefix = string.Join('_', parts.Skip(1));
                    if (source is not null) suggestions.AddRange(source.Cells.Where(x => x.Key.Row == 0).Select(x => CleanAutocompleteHeader(x.Value)).Where(x => !string.IsNullOrWhiteSpace(x) && x.Contains(prefix, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Select(x => $"<From_{source.Name}_{x}_r>"));
                }
            }
            else suggestions.AddRange(new[] { "<From_", "<datum>", "<currentdatum>", "<currenttime>", "<currentdatetime>", "<currenttimestamp>", "<randomnumber>", "<randomnumber:1-100>", "<guid>", "<guid:N>" });
        }
        else if (force) suggestions.AddRange(new[] { "<From_", "<datum>", "<currentdatetime>", "<randomnumber>", "<guid>", "{eq}", "{contains}", "{exists}" });
        suggestions = suggestions.Where(x => force || x.Contains(token, StringComparison.OrdinalIgnoreCase)).Distinct().Take(80).ToList();
        if (suggestions.Count == 0) { _suggestionPopup.IsOpen = false; return; }
        _suggestions.ItemsSource = suggestions; _suggestions.SelectedIndex = 0;
        _suggestionSheet = view; _suggestionGrid = grid; _suggestionPopup.PlacementTarget = editor is not null ? editor : grid; _suggestionPopup.IsOpen = true;
    }

    private static string CleanAutocompleteHeader(string header)
        => Regex.Replace(header ?? string.Empty, @"\{[^{}\r\n]*\}", string.Empty).Trim();

    private void AcceptSuggestion(string suggestion)
    {
        if (_suggestionGrid is null || _suggestionSheet is null || _suggestionGrid.Selection.Count == 0) return;
        var selection = _suggestionGrid.Selection[0];
        var key = (selection.Left, selection.Top);
        var editor = _suggestionGrid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox");
        var text = editor?.Text ?? (_suggestionGrid.Data.TryGetValue(key, out var current) ? current ?? "" : "");
        var lt = text.LastIndexOf('<'); var brace = text.LastIndexOf('{'); var start = Math.Max(lt, brace); if (start < 0) start = text.Length;
        var next = text[..start] + suggestion + text[Math.Min(text.Length, start + Math.Max(0, text.Length - start))..];
        if (editor is not null && _suggestionGrid.IsEditing)
        {
            editor.Text = next;
            editor.CaretIndex = next.Length;
            editor.Focus();
        }
        else _suggestionGrid.SetData(new[] { new KeyValuePair<(int, int), string>(key, next) });
        _suggestionPopup.IsOpen = false;
    }

    private static void ScheduleGridLoad(SheetView view, Spreadsheet grid, Dictionary<(int, int), string> projected, HashSet<int> hiddenRows, HashSet<int> hiddenColumns)
    {
        var attempts = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            try
            {
                // Spreadalonia initializes its backing cell store during its
                // template/layout pass. Retrying at Background avoids calling
                // SetData during the earlier render phase, where it can throw
                // on a newly materialized tab.
                grid.SetData(projected);
                view.GridDataLoaded = true;
                timer.Stop();
                CaptureFilterDimensions(view);
                if (hiddenColumns.Count > 0)
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { grid.SetWidth(hiddenColumns.ToDictionary(column => column, _ => 0d)); view.HiddenColumns.UnionWith(hiddenColumns); }
                        catch (NullReferenceException) { }
                    }, DispatcherPriority.Background);
                if (hiddenRows.Count > 0)
                    Dispatcher.UIThread.Post(() =>
                    {
                        try { grid.SetHeight(hiddenRows.ToDictionary(row => row, _ => 0d)); view.HiddenRows.UnionWith(hiddenRows); }
                        catch (NullReferenceException) { }
                    }, DispatcherPriority.Background);
            }
            catch (NullReferenceException)
            {
                if (++attempts >= 50) timer.Stop();
            }
        };
        timer.Start();
    }

    private static void CaptureFilterDimensions(SheetView view)
    {
        if (view.Grid is null) return;
        for (var column = 0; column < Math.Max(1, view.ColumnCount); column++)
            if (!view.FilterColumnWidths.ContainsKey(column)) view.FilterColumnWidths[column] = view.Grid.GetCellSize(column, 0).width;
        for (var row = 0; row < Math.Max(1, view.RowCount); row++)
            if (!view.FilterRowHeights.ContainsKey(row)) view.FilterRowHeights[row] = view.Grid.GetCellSize(0, row).height;
    }

    private static void SyncEditingCell(Spreadsheet grid, SheetView view, string value)
    {
        if (grid.Selection.Count == 0) return;
        var selection = grid.Selection[0];
        if (!view.DisplayRows.TryGetValue(selection.Top, out var originalRow) ||
            !view.DisplayColumns.TryGetValue(selection.Left, out var originalColumn)) return;

        var key = (originalColumn, originalRow);
        if (string.IsNullOrEmpty(value)) view.Cells.Remove(key);
        else view.Cells[key] = value;
    }

    private static void RefreshHighlightOverlay(SheetView view)
    {
        if (view.Grid is null || view.HighlightOverlay is null) return;
        view.HighlightOverlay.Children.Clear();
        var table = view.Grid.GetVisualDescendants().OfType<Control>().FirstOrDefault(x => x.Name == "ContentTable");
        if (table is null) return;
        var origin = table.TranslatePoint(new Point(0, 0), view.HighlightOverlay);
        if (origin is null) return;
        var horizontalScroll = view.Grid.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(x => x.Name == "HorizontalScrollBar");
        var verticalScroll = view.Grid.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(x => x.Name == "VerticalScrollBar");
        var offset = new Vector(horizontalScroll?.Value ?? 0, verticalScroll?.Value ?? 0);
        var columnStarts = new Dictionary<int, double>();
        var rowStarts = new Dictionary<int, double>();
        double x = 0;
        for (var column = 0; column < view.ColumnCount; column++)
        {
            columnStarts[column] = x;
            x += view.Grid.GetCellSize(column, 0).width;
        }
        double y = 0;
        for (var row = 0; row < view.RowCount; row++)
        {
            rowStarts[row] = y;
            y += view.Grid.GetCellSize(0, row).height;
        }

        foreach (var item in view.Styles)
        {
            if (string.IsNullOrWhiteSpace(item.Value.Highlight) ||
                !view.DisplayColumns.TryGetValue(item.Key.Col, out var displayColumn) ||
                !view.DisplayRows.TryGetValue(item.Key.Row, out var displayRow) ||
                !columnStarts.TryGetValue(displayColumn, out var left) ||
                !rowStarts.TryGetValue(displayRow, out var top)) continue;
            var size = view.Grid.GetCellSize(displayColumn, displayRow);
            var color = Color.Parse(item.Value.Highlight);
            var fill = new SolidColorBrush(Color.FromArgb(72, color.R, color.G, color.B));
            var cell = new Border { Background = fill, Width = size.width, Height = size.height, IsHitTestVisible = false };
            Canvas.SetLeft(cell, origin.Value.X + left - offset.X);
            Canvas.SetTop(cell, origin.Value.Y + top - offset.Y);
            view.HighlightOverlay.Children.Add(cell);
        }
    }

    private static void SyncView(SheetView view)
    {
        if (view.Grid is null || !view.GridDataLoaded || view.DisplayRows.Count == 0 || view.DisplayColumns.Count == 0 || view.Grid.Data is null) return;
        // A structural refresh can briefly expose the same coordinate more
        // than once in the grid data. Collapse it deterministically instead
        // of throwing a duplicate-key exception and taking down the editor.
        var visibleCells = view.Grid.Data
            .GroupBy(x => x.Key)
            .ToDictionary(group => group.Key, group => group.Last().Value);
        EnsureDisplayMapCoverage(view, visibleCells.Keys);
        var editor = view.Grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(x => x.Name == "EditingBox" && x.IsVisible);
        if (view.Grid.IsEditing && editor is not null && view.Grid.Selection.Count > 0)
        {
            var selected = view.Grid.Selection[0];
            visibleCells[(selected.Left, selected.Top)] = editor.Text ?? string.Empty;
        }

        // Remove only currently visible cells. The previous row×column Cartesian
        // allocation could freeze the UI for large sheets during insert/delete.
        var visibleOriginalKeys = view.Cells.Keys
            .Where(key => view.DisplayRows.ContainsKey(key.Row) && view.DisplayColumns.ContainsKey(key.Col))
            .ToArray();
        foreach (var original in visibleOriginalKeys) view.Cells.Remove(original);
        var originalRows = view.DisplayRows.ToDictionary(x => x.Value, x => x.Key);
        var originalColumns = view.DisplayColumns.ToDictionary(x => x.Value, x => x.Key);
        foreach (var cell in visibleCells)
        {
            if (!originalRows.TryGetValue(cell.Key.Item2, out var row) || !originalColumns.TryGetValue(cell.Key.Item1, out var col)) continue;
            if (!string.IsNullOrEmpty(cell.Value)) view.Cells[(col, row)] = cell.Value;
        }
    }

    private static void EnsureDisplayMapCoverage(SheetView view, IEnumerable<(int Col, int Row)> cells)
    {
        var maxDisplayColumn = cells.Select(x => x.Col).DefaultIfEmpty(-1).Max();
        var maxDisplayRow = cells.Select(x => x.Row).DefaultIfEmpty(-1).Max();
        var mappedDisplayColumns = view.DisplayColumns.Values.ToHashSet();
        var mappedDisplayRows = view.DisplayRows.Values.ToHashSet();

        for (var display = 0; display <= maxDisplayColumn; display++)
        {
            if (mappedDisplayColumns.Contains(display)) continue;
            var original = view.ColumnCount++;
            view.DisplayColumns[original] = display;
            mappedDisplayColumns.Add(display);
        }
        for (var display = 0; display <= maxDisplayRow; display++)
        {
            if (mappedDisplayRows.Contains(display)) continue;
            var original = view.RowCount++;
            view.DisplayRows[original] = display;
            mappedDisplayRows.Add(display);
        }
    }

    private void RefreshStatus()
    {
        var view = _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);
        if (view is null) return;
        _status.Text = $"Showing {view.DisplayRows.Count} rows × {view.DisplayColumns.Count} columns in {view.Name}. Use & for AND filters; row indexes are zero-based with the header at 0.";
        _status.Foreground = DashboardStyle.Muted;
    }

    private SheetView? SelectedSheet() => _sheets.Values.ElementAtOrDefault(_tabs.SelectedIndex);

    private Control BuildSheetTabHeader(SheetView view)
    {
        var name = DashboardStyle.Text(view.Name, 12, false);
        name.PointerPressed += (_, e) => { if (e.ClickCount >= 2) RenameSheet(view); };
        var remove = DashboardStyle.Action("−", () => RemoveSheet(view));
        remove.Padding = new Thickness(4, 0); remove.Margin = new Thickness(4, 0, 0, 0); remove.MinWidth = 22;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { name, remove } };
    }

    private void AddSheet()
    {
        var number = 1;
        string name;
        do name = $"Sheet{number++}"; while (_sheets.ContainsKey(name));

        var tab = new TabItem { Header = "" };
        var view = new SheetView { Name = name, Cells = [], OriginalCells = [], Tab = tab, RowCount = 1, ColumnCount = 1, OriginalRowCount = 0, OriginalColumnCount = 0 };
        tab.Header = BuildSheetTabHeader(view);
        _sheets.Add(name, view);
        _tabs.Items.Add(tab);
        _tabs.SelectedIndex = _tabs.ItemCount - 1;
        RefreshFilteredViews(syncExisting: false);
        _status.Foreground = DashboardStyle.Green;
        _status.Text = $"Added sheet '{name}'. Press Ctrl+S or click Save workbook to write it.";
    }

    private void RemoveSheet() => RemoveSheet(SelectedSheet());

    private void RemoveSheet(SheetView? view)
    {
        if (view is null) return;
        if (_sheets.Count <= 1)
        {
            _status.Foreground = DashboardStyle.Red;
            _status.Text = "A workbook must contain at least one worksheet.";
            return;
        }

        SyncView(view);
        _sheets.Remove(view.Name);
        if (view.OriginalName is not null) _removedSheetNames.Add(view.OriginalName);
        _tabs.Items.Remove(view.Tab);
        _tabs.SelectedIndex = Math.Clamp(_tabs.SelectedIndex, 0, _tabs.ItemCount - 1);
        RefreshFilteredViews(syncExisting: false);
        _status.Foreground = DashboardStyle.Green;
        _status.Text = $"Removed sheet '{view.Name}'. Press Ctrl+S or click Save workbook to write it.";
    }

    private void RenameSheet() => RenameSheet(SelectedSheet());

    private void RenameSheet(SheetView? view)
    {
        if (view is null) return;
        var input = new TextBox { Text = view.Name, Width = 300 };
        var dialog = new Window { Title = "Rename sheet", Width = 440, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var error = DashboardStyle.Text("", 11, false, DashboardStyle.Red);
        var ok = DashboardStyle.Action("Rename", () =>
        {
            var name = input.Text?.Trim() ?? "";
            if (!IsValidSheetName(name)) { error.Text = "Use 1–31 characters and do not use : \\ / ? * [ ]."; return; }
            if (!name.Equals(view.Name, StringComparison.OrdinalIgnoreCase) && _sheets.ContainsKey(name)) { error.Text = $"A sheet named '{name}' already exists."; return; }
            var oldName = view.Name;
            _sheets.Remove(oldName);
            view.Name = name;
            view.Tab.Header = BuildSheetTabHeader(view);
            _sheets.Add(name, view);
            dialog.Close();
            RefreshFilteredViews(syncExisting: false);
            _status.Foreground = DashboardStyle.Green;
            _status.Text = $"Renamed sheet '{oldName}' to '{name}'. Press Ctrl+S or click Save workbook to write it.";
        }, "primary");
        var cancel = DashboardStyle.Action("Cancel", () => dialog.Close());
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 8, Children =
        {
            DashboardStyle.Text("New sheet name", 14, true), input, error,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } }
        }};
        dialog.Show(this);
        input.Focus(); input.SelectAll();
    }

    private static bool IsValidSheetName(string name) => name.Length is >= 1 and <= 31 && name.IndexOfAny([':', '\\', '/', '?', '*', '[', ']']) < 0;

    private void SaveWorkbook()
    {
        var temp = _sourcePath + ".saving.xlsx";
        var backup = _sourcePath + ".bak.xlsx";
        var formattingSkipped = false;
        Log($"Spreadsheet save started. workbook={_sourcePath}; sheets={_sheets.Count}; totalModelCells={_sheets.Values.Sum(x => x.Cells.Count)}");
        try
        {
            // Structural edits are already applied to the source-coordinate model. A subsequent
            // read from Spreadalonia can still expose its pre-refresh virtual grid and duplicate
            // the edge column/row, so never overwrite a dirty structural model during save.
            foreach (var view in _sheets.Values)
                if (!view.StructureDirty) SyncView(view);
            var changedViews = _sheets.Values.Where(HasChanges).ToList();
            if (changedViews.Count == 0 && _removedSheetNames.Count == 0)
            {
                _status.Foreground = DashboardStyle.Muted;
                _status.Text = "No changes detected. Workbook was not modified and no backup was created.";
                Log($"Spreadsheet save skipped. workbook={_sourcePath}; reason=no-value-style-or-structure-differences");
                return;
            }
            using var workbook = new XLWorkbook(_sourcePath);
            var stepChanges = CaptureConfigStepChanges(workbook);
            var columnChanges = CaptureColumnChanges(workbook);
            WriteWorkbookChanges(workbook, includeEditorStyles: true);
            WorkbookSynchronization.SynchronizeRequestResponse(workbook);
            WorkbookSynchronization.SynchronizeStepColumns(workbook, stepChanges);
            WorkbookSynchronization.SynchronizeColumnReferences(workbook, columnChanges);
            workbook.SaveAs(temp, true);
        }
        catch (Exception ex) when (IsStylesXmlFailure(ex))
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                using var recoveryWorkbook = new XLWorkbook(_sourcePath);
                var stepChanges = CaptureConfigStepChanges(recoveryWorkbook);
                var columnChanges = CaptureColumnChanges(recoveryWorkbook);
                WriteWorkbookChanges(recoveryWorkbook, includeEditorStyles: true);
                WorkbookSynchronization.SynchronizeRequestResponse(recoveryWorkbook);
                WorkbookSynchronization.SynchronizeStepColumns(recoveryWorkbook, stepChanges);
                WorkbookSynchronization.SynchronizeColumnReferences(recoveryWorkbook, columnChanges);
                // Older workbooks can contain legacy font/color ordering that
                // ClosedXML reads but rejects during strict OpenXML validation.
                // Validation-disabled output remains readable by Excel and keeps
                // the workbook contents and editor changes.
                recoveryWorkbook.SaveAs(temp, false);
                formattingSkipped = true;
                Log($"Spreadsheet save used legacy-style recovery. workbook={_sourcePath}; error={ex.Message}");
            }
            catch (Exception recoveryException)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                _status.Foreground = DashboardStyle.Red;
                _status.Text = $"Could not save workbook after style recovery: {recoveryException.Message}";
                Log($"Spreadsheet save recovery failed. workbook={_sourcePath}; error={recoveryException}");
                return;
            }
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Could not save workbook: {ex.Message}";
            Log($"Spreadsheet save failed. workbook={_sourcePath}; error={ex}");
            return;
        }

        try
        {
            File.Copy(_sourcePath, backup, true);
            File.Move(temp, _sourcePath, true);
            _removedSheetNames.Clear();
            foreach (var view in _sheets.Values)
            {
                view.StructureDirty = false;
                view.OriginalRowCount = view.RowCount;
                view.OriginalColumnCount = view.ColumnCount;
                view.OriginalCells.Clear();
                foreach (var cell in view.Cells) view.OriginalCells[cell.Key] = cell.Value;
                view.OriginalStyles.Clear();
                foreach (var style in view.Styles) view.OriginalStyles[style.Key] = CloneStyle(style.Value);
            }
            _status.Foreground = DashboardStyle.Green;
            _status.Text = formattingSkipped
                ? $"Workbook saved: {_sourcePath}. Backup: {backup}. Legacy styles were preserved using non-strict validation."
                : $"Workbook saved: {_sourcePath}. Backup: {backup}";
            Log($"Spreadsheet save completed. workbook={_sourcePath}; backup={backup}; formattingSkipped={formattingSkipped}");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            _status.Foreground = DashboardStyle.Red;
            _status.Text = $"Could not save workbook: {ex.Message}";
            Log($"Spreadsheet save replacement failed. workbook={_sourcePath}; error={ex}");
        }
    }

    private void WriteWorkbookChanges(XLWorkbook workbook, bool includeEditorStyles)
    {
        foreach (var removedName in _removedSheetNames.ToArray())
        {
            var removed = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(removedName, StringComparison.OrdinalIgnoreCase));
            removed?.Delete();
        }

        foreach (var view in _sheets.Values)
        {
            var sheet = view.OriginalName is not null
                ? workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(view.OriginalName, StringComparison.OrdinalIgnoreCase))
                : null;
            sheet ??= workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(view.Name, StringComparison.OrdinalIgnoreCase));
            sheet ??= workbook.Worksheets.Add(view.Name);
            if (!sheet.Name.Equals(view.Name, StringComparison.Ordinal)) sheet.Name = view.Name;
            if (view.StructureDirty)
            {
                var maxRows = Math.Max(view.OriginalRowCount, view.RowCount);
                var maxColumns = Math.Max(view.OriginalColumnCount, view.ColumnCount);
                for (var row = 0; row < maxRows; row++)
                    for (var column = 0; column < maxColumns; column++)
                    {
                        var target = sheet.Cell(row + 1, column + 1);
                        if (view.Cells.TryGetValue((column, row), out var value))
                            WriteCell(target, value, view.Styles.GetValueOrDefault((column, row)), includeEditorStyles);
                        else
                            target.Value = string.Empty;
                    }
                continue;
            }

            var changedKeys = view.OriginalCells.Keys.Concat(view.Cells.Keys).Distinct().Where(key =>
                !view.OriginalCells.TryGetValue(key, out var original) || !view.Cells.TryGetValue(key, out var current) || !string.Equals(original, current, StringComparison.Ordinal)).ToList();
            foreach (var key in changedKeys)
            {
                var target = sheet.Cell(key.Row + 1, key.Col + 1);
                if (view.Cells.TryGetValue(key, out var value)) target.Value = value;
                else target.Clear(XLClearOptions.Contents);
                if (includeEditorStyles && view.Styles.TryGetValue(key, out var style)) WriteStyle(target, style);
            }
            foreach (var key in view.Styles.Keys.Concat(view.OriginalStyles.Keys).Distinct())
            {
                if (StylesEqual(view.Styles.GetValueOrDefault(key), view.OriginalStyles.GetValueOrDefault(key))) continue;
                if (view.Styles.TryGetValue(key, out var style)) WriteStyle(sheet.Cell(key.Row + 1, key.Col + 1), style);
            }
        }
    }

    private List<WorkbookSynchronization.StepChange> CaptureConfigStepChanges(XLWorkbook workbook)
    {
        var changes = new List<WorkbookSynchronization.StepChange>();
        var oldSheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase));
        var current = _sheets.Values.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase));
        if (oldSheet is null || current is null) return changes;

        var oldHeaders = HeaderMap(oldSheet.FirstRowUsed()?.CellsUsed().Select(x => (x.GetString().Trim(), x.Address.ColumnNumber - 1)));
        if (oldHeaders is null || !oldHeaders.TryGetValue("stepname", out var oldStepColumn) || !oldHeaders.TryGetValue("testcaseindex", out var oldTestcaseColumn)) return changes;
        var currentHeaders = HeaderMap(Enumerable.Range(0, current.ColumnCount).Select(c => (current.Cells.GetValueOrDefault((c, 0), "").Trim(), c)));
        if (!currentHeaders.TryGetValue("stepname", out var currentStepColumn) || !currentHeaders.TryGetValue("testcaseindex", out var currentTestcaseColumn)) return changes;

        var currentByPosition = new Dictionary<int, (string Testcase, string Step)>();
        for (var row = 1; row < current.RowCount; row++)
        {
            var testcase = current.Cells.GetValueOrDefault((currentTestcaseColumn, row), "").Trim();
            var step = current.Cells.GetValueOrDefault((currentStepColumn, row), "").Trim();
            if (!string.IsNullOrWhiteSpace(testcase) && !string.IsNullOrWhiteSpace(step)) currentByPosition[row + 1] = (testcase, step);
        }

        foreach (var oldRow in oldSheet.RowsUsed().Skip(1))
        {
            var oldName = oldRow.Cell(oldStepColumn + 1).GetString().Trim();
            var testcase = oldRow.Cell(oldTestcaseColumn + 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(oldName)) continue;
            if (!currentByPosition.TryGetValue(oldRow.RowNumber(), out var currentStep))
            {
                changes.Add(new WorkbookSynchronization.StepChange(testcase, oldName, null));
                continue;
            }
            if (string.Equals(testcase, currentStep.Testcase, StringComparison.OrdinalIgnoreCase) && !string.Equals(oldName, currentStep.Step, StringComparison.OrdinalIgnoreCase))
                changes.Add(new WorkbookSynchronization.StepChange(testcase, oldName, currentStep.Step));
        }
        return changes;
    }

    private List<WorkbookSynchronization.ColumnChange> CaptureColumnChanges(XLWorkbook workbook)
    {
        var changes = new List<WorkbookSynchronization.ColumnChange>();
        foreach (var sheetName in new[] { "request", "response" })
        {
            var oldSheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
            var current = _sheets.Values.FirstOrDefault(x => x.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
            if (oldSheet is null || current is null) continue;
            var oldLast = oldSheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var oldHeaders = Enumerable.Range(0, oldLast).Select(column => oldSheet.Cell(1, column + 1).GetString().Trim()).ToList();
            var currentHeaders = Enumerable.Range(0, current.ColumnCount).Select(column => current.Cells.GetValueOrDefault((column, 0), "").Trim()).ToList();
            if (oldHeaders.Count == currentHeaders.Count)
            {
                for (var column = 0; column < oldHeaders.Count; column++)
                    if (!string.Equals(oldHeaders[column], currentHeaders[column], StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(oldHeaders[column]))
                        changes.Add(new WorkbookSynchronization.ColumnChange(oldHeaders[column], string.IsNullOrWhiteSpace(currentHeaders[column]) ? null : currentHeaders[column]));
            }
            else
            {
                var currentClean = currentHeaders.Select(CleanHeaderForComparison).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var oldHeader in oldHeaders.Where(x => !string.IsNullOrWhiteSpace(x)))
                    if (!currentClean.Contains(CleanHeaderForComparison(oldHeader)))
                        changes.Add(new WorkbookSynchronization.ColumnChange(oldHeader, null));
            }
        }
        return changes;
    }

    private static string CleanHeaderForComparison(string value) => Regex.Replace(value.Trim(), @"^\{[^{}]*\}", "").Trim();

    private static Dictionary<string, int> HeaderMap(IEnumerable<(string Header, int Column)>? entries)
        => entries is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : entries.Where(x => !string.IsNullOrWhiteSpace(x.Header))
                .GroupBy(x => x.Header, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Column, StringComparer.OrdinalIgnoreCase);

    private static bool HasChanges(SheetView view)
    {
        if (view.StructureDirty || view.RowCount != view.OriginalRowCount || view.ColumnCount != view.OriginalColumnCount) return true;
        var keys = view.OriginalCells.Keys.Concat(view.Cells.Keys).Distinct();
        if (keys.Any(key => !view.OriginalCells.TryGetValue(key, out var oldValue) || !view.Cells.TryGetValue(key, out var newValue) || !string.Equals(oldValue, newValue, StringComparison.Ordinal))) return true;
        return view.Styles.Keys.Concat(view.OriginalStyles.Keys).Distinct().Any(key => !StylesEqual(view.Styles.GetValueOrDefault(key), view.OriginalStyles.GetValueOrDefault(key)));
    }

    private static bool StylesEqual(SheetView.CellStyle? left, SheetView.CellStyle? right) => left?.Bold == right?.Bold && left?.Italic == right?.Italic && string.Equals(left?.TextColor, right?.TextColor, StringComparison.OrdinalIgnoreCase) && string.Equals(left?.Highlight, right?.Highlight, StringComparison.OrdinalIgnoreCase);
    private static SheetView.CellStyle CloneStyle(SheetView.CellStyle style) => new() { Bold = style.Bold, Italic = style.Italic, TextColor = style.TextColor, Highlight = style.Highlight };
    private static void WriteCell(IXLCell cell, string value, SheetView.CellStyle? style, bool includeStyles) { cell.Value = value; if (includeStyles && style is not null) WriteStyle(cell, style); }
    private static void WriteStyle(IXLCell cell, SheetView.CellStyle style)
    {
        cell.Style.Font.Bold = style.Bold; cell.Style.Font.Italic = style.Italic;
        if (!string.IsNullOrWhiteSpace(style.TextColor)) cell.Style.Font.FontColor = XLColor.FromHtml(style.TextColor);
        if (!string.IsNullOrWhiteSpace(style.Highlight)) cell.Style.Fill.BackgroundColor = XLColor.FromHtml(style.Highlight);
    }

    private static bool IsStylesXmlFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current.Message.Contains("styles.xml", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("unexpected child element", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
