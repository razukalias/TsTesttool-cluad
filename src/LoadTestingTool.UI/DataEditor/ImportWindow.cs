using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// Imports rows and columns from an Excel or CSV file into the selected testcase or the open sheet. Pick the file, then the sheet, the range (B3:F40) and the
/// header row; tick the rows and columns to take in the preview; choose what to do with them (add / insert / replace rows, fill existing rows, new columns)
/// and how the columns are matched (by header, by position, by hand). The line above the buttons says what will happen; replacing asks first.
/// </summary>
public sealed class ImportWindow : Window
{
    private const int PreviewRows = 50;
    private const int PreviewColumns = 40;

    private static readonly (string Text, ImportMode Mode)[] Modes =
    [
        ("Add rows at the end", ImportMode.AppendRows),
        ("Insert rows below the selected row", ImportMode.InsertRowsBelow),
        ("Replace all rows (asks first)", ImportMode.ReplaceRows),
        ("Fill existing rows / replace column values", ImportMode.FillExistingRows),
        ("Add as new columns", ImportMode.NewColumns),
    ];

    private bool _loading;
    private readonly TextBlock _file = new() { Text = "No file chosen", Foreground = Brush.Parse("#8EA4BC"), VerticalAlignment = VerticalAlignment.Center, Tag = "import-file" };
    private readonly ComboBox _sheet = new() { MinWidth = 150, Tag = "import-sheet" };
    private readonly TextBox _range = new() { Watermark = "B3:F40 — empty = whole sheet", MinWidth = 190, Tag = "import-range" };
    private readonly NumericUpDown _headerRow = new() { Minimum = 0, Maximum = 100000, Value = 1, FormatString = "0", Width = 110, Tag = "import-header-row" };
    private readonly ComboBox _delimiter = new() { MinWidth = 100, Tag = "import-delimiter" };
    private readonly ComboBox _encoding = new() { MinWidth = 120, Tag = "import-encoding" };
    private readonly StackPanel _csvOptions;
    private readonly TextBox _columnFilter = new() { Watermark = "Tick columns whose header contains…", MinWidth = 230, Tag = "import-column-filter" };
    private readonly Border _previewHost = new();
    private readonly TextBlock _previewNote = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "import-preview-note" };
    private readonly ComboBox _mode = new() { Tag = "import-mode", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _match = new() { Tag = "import-match", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _rowMatch = new() { Tag = "import-rowmatch", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _create = new() { Content = "Create columns that match nothing as new columns", Tag = "import-create-unmatched" };
    private readonly CheckBox _clear = new() { Content = "An empty file cell clears the existing value", Tag = "import-clear-empty" };
    private readonly ComboBox _step = new() { Tag = "import-step", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _kind = new() { Tag = "import-kind", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _verb = new() { Tag = "import-verb", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _placement = new() { Tag = "import-placement", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _rowMatchBox;
    private readonly StackPanel _newColumnBox;
    private readonly StackPanel _verbBox;
    private readonly StackPanel _stepBox;
    private readonly StackPanel _kindBox;
    private readonly TextBlock _impact = new() { TextWrapping = TextWrapping.Wrap, Tag = "import-impact" };
    private readonly TextBlock _error = new() { Foreground = Brush.Parse("#FF8998"), TextWrapping = TextWrapping.Wrap, Tag = "import-error" };
    private readonly Button _run = new() { Content = "Import", Classes = { "primary" }, MinWidth = 120, Tag = "import-run", IsEnabled = false };

    public ImportSession Session { get; }

    /// <summary>Replaces the confirmation dialog (tests): title and text in, true to go on.</summary>
    internal Func<string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>Set after a successful import.</summary>
    public ImportReport? Result { get; private set; }

    public ImportWindow(DataEditorViewModel editor, EditorSettings? settings = null)
    {
        Session = new ImportSession(editor, settings);
        Title = $"Import into {editor.ImportDestination}";
        Width = 1120;
        Height = 760;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var choose = new Button { Content = "Choose file…", Tag = "import-choose", MinHeight = 34 };
        choose.Click += async (_, _) => await ChooseAsync();

        foreach (var (label, _) in ImportSource.DelimiterChoices) _delimiter.Items.Add(label);
        _delimiter.SelectedIndex = 0;
        foreach (var name in ImportSource.EncodingChoices) _encoding.Items.Add(name);
        _encoding.SelectedIndex = 0;
        foreach (var (text, _) in Modes) _mode.Items.Add(text);
        _mode.SelectedIndex = 0;
        _match.ItemsSource = new[] { "By header name", "By position", "By hand (pick per column)" };
        _match.SelectedIndex = 0;
        _rowMatch.ItemsSource = new[] { "By Data ID", "By position (row 1 to row 1…)" };
        _rowMatch.SelectedIndex = 0;
        _step.ItemsSource = editor.ImportStepNames;
        _kind.ItemsSource = new[] { "Request value", "Expected value" };
        _kind.SelectedIndex = 0;
        _verb.ItemsSource = DataEditorViewModel.AssertionVerbs;
        _verb.SelectedItem = "eq";
        _placement.ItemsSource = new[] { "At the end", "Before the selected column", "After the selected column" };
        _placement.SelectedIndex = 0;

        _csvOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Children = { Labeled("Delimiter", _delimiter), Labeled("Encoding", _encoding) } };
        var options = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Children = { Pad(Labeled("Sheet", _sheet)), Pad(Labeled("Range", _range)), Pad(Labeled("Header row (0 = none)", _headerRow)), Pad(_csvOptions) } };
        var fileRow = new DockPanel { Children = { choose, new Border { Margin = new Thickness(10, 0, 0, 0), Child = _file } } };
        var top = new StackPanel { Children = { fileRow, options } };
        DockPanel.SetDock(top, Dock.Top);

        _rowMatchBox = Labeled("Match existing rows", _rowMatch);
        _stepBox = Labeled("Step of new columns", _step);
        _kindBox = Labeled("Kind of new columns", _kind);
        _verbBox = Labeled("Compare with (expected)", _verb);
        _newColumnBox = Labeled("Place new columns", _placement);
        var side = new StackPanel
        {
            Spacing = 10, Margin = new Thickness(14, 0, 0, 0), Width = 300,
            Children =
            {
                new TextBlock { Text = "WHAT TO DO", FontWeight = FontWeight.Bold, FontSize = 12 },
                Labeled("Into " + editor.ImportDestination, _mode), Labeled("Match the columns", _match), _rowMatchBox,
                _create, _clear, _stepBox, _kindBox, _verbBox, _newColumnBox,
                new TextBlock { Text = "Tick the rows and columns to take in the preview. Rows of the file below the header row are the data rows.", Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap },
            },
        };
        var sideScroll = new ScrollViewer { Content = side, Width = 320 };
        DockPanel.SetDock(sideScroll, Dock.Right);

        var close = new Button { Content = "Close", MinWidth = 80, IsCancel = true, Tag = "import-close" };
        close.Click += (_, _) => Close();
        _run.Click += async (_, _) => await RunAsync();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { close, _run } };
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0), Children = { _impact, _error, buttons } };
        DockPanel.SetDock(bottom, Dock.Bottom);

        var pick = new WrapPanel { Margin = new Thickness(0, 10, 0, 6) };
        pick.Children.Add(Small("All columns", "import-all-columns", () => TickColumns(_ => true)));
        pick.Children.Add(Small("No columns", "import-no-columns", () => TickColumns(_ => false)));
        pick.Children.Add(Pad(_columnFilter));
        pick.Children.Add(Small("All rows", "import-all-rows", () => TickRows(_ => true)));
        pick.Children.Add(Small("No rows", "import-no-rows", () => TickRows(_ => false)));
        var center = new DockPanel { Children = { pick, _previewNote, new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _previewHost } } };
        DockPanel.SetDock(pick, Dock.Top);
        DockPanel.SetDock(_previewNote, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(16), Children = { top, bottom, sideScroll, center } };

        _sheet.SelectionChanged += (_, _) => { if (!_loading) { Session.SheetIndex = Math.Max(0, _sheet.SelectedIndex); Session.HeaderRow = Session.Source is null ? 1 : ImportSession.GuessHeaderRow(Session.Source.Sheets[Session.SheetIndex]); Reload(); } };
        _range.TextChanged += (_, _) => { if (!_loading) { Session.RangeText = _range.Text ?? string.Empty; Reload(); } };
        _headerRow.ValueChanged += (_, _) => { if (!_loading) { Session.HeaderRow = (int)(_headerRow.Value ?? 0); Reload(); } };
        _delimiter.SelectionChanged += (_, _) => { if (!_loading && Session.FilePath is not null) { Session.Delimiter = ImportSource.DelimiterChoices[Math.Max(0, _delimiter.SelectedIndex)].Value; ReopenFile(); } };
        _encoding.SelectionChanged += (_, _) => { if (!_loading && Session.FilePath is not null) { Session.Encoding = _encoding.SelectedItem as string ?? "Auto"; ReopenFile(); } };
        _columnFilter.TextChanged += (_, _) => { if (_columnFilter.Text is { Length: > 0 } text && Session.Table is { } table) TickColumns(i => table.Headers[i].Contains(text, StringComparison.OrdinalIgnoreCase)); };
        foreach (var box in new[] { _mode, _match, _rowMatch, _step, _kind, _verb, _placement })
            box.SelectionChanged += (_, _) => { if (!_loading) ReadChoices(); };
        _create.IsCheckedChanged += (_, _) => { if (!_loading) ReadChoices(); };
        _clear.IsCheckedChanged += (_, _) => { if (!_loading) ReadChoices(); };
        UpdateVisibility();
    }

    private static Control Pad(Control child) => new Border { Margin = new Thickness(0, 0, 12, 6), Child = child };

    private static StackPanel Labeled(string label, Control box) => new() { Spacing = 2, Children = { new TextBlock { Text = label, Foreground = Brush.Parse("#8EA4BC"), FontSize = 11 }, box } };

    private Button Small(string text, string tag, Action click)
    {
        var button = new Button { Content = text, Tag = tag, Margin = new Thickness(0, 0, 8, 6), MinHeight = 30 };
        button.Click += (_, _) => click();
        return button;
    }

    // ---- File ------------------------------------------------------------------------------------------------

    private async Task ChooseAsync()
    {
        if (StorageProvider is not { } storage) return;
        var types = new[]
        {
            new FilePickerFileType("Excel and CSV files") { Patterns = ["*.xlsx", "*.xlsm", "*.csv", "*.txt"] },
            new FilePickerFileType("All files") { Patterns = ["*"] },
        };
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import from Excel or CSV", FileTypeFilter = types, AllowMultiple = false });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) LoadFile(path);
    }

    /// <summary>Opens a file (also used by the tests).</summary>
    public void LoadFile(string path)
    {
        _error.Text = Session.Open(path);
        _file.Text = Session.FilePath is null ? "No file chosen" : $"{Path.GetFileName(Session.FilePath)}" + (Session.Source?.Truncated is { } cut ? $"  —  {cut}" : string.Empty);
        ShowSession();
    }

    private void ReopenFile()
    {
        _error.Text = Session.Reopen();
        ShowSession();
    }

    /// <summary>Puts the session's state into the controls.</summary>
    private void ShowSession()
    {
        _loading = true;
        try
        {
            var source = Session.Source;
            _csvOptions.IsVisible = source?.IsCsv == true;
            _sheet.ItemsSource = source?.Sheets.Select(s => s.Name).ToList();
            _sheet.IsEnabled = source is { IsCsv: false };
            _sheet.SelectedIndex = source is null ? -1 : Session.SheetIndex;
            _range.Text = Session.RangeText;
            _headerRow.Value = Session.HeaderRow;
            _mode.SelectedIndex = Array.FindIndex(Modes, m => m.Mode == Session.Mode);
            _match.SelectedIndex = (int)Session.Match;
            _rowMatch.SelectedIndex = (int)Session.RowMatch;
            _create.IsChecked = Session.CreateUnmatched;
            _clear.IsChecked = Session.ClearOnEmpty;
            _step.SelectedItem = Session.Step;
            _kind.SelectedIndex = Session.Expected ? 1 : 0;
            _verb.SelectedItem = Session.Verb;
            _placement.SelectedIndex = (int)Session.Placement;
        }
        finally { _loading = false; }
        Rebuild();
    }

    private void Reload()
    {
        Session.Refresh();
        Rebuild();
    }

    private void ReadChoices()
    {
        Session.Mode = Modes[Math.Max(0, _mode.SelectedIndex)].Mode;
        Session.Match = (ImportMatch)Math.Max(0, _match.SelectedIndex);
        Session.RowMatch = (ImportRowMatch)Math.Max(0, _rowMatch.SelectedIndex);
        Session.CreateUnmatched = _create.IsChecked == true;
        Session.ClearOnEmpty = _clear.IsChecked == true;
        Session.Step = _step.SelectedItem as string;
        Session.Expected = _kind.SelectedIndex == 1;
        Session.Verb = _verb.SelectedItem as string ?? "eq";
        Session.Placement = (ImportPlacement)Math.Max(0, _placement.SelectedIndex);
        Rebuild();
    }

    private void TickColumns(Func<int, bool> chosen) { Session.TickColumnsWhere(chosen); Rebuild(); }
    private void TickRows(Func<int, bool> chosen) { Session.TickRowsWhere(chosen); Rebuild(); }

    // ---- Preview ---------------------------------------------------------------------------------------------

    private void UpdateVisibility()
    {
        var editor = Session.Editor;
        var fills = Session.Mode is ImportMode.FillExistingRows or ImportMode.NewColumns;
        _rowMatchBox.IsVisible = fills && editor.ImportHasDataId;
        _clear.IsVisible = Session.Mode == ImportMode.FillExistingRows;
        _create.IsVisible = Session.Mode is ImportMode.AppendRows or ImportMode.InsertRowsBelow or ImportMode.ReplaceRows && Session.Match != ImportMatch.Manual;
        var makesColumns = Session.Mode == ImportMode.NewColumns || (_create.IsVisible && Session.CreateUnmatched) || Session.Match == ImportMatch.Manual;
        _stepBox.IsVisible = makesColumns && editor.ImportHasDataId;
        _kindBox.IsVisible = makesColumns && editor.ImportHasDataId;
        _verbBox.IsVisible = makesColumns && editor.ImportHasDataId && Session.Expected;
        _newColumnBox.IsVisible = Session.Mode == ImportMode.NewColumns;
    }

    private void Rebuild()
    {
        UpdateVisibility();
        _error.Text = Session.RangeError ?? _error.Text;
        var table = Session.Table;
        if (table is null) { _previewHost.Child = null; _previewNote.Text = string.Empty; _impact.Text = string.Empty; _run.IsEnabled = false; return; }
        if (Session.RangeError is null && _error.Text is { Length: > 0 } && !_error.Text.StartsWith("Could not")) _error.Text = string.Empty;

        var labels = Session.Labels();
        var targets = Session.Targets;
        var columns = Math.Min(table.ColumnCount, PreviewColumns);
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 120 });
        var shown = Math.Min(table.Rows.Count, PreviewRows);
        for (var r = 0; r < shown + 1; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        grid.Children.Add(Place(new TextBlock { Text = "row", Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, Margin = new Thickness(4, 2, 8, 2) }, 0, 1));
        for (var c = 0; c < columns; c++)
        {
            var index = c;
            var tick = new CheckBox { IsChecked = Session.TakeColumn[c], Tag = "import-column-tick", Content = new TextBlock { Text = table.Headers[c].Length == 0 ? $"({ImportRange.Letter(table.ColumnNumbers[c])})" : table.Headers[c], FontWeight = FontWeight.SemiBold } };
            tick.IsCheckedChanged += (_, _) => { Session.TakeColumn[index] = tick.IsChecked == true; RefreshSummary(); };
            Control target;
            if (Session.Match == ImportMatch.Manual && Session.Mode != ImportMode.NewColumns)
            {
                var combo = new ComboBox { Tag = "import-column-target", FontSize = 11, MinWidth = 110 };
                var choices = new List<(string Text, int Key)> { ("skip", ImportPlan.Skip), ("new column", ImportPlan.Create) };
                choices.AddRange(targets.Select(t => (t.Display, t.Key)));
                combo.ItemsSource = choices.Select(x => x.Text).ToList();
                combo.SelectedIndex = Math.Max(0, choices.FindIndex(x => x.Key == Session.Manual[index]));
                combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { Session.Manual[index] = choices[combo.SelectedIndex].Key; RefreshSummary(); } };
                target = combo;
            }
            else target = new TextBlock { Text = c < labels.Length ? labels[c] : string.Empty, FontSize = 11, Foreground = Brush.Parse(c < labels.Length && labels[c] == "skipped" ? "#6C7A8A" : "#7DD3A8"), Tag = "import-column-label" };
            var header = new StackPanel { Margin = new Thickness(4, 2, 10, 4), Children = { tick, target } };
            grid.Children.Add(Place(header, 0, c + 2));
        }
        for (var r = 0; r < shown; r++)
        {
            var index = r;
            var row = table.Rows[r];
            var tick = new CheckBox { IsChecked = Session.TakeRow[r], Tag = "import-row-tick", Content = row.Row.ToString(), FontSize = 11 };
            tick.IsCheckedChanged += (_, _) => { Session.TakeRow[index] = tick.IsChecked == true; RefreshSummary(); };
            grid.Children.Add(Place(tick, r + 1, 0));
            for (var c = 0; c < columns; c++)
            {
                var text = c < row.Cells.Length ? row.Cells[c] : string.Empty;
                var cell = new TextBlock { Text = text.Length > 40 ? text[..40] + "…" : text, Margin = new Thickness(4, 2, 10, 2), FontSize = 12 };
                if (text.Length > 40) ToolTip.SetTip(cell, text);
                grid.Children.Add(Place(cell, r + 1, c + 2));
            }
        }
        _previewHost.Child = grid;
        _previewNote.Text = $"{table.Rows.Count} data {(table.Rows.Count == 1 ? "row" : "rows")} and {table.ColumnCount} {(table.ColumnCount == 1 ? "column" : "columns")} in the chosen part"
            + (table.Rows.Count > PreviewRows || table.ColumnCount > PreviewColumns ? $"; the preview shows the first {Math.Min(PreviewRows, table.Rows.Count)} rows and {columns} columns, the tick buttons above reach all of them." : ".");
        RefreshSummary();
    }

    private static Control Place(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    /// <summary>The impact line and the Import button, after any change of ticks or choices.</summary>
    private void RefreshSummary()
    {
        UpdateVisibility();
        var plan = Session.BuildPlan();
        _impact.Text = plan is null ? string.Empty : $"{plan.TakenRowCount} of {plan.TakeRow.Length} rows and {plan.TakeColumn.Count(t => t)} of {plan.TakeColumn.Length} columns ticked. {Session.Impact()}";
        _impact.Foreground = Brush.Parse(Session.NeedsConfirmation ? "#FFB86B" : "#C8D3E0");
        _run.IsEnabled = plan is not null && (plan.TakenRowCount > 0 || plan.Mode == ImportMode.NewColumns);
        _run.Content = Session.NeedsConfirmation ? "Replace…" : "Import";
        var labels = Session.Labels();
        if (Session.Match != ImportMatch.Manual || Session.Mode == ImportMode.NewColumns)
            foreach (var block in _previewHost.GetVisualDescendants().OfType<TextBlock>().Where(b => Equals(b.Tag, "import-column-label")).Select((b, i) => (b, i)))
            {
                if (block.i >= labels.Length) break;
                block.b.Text = labels[block.i];
                block.b.Foreground = Brush.Parse(labels[block.i] == "skipped" ? "#6C7A8A" : "#7DD3A8");
            }
    }

    // ---- Run -------------------------------------------------------------------------------------------------

    /// <summary>Runs the import; replace modes ask first. Closes the window when it worked.</summary>
    public async Task<bool> RunAsync()
    {
        if (Session.BuildPlan() is not { } plan) return false;
        if (DataEditorViewModel.NeedsConfirmation(plan))
        {
            var title = plan.Mode == ImportMode.ReplaceRows ? "Replace the rows?" : "Clear cells?";
            var go = Confirm is not null ? await Confirm(title, Session.Impact()) : await AskAsync(title, Session.Impact(), plan.Mode == ImportMode.ReplaceRows ? "Replace" : "Import and clear");
            if (!go) return false;
        }
        var report = Session.Run();
        if (report is null) return false;
        if (report.Error is not null) { _error.Text = report.Error; return false; }
        Result = report;
        Close();
        return true;
    }

    private async Task<bool> AskAsync(string title, string detail, string yesText)
    {
        var yes = new Button { Content = yesText, Classes = { "primary" } };
        var no = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        var dialog = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 14,
                Children =
                {
                    new TextBlock { Text = detail + "\n\nOne Undo brings the old rows back; nothing is saved until you save.", TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } },
                },
            },
        };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(this);
    }
}
