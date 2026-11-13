using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClosedXML.Excel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoadTestingTool.UI;

internal sealed class TestStepConfigurationEditor : UserControl
{
    private sealed class StepRow
    {
        public int OriginalRow { get; set; }
        public Dictionary<int, string> Cells { get; } = [];
        public string Get(Dictionary<string, int> headers, string key, string fallback = "") => headers.TryGetValue(key, out var c) && Cells.TryGetValue(c, out var v) ? v : fallback;
        public void Set(Dictionary<string, int> headers, string key, string value) { if (headers.TryGetValue(key, out var c)) Cells[c] = value; }
    }

    private readonly InstanceInfo _item;
    private readonly TabControl _testcases = new();
    private readonly StackPanel _steps = new() { Spacing = 5 };
    private readonly StackPanel _editor = new() { Spacing = 8 };
    private readonly TextBox _newStepName = new() { Watermark = "Step name", MinWidth = 260 };
    private readonly ComboBox _newStepType = new() { ItemsSource = new[] { "HTTP", "GraphQL", "CaseQL", "File", "Script", "SQL", "ActionManual" }, SelectedIndex = 0, MinWidth = 180 };
    private readonly TextBlock _status = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly Dictionary<string, int> _headers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<StepRow> _rows = [];
    private readonly Dictionary<int, List<StepRow>> _byTestcase = [];
    private int _selectedTestcase;
    private StepRow? _selectedStep;
    private bool _loading;
    private bool _dirty;
    private TextBox _stepName = null!;
    private ComboBox _stepType = null!;
    private CheckBox _enabled = null!;
    private ComboBox _stage = null!;
    private TextBox _timeout = null!;
    private TextBox _manualTimeoutMinutes = null!;
    private TextBox _wait = null!;
    private TextBox _targetUrl = null!;
    private ComboBox _verb = null!;
    private TextBox _template = null!;
    private TextBox _contentType = null!;
    private TextBox _expectedStatus = null!;
    private TextBox _graphqlSource = null!;
    private TextBox _graphqlInline = null!;
    private TextBox _graphqlOperation = null!;
    private TextBox _graphqlVariables = null!;
    private ComboBox _fileAction = null!;
    private TextBox _filePath = null!;
    private TextBox _fileContent = null!;
    private TextBox _fileOutput = null!;
    private TextBox _fileJsonPath = null!;
    private ComboBox _fileEncoding = null!;
    private ComboBox _fileMode = null!;
    private TextBox _fileMaxBytes = null!;
    private TextBox _fileContentType = null!;
    private TextBox _scriptSource = null!;
    private TextBox _scriptInline = null!;
    private TextBox _sqlConnection = null!;
    private ComboBox _sqlCommandType = null!;
    private TextBox _sqlSource = null!;
    private TextBox _sqlCommandText = null!;
    private TextBox _sqlProcedure = null!;
    private TextBox _sqlParameters = null!;
    private TextBox _sqlOutputs = null!;
    private TextBox _multipartTextName = null!;
    private TextBox _multipartTextValue = null!;
    private TextBox _multipartFileName = null!;
    private TextBox _multipartFilePath = null!;
    private TextBox _multipartFileContentType = null!;
    private MultipartPartsEditor _multipartEditor = null!;
    private TextBox _stepConfig = null!;
    private CheckBox _assertEnabled = null!;
    private CheckBox _ignoreEmpty = null!;
    private CheckBox _assertOnlyResponse = null!;
    private CheckBox _stopOnFailure = null!;
    private StackPanel _assertionFields = null!;
    private StackPanel _typeFields = null!;
    private StackPanel _headerFields = null!;
    private readonly List<(TextBox Name, TextBox Value)> _headerEditors = [];

    public TestStepConfigurationEditor(InstanceInfo item)
    {
        _item = item;
        Margin = new Thickness(8);
        LoadWorkbook();
        Content = BuildView();
        RenderTestcases();
    }

    private Control BuildView()
    {
        var title = DashboardStyle.Text("Configure test steps", 18, true);
        var help = DashboardStyle.Text("Select a testcase, then select a step. Move and delete actions operate on the complete config row and are saved to the workbook with one automatic backup.", 12, false, DashboardStyle.Muted);
        var testcaseLabel = DashboardStyle.Text("Testcases", 13, true);
        var stepsHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,260,180,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 10, 0, 4) };
        stepsHeader.Children.Add(DashboardStyle.Text("Steps in selected testcase", 14, true));
        Grid.SetColumn(_newStepName, 1); stepsHeader.Children.Add(_newStepName);
        Grid.SetColumn(_newStepType, 2); stepsHeader.Children.Add(_newStepType);
        var add = DashboardStyle.Action("＋ Add", AddStep, "primary"); add.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(add, 3); stepsHeader.Children.Add(add);
        var moveUp = DashboardStyle.Action("↑", MoveSelectedUp); moveUp.Margin = new Thickness(8, 0, 0, 0); ToolTip.SetTip(moveUp, "Move selected step up"); Grid.SetColumn(moveUp, 4); stepsHeader.Children.Add(moveUp);
        var moveDown = DashboardStyle.Action("↓", MoveSelectedDown); moveDown.Margin = new Thickness(8, 0, 0, 0); ToolTip.SetTip(moveDown, "Move selected step down"); Grid.SetColumn(moveDown, 5); stepsHeader.Children.Add(moveDown);
        var delete = DashboardStyle.Action("− Delete", DeleteSelected); delete.Margin = new Thickness(8, 0, 0, 0); delete.Classes.Add("danger"); ToolTip.SetTip(delete, "Delete selected complete workbook row"); Grid.SetColumn(delete, 6); stepsHeader.Children.Add(delete);
        _newStepName.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { AddStep(); e.Handled = true; } };
        var testcaseSection = new StackPanel { Spacing = 5, Children = { testcaseLabel, _testcases } };
        var separator = new Border { Height = 1, Background = DashboardStyle.Line, Margin = new Thickness(0, 8) };
        var editorScroll = new ScrollViewer { Content = _editor, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var content = new StackPanel { Spacing = 8, Children = { title, help, testcaseSection, stepsHeader, _steps, separator, DashboardStyle.Text("Selected step editor", 14, true), editorScroll, _status } };
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private void LoadWorkbook()
    {
        try
        {
            using var workbookStream = new FileStream(_item.WorkbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(workbookStream);
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The workbook has no config sheet.");
            var header = sheet.FirstRowUsed() ?? throw new InvalidDataException("The config sheet has no header row.");
            foreach (var cell in header.CellsUsed()) _headers[cell.GetString().Trim()] = cell.Address.ColumnNumber;
            if (!_headers.ContainsKey("manualtimeoutminutes")) _headers["manualtimeoutminutes"] = _headers.Values.DefaultIfEmpty(0).Max() + 1;
            if (!_headers.ContainsKey("testcaseindex") || !_headers.ContainsKey("stepname")) throw new InvalidDataException("The config sheet needs testcaseindex and stepname columns.");
            var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? _headers.Values.DefaultIfEmpty(1).Max();
            foreach (var row in sheet.RowsUsed().Skip(1))
            {
                if (row.CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString()))) continue;
                if (!int.TryParse(row.Cell(_headers["testcaseindex"]).GetString(), out var index)) continue;
                var record = new StepRow { OriginalRow = row.RowNumber() };
                for (var column = 1; column <= lastColumn; column++) record.Cells[column] = row.Cell(column).GetString();
                _rows.Add(record);
                if (!_byTestcase.TryGetValue(index, out var list)) _byTestcase[index] = list = [];
                list.Add(record);
            }
            foreach (var list in _byTestcase.Values) list.Sort((a, b) => a.OriginalRow.CompareTo(b.OriginalRow));
            _selectedTestcase = _byTestcase.Keys.OrderBy(x => x).FirstOrDefault();
            _status.Text = $"Loaded {_rows.Count} step rows from {_item.Name}.";
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.Foreground = DashboardStyle.Red; }
    }

    private void RenderTestcases()
    {
        _loading = true; _testcases.ItemsSource = _byTestcase.Keys.OrderBy(x => x).Select(index => new TabItem { Header = TestcaseLabel(index), Content = new Border { Height = 1 } }).ToList();
        _testcases.SelectionChanged -= TestcaseChanged; _testcases.SelectionChanged += TestcaseChanged;
        var selected = _byTestcase.Keys.OrderBy(x => x).ToList().IndexOf(_selectedTestcase);
        _testcases.SelectedIndex = selected < 0 ? 0 : selected;
        _loading = false;
        RenderSteps();
    }

    private Control TestcaseLabel(int index) => DashboardStyle.Text(_byTestcase.TryGetValue(index, out var rows) && rows.Count > 0 ? rows[0].Get(_headers, "testcase", $"Testcase {index}") : $"Testcase {index}", 12, true);
    private void TestcaseChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || _testcases.SelectedIndex < 0) return;
        _selectedTestcase = _byTestcase.Keys.OrderBy(x => x).ElementAt(_testcases.SelectedIndex);
        RenderSteps();
    }

    private void RenderSteps()
    {
        var previous = _selectedStep;
        _steps.Children.Clear();
        if (!_byTestcase.TryGetValue(_selectedTestcase, out var rows) || rows.Count == 0)
        {
            _selectedStep = null;
            _steps.Children.Add(DashboardStyle.Text("No steps. Use ＋ Add test step to create one.", 12, false, DashboardStyle.Muted)); _editor.Children.Clear(); return;
        }
        _selectedStep = rows.Contains(previous) ? previous : rows[0];
        foreach (var row in rows) _steps.Children.Add(BuildStepRow(row));
        ShowStep(_selectedStep);
    }

    private Control BuildStepRow(StepRow row)
    {
        var name = DashboardStyle.Text(row.Get(_headers, "stepname", "New step"), 12, true);
        var type = DashboardStyle.Text(row.Get(_headers, "steptype", "HTTP"), 11, false, DashboardStyle.Muted);
        var description = new StackPanel { Spacing = 2, Children = { name, type } };
        var up = DashboardStyle.Action("↑", () => Move(row, -1)); up.Padding = new Thickness(8, 4); ToolTip.SetTip(up, "Move this complete row up");
        var down = DashboardStyle.Action("↓", () => Move(row, 1)); down.Padding = new Thickness(8, 4); ToolTip.SetTip(down, "Move this complete row down");
        var remove = DashboardStyle.Action("− Delete", () => Delete(row)); remove.Padding = new Thickness(8, 4); remove.Classes.Add("danger"); ToolTip.SetTip(remove, "Delete this complete workbook row");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(description); Grid.SetColumn(up, 1); grid.Children.Add(up); Grid.SetColumn(down, 2); grid.Children.Add(down); Grid.SetColumn(remove, 3); grid.Children.Add(remove);
        var border = new Border { Background = row == _selectedStep ? DashboardStyle.Brush("#193B61") : DashboardStyle.Panel, BorderBrush = row == _selectedStep ? DashboardStyle.Blue : DashboardStyle.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 6), Child = grid };
        border.PointerPressed += (_, _) => { _selectedStep = row; RenderSteps(); };
        return border;
    }

    private void ShowStep(StepRow row)
    {
        _selectedStep = row; _loading = true; _editor.Children.Clear();
        _stepName = new TextBox { Text = row.Get(_headers, "stepname", "NewStep") };
        _stepType = new ComboBox { ItemsSource = new[] { "HTTP", "GraphQL", "CaseQL", "File", "Script", "SQL", "ActionManual" }, SelectedItem = row.Get(_headers, "steptype", "HTTP") };
        _enabled = new CheckBox { Content = "Enabled", IsChecked = !row.Get(_headers, "enabled", "true").Equals("false", StringComparison.OrdinalIgnoreCase) };
        _stage = new ComboBox { ItemsSource = new[] { "Setup", "Test", "Teardown" }, SelectedItem = row.Get(_headers, "stage", "Test") };
        _timeout = new TextBox { Text = row.Get(_headers, "timeout", "30") }; _manualTimeoutMinutes = new TextBox { Text = row.Get(_headers, "manualtimeoutminutes", "15") }; _wait = new TextBox { Text = row.Get(_headers, "wait", "0") };
        _targetUrl = new TextBox { Text = row.Get(_headers, "targeturl") }; _verb = new ComboBox { ItemsSource = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" }, SelectedItem = row.Get(_headers, "verb", "POST") };
        _template = new TextBox { Text = row.Get(_headers, "templetsource") }; _contentType = new TextBox { Text = row.Get(_headers, "contenttype", "application/json"), Watermark = "application/json" };
        _multipartTextName = new TextBox { Text = "description" }; _multipartTextValue = new TextBox { Text = "sample upload" }; _multipartFileName = new TextBox { Text = "file" }; _multipartFilePath = new TextBox { Text = "Files/sample-upload.txt" }; _multipartFileContentType = new TextBox { Text = "text/plain" };
        _multipartEditor = new MultipartPartsEditor();
        _expectedStatus = new TextBox { Text = row.Get(_headers, "expectedstatus") };
        _graphqlSource = new TextBox(); _graphqlInline = new TextBox { AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap }; _graphqlOperation = new TextBox(); _graphqlVariables = new TextBox { Text = "{}", AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") };
        _fileAction = new ComboBox { ItemsSource = new[] { "Read", "Json", "ReadBinary", "Write", "Append", "WriteBinary", "AppendBinary" }, SelectedItem = "Read" }; _filePath = new TextBox(); _fileContent = new TextBox { AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap }; _fileOutput = new TextBox { Text = "fileContent" }; _fileJsonPath = new TextBox(); _fileEncoding = new ComboBox { ItemsSource = new[] { "Base64", "UTF8", "ASCII", "Unicode" }, SelectedIndex = 0 }; _fileMode = new ComboBox { ItemsSource = new[] { "Value", "Reference", "Base64" }, SelectedIndex = 1 }; _fileMaxBytes = new TextBox { Text = "52428800" }; _fileContentType = new TextBox { Text = "application/octet-stream" };
        _scriptSource = new TextBox(); _scriptInline = new TextBox { AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") };
        _sqlConnection = new TextBox(); _sqlCommandType = new ComboBox { ItemsSource = new[] { "Text", "StoredProcedure" }, SelectedIndex = 0 }; _sqlSource = new TextBox(); _sqlCommandText = new TextBox { AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap }; _sqlProcedure = new TextBox(); _sqlParameters = new TextBox { Text = "{}", AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") }; _sqlOutputs = new TextBox { Text = "{}", AcceptsReturn = true, MinHeight = 70, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") };
        LoadStepConfigFields(row.Get(_headers, "stepconfig", "{}"));
        _stepConfig = new TextBox { Text = row.Get(_headers, "stepconfig", "{}"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150, FontFamily = new FontFamily("Consolas") };
        _assertEnabled = new CheckBox { Content = "Enable assertions", IsChecked = !row.Get(_headers, "assert", row.Get(_headers, "assertenabled", "true")).Equals("false", StringComparison.OrdinalIgnoreCase) };
        _ignoreEmpty = new CheckBox { Content = "Ignore empty values", IsChecked = row.Get(_headers, "ignoreempty", "false").Equals("true", StringComparison.OrdinalIgnoreCase) };
        _assertOnlyResponse = new CheckBox { Content = "Assert only response fields", IsChecked = row.Get(_headers, "assertonlyresponse", "false").Equals("true", StringComparison.OrdinalIgnoreCase) };
        _stopOnFailure = new CheckBox { Content = "Stop on failure", IsChecked = !row.Get(_headers, "stoponfailure", "true").Equals("false", StringComparison.OrdinalIgnoreCase) };
        _assertionFields = new StackPanel { Spacing = 6, Children = { DashboardStyle.Text("Assertions", 13, true), _assertEnabled, _ignoreEmpty, _assertOnlyResponse, _stopOnFailure } };
        _typeFields = new StackPanel { Spacing = 6 };
        foreach (var control in new Control[] { _stepName, _stepType, _enabled, _stage, _timeout, _manualTimeoutMinutes, _wait, _targetUrl, _verb, _template, _contentType, _expectedStatus, _multipartTextName, _multipartTextValue, _multipartFileName, _multipartFilePath, _multipartFileContentType, _graphqlSource, _graphqlInline, _graphqlOperation, _graphqlVariables, _fileAction, _filePath, _fileContent, _fileOutput, _fileJsonPath, _fileEncoding, _fileMode, _fileMaxBytes, _fileContentType, _scriptSource, _scriptInline, _sqlConnection, _sqlCommandType, _sqlSource, _sqlCommandText, _sqlProcedure, _sqlParameters, _sqlOutputs, _stepConfig, _assertEnabled, _ignoreEmpty, _assertOnlyResponse, _stopOnFailure }) control.PropertyChanged += (_, _) => { if (!_loading) UpdateRowFromForm(); };
        _headerFields = BuildHeaderFields(row);
        _stepType.SelectionChanged += (_, _) => { if (!_loading) { UpdateRowFromForm(); RenderTypeFields(); } };
        _contentType.TextChanged += (_, _) => { if (!_loading) { UpdateRowFromForm(); RenderTypeFields(); } };
        _multipartEditor.Changed += (_, _) => { if (!_loading && _contentType.Text.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) GenerateStepConfig(); };
        _editor.Children.Add(DashboardStyle.Text($"Selected step: {row.Get(_headers, "stepname", "New step")}", 15, true));
        _editor.Children.Add(Field("Step name", _stepName)); _editor.Children.Add(Field("Step type", _stepType)); _editor.Children.Add(_enabled); _editor.Children.Add(Field("Stage", _stage)); _editor.Children.Add(Field("Timeout (seconds)", _timeout)); _editor.Children.Add(Field("ActionManual timeout (minutes)", _manualTimeoutMinutes)); _editor.Children.Add(Field("Wait (milliseconds)", _wait)); _editor.Children.Add(_assertionFields); _editor.Children.Add(_typeFields);
        _editor.Children.Add(Field("Advanced StepConfig JSON", _stepConfig));
        RenderTypeFields();
        _loading = false;
    }

    private void RenderTypeFields()
    {
        // The field controls are reused when the type changes. Remove them from
        // their old Field StackPanels first; Avalonia otherwise rejects adding a
        // TextBox that still has the previous StackPanel as its visual parent.
        foreach (var panel in _typeFields.Children.OfType<Panel>().Where(x => !ReferenceEquals(x, _headerFields)).ToList()) panel.Children.Clear();
        _typeFields.Children.Clear(); var type = _stepType.SelectedItem?.ToString() ?? "HTTP";
        if (type == "HTTP") { _typeFields.Children.Add(DashboardStyle.Text("HTTP request configuration", 13, true)); _typeFields.Children.Add(Field("Target URL", _targetUrl)); _typeFields.Children.Add(Field("HTTP verb", _verb)); _typeFields.Children.Add(Field("Content type", _contentType)); _typeFields.Children.Add(Field("Expected status", _expectedStatus)); _typeFields.Children.Add(_headerFields); _typeFields.Children.Add(Field("Template source", _template)); _typeFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { DashboardStyle.Action("New / edit template", EditTemplate, "primary"), DashboardStyle.Action("Validate template", ValidateTemplate) } }); if (_contentType.Text.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) _typeFields.Children.Add(_multipartEditor); }
        if (type is "GraphQL" or "CaseQL") { _typeFields.Children.Add(DashboardStyle.Text($"{type} configuration", 13, true)); _typeFields.Children.Add(Field("Target URL", _targetUrl)); _typeFields.Children.Add(Field("HTTP verb", _verb)); _typeFields.Children.Add(Field("Template source", _template)); _typeFields.Children.Add(_headerFields); _typeFields.Children.Add(Field("Source", _graphqlSource)); _typeFields.Children.Add(Field("Inline query", _graphqlInline)); _typeFields.Children.Add(Field("Operation name", _graphqlOperation)); _typeFields.Children.Add(Field("Variables JSON", _graphqlVariables)); _typeFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { DashboardStyle.Action("New / edit template", EditTemplate, "primary"), DashboardStyle.Action("Validate template", ValidateTemplate), DashboardStyle.Action($"Generate {type} StepConfig", GenerateStepConfig) } }); }
        if (type == "File") { _typeFields.Children.Add(DashboardStyle.Text("File configuration", 13, true)); _typeFields.Children.Add(Field("Template source", _template)); _typeFields.Children.Add(Field("Action", _fileAction)); _typeFields.Children.Add(Field("Path", _filePath)); _typeFields.Children.Add(Field("Content", _fileContent)); _typeFields.Children.Add(Field("Output variable", _fileOutput)); _typeFields.Children.Add(Field("JSON path", _fileJsonPath)); _typeFields.Children.Add(Field("Encoding", _fileEncoding)); _typeFields.Children.Add(Field("Output mode", _fileMode)); _typeFields.Children.Add(Field("Maximum bytes", _fileMaxBytes)); _typeFields.Children.Add(Field("Content type", _fileContentType)); _typeFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { DashboardStyle.Action("New / edit template", EditTemplate, "primary"), DashboardStyle.Action("Validate template", ValidateTemplate), DashboardStyle.Action("Generate File StepConfig", GenerateStepConfig) } }); }
        if (type == "Script") { _typeFields.Children.Add(DashboardStyle.Text("Script configuration", 13, true)); _typeFields.Children.Add(Field("Template source", _template)); _typeFields.Children.Add(Field("Script source", _scriptSource)); _typeFields.Children.Add(Field("Inline script", _scriptInline)); _typeFields.Children.Add(DashboardStyle.Text("Trusted scripts must be enabled in the instance Security settings.", 12, false, DashboardStyle.Muted)); _typeFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { DashboardStyle.Action("New / edit template", EditTemplate, "primary"), DashboardStyle.Action("Validate template", ValidateTemplate), DashboardStyle.Action("Generate Script StepConfig", GenerateStepConfig) } }); }
        if (type == "SQL") { _typeFields.Children.Add(DashboardStyle.Text("SQL configuration", 13, true)); _typeFields.Children.Add(Field("Template source", _template)); _typeFields.Children.Add(Field("Connection profile", _sqlConnection)); _typeFields.Children.Add(Field("Command type", _sqlCommandType)); _typeFields.Children.Add(Field("Source", _sqlSource)); _typeFields.Children.Add(Field("Command text", _sqlCommandText)); _typeFields.Children.Add(Field("Procedure", _sqlProcedure)); _typeFields.Children.Add(Field("Parameters JSON", _sqlParameters)); _typeFields.Children.Add(Field("Outputs JSON", _sqlOutputs)); _typeFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { DashboardStyle.Action("New / edit template", EditTemplate, "primary"), DashboardStyle.Action("Validate template", ValidateTemplate), DashboardStyle.Action("Generate SQL StepConfig", GenerateStepConfig) } }); }
        if (type == "ActionManual") { _typeFields.Children.Add(DashboardStyle.Text("ActionManual configuration", 13, true)); _typeFields.Children.Add(DashboardStyle.Text($"The Request sheet must contain a column named '{_stepName.Text}.manualinfo'. The value is shown to the operator. The current row pauses until Pass or Fail, and a failure stops the row immediately.", 12, false, DashboardStyle.Muted)); }
    }

    private void LoadStepConfigFields(string raw)
    {
        try
        {
            var root = JsonNode.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw) as JsonObject;
            string Text(string key) => root?[key]?.ToString() ?? "";
            if (root is not null) using (var document = JsonDocument.Parse(root.ToJsonString())) _multipartEditor.Load(document.RootElement);
            _graphqlSource.Text = Text("Source"); _graphqlInline.Text = Text("Inline"); _graphqlOperation.Text = Text("OperationName"); _graphqlVariables.Text = root?["Variables"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
            _fileAction.SelectedItem = Text("Action") is { Length: > 0 } fileAction ? fileAction : "Read"; _filePath.Text = Text("Path"); _fileContent.Text = Text("Content"); _fileOutput.Text = Text("OutputVariable") is { Length: > 0 } output ? output : "fileContent"; _fileJsonPath.Text = Text("JsonPath"); _fileEncoding.SelectedItem = Text("Encoding") is { Length: > 0 } encoding ? encoding : "Base64"; _fileMode.SelectedItem = Text("OutputMode") is { Length: > 0 } mode ? mode : "Reference"; _fileMaxBytes.Text = Text("MaxBytes") is { Length: > 0 } maxBytes ? maxBytes : "52428800"; _fileContentType.Text = Text("ContentType") is { Length: > 0 } contentType ? contentType : "application/octet-stream";
            _scriptSource.Text = Text("Source"); _scriptInline.Text = Text("Inline"); _sqlConnection.Text = Text("ConnectionProfile"); _sqlCommandType.SelectedItem = Text("CommandType") is { Length: > 0 } commandType ? commandType : "Text"; _sqlSource.Text = Text("Source"); _sqlCommandText.Text = Text("CommandText"); _sqlProcedure.Text = Text("Procedure"); _sqlParameters.Text = root?["Parameters"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}"; _sqlOutputs.Text = root?["Outputs"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
            if (root?["Parts"] is JsonArray parts)
            {
                var textPart = parts.OfType<JsonObject>().FirstOrDefault(p => !string.Equals(p["Type"]?.ToString(), "file", StringComparison.OrdinalIgnoreCase));
                var filePart = parts.OfType<JsonObject>().FirstOrDefault(p => string.Equals(p["Type"]?.ToString(), "file", StringComparison.OrdinalIgnoreCase));
                _multipartTextName.Text = textPart?["Name"]?.ToString() ?? "description"; _multipartTextValue.Text = textPart?["Value"]?.ToString() ?? "";
                _multipartFileName.Text = filePart?["Name"]?.ToString() ?? "file"; _multipartFilePath.Text = filePart?["Path"]?.ToString() ?? "Files/sample-upload.txt"; _multipartFileContentType.Text = filePart?["ContentType"]?.ToString() ?? "application/octet-stream";
            }
        }
        catch { }
    }

    private StackPanel BuildHeaderFields(StepRow row)
    {
        _headerEditors.Clear();
        foreach (var key in _headers.Keys.Where(x => x.StartsWith("header_", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x))
            _headerEditors.Add((new TextBox { Text = key[7..] }, new TextBox { Text = row.Get(_headers, key) }));
        _headerFields = new StackPanel { Spacing = 5 };
        RebuildHeaderFields();
        return _headerFields;
    }

    private void RebuildHeaderFields()
    {
        // Detach the old row grids before reusing their TextBox controls. Avalonia
        // rejects adding a control to a new Grid while it still has the old Grid
        // as its visual parent.
        foreach (var grid in _headerFields.Children.OfType<Grid>().ToList()) grid.Children.Clear();
        _headerFields.Children.Clear();
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        title.Children.Add(DashboardStyle.Text("HTTP headers", 13, true));
        var add = DashboardStyle.Action("＋ Add header", () => { _headerEditors.Add((new TextBox { Watermark = "Header name" }, new TextBox { Watermark = "Header value" })); RebuildHeaderFields(); }); add.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(add, 1); title.Children.Add(add); _headerFields.Children.Add(title);
        foreach (var pair in _headerEditors.ToList())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,Auto") };
            row.Children.Add(pair.Name); Grid.SetColumn(pair.Value, 1); row.Children.Add(pair.Value);
            var remove = DashboardStyle.Action("− Remove", () => { _headerEditors.Remove(pair); RebuildHeaderFields(); }); remove.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(remove, 2); row.Children.Add(remove); _headerFields.Children.Add(row);
            pair.Name.PropertyChanged += (_, _) => { if (!_loading) UpdateRowFromForm(); }; pair.Value.PropertyChanged += (_, _) => { if (!_loading) UpdateRowFromForm(); };
        }
        if (_headerEditors.Count == 0) _headerFields.Children.Add(DashboardStyle.Text("No headers configured. Use Add header to create one.", 11, false, DashboardStyle.Muted));
    }

    private void GenerateStepConfig()
    {
        var type = _stepType.SelectedItem?.ToString() ?? "HTTP";
        object value = type switch
        {
            "GraphQL" or "CaseQL" => new { Source = _graphqlSource.Text, Inline = _graphqlInline.Text, OperationName = _graphqlOperation.Text, Variables = ParseJson(_graphqlVariables.Text) },
            "File" => new { Action = _fileAction.SelectedItem?.ToString(), Path = _filePath.Text, Content = _fileContent.Text, OutputVariable = _fileOutput.Text, JsonPath = _fileJsonPath.Text, Encoding = _fileEncoding.SelectedItem?.ToString(), OutputMode = _fileMode.SelectedItem?.ToString(), MaxBytes = ParseLong(_fileMaxBytes.Text, 52428800), ContentType = _fileContentType.Text },
            "Script" => new { Source = _scriptSource.Text, Inline = _scriptInline.Text },
            "SQL" => new { ConnectionProfile = _sqlConnection.Text, CommandType = _sqlCommandType.SelectedItem?.ToString(), Source = _sqlSource.Text, CommandText = _sqlCommandText.Text, Procedure = _sqlProcedure.Text, Parameters = ParseJson(_sqlParameters.Text), Outputs = ParseJson(_sqlOutputs.Text) },
            "HTTP" when _contentType.Text.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) => new { Parts = _multipartEditor.BuildParts() },
            _ => new { }
        };
        _stepConfig.Text = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }); UpdateRowFromForm();
    }

    private static JsonElement ParseJson(string? value) { try { using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value); return doc.RootElement.Clone(); } catch { return JsonDocument.Parse("{}").RootElement.Clone(); } }
    private static long ParseLong(string? value, long fallback) => long.TryParse(value, out var parsed) && parsed >= 0 ? parsed : fallback;

    private void UpdateRowFromForm()
    {
        if (_selectedStep is null) return;
        _selectedStep.Set(_headers, "stepname", _stepName.Text?.Trim() ?? ""); _selectedStep.Set(_headers, "steptype", _stepType.SelectedItem?.ToString() ?? "HTTP"); _selectedStep.Set(_headers, "enabled", (_enabled.IsChecked == true).ToString().ToLowerInvariant()); _selectedStep.Set(_headers, "stage", _stage.SelectedItem?.ToString() ?? "Test"); _selectedStep.Set(_headers, "timeout", _timeout.Text ?? "0"); _selectedStep.Set(_headers, "manualtimeoutminutes", _manualTimeoutMinutes.Text ?? "15"); _selectedStep.Set(_headers, "wait", _wait.Text ?? "0"); _selectedStep.Set(_headers, "targeturl", _targetUrl.Text ?? ""); _selectedStep.Set(_headers, "verb", _verb.SelectedItem?.ToString() ?? "POST"); _selectedStep.Set(_headers, "templetsource", _template.Text ?? ""); _selectedStep.Set(_headers, "contenttype", _contentType.Text ?? ""); _selectedStep.Set(_headers, "expectedstatus", _expectedStatus.Text ?? ""); _selectedStep.Set(_headers, "stepconfig", _stepConfig.Text ?? "{}"); _selectedStep.Set(_headers, "assert", (_assertEnabled.IsChecked == true).ToString().ToLowerInvariant()); _selectedStep.Set(_headers, "ignoreempty", (_ignoreEmpty.IsChecked == true).ToString().ToLowerInvariant()); _selectedStep.Set(_headers, "assertonlyresponse", (_assertOnlyResponse.IsChecked == true).ToString().ToLowerInvariant()); _selectedStep.Set(_headers, "stoponfailure", (_stopOnFailure.IsChecked == true).ToString().ToLowerInvariant());
        foreach (var key in _headers.Keys.Where(x => x.StartsWith("header_", StringComparison.OrdinalIgnoreCase)).ToList()) _selectedStep.Set(_headers, key, "");
        foreach (var pair in _headerEditors)
        {
            var name = pair.Name.Text?.Trim() ?? ""; if (string.IsNullOrWhiteSpace(name)) continue;
            var key = "header_" + name; if (!_headers.ContainsKey(key)) _headers[key] = _headers.Values.DefaultIfEmpty(0).Max() + 1; _selectedStep.Set(_headers, key, pair.Value.Text ?? "");
        }
        _dirty = true; _status.Text = "Unsaved workbook changes."; _status.Foreground = DashboardStyle.Brush("#F6C76B");
    }

    private void AddStep()
    {
        if (!_byTestcase.TryGetValue(_selectedTestcase, out var rows)) return;
        var name = _newStepName.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) { _status.Text = "Enter a step name before adding the step."; _status.Foreground = DashboardStyle.Red; return; }
        if (rows.Any(x => x.Get(_headers, "stepname").Equals(name, StringComparison.OrdinalIgnoreCase))) { _status.Text = $"A step named '{name}' already exists in this testcase."; _status.Foreground = DashboardStyle.Red; return; }
        var row = new StepRow { OriginalRow = int.MaxValue }; foreach (var col in _headers.Values) row.Cells[col] = "";
        var type = _newStepType.SelectedItem?.ToString() ?? "HTTP";
        row.Set(_headers, "testcaseindex", _selectedTestcase.ToString()); row.Set(_headers, "testcase", rows.FirstOrDefault()?.Get(_headers, "testcase", $"Testcase {_selectedTestcase}") ?? $"Testcase {_selectedTestcase}"); row.Set(_headers, "stepname", name); row.Set(_headers, "steptype", type); row.Set(_headers, "enabled", "true"); row.Set(_headers, "stage", "Test"); row.Set(_headers, "stepconfig", "{}");
        rows.Add(row); RebuildWorkbookOrder(); _newStepName.Text = ""; _dirty = true; _selectedStep = row; RenderSteps(); _status.Text = "New step added. Complete its fields below, then save."; _status.Foreground = DashboardStyle.Brush("#F6C76B");
    }

    private void EditTemplate()
    {
        var source = string.IsNullOrWhiteSpace(_template.Text) ? $"{_stepName.Text?.Trim() ?? "step"}.json" : _template.Text!.Trim();
        var testcaseName = _byTestcase.GetValueOrDefault(_selectedTestcase)?.FirstOrDefault()?.Get(_headers, "testcase", _selectedTestcase.ToString()) ?? _selectedTestcase.ToString();
        var editor = new TemplateEditorWindow(_item.TemplatesFolder, source, _stepType.SelectedItem?.ToString() ?? "HTTP", value => { _template.Text = value; UpdateRowFromForm(); _status.Text = "Template saved."; _status.Foreground = DashboardStyle.Green; }, _item.Name, testcaseName, _stepName.Text);
        editor.Show();
    }

    private void ValidateTemplate()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_template.Text)) throw new InvalidDataException("No template source configured.");
            var path = Path.GetFullPath(Path.Combine(_item.TemplatesFolder, _template.Text.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(_item.TemplatesFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new InvalidDataException($"Template file was not found: {_template.Text}");
            _status.Text = "Template file exists and is inside the instance Templates folder."; _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.Foreground = DashboardStyle.Red; }
    }

    private void Move(StepRow row, int delta)
    {
        if (!_byTestcase.TryGetValue(_selectedTestcase, out var rows)) return; var index = rows.IndexOf(row); var target = index + delta; if (index < 0 || target < 0 || target >= rows.Count) return; (rows[index], rows[target]) = (rows[target], rows[index]); RebuildWorkbookOrder(); _dirty = true; RenderSteps(); _status.Text = "Step order changed. Save to update the workbook.";
    }

    private void MoveSelectedUp() { if (_selectedStep is not null) Move(_selectedStep, -1); }
    private void MoveSelectedDown() { if (_selectedStep is not null) Move(_selectedStep, 1); }
    private void DeleteSelected() { if (_selectedStep is not null) Delete(_selectedStep); }

    private void Delete(StepRow row)
    {
        if (!_byTestcase.TryGetValue(_selectedTestcase, out var rows)) return;
        var confirm = new Window { Title = "Delete step", Width = 430, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = DashboardStyle.Action("Delete complete row", () => { rows.Remove(row); RebuildWorkbookOrder(); _dirty = true; confirm.Close(); RenderSteps(); _status.Text = "Step marked for deletion. Save to update the workbook."; }, "primary");
        var no = DashboardStyle.Action("Cancel", confirm.Close); confirm.Content = new Border { Padding = new Thickness(18), Child = new StackPanel { Spacing = 12, Children = { DashboardStyle.Text($"Delete step '{row.Get(_headers, "stepname", "step")}'? The complete config row will be removed.", 13), new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } } } } }; confirm.Show();
    }

    private void RebuildWorkbookOrder()
    {
        _rows.Clear();
        foreach (var group in _byTestcase.Values) _rows.AddRange(group);
    }

    public bool TrySave(out string error)
    {
        error = ""; if (!_dirty) return true;
        try
        {
            var headerNames = _headerEditors.Select(x => x.Name.Text?.Trim() ?? "").Where(x => x.Length > 0).ToList();
            if (headerNames.Count != headerNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()) throw new InvalidDataException("HTTP header names must be unique.");
            if (headerNames.Any(x => x.Contains('\n') || x.Contains('\r') || x.StartsWith("header_", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("HTTP header names must contain only the header name, without the header_ prefix or line breaks.");
            using var workbook = new XLWorkbook(_item.WorkbookPath); var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The workbook has no config sheet.");
            var backup = $"{_item.WorkbookPath}.backup.{DateTime.Now:yyyyMMdd_HHmmssfff}.xlsx"; File.Copy(_item.WorkbookPath, backup, false);
            var original = sheet.RowsUsed().Skip(1).Where(r => r.CellsUsed().Any(c => !string.IsNullOrWhiteSpace(c.GetString()))).ToList();
            var stepChanges = BuildStepChanges(original);
            foreach (var header in _headers) sheet.Cell(1, header.Value).Value = header.Key;
            var lastColumn = Math.Max(sheet.LastColumnUsed()?.ColumnNumber() ?? 1, _headers.Values.DefaultIfEmpty(1).Max());
            for (var i = 0; i < _rows.Count; i++)
            {
                var target = sheet.Row(i + 2); var source = _rows[i];
                for (var column = 1; column <= lastColumn; column++) target.Cell(column).Value = source.Cells.GetValueOrDefault(column, "");
            }
            for (var row = _rows.Count + 2; row <= original.Count + 1; row++) for (var column = 1; column <= lastColumn; column++) sheet.Cell(row, column).Clear(XLClearOptions.Contents);
            var warnings = WorkbookSynchronization.SynchronizeStepColumns(workbook, stepChanges);
            workbook.Save(); _dirty = false; error = $"Workbook saved. Backup: {Path.GetFileName(backup)}"; _status.Text = error; _status.Foreground = DashboardStyle.Green; return true;
        }
        catch (Exception ex) { error = ex.Message; _status.Text = $"Could not save step changes: {ex.Message}"; _status.Foreground = DashboardStyle.Red; return false; }
    }

    private List<WorkbookSynchronization.StepChange> BuildStepChanges(IReadOnlyList<IXLRow> original)
    {
        var changes = new List<WorkbookSynchronization.StepChange>();
        var currentByRow = _rows.Where(x => x.OriginalRow != int.MaxValue).ToDictionary(x => x.OriginalRow);
        foreach (var oldRow in original)
        {
            var testcase = oldRow.Cell(_headers.GetValueOrDefault("testcaseindex")).GetString().Trim();
            var oldName = oldRow.Cell(_headers.GetValueOrDefault("stepname")).GetString().Trim();
            if (string.IsNullOrWhiteSpace(oldName)) continue;
            var rowNumber = oldRow.RowNumber();
            if (!currentByRow.TryGetValue(rowNumber, out var current)) changes.Add(new WorkbookSynchronization.StepChange(testcase, oldName, null));
            else
            {
                var newName = current.Get(_headers, "stepname").Trim();
                if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(newName))
                    changes.Add(new WorkbookSynchronization.StepChange(testcase, oldName, newName));
            }
        }
        return changes;
    }

    private static Control Field(string label, Control control) => new StackPanel { Spacing = 3, Children = { DashboardStyle.Text(label, 11, false, DashboardStyle.Muted), control } };
}
