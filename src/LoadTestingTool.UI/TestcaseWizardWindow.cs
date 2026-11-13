using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

public sealed class TestcaseWizardDraft
{
    public int TestcaseIndex { get; init; }
    public string TestcaseName { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string StepType { get; init; } = "HTTP";
    public string Stage { get; init; } = "Test";
    public bool Enabled { get; init; } = true;
    public int TimeoutSeconds { get; init; } = 30;
    public int WaitMilliseconds { get; init; }
    public string TargetUrl { get; init; } = string.Empty;
    public string Verb { get; init; } = "GET";
    public string TemplateSource { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/json";
    public string ExpectedStatus { get; init; } = string.Empty;
    public bool CreateInitialRow { get; init; } = true;
    public string DataId { get; init; } = "Row1";
}

internal sealed class TestcaseWizardWindow : Window
{
    private readonly Action<TestcaseWizardDraft> _created;
    private readonly TextBox _index = Field("Auto");
    private readonly TextBox _name = Field("NewTestcase");
    private readonly TextBox _stepName = Field("Request");
    private readonly ComboBox _stepType = new() { ItemsSource = new[] { "HTTP", "GraphQL", "CaseQL", "File", "Script", "SQL", "ActionManual" }, SelectedIndex = 0, Width = 260 };
    private readonly ComboBox _stage = new() { ItemsSource = new[] { "Setup", "Test", "Teardown" }, SelectedIndex = 1, Width = 220 };
    private readonly CheckBox _enabled = new() { Content = "Enable the initial step", IsChecked = true };
    private readonly TextBox _timeout = Field("30");
    private readonly TextBox _wait = Field("0");
    private readonly TextBox _url = Field("https://example.test/api");
    private readonly ComboBox _verb = new() { ItemsSource = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" }, SelectedIndex = 0, Width = 220 };
    private readonly TextBox _template = Field("");
    private readonly ComboBox _contentType = new() { ItemsSource = new[] { "application/json", "application/xml", "text/plain", "application/graphql", "application/octet-stream" }, SelectedIndex = 0, Width = 260 };
    private readonly TextBox _expectedStatus = Field("");
    private readonly CheckBox _createRow = new() { Content = "Create an initial request data row", IsChecked = true };
    private readonly TextBox _dataId = Field("Row1");
    private readonly TextBlock _typeHelp = DashboardStyle.Text("HTTP fields are used when the initial step type is HTTP.", 11, false, DashboardStyle.Muted);
    private readonly TextBlock _error = DashboardStyle.Text("", 12, false, DashboardStyle.Red);

    public bool Saved { get; private set; }

    public TestcaseWizardWindow(int nextIndex, Action<TestcaseWizardDraft> created)
    {
        _created = created;
        Title = "Create test case";
        Width = 720;
        Height = 760;
        MinWidth = 620;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _index.Text = nextIndex.ToString();
        _stepType.SelectionChanged += (_, _) => UpdateTypeHelp();
        _createRow.IsCheckedChanged += (_, _) => _dataId.IsEnabled = _createRow.IsChecked == true;
        var common = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        AddRow(common, 0, "Testcase index", _index);
        AddRow(common, 1, "Testcase name", _name);
        AddRow(common, 2, "Initial step name", _stepName);
        AddRow(common, 3, "Step type", _stepType);
        AddRow(common, 4, "Stage", _stage);
        AddRow(common, 5, "Timeout (seconds)", _timeout);
        AddRow(common, 6, "Wait (milliseconds)", _wait);

        var request = new StackPanel { Spacing = 7 };
        request.Children.Add(DashboardStyle.Text("Initial step settings", 15, true));
        request.Children.Add(_typeHelp);
        request.Children.Add(Field("Enabled", _enabled));
        request.Children.Add(Field("Target URL", _url));
        request.Children.Add(Field("HTTP verb", _verb));
        request.Children.Add(Field("Template source", _template));
        request.Children.Add(Field("Content type", _contentType));
        request.Children.Add(Field("Expected status (optional)", _expectedStatus));

        var data = new StackPanel { Spacing = 7 };
        data.Children.Add(DashboardStyle.Text("Initial request data", 15, true));
        data.Children.Add(DashboardStyle.Text("A request row makes the new testcase immediately runnable. You can add or edit rows later in Edit dataset.", 11, false, DashboardStyle.Muted));
        data.Children.Add(_createRow);
        data.Children.Add(Field("Data ID", _dataId));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children =
        {
            DashboardStyle.Action("Cancel", Close),
            DashboardStyle.Action("Create test case", Save, "primary")
        }};
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children =
        {
            DashboardStyle.Text("Create test case", 22, true),
            DashboardStyle.Text("This creates the testcase directly in the selected instance workbook. The new testcase will appear in Configure test steps after the workbook is refreshed.", 12, false, DashboardStyle.Muted),
            common,
            request,
            data,
            _error,
            buttons
        }};
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        _dataId.IsEnabled = true;
        UpdateTypeHelp();
    }

    private void UpdateTypeHelp()
    {
        var type = _stepType.SelectedItem?.ToString() ?? "HTTP";
        _typeHelp.Text = type.Equals("HTTP", StringComparison.OrdinalIgnoreCase)
            ? "HTTP fields below will be written to the new config row. Other step types start with an empty StepConfig and can be completed in Configure test steps."
            : $"The initial step will be created as {type}. Complete its type-specific StepConfig in Configure test steps after creation.";
        var isHttp = type.Equals("HTTP", StringComparison.OrdinalIgnoreCase);
        _url.IsEnabled = isHttp; _verb.IsEnabled = isHttp; _template.IsEnabled = isHttp; _contentType.IsEnabled = isHttp; _expectedStatus.IsEnabled = isHttp;
    }

    private void Save()
    {
        _error.Text = "";
        if (!int.TryParse(_index.Text, out var index) || index < 1) { _error.Text = "Testcase index must be a positive number."; return; }
        var name = _name.Text?.Trim() ?? "";
        var stepName = _stepName.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) { _error.Text = "Testcase name is required."; return; }
        if (string.IsNullOrWhiteSpace(stepName)) { _error.Text = "Initial step name is required."; return; }
        if (!int.TryParse(_timeout.Text, out var timeout) || timeout < 0) { _error.Text = "Timeout must be zero or a positive number of seconds."; return; }
        if (!int.TryParse(_wait.Text, out var wait) || wait < 0) { _error.Text = "Wait must be zero or a positive number of milliseconds."; return; }
        var type = _stepType.SelectedItem?.ToString() ?? "HTTP";
        var dataId = _dataId.Text?.Trim() ?? "";
        if (_createRow.IsChecked == true && string.IsNullOrWhiteSpace(dataId)) { _error.Text = "Data ID is required when creating the initial request row."; return; }
        _created(new TestcaseWizardDraft
        {
            TestcaseIndex = index, TestcaseName = name, StepName = stepName, StepType = type,
            Stage = _stage.SelectedItem?.ToString() ?? "Test", Enabled = _enabled.IsChecked == true,
            TimeoutSeconds = timeout, WaitMilliseconds = wait, TargetUrl = _url.Text?.Trim() ?? "",
            Verb = _verb.SelectedItem?.ToString() ?? "GET", TemplateSource = _template.Text?.Trim() ?? "",
            ContentType = _contentType.SelectedItem?.ToString() ?? "application/json", ExpectedStatus = _expectedStatus.Text?.Trim() ?? "",
            CreateInitialRow = _createRow.IsChecked == true, DataId = dataId
        });
        Saved = true;
        Close();
    }

    private static TextBox Field(string text) => new() { Text = text, MinWidth = 260 };
    private static Control Field(string label, Control control) => new StackPanel { Spacing = 3, Children = { DashboardStyle.Text(label, 11, false, DashboardStyle.Muted), control } };
    private static void AddRow(Grid grid, int row, string label, Control control)
    {
        var labelControl = DashboardStyle.Text(label, 12, false, DashboardStyle.Muted);
        grid.Children.Add(labelControl); Grid.SetRow(labelControl, row);
        grid.Children.Add(control); Grid.SetRow(control, row); Grid.SetColumn(control, 1);
    }
}
