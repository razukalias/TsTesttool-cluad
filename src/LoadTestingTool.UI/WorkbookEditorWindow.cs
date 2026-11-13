using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClosedXML.Excel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LoadTestingTool.UI;

public sealed class WorkbookEditorWindow : Window
{
    private readonly string _path;
    private readonly Dictionary<string, SheetDraft> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _sheetHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly TabControl _tabs = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _save;
    private readonly TextBox _addColumnsCount = new() { Text = "10", Width = 52 };
    private readonly TextBox _addRowsCount = new() { Text = "10", Width = 52 };
    public string? SavedBackup { get; private set; }

    public WorkbookEditorWindow(string workbookPath, string? instanceName = null)
    {
        _path = workbookPath;
        Title = $"DataEngine Editor — {Path.GetFileName(workbookPath)}";
        Width = 1250; Height = 920; MinWidth = 900; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        using var workbookStream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var workbook = new XLWorkbook(workbookStream);
        var sheetHeaders = workbook.Worksheets.ToDictionary(x => x.Name,
            x => x.FirstRowUsed()?.CellsUsed().Select(c => c.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var pair in sheetHeaders) _sheetHeaders[pair.Key] = pair.Value;
        var orderedSheetNames = new[] { "config", "request", "response", "README" }
            .Concat(workbook.Worksheets.Select(x => x.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in orderedSheetNames)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sheet is null) continue;
            var draft = new SheetDraft(sheet, name, sheetHeaders, SetStatus, () => Save(close: false));
            _sheets[name] = draft; _tabs.Items.Add(new TabItem { Header = name, Content = draft.View });
        }
        _save = DashboardStyle.Action("Save workbook", () => Save(), "primary");
        var top = new StackPanel { Spacing = 4, Children = {
            DashboardStyle.Text($"DataEngine Editor — Instance: {Display(instanceName)}", 21, true),
            DashboardStyle.Text($"Workbook: {Path.GetFileName(workbookPath)}", 12, false, DashboardStyle.Muted),
            DashboardStyle.Text("Excel-style selection: click A/B/C column indexes or row numbers; Ctrl adds and Shift selects ranges. Edit headers directly. Drag column borders to resize. Type < or { for autocomplete; Ctrl+Space opens it.", 12, false, DashboardStyle.Muted) } };
        _status.Foreground = DashboardStyle.Red;
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children = { DashboardStyle.Action("Help", ShowHelp), DashboardStyle.Action("New sheet", AddNewSheet), DashboardStyle.Action("Rename sheet", RenameCurrentSheet), DashboardStyle.Action("Add test step", () => OpenStepWizard(false), "primary"), DashboardStyle.Action("Edit selected step", () => OpenStepWizard(true)), DashboardStyle.Action("Import", ShowImportMenu), DashboardStyle.Action("Open raw workbook", () => SystemDefaultFileOpener.Open(_path)) } };
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Children = { DashboardStyle.Text("Columns", 11, false, DashboardStyle.Muted), _addColumnsCount, DashboardStyle.Action("Add columns at end", AddColumnsToCurrentSheet), DashboardStyle.Text("Rows", 11, false, DashboardStyle.Muted), _addRowsCount, DashboardStyle.Action("Add rows at end", AddRowsToCurrentSheet), DashboardStyle.Action("Cancel", () => Close(null)), _save } };
        var buttons = new StackPanel { Spacing = 6, Children = { row1, row2 } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(18) };
        root.Children.Add(top); Grid.SetRow(_tabs, 1); root.Children.Add(_tabs); Grid.SetRow(_status, 2); root.Children.Add(_status); Grid.SetRow(buttons, 3); root.Children.Add(buttons); Content = root;
        AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control && e.Key == Key.S)
            {
                Save(close: false); e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        _tabs.SelectedIndex = 0;
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private void AddNewSheet()
    {
        var index = 1;
        string name;
        do name = $"Sheet{index++}"; while (_sheets.ContainsKey(name));
        using var temporary = new XLWorkbook();
        var source = temporary.AddWorksheet(name); source.Cell(1, 1).Value = "Column1";
        var draft = new SheetDraft(source, name, _sheetHeaders, SetStatus, () => Save(close: false));
        _sheets[name] = draft;
        _tabs.Items.Add(new TabItem { Header = name, Content = draft.View });
        _tabs.SelectedIndex = _tabs.ItemCount - 1;
        SetStatus($"Created new sheet '{name}'. Press Ctrl+S to save it without closing the editor.");
    }

    private void ShowHelp()
    {
        ContextHelpWindow.Show(this, "DataEngine editor help", "Sheets: config controls testcase and step execution; request contains input rows; response contains expected values and assertions; README contains guidance.\n\nSelection: click a column letter or row number. Ctrl-click adds another selection. Shift-click selects a range. Click inside a cell to edit its text. Headers can be edited like normal cells.\n\nAutocomplete: type < for functions and From_ references, or { for assertion verbs. Ctrl+Space opens suggestions. Enter, Tab, or clicking a suggestion inserts it.\n\nOperations: use the row and column indexes for copy, paste, insert, delete, wrap, highlight, and auto-fit. The Add columns/rows at end controls use the quantities shown beside them.\n\nImports: Excel lets you choose individual sheets; JSON and XML are flattened into a new sheet. Press Ctrl+S to save and create a backup without closing the editor. The Save workbook button saves and closes.");
    }

    private void AddRowToCurrentSheet()
    {
        AddRowsToCurrentSheet(1);
    }

    private void AddRowsToCurrentSheet() => AddRowsToCurrentSheet(ParseQuantity(_addRowsCount.Text));
    private void AddRowsToCurrentSheet(int count) { if (CurrentSheet() is { } draft) draft.AddRowsAtEnd(count); }
    private void AddColumnsToCurrentSheet() { if (CurrentSheet() is { } draft) draft.AddColumnsAtEnd(ParseQuantity(_addColumnsCount.Text)); }
    private static int ParseQuantity(string? text) => int.TryParse(text, out var value) ? Math.Clamp(value, 1, 10000) : 10;
    private SheetDraft? CurrentSheet() => _tabs.SelectedItem is TabItem tab && tab.Header is string name && _sheets.TryGetValue(name, out var draft) ? draft : null;

    private void ShowImportMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(ImportItem("Import Excel workbook (.xlsx)", () => _ = ImportExcelAsync()));
        menu.Items.Add(ImportItem("Import JSON (.json, flattened)", () => _ = ImportJsonAsync()));
        menu.Items.Add(ImportItem("Import XML (.xml, flattened)", () => _ = ImportXmlAsync()));
        menu.Open(this);
    }
    private static MenuItem ImportItem(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); return item; }

    private async Task ImportExcelAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import Excel workbook", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("Excel workbook") { Patterns = new[] { "*.xlsx", "*.xlsm" } } } });
        if (files.Count == 0) return;
        try
        {
            using var workbook = new XLWorkbook(files[0].Path.LocalPath);
            var selected = await ChooseExcelSheetsAsync(workbook.Worksheets.Select(x => x.Name).ToArray());
            if (selected is null || selected.Count == 0) return;
            foreach (var sheet in workbook.Worksheets.Where(x => selected.Contains(x.Name))) AddImportedSheet(sheet.Name, SheetTable(sheet));
            SetStatus($"Imported {selected.Count} selected Excel sheet(s). Press Ctrl+S to save.");
        }
        catch (Exception ex) { SetStatus($"Excel import failed: {ex.Message}", true); }
    }

    private async Task<HashSet<string>?> ChooseExcelSheetsAsync(IReadOnlyList<string> names)
    {
        var checks = names.Select(name => new CheckBox { Content = name, IsChecked = true, Margin = new Thickness(4) }).ToList();
        var dialog = new Window { Title = "Choose Excel sheets to import", Width = 460, Height = 520, MinWidth = 360, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var listPanel = new StackPanel();
        foreach (var check in checks) listPanel.Children.Add(check);
        var list = new ScrollViewer { Content = listPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var selectAll = DashboardStyle.Action("Select all", () => { foreach (var check in checks) check.IsChecked = true; });
        var selectNone = DashboardStyle.Action("Select none", () => { foreach (var check in checks) check.IsChecked = false; });
        var cancel = DashboardStyle.Action("Cancel", () => dialog.Close(false));
        var import = DashboardStyle.Action("Import selected", () => dialog.Close(true), "primary");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, import } };
        var dialogGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16) };
        dialogGrid.Children.Add(new StackPanel { Spacing = 6, Children = { DashboardStyle.Text("Select the worksheets to import", 17, true), DashboardStyle.Text("Each selected worksheet will be added as a new sheet.", 11, false, DashboardStyle.Muted), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { selectAll, selectNone } } } });
        dialogGrid.Children.Add(list);
        dialogGrid.Children.Add(footer);
        Grid.SetRow(list, 1); Grid.SetRow(footer, 2);
        dialog.Content = dialogGrid;
        /* legacy layout shape retained in source history:
        new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16), Children =
        {
            new StackPanel { Spacing = 6, Children = { DashboardStyle.Text("Select the worksheets to import", 17, true), DashboardStyle.Text("Each selected worksheet will be added as a new sheet.", 11, false, DashboardStyle.Muted), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { selectAll, selectNone } } } },
            list,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, import } }
        }}; */
        var accepted = await dialog.ShowDialog<bool?>(this);
        return accepted == true ? checks.Where(x => x.IsChecked == true).Select(x => x.Content?.ToString() ?? "").Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
    }

    private async Task ImportJsonAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import JSON", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } } });
        if (files.Count == 0) return;
        try { var table = FlattenJson(await File.ReadAllTextAsync(files[0].Path.LocalPath)); AddImportedSheet(Path.GetFileNameWithoutExtension(files[0].Name), table); SetStatus($"Imported flattened JSON as a new sheet. Press Ctrl+S to save."); }
        catch (Exception ex) { SetStatus($"JSON import failed: {ex.Message}", true); }
    }

    private async Task ImportXmlAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import XML", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("XML") { Patterns = new[] { "*.xml" } } } });
        if (files.Count == 0) return;
        try { var table = FlattenXml(await File.ReadAllTextAsync(files[0].Path.LocalPath)); AddImportedSheet(Path.GetFileNameWithoutExtension(files[0].Name), table); SetStatus($"Imported flattened XML as a new sheet. Press Ctrl+S to save."); }
        catch (Exception ex) { SetStatus($"XML import failed: {ex.Message}", true); }
    }

    private void AddImportedSheet(string baseName, (string[] Headers, List<List<string>> Rows) table)
    {
        var name = UniqueSheetName(baseName); var values = new List<List<string>> { table.Headers.ToList() }; values.AddRange(table.Rows.Select(r => r.Concat(Enumerable.Repeat("", Math.Max(0, table.Headers.Length - r.Count))).Take(table.Headers.Length).ToList()));
        using var temporary = new XLWorkbook(); var source = temporary.AddWorksheet(name); for (var r = 0; r < values.Count; r++) for (var c = 0; c < values[r].Count; c++) source.Cell(r + 1, c + 1).Value = values[r][c];
        var draft = new SheetDraft(source, name, _sheetHeaders, SetStatus, () => Save(close: false)); _sheets[name] = draft; _tabs.Items.Add(new TabItem { Header = name, Content = draft.View }); _tabs.SelectedIndex = _tabs.ItemCount - 1;
    }
    private string UniqueSheetName(string baseName) { var clean = string.IsNullOrWhiteSpace(baseName) ? "ImportedData" : new string(baseName.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray()); if (clean.Length > 31) clean = clean[..31]; var name = clean; var i = 2; while (_sheets.ContainsKey(name)) { var suffix = $"_{i++}"; name = clean[..Math.Min(clean.Length, 31 - suffix.Length)] + suffix; } return name; }
    private static (string[] Headers, List<List<string>> Rows) SheetTable(IXLWorksheet sheet) { var used = sheet.RangeUsed(); if (used is null) return (["Column1"], new List<List<string>> { new() { "" } }); var rows = used.Rows().Select(r => r.Cells().Select(c => c.GetString()).ToList()).ToList(); var width = rows.Max(r => r.Count); return (rows[0].Concat(Enumerable.Repeat("", width - rows[0].Count)).ToArray(), rows.Skip(1).Select(r => r.Concat(Enumerable.Repeat("", width - r.Count)).ToList()).ToList()); }
    private static (string[] Headers, List<List<string>> Rows) FlattenJson(string text)
    {
        using var document = JsonDocument.Parse(text); var root = document.RootElement; var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : root.ValueKind == JsonValueKind.Object && root.EnumerateObject().FirstOrDefault(p => p.Value.ValueKind == JsonValueKind.Array).Value.ValueKind == JsonValueKind.Array ? root.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Array).Value.EnumerateArray().ToList() : new List<JsonElement> { root }; var maps = items.Select(FlattenJsonElement).ToList(); var headers = maps.SelectMany(m => m.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); return (headers.Length == 0 ? ["value"] : headers, maps.Select(m => headers.Select(h => m.TryGetValue(h, out var v) ? v : "").ToList()).ToList());
    }
    private static Dictionary<string, string> FlattenJsonElement(JsonElement element)
    { var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); FlattenJsonValue(element, "", result); return result; }
    private static void FlattenJsonValue(JsonElement value, string path, Dictionary<string, string> result)
    { if (value.ValueKind == JsonValueKind.Object) foreach (var p in value.EnumerateObject()) FlattenJsonValue(p.Value, string.IsNullOrEmpty(path) ? p.Name : $"{path}.{p.Name}", result); else if (value.ValueKind == JsonValueKind.Array) { var i = 0; foreach (var item in value.EnumerateArray()) FlattenJsonValue(item, $"{path}[{i++}]", result); } else result[path.Length == 0 ? "value" : path] = value.ValueKind == JsonValueKind.Null ? "" : value.ToString(); }
    private static (string[] Headers, List<List<string>> Rows) FlattenXml(string text)
    { var root = XDocument.Parse(text).Root ?? throw new InvalidDataException("XML has no root element."); var repeated = root.Elements().GroupBy(x => x.Name.LocalName).FirstOrDefault(g => g.Count() > 1)?.ToList(); var records = repeated is { Count: > 0 } ? repeated : new List<XElement> { root }; var maps = records.Select(x => FlattenXmlElement(x, x == root ? "" : x.Name.LocalName)).ToList(); var headers = maps.SelectMany(m => m.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); return (headers.Length == 0 ? ["value"] : headers, maps.Select(m => headers.Select(h => m.TryGetValue(h, out var v) ? v : "").ToList()).ToList()); }
    private static Dictionary<string, string> FlattenXmlElement(XElement element, string path) { var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); foreach (var attr in element.Attributes()) result[$"{path}.@{attr.Name.LocalName}".TrimStart('.')] = attr.Value; var children = element.Elements().ToList(); if (children.Count == 0) result[path.Length == 0 ? element.Name.LocalName : path] = element.Value; else foreach (var group in children.GroupBy(x => x.Name.LocalName)) { var i = 0; foreach (var child in group) { var childPath = group.Count() > 1 ? $"{path}.{child.Name.LocalName}[{i++}]".TrimStart('.') : $"{path}.{child.Name.LocalName}".TrimStart('.'); foreach (var pair in FlattenXmlElement(child, childPath)) result[pair.Key] = pair.Value; } } return result; }

    private void RenameCurrentSheet()
    {
        if (_tabs.SelectedItem is not TabItem tab || tab.Content is not Control) return;
        var oldName = tab.Header?.ToString() ?? "Sheet";
        var input = new TextBox { Text = oldName, Width = 300 };
        var dialog = new Window { Title = "Rename sheet", Width = 420, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = DashboardStyle.Action("Rename", () => { }, "primary");
        ok.Click += (_, _) =>
        {
            var newName = input.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(newName) || newName.Equals(oldName, StringComparison.OrdinalIgnoreCase)) { dialog.Close(); return; }
            if (_sheets.Keys.Any(x => x.Equals(newName, StringComparison.OrdinalIgnoreCase))) { SetStatus($"A sheet named '{newName}' already exists.", true); return; }
            var draft = _sheets[oldName]; _sheets.Remove(oldName); _sheets[newName] = draft; draft.Rename(newName); tab.Header = newName; dialog.Close(); SetStatus($"Renamed sheet '{oldName}' to '{newName}'. Press Ctrl+S to save it.");
        };
        var cancel = DashboardStyle.Action("Cancel", () => dialog.Close());
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { DashboardStyle.Text("New sheet name", 14, true), input, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, ok } } } };
        dialog.Show(this);
        input.Focus(); input.SelectAll();
    }

    private void SetStatus(string message, bool error = false) { _status.Text = message; _status.Foreground = error ? DashboardStyle.Red : DashboardStyle.Muted; }

    private void OpenStepWizard(bool edit)
    {
        if (_tabs.SelectedItem is not TabItem tab || tab.Header is not string sheetName || !sheetName.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Select the config sheet before using the test-step wizard.", true); return;
        }
        if (!_sheets.TryGetValue(sheetName, out var draft)) return;
        var existing = edit ? draft.SelectedConfigStep() : null;
        if (edit && existing is null) { SetStatus("Select a config data row first, then choose Edit selected step.", true); return; }
        var wizard = new TestStepWizardWindow(step => { draft.ApplyConfigStep(step); SetStatus(edit ? "Test step updated. Press Ctrl+S to save." : "Test step added. Press Ctrl+S to save."); }, existing, Path.Combine(Path.GetDirectoryName(_path)!, "Templates"), Path.GetFileName(Path.GetDirectoryName(_path)!));
        wizard.Show(this);
    }

    private void Save(bool close = true)
    {
        try
        {
            foreach (var draft in _sheets.Values) draft.Validate();
            using var workbook = new XLWorkbook(_path);
            foreach (var (name, draft) in _sheets.ToArray())
            {
                var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(draft.OriginalName, StringComparison.OrdinalIgnoreCase)) ?? workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? workbook.AddWorksheet(name);
                if (!sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) sheet.Name = name;
                draft.WriteTo(sheet);
            }
            WorkbookSynchronization.SynchronizeRequestResponse(workbook);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
            SavedBackup = $"{_path}.backup.{stamp}.xlsx";
            File.Copy(_path, SavedBackup, overwrite: false); workbook.Save(); SetStatus(close ? $"Saved workbook. Backup: {Path.GetFileName(SavedBackup)}" : $"Saved workbook and backup. Editor remains open. Backup: {Path.GetFileName(SavedBackup)}");
            if (close) Close();
        }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    private sealed class SheetDraft
    {
        private static readonly string[] AssertionVerbs = { "eq", "ne", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "gt", "gte", "lt", "lte", "in", "size" };
        private static readonly string[] Functions = { "<From_", "<datum>", "<currentdatum>", "<currenttime>", "<currentdatetime>", "<currenttimestamp>", "<randomnumber>", "<randomnumber:1-100>", "<guid>", "<guid:N>" };
        private readonly string _sheetName;
        private readonly Dictionary<string, string[]> _allHeaders;
        private readonly Action<string, bool> _status;
        private readonly Action _saveWorkbook;
        public string OriginalName { get; }
        public string SheetName { get; private set; }
        private readonly List<List<string>> _values = [];
        private readonly List<double> _widths = [];
        private readonly Button _transposeButton;
        private readonly StackPanel _rows = new() { Spacing = 1 };
        private readonly TextBox _columnFilter = new() { Watermark = "Headers: header1 & header2…", Width = 280 };
        private readonly TextBox _rowFilter = new() { Watermark = "Rows: 0,1,2…", Width = 130 };
        private readonly TextBox _cellValueFilter = new() { Watermark = "Cell values: value1 & value2…", Width = 250 };
        private readonly TextBlock _position = DashboardStyle.Text("Select a cell, row, or column", 11, false, DashboardStyle.Muted);
        private readonly ComboBox _zoom = new() { Width = 90, ItemsSource = new[] { "80%", "90%", "100%", "110%", "125%", "150%" }, SelectedIndex = 2 };
        private readonly Popup _suggestionPopup = new() { PlacementMode = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = false };
        private readonly ListBox _suggestions = new() { MaxHeight = 220, MinWidth = 280 };
        private readonly HashSet<int> _selectedRows = [];
        private readonly HashSet<int> _selectedColumns = [];
        private readonly HashSet<(int Row, int Column)> _selectedCells = [];
        private readonly Dictionary<(int Row, int Column), bool> _wrap = [];
        private readonly Dictionary<(int Row, int Column), string?> _cellColors = [];
        private readonly Dictionary<(int Row, int Column), TextBox> _cellBoxes = [];
        private readonly Dictionary<int, TextBlock> _rowHeaders = [];
        private int[] _resizeColumns = [];
        private bool _resizing;
        private double _resizeLastX;
        private int _selectedRow = -1, _selectedColumn = -1, _anchorRow = -1, _anchorColumn = -1;
        private bool _updatingSuggestions, _selectingSuggestion;
        private TextBox? _activeBox;
        private IReadOnlyList<int> _visibleColumns = [];
        private List<List<string>>? _copiedRows;
        private List<List<string>>? _copiedColumns;
        private List<List<string>>? _copiedCells;
        private List<List<List<string>>> _undo = [];
        private List<List<List<string>>> _redo = [];
        private double _fontSize = 11;
        private bool _transposed;
        public Control View { get; }

        public SheetDraft(IXLWorksheet sheet, string name, IReadOnlyDictionary<string, string[]> headers, Action<string, bool> status, Action saveWorkbook)
        {
            _sheetName = name; SheetName = name; OriginalName = name; _allHeaders = new Dictionary<string, string[]>(headers, StringComparer.OrdinalIgnoreCase); _status = status; _saveWorkbook = saveWorkbook;
            var used = sheet.RangeUsed(); var lastRow = used?.RangeAddress.LastAddress.RowNumber ?? 1; var lastColumn = used?.RangeAddress.LastAddress.ColumnNumber ?? 1;
            for (var r = 1; r <= lastRow; r++)
            {
                var row = Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(r, c).GetString()).ToList();
                _values.Add(row);
                for (var c = 1; c <= lastColumn; c++)
                    if (sheet.Cell(r, c).Style.Alignment.WrapText)
                        _wrap[(r - 1, c - 1)] = true;
            }
            if (_values.Count == 0) _values.Add(Enumerable.Repeat("", Math.Max(1, lastColumn)).ToList());
            for (var c = 0; c < _values[0].Count; c++)
            {
                var existingWidth = sheet.Column(c + 1).Width;
                _widths.Add(existingWidth > 0 ? Math.Clamp(existingWidth * 7.5, 100, 700) : 190d);
            }
            var storedFontSize = sheet.CellsUsed().Select(c => c.Style.Font.FontSize).FirstOrDefault(size => size > 0);
            if (storedFontSize > 0) _fontSize = Math.Clamp(storedFontSize, 8, 24);
            _columnFilter.TextChanged += (_, _) => Render();
            _rowFilter.TextChanged += (_, _) => Render();
            _cellValueFilter.TextChanged += (_, _) => Render();
            _zoom.SelectionChanged += (_, _) => { _fontSize = double.TryParse((_zoom.SelectedItem?.ToString() ?? "100%").TrimEnd('%'), out var z) ? 11 * z / 100 : 11; Render(); };
            _suggestions.SelectionChanged += (_, _) => { if (!_updatingSuggestions && _suggestionPopup.IsOpen && _suggestions.SelectedItem is string value) AcceptSuggestion(value); };
            _suggestions.PointerReleased += (_, _) => { if (_suggestions.SelectedItem is string value) AcceptSuggestion(value); };
            _suggestionPopup.Child = _suggestions;
            var help = DashboardStyle.Text("Mouse: use column letters and row numbers; Ctrl adds selections; Shift selects ranges. Drag column borders to resize. Right-click cells, rows, or headers for operations. Wrap, copy, insert, delete, and auto-fit are available in context menus. Filters accept multiple terms with &: headers and cell values match any term; row indexes are zero-based with the header at 0, for example 0,1,2.", 11, false, DashboardStyle.Muted);
            var zoomOut = DashboardStyle.Action("A−", () => ChangeFont(-1)); zoomOut.Padding = new Thickness(7, 5); var zoomIn = DashboardStyle.Action("A+", () => ChangeFont(1)); zoomIn.Padding = new Thickness(7, 5); _transposeButton = DashboardStyle.Action("Transpose view", ToggleTranspose); var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _columnFilter, _rowFilter, _cellValueFilter, DashboardStyle.Text("Zoom", 11, false, DashboardStyle.Muted), _zoom, zoomOut, zoomIn, _transposeButton, _position } };
            var gridScroll = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var table = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Children = { toolbar, help, gridScroll } }; Grid.SetRow(help, 1); Grid.SetRow(gridScroll, 2);
            var editorHost = new Grid { Children = { table, _suggestionPopup } };
            editorHost.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (HandleSelectionClipboardKey(e)) e.Handled = true; }, RoutingStrategies.Tunnel);
            View = new Border { BorderBrush = DashboardStyle.Line, BorderThickness = new Thickness(1), Child = editorHost };
            Render();
        }

        private string[] Headers => _values.Count == 0 ? [] : _values[0].ToArray();
        private string ScaleWidth(double width) => Math.Clamp(width, 100, 700).ToString(System.Globalization.CultureInfo.InvariantCulture);
        private void Render()
        {
            _rows.Children.Clear(); _cellBoxes.Clear(); _rowHeaders.Clear(); var headerTerms = FilterTerms(_columnFilter.Text); var valueTerms = FilterTerms(_cellValueFilter.Text); var rowFilter = ParseRowFilter(_rowFilter.Text);
            if (_transposed) { RenderTransposed(); return; }
            var visibleRows = Enumerable.Range(1, Math.Max(0, _values.Count - 1)).Where(r => !rowFilter.Any() || rowFilter.Contains(r)).ToArray();
            _visibleColumns = Enumerable.Range(0, Headers.Length).Where(i => MatchesAny(Headers[i], headerTerms) && (!valueTerms.Any() || visibleRows.Any(r => MatchesAny(_values[r][i], valueTerms)))).ToArray();
            if (_visibleColumns.Count == 0) { _rows.Children.Add(DashboardStyle.Text("No columns match the active header/value filters. Clear or adjust the filters to restore the grid.", color: DashboardStyle.Muted)); return; }
            AddColumnIndexView(); AddRowView(0, true); foreach (var r in visibleRows) AddRowView(r, false);
        }
        private static string[] FilterTerms(string? text) => (text ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        private static bool MatchesAny(string value, IReadOnlyList<string> terms) => terms.Count == 0 || terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
        private static HashSet<int> ParseRowFilter(string? text)
        {
            var rows = new HashSet<int>();
            foreach (var token in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var range = token.Split('-', StringSplitOptions.TrimEntries);
                if (range.Length == 2 && int.TryParse(range[0], out var first) && int.TryParse(range[1], out var last))
                {
                    for (var row = Math.Max(0, Math.Min(first, last)); row <= Math.Max(first, last); row++) rows.Add(row);
                }
                else if (range.Length == 1 && int.TryParse(token, out var row) && row >= 0) rows.Add(row);
            }
            return rows;
        }

        private void ToggleTranspose()
        {
            _transposed = !_transposed;
            _transposeButton.Content = _transposed ? "Normal view" : "Transpose view";
            _status($"{(_transposed ? "Transpose view is read-only; workbook orientation is unchanged." : "Normal editable view restored.")}", false);
            Render();
        }

        private void RenderTransposed()
        {
            var sourceRows = _values.Count; var sourceColumns = _values.Count == 0 ? 0 : _values[0].Count;
            if (sourceRows == 0 || sourceColumns == 0) return;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90," + string.Join(',', Enumerable.Repeat("180", sourceRows))), Background = DashboardStyle.Brush("#102943") };
            for (var c = 0; c < sourceRows; c++)
            {
                var label = DashboardStyle.Text(c == 0 ? "Header" : $"Row {c}", 11, true); label.Background = DashboardStyle.Brush("#173559"); label.Margin = new Thickness(1); Grid.SetColumn(label, c + 1); grid.Children.Add(label);
            }
            for (var sourceColumn = 0; sourceColumn < sourceColumns; sourceColumn++)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90," + string.Join(',', Enumerable.Repeat("180", sourceRows))) };
                var label = DashboardStyle.Text(ColumnLetter(sourceColumn), 11, true); label.Background = DashboardStyle.Brush("#173559"); label.Margin = new Thickness(1); Grid.SetColumn(label, 0); row.Children.Add(label);
                for (var sourceRow = 0; sourceRow < sourceRows; sourceRow++)
                {
                    var cell = DashboardStyle.Text(_values[sourceRow][sourceColumn], 11, false); cell.TextWrapping = TextWrapping.Wrap; cell.Margin = new Thickness(1); cell.Background = DashboardStyle.Brush("#0B1726"); Grid.SetColumn(cell, sourceRow + 1); row.Children.Add(cell);
                }
                Grid.SetRow(row, sourceColumn); grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); grid.Children.Add(row);
            }
            _rows.Children.Add(new TextBlock { Text = "Virtual transpose view — editing is disabled; use Normal view to edit and save.", Foreground = DashboardStyle.Brush("#C8D7E8"), Margin = new Thickness(4) });
            _rows.Children.Add(grid);
        }

        private void AddColumnIndexView()
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52," + string.Join(',', _visibleColumns.Select(i => ScaleWidth(_widths[i])))), Background = DashboardStyle.Brush("#102943") };
            var corner = DashboardStyle.Text("", 11, true); corner.TextAlignment = TextAlignment.Center; corner.VerticalAlignment = VerticalAlignment.Center; corner.ContextMenu = RowMenu(0, true); Grid.SetColumn(corner, 0); grid.Children.Add(corner);
            for (var n = 0; n < _visibleColumns.Count; n++)
            {
                var column = _visibleColumns[n];
                var index = DashboardStyle.Text(ColumnLetter(column), 11, true); index.TextAlignment = TextAlignment.Center; index.VerticalAlignment = VerticalAlignment.Center; index.Background = _selectedColumns.Contains(column) ? DashboardStyle.Brush("#275B8F") : Brushes.Transparent;
                index.PointerPressed += (_, e) => { if (e.GetCurrentPoint(index).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed) { SelectColumn(column, e); e.Handled = true; } };
                index.ContextMenu = ColumnMenu(column); Grid.SetColumn(index, n + 1); grid.Children.Add(index);
            }
            _rows.Children.Add(grid);
        }

        public void Rename(string name) => SheetName = name;

        public WizardStepDraft? SelectedConfigStep()
        {
            if (!SheetName.Equals("config", StringComparison.OrdinalIgnoreCase) || _selectedRow < 1 || _selectedRow >= _values.Count) return null;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < Headers.Length; column++) values[Headers[column]] = _values[_selectedRow][column];
            return new WizardStepDraft(_selectedRow, values);
        }

        public void ApplyConfigStep(WizardStepDraft draft)
        {
            if (!SheetName.Equals("config", StringComparison.OrdinalIgnoreCase)) return;
            var row = draft.Row ?? _values.Count;
            while (_values.Count <= row) _values.Add(Enumerable.Repeat("", Headers.Length).ToList());
            foreach (var pair in draft.Values)
            {
                var column = Array.FindIndex(Headers, h => h.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                if (column >= 0) _values[row][column] = pair.Value;
            }
            _selectedRow = row; _selectedRows.Clear(); _selectedRows.Add(row); Render();
        }

        private static string ColumnLetter(int column)
        {
            var value = column + 1; var result = "";
            while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; }
            return result;
        }

        private void AddRowView(int row, bool header)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52," + string.Join(',', _visibleColumns.Select(i => ScaleWidth(_widths[i])))), Background = header ? DashboardStyle.Brush("#173559") : Brushes.Transparent };
            var number = DashboardStyle.Text(header ? "#" : row.ToString(), 11, header);
            number.Background = (_selectedRows.Contains(row) || (header && _selectedRows.Count == _values.Count - 1)) ? DashboardStyle.Brush("#275B8F") : Brushes.Transparent;
            number.PointerPressed += (_, e) => { if (e.GetCurrentPoint(number).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return; if (!header) SelectRow(row, e); else SelectAllRows(); }; number.ContextMenu = RowMenu(row, header); _rowHeaders[row] = number; Grid.SetColumn(number, 0); grid.Children.Add(number);
            for (var n = 0; n < _visibleColumns.Count; n++)
            {
                var column = _visibleColumns[n];
                var cell = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10") };
                var isWrapped = _wrap.TryGetValue((row, column), out var wrapped) && wrapped;
                var box = new TextBox { Text = _values[row][column], FontSize = _fontSize, MinWidth = 80, Margin = new Thickness(1), AcceptsReturn = true, IsReadOnly = false, Focusable = true, TextWrapping = isWrapped ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = isWrapped ? WrappedHeight(_values[row][column], _widths[column]) : 30 };
                ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Hidden); ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Hidden);
                box.Background = _selectedCells.Contains((row, column)) || _selectedColumns.Contains(column) || _selectedRows.Contains(row) ? DashboardStyle.Brush("#173559") : (_cellColors.TryGetValue((row, column), out var cellColor) && cellColor is not null ? DashboardStyle.Brush(cellColor) : Brushes.Transparent);
                box.CaretIndex = box.Text?.Length ?? 0;
                _cellBoxes[(row, column)] = box;
                box.TextChanged += (_, _) => { _values[row][column] = box.Text ?? ""; if (ReferenceEquals(box, _activeBox)) UpdateSuggestions(box); };
                box.GotFocus += (_, _) => { _activeBox = box; };
                if (header)
                {
                    box.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
                    {
                        if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                        _activeBox = box;
                        box.Focus();
                        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != KeyModifiers.None) SelectColumn(column, e);
                        e.Handled = true;
                    }, RoutingStrategies.Tunnel);
                }
                else box.AddHandler(InputElement.PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) { _activeBox = box; SelectCell(row, column, box, e); } }, RoutingStrategies.Tunnel);
                box.AddHandler(InputElement.KeyDownEvent, (_, e) => HandleKey(box, e), RoutingStrategies.Tunnel);
                box.ContextMenu = header ? ColumnMenu(column) : CellMenu(row, column);
                if (header) { box.Foreground = DashboardStyle.Brush("#E3EDF9"); ToolTip.SetTip(box, "Edit this header directly. Use the column letter above it to select the column."); }
                Grid.SetColumn(box, 0); cell.Children.Add(box);
                var grip = new Border { Width = 10, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Cursor = new Cursor(StandardCursorType.SizeWestEast), Background = DashboardStyle.Brush("#45698E"), Opacity = 0.75, IsHitTestVisible = true };
                grip.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
                    BeginResize(column); _resizing = true; _resizeLastX = e.GetPosition(null).X; e.Pointer.Capture(grip); e.Handled = true;
                };
                grip.PointerMoved += (_, e) =>
                {
                    if (!_resizing) return; var x = e.GetPosition(null).X; AccumulateResize(x - _resizeLastX); _resizeLastX = x; e.Handled = true;
                };
                grip.PointerReleased += (_, e) =>
                {
                    if (!_resizing) return; _resizing = false; e.Pointer.Capture(null); EndResize(); e.Handled = true;
                };
                Grid.SetColumn(grip, 1); cell.Children.Add(grip);
                Grid.SetColumn(cell, n + 1); grid.Children.Add(cell);
            }
            _rows.Children.Add(grid);
        }

        private (bool Add, bool Range) SelectionIntent(PointerEventArgs? e, string scope)
        {
            var add = e is not null && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control;
            var range = e is not null && (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
            return (add, range);
        }
        private void SelectCell(int row, int column, TextBox box, PointerEventArgs? args)
        {
            var (add, range) = SelectionIntent(args, "cells");
            _selectedRow = row; _selectedColumn = column; _activeBox = box; _selectedRows.Clear(); _selectedColumns.Clear();
            if (range && _anchorRow >= 0)
            {
                _selectedCells.Clear(); for (var r = Math.Min(_anchorRow, row); r <= Math.Max(_anchorRow, row); r++) for (var c = Math.Min(_anchorColumn, column); c <= Math.Max(_anchorColumn, column); c++) _selectedCells.Add((r, c));
            }
            else if (!add) { _selectedCells.Clear(); _selectedCells.Add((row, column)); _anchorRow = row; _anchorColumn = column; }
            else _selectedCells.Add((row, column));
            UpdateSelectionText(); RefreshSelectionVisuals(); if (!box.IsReadOnly) UpdateSuggestions(box);
        }
        private void SelectRow(int row, PointerEventArgs? e)
        {
            var (add, range) = SelectionIntent(e, "rows"); _selectedCells.Clear(); _selectedColumns.Clear();
            if (!add && !range) _selectedRows.Clear();
            if (range && _anchorRow >= 1) for (var r = Math.Min(_anchorRow, row); r <= Math.Max(_anchorRow, row); r++) _selectedRows.Add(r); else _selectedRows.Add(row);
            _selectedRow = row; if (!range) _anchorRow = row; UpdateSelectionText(); Render();
        }
        private void SelectRowDirect(int row, bool add) { _selectedCells.Clear(); _selectedColumns.Clear(); if (!add) _selectedRows.Clear(); _selectedRows.Add(row); _selectedRow = row; _anchorRow = row; UpdateSelectionText(); Render(); }
        private void SelectAllRows() { _selectedRows.Clear(); for (var r = 1; r < _values.Count; r++) _selectedRows.Add(r); UpdateSelectionText(); Render(); }
        private void SelectColumn(int column, PointerEventArgs? e)
        {
            var (add, range) = SelectionIntent(e, "columns"); _selectedCells.Clear(); _selectedRows.Clear();
            if (!add && !range) _selectedColumns.Clear();
            if (range && _anchorColumn >= 0) for (var c = Math.Min(_anchorColumn, column); c <= Math.Max(_anchorColumn, column); c++) _selectedColumns.Add(c); else _selectedColumns.Add(column);
            _selectedColumn = column; if (!range) _anchorColumn = column; UpdateSelectionText(); Render();
        }
        private void SelectColumnDirect(int column, bool add) { _selectedCells.Clear(); _selectedRows.Clear(); if (!add) _selectedColumns.Clear(); _selectedColumns.Add(column); _selectedColumn = column; _anchorColumn = column; UpdateSelectionText(); Render(); }
        private void UpdateSelectionText() => _position.Text = $"Sheet: {_sheetName} | {_selectedCells.Count} cells | {_selectedRows.Count} rows | {_selectedColumns.Count} columns";

        private void HandleKey(TextBox box, KeyEventArgs e)
        {
            if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { UpdateSuggestions(box, true); e.Handled = true; return; }
            if (e.Key == Key.S && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { _saveWorkbook(); e.Handled = true; return; }
            if (e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control && HasTextSelection(box)) { _ = CopyTextSelectionAsync(box); e.Handled = true; return; }
            if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { _ = PasteTextSelectionAsync(box); e.Handled = true; return; }
            if (e.Key == Key.C && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control && (_selectedRows.Count > 0 || _selectedColumns.Count > 0)) { if (_selectedColumns.Count > 0) CopyColumns(); else CopyRows(); e.Handled = true; return; }
            if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control && (_selectedRows.Count > 0 || _selectedColumns.Count > 0)) { if (_copiedColumns is not null && _selectedColumns.Count > 0) PasteColumns(); else if (_copiedRows is not null && _selectedRows.Count > 0) PasteRows(); e.Handled = true; return; }
            if (e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { box.SelectAll(); e.Handled = true; return; }
            if (e.Key == Key.Escape) { CloseSuggestions(); return; }
            if ((e.Key == Key.Enter || e.Key == Key.Tab) && _suggestionPopup.IsOpen && _suggestions.SelectedItem is string value) { AcceptSuggestion(value); e.Handled = true; return; }
            if (e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { Undo(); e.Handled = true; }
            else if (e.Key == Key.Y && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control) { Redo(); e.Handled = true; }
        }

        private static bool HasTextSelection(TextBox box) => box.SelectionStart != box.SelectionEnd;

        private async Task CopyTextSelectionAsync(TextBox box)
        {
            var clipboard = TopLevel.GetTopLevel(box)?.Clipboard;
            if (clipboard is null || !HasTextSelection(box)) return;
            await clipboard.SetTextAsync(box.SelectedText ?? string.Empty);
        }

        private async Task PasteTextSelectionAsync(TextBox box)
        {
            var clipboard = TopLevel.GetTopLevel(box)?.Clipboard;
            var pasted = clipboard is null ? null : await clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(pasted)) return;
            var text = box.Text ?? string.Empty;
            var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, text.Length);
            var end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), start, text.Length);
            box.Text = text[..start] + pasted + text[end..];
            box.CaretIndex = start + pasted.Length;
            box.SelectionStart = box.CaretIndex;
            box.SelectionEnd = box.CaretIndex;
        }

        private bool HandleSelectionClipboardKey(KeyEventArgs e)
        {
            if ((e.KeyModifiers & KeyModifiers.Control) != KeyModifiers.Control) return false;
            if (e.Key == Key.C && (_selectedRows.Count > 0 || _selectedColumns.Count > 0)) { if (_selectedColumns.Count > 0) CopyColumns(); else CopyRows(); return true; }
            if (e.Key == Key.V && (_selectedRows.Count > 0 || _selectedColumns.Count > 0)) { if (_copiedColumns is not null && _selectedColumns.Count > 0) PasteColumns(); else if (_copiedRows is not null && _selectedRows.Count > 0) PasteRows(); return true; }
            return false;
        }

        private void UpdateSuggestions(TextBox box, bool force = false)
        {
            var value = box.Text ?? ""; var caret = Math.Clamp(box.CaretIndex, 0, value.Length); var before = value[..caret];
            var lt = before.LastIndexOf('<'); var brace = before.LastIndexOf('{'); var tokenStart = Math.Max(lt, brace);
            var token = tokenStart >= 0 ? before[tokenStart..] : ""; var suggestions = new List<string>();
            if (brace > lt && !before[(brace + 1)..].Contains('}')) suggestions.AddRange(AssertionVerbs.Select(x => "{" + x + "}"));
            else if (lt >= 0 && !before[(lt + 1)..].Contains('>'))
            {
                var from = before[(lt + 1)..];
                if (from.StartsWith("From_", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = from[5..].Split('_'); var sheet = parts[0]; var prefix = parts.Length > 1 ? string.Join('_', parts.Skip(1)) : "";
                    if (parts.Length <= 1) suggestions.AddRange(_allHeaders.Keys.Select(x => $"<From_{x}_"));
                    else if (_allHeaders.TryGetValue(sheet, out var headers))
                    {
                        var cleanHeaders = headers.Select(CleanAutocompleteHeader).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
                        suggestions.AddRange(cleanHeaders.Where(x => x.Contains(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => $"<From_{sheet}_{x}_r>"));
                    }
                }
                else suggestions.AddRange(Functions);
            }
            else if (force) suggestions.AddRange(Functions.Concat(AssertionVerbs.Select(x => "{" + x + "}")));
            suggestions = suggestions.Where(x => force || x.Contains(token, StringComparison.OrdinalIgnoreCase)).Distinct().Take(80).ToList();
            if (suggestions.Count == 0) { CloseSuggestions(); return; }
            _updatingSuggestions = true; _suggestions.ItemsSource = suggestions; _suggestions.SelectedIndex = 0; _updatingSuggestions = false;
            _suggestionPopup.PlacementTarget = box; _suggestionPopup.IsOpen = true;
        }
        private void AcceptSuggestion(string suggestion)
        {
            if (_activeBox is null || _selectingSuggestion) return; _selectingSuggestion = true;
            var value = _activeBox.Text ?? ""; var caret = Math.Clamp(_activeBox.CaretIndex, 0, value.Length); var before = value[..caret]; var lt = before.LastIndexOf('<'); var brace = before.LastIndexOf('{'); var start = Math.Max(lt, brace); if (start < 0) start = caret;
            _activeBox.Text = value[..start] + suggestion + value[caret..]; _activeBox.CaretIndex = start + suggestion.Length; _activeBox.Focus(); CloseSuggestions(); _selectingSuggestion = false;
        }
        private void CloseSuggestions() { _suggestionPopup.IsOpen = false; }

        private static string CleanAutocompleteHeader(string header)
            => Regex.Replace(header ?? string.Empty, @"\{[^{}\r\n]*\}", string.Empty).Trim();

        private ContextMenu CellMenu(int row, int column) => MakeMenu(
            Menu("Select this row", () => SelectRowDirect(row, false)), Menu("Add this row", () => SelectRowDirect(row, true)),
            Menu("Select this column", () => SelectColumnDirect(column, false)), Menu("Add this column", () => SelectColumnDirect(column, true)),
            Menu("Copy selection", CopySelection), Menu("Paste", PasteSelection), Menu("Undo", Undo), Menu("Redo", Redo),
            Menu("Insert row above", () => InsertRows(row, false)), Menu("Insert row below", () => InsertRows(row, true)), Menu("Delete selected rows", DeleteRows),
            Menu("Insert column before", () => InsertColumns(column, false)), Menu("Insert column after", () => InsertColumns(column, true)), Menu("Delete selected columns", DeleteColumns),
            Menu("Wrap selected cells", () => SetWrap(true)), Menu("No wrap", () => SetWrap(false)), Menu("Auto-fit selected columns", AutoFitSelectedColumns),
            Menu("Highlight selected cells", () => SetCellColor("#284D70")), Menu("Clear highlight", () => SetCellColor(null)), Menu("Increase font", () => ChangeFont(1)), Menu("Decrease font", () => ChangeFont(-1)), Menu("Delete cell contents", () => ClearSelection()));
        private ContextMenu RowMenu(int row, bool header) => header ? MakeMenu(Menu("Select all rows", SelectAllRows), Menu("Add row at end", () => InsertRows(_values.Count, true)), Menu("Copy rows", CopyRows), Menu("Paste rows", PasteRows), Menu("Delete selected rows", DeleteRows), Menu("Wrap selected rows", () => SetWrapForRows(true)), Menu("No wrap selected rows", () => SetWrapForRows(false)), Menu("Select all columns", SelectAllColumns), Menu("Select all cells", SelectAllCells)) : MakeMenu(Menu("Select row", () => SelectRowDirect(row, false)), Menu("Add row at end", () => InsertRows(_values.Count, true)), Menu("Insert row above", () => InsertRows(row, false)), Menu("Insert row below", () => InsertRows(row, true)), Menu("Copy rows", CopyRows), Menu("Paste rows", PasteRows), Menu("Duplicate rows", PasteRows), Menu("Delete selected rows", DeleteRows), Menu("Wrap selected rows", () => SetWrapForRows(true, row)), Menu("No wrap selected rows", () => SetWrapForRows(false, row)));
        private ContextMenu ColumnMenu(int column) => MakeMenu(Menu("Edit header", () => BeginHeaderEdit(column)), Menu("Select column", () => SelectColumnDirect(column, false)), Menu("Add column at end", () => InsertColumns(Headers.Length, true)), Menu("Insert column before", () => InsertColumns(column, false)), Menu("Insert column after", () => InsertColumns(column, true)), Menu("Copy columns", CopyColumns), Menu("Paste columns", PasteColumns), Menu("Delete selected columns", DeleteColumns), Menu("Auto-fit selected columns", AutoFitSelectedColumns), Menu("Set selected width (280)", () => SetSelectedColumnsWidth(280)), Menu("Reset selected width", () => SetSelectedColumnsWidth(190)), Menu("Wrap selected columns", () => SetWrapForColumns(true, column)), Menu("No wrap selected columns", () => SetWrapForColumns(false, column)), Menu("Select all columns", SelectAllColumns), Menu("Select all cells", SelectAllCells));
        private void BeginHeaderEdit(int column)
        {
            if (!_cellBoxes.TryGetValue((0, column), out var box)) return;
            _activeBox = box; box.Focusable = true; box.IsReadOnly = false; box.Focus(); box.CaretIndex = box.Text?.Length ?? 0;
            Dispatcher.UIThread.Post(() => { if ((box.Text ?? "").Contains('<') || (box.Text ?? "").Contains('{')) UpdateSuggestions(box, true); }, DispatcherPriority.Background);
        }
        private static ContextMenu MakeMenu(params MenuItem[] items) { var menu = new ContextMenu(); foreach (var item in items) menu.Items.Add(item); return menu; }
        private static MenuItem Menu(string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); return item; }

        private void SelectAllColumns() { _selectedColumns.Clear(); for (var c = 0; c < Headers.Length; c++) _selectedColumns.Add(c); UpdateSelectionText(); Render(); }
        private void SelectAllCells() { _selectedCells.Clear(); for (var r = 1; r < _values.Count; r++) for (var c = 0; c < Headers.Length; c++) _selectedCells.Add((r, c)); SelectAllRows(); SelectAllColumns(); }
        private IEnumerable<int> TargetColumns() => _selectedColumns.Count > 0 ? _selectedColumns : (_selectedColumn >= 0 ? new[] { _selectedColumn } : Enumerable.Range(0, Headers.Length));
        private IEnumerable<int> TargetRows() => _selectedRows.Count > 0 ? _selectedRows : (_selectedRow > 0 ? new[] { _selectedRow } : Enumerable.Range(1, _values.Count - 1));
        private void SaveUndo() { _undo.Add(_values.Select(x => x.ToList()).ToList()); if (_undo.Count > 30) _undo.RemoveAt(0); _redo.Clear(); }
        private void Undo() { if (_undo.Count == 0) return; _redo.Add(_values.Select(x => x.ToList()).ToList()); _values.Clear(); _values.AddRange(_undo[^1].Select(x => x.ToList())); _undo.RemoveAt(_undo.Count - 1); Render(); }
        private void Redo() { if (_redo.Count == 0) return; _undo.Add(_values.Select(x => x.ToList()).ToList()); _values.Clear(); _values.AddRange(_redo[^1].Select(x => x.ToList())); _redo.RemoveAt(_redo.Count - 1); Render(); }
        private void InsertRows(int row, bool below) { SaveUndo(); var at = Math.Clamp(row + (below ? 1 : 0), 1, _values.Count); _values.Insert(at, Enumerable.Repeat("", Headers.Length).ToList()); Render(); }
        public void AddRowAtEnd() { InsertRows(_values.Count, true); _status("Added a new row at the end of the current sheet.", false); }
        public void AddRowsAtEnd(int count) { for (var i = 0; i < count; i++) InsertRows(_values.Count, true); _status($"Added {count} row(s) at the end of the current sheet.", false); }
        public void AddColumnsAtEnd(int count)
        {
            SaveUndo();
            var next = Headers.Length;
            for (var i = 0; i < count; i++) { var header = $"Column{next + i + 1}"; while (Headers.Contains(header, StringComparer.OrdinalIgnoreCase)) header = $"Column{++next}"; foreach (var row in _values) row.Add(row == _values[0] ? header : ""); _widths.Add(190); }
            Render(); _status($"Added {count} column(s) at the end of the current sheet.", false);
        }
        private void DeleteRows() { SaveUndo(); var rows = TargetRows().Where(r => r > 0 && r < _values.Count).OrderByDescending(x => x).ToArray(); foreach (var r in rows) _values.RemoveAt(r); _selectedRows.Clear(); Render(); }
        private void CopyRows() { _copiedRows = TargetRows().Select(r => _values[r].ToList()).ToList(); _status("Copied rows internally. Select a row and choose Duplicate rows or press Ctrl+V.", false); }
        private void PasteRows() { SaveUndo(); if (_copiedRows is null) return; var at = _selectedRow > 0 ? _selectedRow : _values.Count; foreach (var row in _copiedRows.Select(x => x.ToList())) _values.Insert(Math.Min(at, _values.Count), row); Render(); }
        private void InsertColumns(int column, bool after) { SaveUndo(); var cols = TargetColumns().OrderBy(x => x).ToArray(); var at = Math.Clamp(column + (after ? 1 : 0), 0, Headers.Length); foreach (var row in _values) row.Insert(at, ""); _widths.Insert(at, 190); Render(); }
        private void DeleteColumns() { SaveUndo(); var cols = TargetColumns().Where(c => c >= 0 && c < Headers.Length).OrderByDescending(x => x).ToArray(); if (cols.Length >= Headers.Length) return; foreach (var row in _values) foreach (var c in cols) row.RemoveAt(c); foreach (var c in cols) _widths.RemoveAt(c); _selectedColumns.Clear(); Render(); }
        private void CopyColumns() { _copiedColumns = TargetColumns().OrderBy(x => x).Select(c => _values.Select(row => row[c]).ToList()).ToList(); _status("Copied columns internally. Choose Paste columns from a column menu.", false); }
        private void PasteColumns() { SaveUndo(); if (_copiedColumns is null) return; var at = _selectedColumn >= 0 ? _selectedColumn : Headers.Length; for (var i = 0; i < _copiedColumns.Count; i++) { var c = Math.Min(at + i, Headers.Length); for (var r = 0; r < _values.Count; r++) _values[r].Insert(c, _copiedColumns[i][r]); _widths.Insert(c, 190); } Render(); }
        private void CopySelection()
        {
            if (_selectedCells.Count > 0)
            {
                var minR = _selectedCells.Min(x => x.Row); var maxR = _selectedCells.Max(x => x.Row); var minC = _selectedCells.Min(x => x.Column); var maxC = _selectedCells.Max(x => x.Column);
                _copiedCells = Enumerable.Range(minR, maxR - minR + 1).Select(r => Enumerable.Range(minC, maxC - minC + 1).Select(c => _values[r][c]).ToList()).ToList();
                _status("Copied selected cells internally. Ctrl+V pastes the range at the active cell.", false);
            }
            else CopyRows();
        }
        private void PasteSelection()
        {
            if (_copiedCells is not null && _selectedRow >= 0 && _selectedColumn >= 0)
            {
                SaveUndo(); var r0 = _selectedRow; var c0 = _selectedColumn;
                for (var r = 0; r < _copiedCells.Count && r0 + r < _values.Count; r++) for (var c = 0; c < _copiedCells[r].Count && c0 + c < Headers.Length; c++) _values[r0 + r][c0 + c] = _copiedCells[r][c]; Render();
            }
            else if (_copiedRows is not null) PasteRows(); else if (_copiedColumns is not null) PasteColumns();
        }
        private void ClearSelection() { foreach (var (r, c) in _selectedCells) if (r > 0 && r < _values.Count) _values[r][c] = ""; Render(); }
        private void SetWrap(bool value) { foreach (var cell in _selectedCells) _wrap[cell] = value; foreach (var r in TargetRows()) foreach (var c in TargetColumns()) _wrap[(r, c)] = value; Render(); }
        private void SetWrapForRows(bool value, int row = -1)
        {
            IEnumerable<int> rows = _selectedRows.Count > 0 ? _selectedRows : (row > 0 ? new[] { row } : Array.Empty<int>());
            foreach (var r in rows)
                foreach (var c in Enumerable.Range(0, Headers.Length))
                    _wrap[(r, c)] = value;
            _status(value ? "Wrapped the selected rows." : "Removed wrapping from the selected rows.", false);
            Render();
        }
        private void SetWrapForColumns(bool value, int column = -1)
        {
            IEnumerable<int> columns = _selectedColumns.Count > 0 ? _selectedColumns : (_selectedColumn >= 0 ? new[] { _selectedColumn } : (column >= 0 ? new[] { column } : Array.Empty<int>()));
            foreach (var r in Enumerable.Range(1, Math.Max(0, _values.Count - 1)))
                foreach (var c in columns)
                    _wrap[(r, c)] = value;
            _status(value ? "Wrapped the selected columns." : "Removed wrapping from the selected columns.", false);
            Render();
        }
        private static double WrappedHeight(string value, double width)
        {
            var charsPerLine = Math.Max(8, (int)(Math.Max(100, width - 16) / 7.2));
            var lines = value.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(Math.Max(1, line.Length) / (double)charsPerLine)));
            return Math.Clamp(lines * 20 + 10, 30, 420);
        }
        private void SetCellColor(string? color) { foreach (var cell in _selectedCells) _cellColors[cell] = color; foreach (var r in TargetRows()) foreach (var c in TargetColumns()) _cellColors[(r, c)] = color; _status(color is null ? "Highlight cleared for the selected range." : "Highlight applied to the selected range.", false); Render(); }
        private void ChangeFont(double delta) { _fontSize = Math.Clamp(_fontSize + delta, 8, 24); Render(); }
        private void RefreshSelectionVisuals()
        {
            foreach (var ((row, column), box) in _cellBoxes)
                box.Background = _selectedCells.Contains((row, column)) || _selectedColumns.Contains(column) || _selectedRows.Contains(row)
                    ? DashboardStyle.Brush("#173559") : (_cellColors.TryGetValue((row, column), out var color) && color is not null ? DashboardStyle.Brush(color) : Brushes.Transparent);
            foreach (var (row, label) in _rowHeaders)
                label.Background = _selectedRows.Contains(row) ? DashboardStyle.Brush("#275B8F") : Brushes.Transparent;
        }
        private void BeginResize(int column)
        {
            if (!_selectedColumns.Contains(column)) { _selectedCells.Clear(); _selectedRows.Clear(); _selectedColumns.Clear(); _selectedColumns.Add(column); _selectedColumn = column; UpdateSelectionText(); RefreshSelectionVisuals(); }
            _resizeColumns = TargetColumns().Distinct().ToArray();
        }
        private void AccumulateResize(double delta) { foreach (var c in _resizeColumns) _widths[c] = Math.Clamp(_widths[c] + delta, 100, 700); }
        private void EndResize() { if (_resizeColumns.Length > 0) Render(); _resizeColumns = []; }
        private void ResizeSelectedColumns(double delta) { BeginResize(_selectedColumn >= 0 ? _selectedColumn : 0); AccumulateResize(delta); EndResize(); }
        private void AutoFitSelectedColumns() { foreach (var c in TargetColumns()) _widths[c] = Math.Clamp(Math.Max(120, Math.Max(_values.Max(row => row[c].Length * 7.5 + 24), Headers[c].Length * 7.5 + 24)), 100, 700); Render(); }
        private void SetSelectedColumnsWidth(double width) { foreach (var c in TargetColumns()) _widths[c] = Math.Clamp(width, 100, 700); Render(); }

        public void Validate() { if (_values.Count == 0) throw new InvalidDataException($"Sheet '{_sheetName}' cannot be empty."); if (_values[0].All(string.IsNullOrWhiteSpace) && new[] { "config", "request", "response" }.Contains(_sheetName, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException($"Sheet '{_sheetName}' must retain a header row."); if (_values[0].Distinct(StringComparer.OrdinalIgnoreCase).Count() != _values[0].Count) throw new InvalidDataException($"Sheet '{_sheetName}' contains duplicate headers."); }
        public void WriteTo(IXLWorksheet sheet)
        {
            sheet.RangeUsed()?.Clear(XLClearOptions.All);
            for (var r = 0; r < _values.Count; r++) for (var c = 0; c < _values[r].Count; c++) { var cell = sheet.Cell(r + 1, c + 1); cell.Value = _values[r][c]; cell.Style.Alignment.WrapText = _wrap.TryGetValue((r, c), out var wrapped) && wrapped; cell.Style.Font.FontSize = _fontSize; if (_cellColors.TryGetValue((r, c), out var color) && color is not null) cell.Style.Fill.BackgroundColor = XLColor.FromHtml(color); }
            // Keep intentionally blank appended rows visible after reopening. A row that contains no values
            // is otherwise omitted by Excel/ClosedXML's used-range detection.
            for (var r = 0; r < _values.Count; r++) sheet.Row(r + 1).Height = 22;
            for (var c = 0; c < _widths.Count; c++)
                sheet.Column(c + 1).Width = Math.Clamp(_widths[c] / 7.5, 13.3, 93.3);
            sheet.Row(1).Style.Font.Bold = true; sheet.SheetView.FreezeRows(1);
        }
    }
}
