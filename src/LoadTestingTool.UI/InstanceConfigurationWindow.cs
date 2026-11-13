using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;

namespace LoadTestingTool.UI;

public sealed class InstanceConfigurationWindow : Window
{
    private readonly InstanceInfo _item;
    private readonly Action<InstanceInfo, string?, string?, string?, string?, bool?, string?, bool?> _saveExecution;
    private readonly Action<InstanceInfo, RunSelectionResult> _saveSelection;
    private readonly Action<InstanceInfo, bool> _saveReporting;
    private readonly Action<InstanceInfo, TestcaseWizardDraft> _createTestcase;
    private readonly RunSelectionEditor _selectionEditor;
    private readonly CheckBox _generateResults;
    private readonly CheckBox _internalLogging;
    private readonly CheckBox _saveRequestBody;
    private readonly CheckBox _saveResponseBody;
    private readonly CheckBox _maskSensitive;
    private readonly CheckBox _aggregateHistory;
    private readonly CheckBox _rowHistory;
    private readonly Dictionary<string, CheckBox> _historySheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly TestStepConfigurationEditor _testStepsEditor;

    public InstanceConfigurationWindow(InstanceInfo item, Action<InstanceInfo, RunSelectionResult> saveSelection, Action<InstanceInfo, string?, string?, string?, string?, bool?, string?, bool?> saveExecution, Action<InstanceInfo, bool> saveReporting, Action<InstanceInfo, TestcaseWizardDraft> createTestcase)
    {
        _item = item;
        _saveSelection = saveSelection;
        _saveExecution = saveExecution;
        _saveReporting = saveReporting;
        _createTestcase = createTestcase;
        Title = $"Instance configuration — {item.Name}";
        Width = 980;
        Height = 720;
        MinWidth = 780;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
            {
                Save(); e.Handled = true;
            }
        };

        _generateResults = new CheckBox { Content = "Generate technical Results artifacts", IsChecked = item.GenerateResults };
        _internalLogging = new CheckBox { Content = "Enable internal logging", IsChecked = item.InternalLoggingEnabled };
        _saveRequestBody = new CheckBox { Content = "Save request bodies to History", IsChecked = item.SaveRequestBodyToHistory };
        _saveResponseBody = new CheckBox { Content = "Save response bodies to History", IsChecked = item.SaveResponseBodyToHistory };
        _maskSensitive = new CheckBox { Content = "Mask sensitive data", IsChecked = item.MaskSensitiveData };
        _aggregateHistory = new CheckBox { Content = "Enable aggregate history", IsChecked = item.GenerateAggregateHistory };
        _rowHistory = new CheckBox { Content = "Enable row history", IsChecked = item.GenerateRowHistory };
        _selectionEditor = new RunSelectionEditor(item);
        _testStepsEditor = new TestStepConfigurationEditor(item);

        var tabs = new TabControl
        {
            Margin = new Thickness(18, 0, 18, 12),
            ItemsSource = new object[]
            {
                Tab("Execution", BuildExecutionTab()),
                Tab("Configure test steps", _testStepsEditor),
                Tab("History", BuildHistoryTab()),
                Tab("Reporting", BuildReportingTab()),
                Tab("Storage", BuildStorageTab()),
                Tab("Logging", BuildLoggingTab()),
                Tab("Security", BuildSecurityTab()),
                Tab("Help", BuildHelpTab())
            }
        };
        var save = DashboardStyle.Action("Save", Save, "primary");
        var cancel = DashboardStyle.Action("Cancel", Close);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } };
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(18) };
        root.Children.Add(tabs);
        Grid.SetRow(buttons, 1); root.Children.Add(buttons);
        Content = root;
    }

    private static TabItem Tab(string title, Control content) => new() { Header = title, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };

    private Control BuildExecutionTab()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(8) };
        panel.Children.Add(DashboardStyle.Text("Choose what runs and how the instance executes. All selection and execution settings are editable here and saved together.", 12, false, DashboardStyle.Muted));
        var nextIndex = _item.TestcaseOptions.Select(x => x.Index).DefaultIfEmpty(0).Max() + 1;
        var create = DashboardStyle.Action("＋ Create test case", () =>
        {
            var wizard = new TestcaseWizardWindow(nextIndex, draft => _createTestcase(_item, draft));
            wizard.Closed += (_, _) => { if (wizard.Saved) Close(); };
            wizard.Show(this);
        }, "primary");
        ToolTip.SetTip(create, "Create a new testcase directly in this instance workbook");
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { create, DashboardStyle.Text("Creates one initial step and an optional request data row.", 11, false, DashboardStyle.Muted) } });
        panel.Children.Add(_selectionEditor);
        return panel;
    }

    private Control BuildStorageTab()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
        panel.Children.Add(DashboardStyle.Text("Storage locations", 18, true));
        panel.Children.Add(DashboardStyle.Text("These locations belong to this instance and are resolved relative to its instance folder.", 12, false, DashboardStyle.Muted));
        panel.Children.Add(KeyValue("Instance folder", _item.FolderPath));
        panel.Children.Add(KeyValue("History", _item.HistoryFolder));
        panel.Children.Add(KeyValue("Results", _item.ResultsFolder));
        panel.Children.Add(KeyValue("Logs", _item.LogsFolder));
        panel.Children.Add(KeyValue("Templates", _item.TemplatesFolder));
        panel.Children.Add(DashboardStyle.Text("The current release keeps the standard instance subfolders. Open-folder actions are available in the Artifacts panel.", 12, false, DashboardStyle.Muted));
        return panel;
    }

    private Control BuildHistoryTab()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        panel.Children.Add(DashboardStyle.Text("History reports", 18, true));
        panel.Children.Add(DashboardStyle.Text("Select the worksheets that should be written to each Excel History workbook.", 12, false, DashboardStyle.Muted));
        panel.Children.Add(_aggregateHistory);
        panel.Children.Add(_rowHistory);
        foreach (var name in new[] { "RunSummary", "Metrics", "TestcaseSummary", "RequestHistory", "RuntimeReferenceErrors", "AssertionHistory", "Variables", "CorrelationHistory", "DataEngineRequestRows", "DataEngineConfig" })
        {
            var enabled = !_item.HistorySheets.TryGetValue(name, out var value) || value;
            var check = new CheckBox { Content = name, IsChecked = enabled };
            _historySheets[name] = check;
            panel.Children.Add(check);
        }
        panel.Children.Add(DashboardStyle.Text("Testcase-level filenames use the readable format Testcase_RowExcelRow_datetime.xlsx. The internal run ID remains metadata for correlation.", 11, false, DashboardStyle.Muted));
        return panel;
    }

    private Control BuildLoggingTab()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
        panel.Children.Add(DashboardStyle.Text("Logging and diagnostics", 18, true));
        panel.Children.Add(_internalLogging);
        panel.Children.Add(KeyValue("Log folder", _item.LogsFolder));
        panel.Children.Add(DashboardStyle.Text("Live dashboard event/control files use Logs/.runtime when technical Results generation is disabled, so Run and Stop continue to work.", 12, false, DashboardStyle.Muted));
        return panel;
    }

    private Control BuildSecurityTab()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
        panel.Children.Add(DashboardStyle.Text("History and payload safety", 18, true));
        panel.Children.Add(_maskSensitive);
        panel.Children.Add(_saveRequestBody);
        panel.Children.Add(_saveResponseBody);
        panel.Children.Add(DashboardStyle.Text("Large FileData/Base64 values and redacted variables remain protected by the Runner masking rules. Disabling body history reduces workbook size but does not affect request execution.", 12, false, DashboardStyle.Muted));
        return panel;
    }

    private Control BuildHelpTab()
    {
        var text = DashboardStyle.Text("Execution controls define what runs. Storage shows where this instance writes files. History controls Excel worksheets. Logging controls diagnostics and live event support. Security controls masking and request/response body persistence. Stop-on-failure and timeout remain step-level settings.", 13);
        text.TextWrapping = TextWrapping.Wrap;
        return new StackPanel { Spacing = 12, Margin = new Thickness(8), Children = { DashboardStyle.Text("Configuration help", 18, true), text, DashboardStyle.Text("When Generate technical Results artifacts is disabled, History and the dashboard remain available; persistent Results folders and metrics.json are omitted.", 12, false, DashboardStyle.Muted) } };
    }

    private Control BuildReportingTab()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(8) };
        panel.Children.Add(DashboardStyle.Text("Reporting and diagnostics", 18, true));
        panel.Children.Add(_generateResults);
        panel.Children.Add(DashboardStyle.Text("Technical Results artifacts are generated from the completed execution and stored under the instance Results folder.", 12, false, DashboardStyle.Muted));
        panel.Children.Add(DashboardStyle.Text("Storage locations", 14, true));
        panel.Children.Add(KeyValue("History", _item.HistoryFolder)); panel.Children.Add(KeyValue("Results", _item.ResultsFolder)); panel.Children.Add(KeyValue("Logs", _item.LogsFolder)); panel.Children.Add(KeyValue("Templates", _item.TemplatesFolder));
        return panel;
    }

    private void Save()
    {
        if (!_testStepsEditor.TrySave(out var stepError))
        {
            var dialog = new Window { Title = "Could not save configuration", Width = 520, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new Border { Padding = new Thickness(18), Child = new StackPanel { Spacing = 12, Children = { DashboardStyle.Text(stepError, 13), DashboardStyle.Action("Close", dialog.Close, "primary") } } };
            dialog.ShowDialog(this);
            return;
        }
        if (!_selectionEditor.TryBuild(out var selection, out var selectionError)) return;
        if (!_selectionEditor.EnableSelectedDisabledSteps(selection!, out var enableError))
        {
            var dialog = new Window { Title = "Could not update step enablement", Width = 520, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            dialog.Content = new Border { Padding = new Thickness(18), Child = new StackPanel { Spacing = 12, Children = { DashboardStyle.Text(enableError, 13), DashboardStyle.Action("Close", dialog.Close, "primary") } } };
            dialog.ShowDialog(this);
            return;
        }
        _saveSelection(_item, selection!);
        _item.GenerateResults = _generateResults.IsChecked == true;
        _item.InternalLoggingEnabled = _internalLogging.IsChecked == true;
        _item.SaveRequestBodyToHistory = _saveRequestBody.IsChecked == true;
        _item.SaveResponseBodyToHistory = _saveResponseBody.IsChecked == true;
        _item.MaskSensitiveData = _maskSensitive.IsChecked == true;
        _item.GenerateAggregateHistory = _aggregateHistory.IsChecked == true;
        _item.GenerateRowHistory = _rowHistory.IsChecked == true;
        foreach (var pair in _historySheets) _item.HistorySheets[pair.Key] = pair.Value.IsChecked == true;
        _saveExecution(_item, selection!.MaxParallelRows.ToString(), selection.Iterations.ToString(), selection.ParallelRowStartIntervalMs.ToString(), selection.IterationIntervalMs.ToString(), _item.GenerateResults, selection.RowsExecutionMode, selection.ScenariosParallel);
        _saveReporting(_item, true);
        Close();
    }

    private static Control Field(string label, Control control)
    {
        return new StackPanel { Spacing = 4, Children = { DashboardStyle.Text(label, 11, false, DashboardStyle.Muted), control } };
    }

    private static Control KeyValue(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*"), Margin = new Thickness(0, 2) };
        grid.Children.Add(DashboardStyle.Text(label, 12, false, DashboardStyle.Muted));
        var text = DashboardStyle.Text(value, 12); text.TextWrapping = TextWrapping.Wrap; Grid.SetColumn(text, 1); grid.Children.Add(text);
        return grid;
    }
}
