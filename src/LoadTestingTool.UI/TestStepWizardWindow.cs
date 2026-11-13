using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI;

internal sealed class TestStepWizardWindow : Window
{
    private readonly Action<WizardStepDraft> _saveStep;
    private readonly string _templatesFolder;
    private readonly string _instanceName;
    private readonly TextBox _testcaseIndex = Field("1");
    private readonly TextBox _testcase = Field("ExampleTest");
    private readonly TextBox _stepName = Field("NewStep");
    private readonly ComboBox _stepType = new() { Width = 230, ItemsSource = new[] { "HTTP", "GraphQL", "CaseQL", "File", "Script", "SQL" }, SelectedIndex = 0 };
    private readonly ComboBox _stage = new() { Width = 180, ItemsSource = new[] { "Setup", "Test", "Teardown" }, SelectedIndex = 1 };
    private readonly CheckBox _enabled = new() { Content = "Enabled", IsChecked = true };
    private readonly TextBox _timeout = Field("30");
    private readonly TextBox _targetUrl = Field("https://example.test/api");
    private readonly ComboBox _verb = new() { Width = 240, ItemsSource = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" }, SelectedIndex = 0 };
    private readonly TextBox _template = Field("request.json");
    private readonly ComboBox _contentType = new() { Width = 240, ItemsSource = new[] { "application/json", "application/xml", "text/plain", "application/graphql", "application/octet-stream", "multipart/form-data" }, SelectedIndex = 0 };
    private readonly TextBox _expectedStatus = Field("200");
    private readonly TextBox _graphqlQuery = Field("query Example { field }");
    private readonly TextBox _graphqlOperation = Field("Example");
    private readonly TextBox _graphqlVariables = Field("{}");
    private readonly ComboBox _fileAction = new() { Width = 240, ItemsSource = new[] { "Read", "Json", "ReadBinary", "Write", "Append", "WriteBinary", "AppendBinary" }, SelectedIndex = 2 };
    private readonly TextBox _filePath = Field("Files/image.png");
    private readonly TextBox _fileOutput = Field("fileData");
    private readonly ComboBox _fileMode = new() { Width = 240, ItemsSource = new[] { "Value", "Reference" }, SelectedIndex = 1 };
    private readonly TextBox _scriptSource = Field("Scripts/setup.csx");
    private readonly TextBox _sqlConnection = Field("Default");
    private readonly TextBox _sqlQuery = Field("select 1");
    private readonly TextBox _multipartTextName = Field("description");
    private readonly TextBox _multipartTextValue = Field("sample upload");
    private readonly TextBox _multipartFileName = Field("file");
    private readonly TextBox _multipartFilePath = Field("Files/sample-upload.txt");
    private readonly TextBox _multipartFileContentType = Field("text/plain");
    private readonly ComboBox _sqlCommandType = new() { Width = 240, ItemsSource = new[] { "Text", "StoredProcedure" }, SelectedIndex = 0 };
    private readonly TextBox _waitMs = Field("0");
    private readonly CheckBox _assertEnabled = new() { Content = "Enable assertions", IsChecked = true };
    private readonly CheckBox _assertOnlyResponse = new() { Content = "Assert only response fields", IsChecked = false };
    private readonly CheckBox _stopOnFailure = new() { Content = "Stop on failure", IsChecked = true };
    private readonly TextBox _stepConfig = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, FontFamily = new Avalonia.Media.FontFamily("Consolas") };
    private readonly TextBlock _help = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly TextBlock _error = DashboardStyle.Text("", 11, false, DashboardStyle.Red);
    private readonly TextBlock _preview = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly StackPanel _httpFields = new() { Spacing = 6 };
    private readonly StackPanel _templateFields = new() { Spacing = 6 };
    private readonly StackPanel _typeFields = new() { Spacing = 6 };
    private readonly MultipartPartsEditor _multipartEditor = new();
    private readonly int? _editRow;

    public TestStepWizardWindow(Action<WizardStepDraft> saveStep, WizardStepDraft? existing = null, string? templatesFolder = null, string? instanceName = null)
    {
        _saveStep = saveStep; _editRow = existing?.Row; _templatesFolder = Path.GetFullPath(templatesFolder ?? Path.Combine(Directory.GetCurrentDirectory(), "Templates")); _instanceName = string.IsNullOrWhiteSpace(instanceName) ? "—" : instanceName;
        Title = existing is null ? "Add test step" : "Edit test step";
        Width = 760; Height = 820; MinWidth = 620; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (existing is not null) Load(existing);
        _stepType.SelectionChanged += (_, _) => UpdateTypeFields();
        _contentType.SelectionChanged += (_, _) => UpdateTypeFields();
        _multipartEditor.Changed += (_, _) => { if (string.Equals(_contentType.SelectedItem?.ToString(), "multipart/form-data", StringComparison.OrdinalIgnoreCase)) GenerateStepConfig(); };
        _stepConfig.TextChanged += (_, _) => UpdatePreview();
        var common = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        AddRow(common, 0, "Testcase index", _testcaseIndex); AddRow(common, 1, "Testcase", _testcase); AddRow(common, 2, "Step name", _stepName); AddRow(common, 3, "Step type", _stepType); AddRow(common, 4, "Stage", _stage); AddRow(common, 5, "Timeout (seconds)", _timeout); AddRow(common, 6, "Wait (milliseconds)", _waitMs); AddRow(common, 7, "Enabled", _enabled); AddRow(common, 8, "Assertions", _assertEnabled); AddRow(common, 9, "Assert mode", _assertOnlyResponse); AddRow(common, 10, "Failure handling", _stopOnFailure);
        _httpFields.Children.Add(Labeled("Target URL", _targetUrl));
        _httpFields.Children.Add(Labeled("HTTP verb", _verb));
        _httpFields.Children.Add(Labeled("Content type", _contentType));
        _httpFields.Children.Add(Labeled("Expected status", _expectedStatus));
        _templateFields.Children.Add(Labeled("Template source", _template));
        _templateFields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { DashboardStyle.Action("New / edit template", EditTemplate, "primary"), DashboardStyle.Action("Validate template", ValidateTemplate) } });
        var configPanel = new StackPanel { Spacing = 5, Children = { DashboardStyle.Text("StepConfig JSON", 13, true), _stepConfig, _help, DashboardStyle.Text("Generated preview", 13, true), _preview } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { DashboardStyle.Action("Help", ShowHelp), DashboardStyle.Action("Cancel", Close), DashboardStyle.Action(existing is null ? "Add step" : "Save step", Save, "primary") } };
        var body = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { DashboardStyle.Text(existing is null ? "Add test step" : "Edit test step", 21, true), DashboardStyle.Text("Switching type changes only the executor section; other values remain intact.", 12, false, DashboardStyle.Muted), common, _httpFields, _templateFields, _typeFields, configPanel, _error, buttons } };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        UpdateTypeFields(); UpdatePreview();
    }

    private static TextBox Field(string value) => new() { Text = value, MinWidth = 240 };
    private static Control Labeled(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*") };
        var labelControl = DashboardStyle.Text(label, 12, false, DashboardStyle.Muted);
        grid.Children.Add(labelControl); grid.Children.Add(control); Grid.SetColumn(control, 1); return grid;
    }
    private static void AddRow(Grid grid, int row, string label, Control control) { grid.Children.Add(DashboardStyle.Text(label, 12, false, DashboardStyle.Muted)); Grid.SetRow(grid.Children[^1], row); grid.Children.Add(control); Grid.SetRow(control, row); Grid.SetColumn(control, 1); }
    private void Load(WizardStepDraft existing)
    {
        Set(_testcaseIndex, existing.Get("testcaseindex")); Set(_testcase, existing.Get("testcase")); Set(_stepName, existing.Get("stepname")); Set(_timeout, existing.Get("timeout", "30")); Set(_waitMs, existing.Get("wait", "0")); Set(_targetUrl, existing.Get("targeturl")); _verb.SelectedItem = existing.Get("verb", "POST"); Set(_template, existing.Get("templetsource")); _contentType.SelectedItem = existing.Get("contenttype", "application/json"); Set(_expectedStatus, existing.Get("expectedstatus")); _assertEnabled.IsChecked = !existing.Get("assert", "true").Equals("false", StringComparison.OrdinalIgnoreCase); _assertOnlyResponse.IsChecked = existing.Get("assertonlyresponse", "false").Equals("true", StringComparison.OrdinalIgnoreCase); _stopOnFailure.IsChecked = !existing.Get("stoponfailure", "true").Equals("false", StringComparison.OrdinalIgnoreCase); _enabled.IsChecked = !existing.Get("enabled", "true").Equals("false", StringComparison.OrdinalIgnoreCase); _stage.SelectedItem = existing.Get("stage", "Test"); _stepType.SelectedItem = existing.Get("steptype", "HTTP"); _stepConfig.Text = existing.Get("stepconfig", "{}"); try { using var document = JsonDocument.Parse(_stepConfig.Text); _multipartEditor.Load(document.RootElement); } catch { }
    }
    private static void Set(TextBox box, string value) => box.Text = value;
    private void UpdateTypeFields()
    {
        var type = _stepType.SelectedItem?.ToString() ?? "HTTP"; _httpFields.IsVisible = type is "HTTP" or "GraphQL";
        _typeFields.Children.Clear();
        _help.Text = type switch
        {
            "HTTP" => "HTTP uses Target URL, HTTP verb, content type, template source, response assertions, and optional wait time.",
            "GraphQL" => "GraphQL uses Target URL and StepConfig such as Source, Query, Variables, and OperationName.",
            "File" => "File StepConfig examples: {\"Action\":\"ReadBinary\",\"Path\":\"Files/image.png\",\"OutputVariable\":\"imageFile\",\"OutputMode\":\"Reference\"}.",
            "Script" => "Script StepConfig should identify the script source and inputs. Trusted scripts must be enabled in the instance.",
            "SQL" => "SQL uses a connection profile, command type, query/template, and parameters.",
            "CaseQL" => "CaseQL is a case-oriented GraphQL step and uses Target URL plus StepConfig.", _ => ""
        };
        if (type == "File") _typeFields.Children.Add(Labeled("File actions", new TextBlock { Text = "Read, Json, Write, Append, ReadBinary, WriteBinary, AppendBinary", TextWrapping = TextWrapping.Wrap }));
        if (type == "Script") _typeFields.Children.Add(Labeled("Security", new TextBlock { Text = "Trusted scripts must be enabled in the instance settings.", TextWrapping = TextWrapping.Wrap }));
        if (type == "GraphQL") { _typeFields.Children.Add(Labeled("Query", _graphqlQuery)); _typeFields.Children.Add(Labeled("Operation name", _graphqlOperation)); _typeFields.Children.Add(Labeled("Variables JSON", _graphqlVariables)); _typeFields.Children.Add(DashboardStyle.Action("Generate GraphQL StepConfig", GenerateStepConfig)); }
        if (type == "File") { _typeFields.Children.Add(Labeled("Action", _fileAction)); _typeFields.Children.Add(Labeled("Path", _filePath)); _typeFields.Children.Add(Labeled("Output variable", _fileOutput)); _typeFields.Children.Add(Labeled("Output mode", _fileMode)); _typeFields.Children.Add(DashboardStyle.Action("Generate File StepConfig", GenerateStepConfig)); }
        if (type == "Script") { _typeFields.Children.Add(Labeled("Script source", _scriptSource)); _typeFields.Children.Add(DashboardStyle.Action("Generate Script StepConfig", GenerateStepConfig)); }
        if (type == "SQL") { _typeFields.Children.Add(Labeled("Connection", _sqlConnection)); _typeFields.Children.Add(Labeled("Command type", _sqlCommandType)); _typeFields.Children.Add(Labeled("Query", _sqlQuery)); _typeFields.Children.Add(DashboardStyle.Action("Generate SQL StepConfig", GenerateStepConfig)); }
        if (type == "HTTP" && string.Equals(_contentType.SelectedItem?.ToString(), "multipart/form-data", StringComparison.OrdinalIgnoreCase)) _typeFields.Children.Add(_multipartEditor);
    }
    private void GenerateStepConfig()
    {
        var type = _stepType.SelectedItem?.ToString() ?? "HTTP";
        if (type == "HTTP" && string.Equals(_contentType.SelectedItem?.ToString(), "multipart/form-data", StringComparison.OrdinalIgnoreCase) && !_multipartEditor.Validate(out var multipartError)) { _error.Text = multipartError; return; }
        object value = type switch
        {
            "GraphQL" => new { Query = _graphqlQuery.Text ?? "", OperationName = _graphqlOperation.Text ?? "", Variables = ParseObject(_graphqlVariables.Text) },
            "File" => new { Action = _fileAction.SelectedItem?.ToString() ?? "ReadBinary", Path = _filePath.Text ?? "", OutputVariable = _fileOutput.Text ?? "", OutputMode = _fileMode.SelectedItem?.ToString() ?? "Reference" },
            "Script" => new { Source = _scriptSource.Text ?? "" },
            "SQL" => new { Connection = _sqlConnection.Text ?? "", CommandType = _sqlCommandType.SelectedItem?.ToString() ?? "Text", Query = _sqlQuery.Text ?? "" },
            "HTTP" when string.Equals(_contentType.SelectedItem?.ToString(), "multipart/form-data", StringComparison.OrdinalIgnoreCase) => new { Parts = _multipartEditor.BuildParts() },
            _ => new { }
        };
        _stepConfig.Text = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
    }
    private static object ParseObject(string? text) { try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text!).RootElement; } catch { return new Dictionary<string, string>(); } }
    private void UpdatePreview()
    {
        try { _preview.Text = JsonSerializer.Serialize(JsonDocument.Parse(NormalizeRuntimeVariables(string.IsNullOrWhiteSpace(_stepConfig.Text) ? "{}" : _stepConfig.Text)).RootElement, new JsonSerializerOptions { WriteIndented = true }); }
        catch { _preview.Text = "Invalid JSON in StepConfig"; }
    }
    private void EditTemplate()
    {
        var source = string.IsNullOrWhiteSpace(_template.Text) ? $"{_stepName.Text?.Trim() ?? "step"}.json" : _template.Text!.Trim();
        var editor = new TemplateEditorWindow(_templatesFolder, source, _stepType.SelectedItem?.ToString() ?? "HTTP", value => { _template.Text = value; _error.Text = "Template saved."; _error.Foreground = DashboardStyle.Green; }, _instanceName, _testcase.Text, _stepName.Text);
        editor.Show(this);
    }

    private void ValidateTemplate()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_template.Text)) throw new InvalidDataException("No template source configured.");
            var path = Path.GetFullPath(Path.Combine(_templatesFolder, _template.Text.Replace('/', Path.DirectorySeparatorChar)));
            var root = _templatesFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new InvalidDataException($"Template file was not found: {_template.Text}");
            _error.Text = "Template file exists. Open the template editor to validate its content."; _error.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { _error.Text = ex.Message; _error.Foreground = DashboardStyle.Red; }
    }

    private void ShowHelp()
    {
        ContextHelpWindow.Show(this, "Test-step wizard help", "Testcase index identifies the testcase row in the workbook. Testcase and Step name are the stable names used by request/response headers.\n\nStep type selects the executor. Stage controls Setup, Test, or Teardown order. Timeout is per step in seconds; zero means no step timeout. Environments is a comma-separated filter such as local,staging.\n\nHTTP and GraphQL use Target URL, verb, template source, content type, and expected status. File uses StepConfig for Read, Write, Append, ReadBinary, WriteBinary, or AppendBinary. Script requires trusted scripts to be enabled. SQL uses StepConfig for the connection, command, and parameters.\n\nStepConfig must be valid JSON. Example HTTP: {\"Headers\":{\"Authorization\":\"Bearer _token_\"}}. Example binary file reference: {\"Action\":\"ReadBinary\",\"Path\":\"Files/image.png\",\"OutputVariable\":\"imageFile\",\"OutputMode\":\"Reference\"}. Use Advanced JSON when you need an executor option that is not shown as a form field.");
    }

    private void Save()
    {
        _error.Text = ""; var type = _stepType.SelectedItem?.ToString() ?? "HTTP";
        if (!int.TryParse(_testcaseIndex.Text, out var index) || index < 1) { _error.Text = "Testcase index must be a positive number."; return; }
        if (string.IsNullOrWhiteSpace(_testcase.Text) || string.IsNullOrWhiteSpace(_stepName.Text)) { _error.Text = "Testcase and step name are required."; return; }
        if (!int.TryParse(_timeout.Text, out var timeout) || timeout < 0) { _error.Text = "Timeout must be zero or a positive number."; return; }
        if (type is "HTTP" or "GraphQL" && string.IsNullOrWhiteSpace(_targetUrl.Text)) { _error.Text = "Target URL is required for HTTP and GraphQL steps."; return; }
        if (!string.IsNullOrWhiteSpace(_template.Text))
        {
            var templatePath = Path.GetFullPath(Path.Combine(_templatesFolder, _template.Text.Replace('/', Path.DirectorySeparatorChar)));
            var templateRoot = _templatesFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!templatePath.StartsWith(templateRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(templatePath)) { _error.Text = "Template source does not exist. Use New / edit template first."; return; }
        }
        try { using var _ = JsonDocument.Parse(NormalizeRuntimeVariables(string.IsNullOrWhiteSpace(_stepConfig.Text) ? "{}" : _stepConfig.Text)); } catch { _error.Text = "StepConfig must contain valid JSON after runtime variables are resolved."; return; }
        var row = new WizardStepDraft(_editRow, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["testcaseindex"] = index.ToString(), ["testcase"] = _testcase.Text?.Trim() ?? "", ["stepname"] = _stepName.Text?.Trim() ?? "", ["steptype"] = type, ["stage"] = _stage.SelectedItem?.ToString() ?? "Test", ["enabled"] = (_enabled.IsChecked == true).ToString().ToLowerInvariant(), ["timeout"] = timeout.ToString(), ["targeturl"] = _targetUrl.Text?.Trim() ?? "", ["verb"] = (_verb.SelectedItem?.ToString() ?? "POST").ToUpperInvariant(), ["templetsource"] = _template.Text?.Trim() ?? "", ["contenttype"] = (_contentType.SelectedItem?.ToString() ?? "application/json"), ["expectedstatus"] = _expectedStatus.Text?.Trim() ?? "", ["stepconfig"] = string.IsNullOrWhiteSpace(_stepConfig.Text) ? "{}" : _stepConfig.Text!.Trim(), ["stoponfailure"] = (_stopOnFailure.IsChecked == true).ToString().ToLowerInvariant(), ["assert"] = (_assertEnabled.IsChecked == true).ToString().ToLowerInvariant(), ["ignoreempty"] = "false", ["assertonlyresponse"] = (_assertOnlyResponse.IsChecked == true).ToString().ToLowerInvariant(), ["wait"] = int.TryParse(_waitMs.Text, out var wait) ? Math.Max(0, wait).ToString() : "0" });
        _saveStep(row); Close();
    }

    private static string NormalizeRuntimeVariables(string content) => Regex.Replace(content, "(?<![\\\"\\w])_[\\p{L}\\p{N}._-]+_(?![\\\"\\w])", "0");
}

internal sealed class WizardStepDraft
{
    public int? Row { get; }
    public IReadOnlyDictionary<string, string> Values { get; }
    public WizardStepDraft(int? row, IReadOnlyDictionary<string, string> values) { Row = row; Values = values; }
    public string Get(string name, string fallback = "") => Values.TryGetValue(name, out var value) ? value : fallback;
}
