using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;

namespace LoadTestingTool.UI;

public sealed class InstanceInfo
{
    public bool Selected { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string WorkbookPath { get; set; } = string.Empty;
    public string WorkbookFingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = "Ready";
    public string LastEvent { get; set; } = "No results yet";
    public string TestcaseSelection { get; set; } = "0";
    public string StepSelectionJson { get; set; } = "";
    public IReadOnlyList<TestcaseOption> TestcaseOptions { get; set; } = [];
    public string SelectionReadError { get; set; } = "";
    public string RowsExecutionMode { get; set; } = "parallel";
    public int MaxParallelRows { get; set; } = 100;
    public int Iterations { get; set; } = 1;
    public int ParallelRowStartIntervalMs { get; set; }
    public int IterationIntervalMs { get; set; }
    public bool SpoolResultsToDisk { get; set; } = true;
    public bool GenerateResults { get; set; } = true;
    public bool GenerateAggregateHistory { get; set; }
    public bool GenerateRowHistory { get; set; } = true;
    public bool InternalLoggingEnabled { get; set; } = true;
    public bool SaveRequestBodyToHistory { get; set; } = true;
    public bool SaveResponseBodyToHistory { get; set; } = true;
    public bool MaskSensitiveData { get; set; } = true;
    public Dictionary<string, bool> HistorySheets { get; } = HistorySheetDefaults();
    public bool RunEnvironmentsParallel { get; set; }
    public string EnvironmentSelection { get; set; } = "";
    public string EnvironmentSelectionJson { get; set; } = "";
    public bool HasAppliedSelection { get; set; }
    public string DataIdSelection { get; set; } = "";
    public Dictionary<int, List<string>> DataIdOptions { get; } = [];
    public List<string> EnvironmentOptions { get; } = [];
    public Process? Process { get; set; }
    public long EventPosition { get; set; }
    public int EventLineIndex { get; set; }
    public Dictionary<string, int> EventLinePositions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string CurrentRunFolder { get; set; } = string.Empty;
    public string CurrentRunId { get; set; } = string.Empty;
    public List<AssertionInfo> Assertions { get; } = [];
    public List<DashboardRequest> Requests { get; } = [];
    public List<ActionManualInfo> ManualActions { get; } = [];
    public List<RunIssueInfo> Issues { get; } = [];
    public List<string> EventMessages { get; } = [];
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedRequests { get; set; }
    public double? AverageMs { get; set; }
    public long? P95Ms { get; set; }
    public double? RequestsPerSecond { get; set; }
    public string ResultsFolder => Path.Combine(FolderPath, "Results");
    public string HistoryFolder => Path.Combine(FolderPath, "History");
    public string TemplatesFolder => Path.Combine(FolderPath, "Templates");
    public string LogsFolder => Path.Combine(FolderPath, "Logs");
    public string InstanceId => Name;

    private static Dictionary<string, bool> HistorySheetDefaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["RunSummary"] = true, ["Metrics"] = true, ["TestcaseSummary"] = true, ["RequestHistory"] = true,
        ["RuntimeReferenceErrors"] = true, ["AssertionHistory"] = true, ["Variables"] = true,
        ["CorrelationHistory"] = true, ["DataEngineRequestRows"] = true, ["DataEngineConfig"] = true
    };
}

public sealed class RunIssueInfo
{
    public string IssueId { get; init; } = Guid.NewGuid().ToString("N");
    public string Severity { get; init; } = "Error";
    public string Category { get; init; } = "Runner";
    public string Message { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.Now;
    public string Identity => $"{Category}|{Severity}|{Environment}|{Testcase}|{DataId}|{StepName}|{Message}";
}

public sealed class ActionManualInfo
{
    public string RequestId { get; init; } = string.Empty;
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string ManualInfo { get; set; } = string.Empty;
    public int TimeoutMinutes { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string ControlFolder { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string Comment { get; set; } = string.Empty;
    public string Identity => $"{TestcaseIndex}|{Testcase}|{DataId}|{StepName}|{Environment}";
}

public sealed class AssertionInfo
{
    public bool Selected { get; set; }
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string ResponsePath { get; set; } = string.Empty;
    public string ResponseHeader { get; set; } = string.Empty;
    public string AssertionVerb { get; set; } = string.Empty;
    public string AssertionType { get; set; } = string.Empty;
    public string AssertionMeaning { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public string ActualValue { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public string FailureMessage { get; init; } = string.Empty;
    public string ExtractVariable { get; set; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public int ExcelRowNumber { get; init; }
    public int ExpectedValueColumn { get; init; }
}

internal sealed record AssertionSnapshot(
    string Environment,
    string Testcase,
    string DataId,
    string StepName,
    string ResponsePath,
    string AssertionVerb,
    string ExpectedValue,
    string ActualValue,
    string Result,
    string FailureMessage);

internal sealed record AssertionComparisonKey(
    string Testcase,
    string DataId,
    string StepName,
    string ResponsePath,
    string AssertionVerb);

public sealed partial class MainWindow : Window
{
    private readonly UiConfig _config;
    private readonly ObservableCollection<InstanceInfo> _instances = [];
    private readonly Dictionary<string, InstanceRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListBox _instanceList = new();
    private readonly TextBlock _details = new();
    private readonly StackPanel _assertionItems = new() { Orientation = Orientation.Vertical };
    private readonly CheckBox _failedOnly = new() { Content = "Show failed only", IsChecked = true };
    private readonly ComboBox _environmentFilter = new() { Width = 150, Margin = new Thickness(8, 6, 6, 0) };
    private readonly ComboBox _internalLogTestcase = new() { Width = 220 };
    private readonly ComboBox _internalLogEnvironment = new() { Width = 220 };
    private readonly ComboBox _overviewStepStatus = new() { Width = 145, ItemsSource = new[] { "All statuses", "Passed", "Failed", "Unexpected error", "Skipped" }, SelectedIndex = 0 };
    private readonly ComboBox _overviewStepEnvironment = new() { Width = 170 };
    private readonly ComboBox _overviewStepTestcase = new() { Width = 190 };
    private readonly TextBox _overviewStepFilter = new() { Width = 280, Watermark = "Filter steps: term1 & term2…" };
    private bool _overviewStepFilterRefreshing;
    private readonly CheckBox _enableInternalLog = new() { Content = "Enable internal log", IsChecked = true, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _readyValue = new();
    private readonly TextBlock _runningValue = new();
    private readonly TextBlock _passedValue = new();
    private readonly TextBlock _failedValue = new();
    private readonly TextBlock _rpsValue = new();
    private InstanceInfo? _selectedInstance;
    private readonly DispatcherTimer _timer;
    private DateTimeOffset _nextWorkbookScan = DateTimeOffset.MinValue;

    public MainWindow()
    {
        Title = "LoadTestingTool Instance Manager";
        Width = 1250;
        Height = 700;
        _config = LoadConfig();
        UiLog("UI started.");
        BuildLayout();
        RefreshInstances();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => PollEvents();
        _timer.Start();
        UiLog($"UI initialized. instancesRoot={Path.GetFullPath(_config.InstancesRoot)}; uiLog={Path.GetFullPath(_config.UiLogFile)}; pollIntervalMs=500");
    }

    private InstanceRow GetOrCreateRow(InstanceInfo item)
    {
        if (_rows.TryGetValue(item.FolderPath, out var existing)) return existing;
        var row = new InstanceRow(item, selected => { UiLog($"UI action: instance row clicked. instance={selected.Name}"); _instanceList.SelectedItem = selected; ShowDetails(selected); }, selected => OpenInstanceConfiguration(selected), selected => OpenSpreadsheetForInstance(selected), selected => OpenWorkbookForInstance(selected), RunSingleInstance, StopSingleInstance, OpenSelectedInternalLogForInstance, OpenLatestInternalLogForInstance, OpenAllInternalLogsForInstance, OpenSelectedHistoryForInstance, OpenLatestHistoryForInstance, UiLog);
        row.SetEnvironmentOptions(item.EnvironmentOptions);
        _rows[item.FolderPath] = row;
        return row;
    }

    private Control CreateAssertionRow(AssertionInfo assertion)
    {
        var check = new CheckBox { IsChecked = assertion.Selected, VerticalAlignment = VerticalAlignment.Top };
        check.IsCheckedChanged += (_, _) => assertion.Selected = check.IsChecked == true;
        var type = string.IsNullOrWhiteSpace(assertion.AssertionType) ? AssertionSemanticsUi.Type(assertion.AssertionVerb, assertion.ActualValue, assertion.FailureMessage) : assertion.AssertionType;
        var meaning = string.IsNullOrWhiteSpace(assertion.AssertionMeaning) ? AssertionSemanticsUi.Meaning(assertion.AssertionVerb, assertion.ActualValue, assertion.FailureMessage) : assertion.AssertionMeaning;
        var text = DashboardStyle.Text($"[{assertion.Environment}] {assertion.Testcase} / Data {assertion.DataId}\n{assertion.StepName} / {assertion.ResponsePath} {assertion.AssertionVerb}\nType: {type}\nMeaning: {meaning}\nExpected: {assertion.ExpectedValue}\nActual: {assertion.ActualValue}", 12);
        text.TextWrapping = TextWrapping.Wrap;
        var content = new StackPanel { Spacing = 6, Children = { DashboardStyle.Badge(assertion.Result == "PASS" ? "Passed" : "Failed"), text } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*") };
        row.Children.Add(check); Grid.SetColumn(content, 1); row.Children.Add(content);
        text.PointerPressed += (_, _) => OpenAssertionEditor(assertion);
        var card = DashboardStyle.Card(row, new Thickness(10)); card.Margin = new Thickness(0, 0, 0, 8); return card;
    }

    private void RefreshAssertionList()
    {
        var selectedEnvironment = _environmentFilter.SelectedItem?.ToString();
        var values = _selectedInstance?.Assertions
            .Where(a => string.IsNullOrWhiteSpace(selectedEnvironment) || selectedEnvironment == "All environments" || a.Environment.Equals(selectedEnvironment, StringComparison.OrdinalIgnoreCase))
            .Where(a => !_failedOnly.IsChecked.GetValueOrDefault() || a.Result == "FAIL")
            .Where(a => MatchesTerms($"{a.Environment} {a.Testcase} {a.DataId} {a.StepName} {a.ResponsePath} {a.AssertionVerb} {a.ExpectedValue} {a.ActualValue} {a.Result}", _assertionSearch.Text))
            .ToList() ?? [];
        var totalPages = Math.Max(1, (int)Math.Ceiling(values.Count / (double)DashboardPageSize));
        _assertionPage = Math.Clamp(_assertionPage, 1, totalPages);
        var pageValues = values.Skip((_assertionPage - 1) * DashboardPageSize).Take(DashboardPageSize);
        _assertionItems.Children.Clear();
        foreach (var assertion in pageValues) _assertionItems.Children.Add(CreateAssertionRow(assertion));
        if (_assertionPreviousButton is not null)
            UpdatePagination(_assertionPageLabel, _assertionPreviousButton, _assertionNextButton, _assertionPage, values.Count, DashboardPageSize);
        UiLog($"Assertion list refreshed. instance={_selectedInstance?.Name ?? "<none>"}; environment={selectedEnvironment ?? "<none>"}; failedOnly={_failedOnly.IsChecked == true}; page={_assertionPage}; pageSize={DashboardPageSize}; visible={values.Count}; total={_selectedInstance?.Assertions.Count ?? 0}");
    }
    private static bool MatchesTerms(string value, string? filter) => (filter ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private void UpdateEnvironmentFilterOptions()
    {
        var current = _environmentFilter.SelectedItem?.ToString() ?? "All environments";
        var environments = (_selectedInstance?.EnvironmentOptions ?? [])
            .Concat(_selectedInstance?.Assertions.Select(a => a.Environment) ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var options = new[] { "All environments" }.Concat(environments).ToList();
        var existing = (_environmentFilter.ItemsSource as IEnumerable<string>)?.ToList() ?? [];
        if (options.SequenceEqual(existing, StringComparer.OrdinalIgnoreCase)
            && options.Any(option => option.Equals(current, StringComparison.OrdinalIgnoreCase))) return;
        _environmentFilter.ItemsSource = options;
        _environmentFilter.SelectedItem = options.FirstOrDefault(option => option.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "All environments";
        UiLog($"Assertion environment options updated. instance={_selectedInstance?.Name ?? "<none>"}; options={string.Join(",", options)}; selected={_environmentFilter.SelectedItem}");
    }

    private void OpenAssertionEditor(AssertionInfo assertion)
    {
        if (_selectedInstance is null) return;
        var editor = new AssertionEditorWindow(assertion);
        editor.Closed += (_, _) => { if (editor.Result is not null) ApplyAssertionEdit(assertion, editor.Result); };
        editor.Show(this);
    }

    private void OpenAssertionWorkbench()
    {
        if (_selectedInstance is null)
        {
            _details.Text = "Select an instance first.";
            UiLog("Assertion Workbench ignored. reason=no-selected-instance");
            return;
        }
        if (_selectedInstance.Assertions.Count == 0)
        {
            _details.Text = "No assertion fields are available yet. Run the instance first or load a run with assertion results.";
            UiLog($"Assertion Workbench ignored. instance={_selectedInstance.Name}; reason=no-assertions");
            return;
        }
        try
        {
            var workbench = new AssertionWorkbenchWindow(_selectedInstance.WorkbookPath, _selectedInstance.Assertions, message => _details.Text = message, () => { RefreshAssertionList(); RefreshDashboardDetails(true); }, UiLog);
            workbench.Show(this);
            UiLog($"Assertion Workbench opened. instance={_selectedInstance.Name}; assertions={_selectedInstance.Assertions.Count}");
        }
        catch (Exception ex)
        {
            _details.Text = $"Could not open Assertion Workbench: {ex.Message}";
            UiLog($"Assertion Workbench failed to open safely. instance={_selectedInstance.Name}; error={ex}");
        }
    }

    private void ApplyAssertionEdit(AssertionInfo assertion, AssertionEditResult result)
    {
        if (_selectedInstance is null) return;
        try
        {
            var workbookPath = _selectedInstance.WorkbookPath;
            var backup = $"{workbookPath}.backup.{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            File.Copy(workbookPath, backup, overwrite: false);
            using var workbook = new XLWorkbook(workbookPath);
            var sheet = workbook.Worksheet("response");
            var header = sheet.Cell(1, assertion.ExpectedValueColumn);
            var responseHeader = result.ResponseHeader.Trim();
            header.Value = responseHeader;
            var cell = sheet.Cell(assertion.ExcelRowNumber, assertion.ExpectedValueColumn);
            cell.Value = result.ExpectedValue;
            workbook.Save();
            assertion.ResponseHeader = responseHeader;
            assertion.ResponsePath = HeaderPath(responseHeader, assertion.StepName);
            assertion.AssertionVerb = HeaderVerb(responseHeader, result.ExpectedValue);
            assertion.ExpectedValue = HeaderExpected(result.ExpectedValue);
            _details.Text = $"Assertion corrected. Backup created: {Path.GetFileName(backup)}. Extract-variable editing is no longer part of this form. Run the instance again to validate it.";
            UiLog($"Assertion corrected. instance={_selectedInstance.Name}; testcase={assertion.Testcase}; dataId={assertion.DataId}; step={assertion.StepName}; header={responseHeader}; backup={backup}");
            RefreshAssertionList(); RefreshDashboardDetails(true);
        }
        catch (Exception ex) { _details.Text = $"Assertion correction failed: {ex.Message}"; UiLog($"Assertion correction failed: {ex}"); }
    }

    private void ApplySelectedFixes()
    {
        if (_selectedInstance is null) { UiLog("Apply selected values ignored. reason=no-selected-instance"); return; }
        var selected = _selectedInstance.Assertions.Where(a => a.Selected && a.Result == "FAIL").ToList();
        UiLog($"Apply selected values started. instance={_selectedInstance.Name}; selectedFailed={selected.Count}");
        if (selected.Count == 0) { _details.Text = "Select one or more failed assertions first."; UiLog($"Apply selected values ignored. instance={_selectedInstance.Name}; reason=no-failed-assertions-selected"); return; }
        try
        {
            var backup = $"{_selectedInstance.WorkbookPath}.backup.{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            File.Copy(_selectedInstance.WorkbookPath, backup, overwrite: false);
            using var workbook = new XLWorkbook(_selectedInstance.WorkbookPath);
            var sheet = workbook.Worksheet("response");
            var skippedMissing = 0;
            var skippedVariables = 0;
            var updated = 0;
            var addedHeaders = 0;
            foreach (var assertion in selected)
            {
                if (assertion.ActualValue == "<missing>") { skippedMissing++; continue; }
                if (assertion.ExcelRowNumber <= 0) continue;
                var column = assertion.ExpectedValueColumn;
                var added = false;
                if (IsUnexpectedAssertion(assertion) || column <= 0)
                {
                    var header = $"{assertion.StepName}.{assertion.ResponsePath}";
                    column = FindResponseHeaderColumn(sheet, header);
                    if (column <= 0)
                    {
                        column = (sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0) + 1;
                        var headerCell = sheet.Cell(1, column);
                        headerCell.Value = header;
                        headerCell.Style.Font.Bold = true;
                        headerCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                        headerCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        added = true;
                        addedHeaders++;
                    }
                }
                var currentCell = sheet.Cell(assertion.ExcelRowNumber, column);
                if (ContainsWorkbookVariable(currentCell.GetString()))
                {
                    skippedVariables++;
                    continue;
                }
                currentCell.Value = assertion.ActualValue;
                if (added)
                {
                    currentCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
                }
                updated++;
            }
            workbook.Save();
            _details.Text = $"Updated {updated} assertion value(s); added {addedHeaders} response header(s). Skipped {skippedMissing} missing response value(s) and {skippedVariables} variable cell(s); dynamic values were not overwritten. New headers are highlighted green. Backup created: {Path.GetFileName(backup)}. Run the instance again to validate.";
            UiLog($"Apply selected values completed. instance={_selectedInstance.Name}; updated={updated}; addedHeaders={addedHeaders}; skippedMissing={skippedMissing}; skippedVariables={skippedVariables}; backup={backup}");
        }
        catch (Exception ex) { _details.Text = $"Workbook update failed: {ex.Message}"; UiLog($"Apply selected values failed. instance={_selectedInstance.Name}; error={ex}"); }
    }

    private static bool IsUnexpectedAssertion(AssertionInfo assertion) => assertion.AssertionVerb.Equals("contract", StringComparison.OrdinalIgnoreCase) || assertion.ExpectedValue.Equals("<response-sheet-path>", StringComparison.OrdinalIgnoreCase);

    private static int FindResponseHeaderColumn(IXLWorksheet sheet, string header)
    {
        var used = sheet.Row(1).CellsUsed();
        return used.FirstOrDefault(cell => cell.GetString().Trim().Equals(header, StringComparison.OrdinalIgnoreCase))?.Address.ColumnNumber ?? 0;
    }

    private static bool ContainsWorkbookVariable(string value) => System.Text.RegularExpressions.Regex.IsMatch(value ?? string.Empty, @"<[^<>]+>");
    private static string HeaderVerb(string header, string cell)
    {
        if (cell.TrimStart().StartsWith("{", StringComparison.Ordinal) && cell.IndexOf('}') > 1)
            return cell[1..cell.IndexOf('}')].Trim().ToLowerInvariant();
        if (header.TrimStart().StartsWith("{", StringComparison.Ordinal) && header.IndexOf('}') > 1)
            return header[(header.IndexOf('{') + 1)..header.IndexOf('}')].Trim().ToLowerInvariant();
        return "eq";
    }
    private static string HeaderExpected(string cell)
    {
        var value = cell.Trim();
        if (value.StartsWith("{", StringComparison.Ordinal) && value.IndexOf('}') > 1) return value[(value.IndexOf('}') + 1)..];
        return value;
    }
    private static string HeaderPath(string header, string fallbackStep)
    {
        var value = header.Trim();
        if (value.StartsWith("{", StringComparison.Ordinal) && value.IndexOf('}') > 1) value = value[(value.IndexOf('}') + 1)..].Trim();
        var separator = value.IndexOf('.');
        return separator > 0 ? value[(value.StartsWith(fallbackStep + ".", StringComparison.OrdinalIgnoreCase) ? fallbackStep.Length + 1 : 0)..] : value;
    }

    private void RefreshInstances()
    {
        var root = Path.GetFullPath(_config.InstancesRoot);
        UiLog($"Instance refresh started. root={root}; prefix={_config.InstancePrefix}");
        Directory.CreateDirectory(root);
        var previous = _instances.ToDictionary(x => x.FolderPath, StringComparer.OrdinalIgnoreCase);
        _instances.Clear();
        var centralBase = Path.GetDirectoryName(_config.SettingsFile)!;
        var candidateMap = new Dictionary<string, (string Name, string Folder, string Workbook)>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _config.Instances.Where(pair => pair.Value.Enabled))
        {
            var derivedFolder = Path.GetFullPath(pair.Key, root);
            var legacyFolder = string.IsNullOrWhiteSpace(pair.Value.Folder) ? string.Empty : Path.GetFullPath(pair.Value.Folder, centralBase);
            var folder = Directory.Exists(derivedFolder) || string.IsNullOrWhiteSpace(legacyFolder) ? derivedFolder : legacyFolder;
            candidateMap[folder] = (pair.Key, folder, pair.Value.Workbook);
        }

        // Central settings remain authoritative for registered folders, while newly copied
        // legacy folders are also discovered without requiring an appsettings edit first.
        foreach (var folder in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly)
                     .Where(folder => Directory.EnumerateFiles(folder, "*.xlsx", SearchOption.TopDirectoryOnly)
                         .Any(file => !Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)))
                     .OrderBy(x => x))
        {
            var fullFolder = Path.GetFullPath(folder);
            candidateMap.TryAdd(fullFolder, (Path.GetFileName(fullFolder), fullFolder, string.Empty));
        }

        var candidates = candidateMap.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var candidate in candidates)
        {
            var folder = candidate.Folder;
            if (!Directory.Exists(folder))
            {
                UiLog($"Skipping registered instance '{candidate.Name}': folder does not exist: {folder}");
                continue;
            }

            var discoveredWorkbook = Directory.GetFiles(folder, "*.xlsx", SearchOption.TopDirectoryOnly)
                .Where(x => !Path.GetFileName(x).StartsWith("~$", StringComparison.Ordinal))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            var preferredWorkbook = Path.Combine(folder, _config.WorkbookName);
            var legacyWorkbook = string.IsNullOrWhiteSpace(candidate.Workbook) ? null : Path.GetFullPath(candidate.Workbook, folder);
            var configuredWorkbook = File.Exists(preferredWorkbook) ? preferredWorkbook : legacyWorkbook;
            var workbook = configuredWorkbook is not null && File.Exists(configuredWorkbook) ? configuredWorkbook : discoveredWorkbook;
            if (workbook is null)
            {
                UiLog($"Skipping registered instance '{candidate.Name}': no XLSX workbook exists in {folder}");
                continue;
            }
            if (!File.Exists(preferredWorkbook) && workbook is not null && !Path.GetFileName(workbook).Equals(_config.WorkbookName, StringComparison.OrdinalIgnoreCase))
                UiLog($"Global workbook '{_config.WorkbookName}' is missing for '{candidate.Name}'; using existing workbook: {workbook}");
            var item = previous.GetValueOrDefault(folder) ?? new InstanceInfo { Name = candidate.Name, FolderPath = folder, WorkbookPath = workbook };
            item.Name = candidate.Name;
            item.WorkbookPath = workbook;
            if (_config.Instances.TryGetValue(candidate.Name, out var centralInstance))
            {
                item.MaxParallelRows = Math.Clamp(centralInstance.MaxParallelRows ?? centralInstance.MaxConcurrentRows ?? _config.MaxParallelRows, 1, 100);
                item.Iterations = Math.Max(1, centralInstance.Iterations ?? _config.Iterations);
                item.ParallelRowStartIntervalMs = Math.Max(0, centralInstance.ParallelRowStartIntervalMs ?? centralInstance.ThreadIntervalMs ?? _config.ParallelRowStartIntervalMs);
                item.IterationIntervalMs = Math.Max(0, centralInstance.IterationIntervalMs ?? _config.IterationIntervalMs);
                item.SpoolResultsToDisk = centralInstance.SpoolResultsToDisk ?? _config.SpoolResultsToDisk;
                item.RowsExecutionMode = NormalizeRowsExecutionMode(centralInstance.RowsExecutionMode ?? centralInstance.ExecutionMode);
                item.RunEnvironmentsParallel = _config.RunEnvironmentsParallel;
                item.GenerateResults = centralInstance.GenerateResults ?? _config.GenerateResults;
                item.GenerateAggregateHistory = centralInstance.GenerateAggregateHistory ?? false;
                item.GenerateRowHistory = centralInstance.GenerateRowHistory ?? true;
                item.InternalLoggingEnabled = centralInstance.InternalLoggingEnabled ?? true;
                item.SaveRequestBodyToHistory = centralInstance.SaveRequestBodyToHistory ?? true;
                item.SaveResponseBodyToHistory = centralInstance.SaveResponseBodyToHistory ?? true;
                item.MaskSensitiveData = centralInstance.MaskSensitiveData ?? true;
                item.TestcaseSelection = centralInstance.TestcaseSelection ?? item.TestcaseSelection;
                item.StepSelectionJson = centralInstance.StepSelection ?? item.StepSelectionJson;
                item.EnvironmentSelection = centralInstance.EnvironmentSelection ?? item.EnvironmentSelection;
                item.DataIdSelection = centralInstance.DataIdSelection ?? item.DataIdSelection;
                item.CurrentRunId = centralInstance.RunId ?? item.CurrentRunId;
                item.EnvironmentSelectionJson = string.IsNullOrWhiteSpace(item.EnvironmentSelection) ? "" : JsonSerializer.Serialize(item.EnvironmentSelection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                item.HasAppliedSelection = !string.IsNullOrWhiteSpace(centralInstance.TestcaseSelection) || !string.IsNullOrWhiteSpace(centralInstance.EnvironmentSelection);
                if (centralInstance.HistorySheets is not null)
                    foreach (var sheet in centralInstance.HistorySheets) item.HistorySheets[sheet.Key] = sheet.Value;
            }
            item.EnvironmentOptions.Clear();
            item.EnvironmentOptions.AddRange(_config.Environments.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
            item.DataIdOptions.Clear();
            item.SelectionReadError = "";
            try
            {
                using var stream = new FileStream(workbook, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var book = new XLWorkbook(stream);
                item.TestcaseOptions = WorkbookSelectionCatalog.Read(book);
                var requestSheet = book.Worksheets.FirstOrDefault(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase));
                var requestHeader = requestSheet?.FirstRowUsed()?.CellsUsed().ToDictionary(x => x.GetString().Trim(), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
                if (requestSheet is not null && requestHeader.TryGetValue("testcaseindex", out var testcaseColumn) && requestHeader.TryGetValue("dataid", out var dataIdColumn))
                    foreach (var row in requestSheet.RowsUsed().Skip(1))
                        if (int.TryParse(row.Cell(testcaseColumn).GetString().Trim(), out var testcaseIndex))
                        {
                            var dataId = row.Cell(dataIdColumn).GetString().Trim();
                            if (!string.IsNullOrWhiteSpace(dataId))
                            {
                                item.DataIdOptions.TryAdd(testcaseIndex, []);
                                item.DataIdOptions[testcaseIndex].Add(dataId);
                            }
                        }
                var workbookInfo = new FileInfo(workbook);
                item.WorkbookFingerprint = $"{workbookInfo.Length}:{workbookInfo.LastWriteTimeUtc.Ticks}";
            }
            catch (Exception ex)
            {
                if (item.TestcaseOptions.Count == 0) item.SelectionReadError = ex.Message;
                UiLog($"Instance refresh could not read workbook '{workbook}' (existing catalog preserved): {ex}");
            }
            _instances.Add(item);
            if (_rows.TryGetValue(folder, out var existingRow)) existingRow.SetEnvironmentOptions(item.EnvironmentOptions);
        }
        FilterInstances();
        RefreshRows();
        _details.Text = $"Found {_instances.Count} instance(s). Configure in the Config tab; tick the instance and press Run selected.";
        UiLog($"Instance refresh completed. root={root}; instances={_instances.Count}; elapsedNotMeasured=true");
    }

    private async Task CreateInstanceAsync()
    {
        var root = Path.GetFullPath(_config.InstancesRoot);
        var builder = new InstanceBuilderWindow(root, _config.WorkbookName);
        builder.Closed += (_, _) =>
        {
            var createdFolder = builder.CreatedFolder;
            if (string.IsNullOrWhiteSpace(createdFolder)) return;
            RegisterCentralInstance(createdFolder);
            RefreshInstances();
            var created = _instances.FirstOrDefault(x => x.FolderPath.Equals(Path.GetFullPath(createdFolder), StringComparison.OrdinalIgnoreCase));
            if (created is not null) { created.Selected = true; _instanceList.SelectedItem = created; ShowDetails(created); RefreshRows(); }
            _details.Text = $"Created instance: {created?.Name ?? Path.GetFileName(createdFolder)}. Select Configure to edit its testcase and steps.";
        };
        builder.Show(this);
        await Task.CompletedTask;
    }

    private void RegisterCentralInstance(string folder)
    {
        try
        {
            var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
            root ??= new JsonObject();
            var instances = root["Instances"] as JsonObject ?? new JsonObject();
            var name = Path.GetFileName(folder);
            instances[name] = new JsonObject { ["Enabled"] = true };
            root["Instances"] = instances;
            File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            _config.Instances[name] = new UiInstanceDefinition { Enabled = true };
            UiLog($"Registered instance centrally. instance={name}; settings={_config.SettingsFile}");
        }
        catch (Exception ex) { UiLog($"Could not register instance centrally: {ex}"); _details.Text = $"Instance created, but central registration failed: {ex.Message}"; }
    }

    private void SaveInstanceExecutionSettings(InstanceInfo item, string? maxParallelRowsText, string? iterationsText, string? threadIntervalText, string? iterationIntervalText, bool? generateResults = null, string? executionMode = null, bool? scenariosParallel = null)
    {
        if (!int.TryParse(maxParallelRowsText, out var maxParallelRows) || maxParallelRows < 1 ||
            !int.TryParse(iterationsText, out var iterations) || iterations < 1 ||
            !int.TryParse(threadIntervalText, out var threadInterval) || threadInterval < 0 ||
            !int.TryParse(iterationIntervalText, out var iterationInterval) || iterationInterval < 0)
        {
            _details.Text = "Execution settings are invalid. MaxParallelRows and iterations must be at least 1; intervals must be 0 or greater.";
            return;
        }
        try
        {
            var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
            root ??= new JsonObject();
            var instances = root["Instances"] as JsonObject ?? new JsonObject();
            var instance = instances[item.Name] as JsonObject ?? new JsonObject();
            instance["MaxParallelRows"] = maxParallelRows;
            instance["Iterations"] = iterations;
            instance["ParallelRowStartIntervalMs"] = threadInterval;
            instance["IterationIntervalMs"] = iterationInterval;
            if (generateResults.HasValue) instance["GenerateResults"] = generateResults.Value;
            if (!string.IsNullOrWhiteSpace(executionMode)) instance["RowsExecutionMode"] = executionMode;
            instances[item.Name] = instance;
            root["Instances"] = instances;
            if (scenariosParallel.HasValue)
            {
                root["RunEnvironmentsParallel"] = scenariosParallel.Value;
                foreach (var pair in instances)
                    if (pair.Value is JsonObject configuredInstance) configuredInstance.Remove("RunEnvironmentsParallel");
            }
            File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            if (_config.Instances.TryGetValue(item.Name, out var central))
            {
                central.MaxParallelRows = maxParallelRows; central.Iterations = iterations; central.ParallelRowStartIntervalMs = threadInterval; central.IterationIntervalMs = iterationInterval;
                if (generateResults.HasValue) central.GenerateResults = generateResults.Value;
                if (!string.IsNullOrWhiteSpace(executionMode)) central.RowsExecutionMode = executionMode;
            }
            item.MaxParallelRows = maxParallelRows; item.Iterations = iterations; item.ParallelRowStartIntervalMs = threadInterval; item.IterationIntervalMs = iterationInterval;
            if (generateResults.HasValue) item.GenerateResults = generateResults.Value;
            if (!string.IsNullOrWhiteSpace(executionMode)) item.RowsExecutionMode = executionMode;
            if (scenariosParallel.HasValue)
            {
                _config.RunEnvironmentsParallel = scenariosParallel.Value;
                foreach (var other in _instances) other.RunEnvironmentsParallel = scenariosParallel.Value;
                item.RunEnvironmentsParallel = scenariosParallel.Value;
            }
            UiLog($"Saved global execution settings. instance={item.Name}; maxParallelRows={maxParallelRows}; iterations={iterations}; threadIntervalMs={threadInterval}; iterationIntervalMs={iterationInterval}");
            _details.Text = $"Saved for {item.Name}: {maxParallelRows} thread(s), {iterations} iteration(s), parallel-row start interval {threadInterval} ms, iteration interval {iterationInterval} ms.";
            RefreshDashboardDetails(force: true);
        }
        catch (Exception ex)
        {
            UiLog($"Could not save execution settings for {item.Name}: {ex}");
            _details.Text = $"Could not save execution settings: {ex.Message}";
        }
    }

    private void SaveBoundedRowSettings(InstanceInfo item, string? maxConcurrentRowsText, bool spoolResultsToDisk)
    {
        if (!int.TryParse(maxConcurrentRowsText, out var maxConcurrentRows) || maxConcurrentRows < 1 || maxConcurrentRows > 100)
        {
            _details.Text = "Max concurrent rows must be between 1 and 100.";
            return;
        }
        try
        {
            var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
            root ??= new JsonObject();
            var instances = root["Instances"] as JsonObject ?? new JsonObject();
            var instance = instances[item.Name] as JsonObject ?? new JsonObject();
            instance["MaxParallelRows"] = maxConcurrentRows;
            instance["SpoolResultsToDisk"] = spoolResultsToDisk;
            instances[item.Name] = instance; root["Instances"] = instances;
            File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            item.MaxParallelRows = maxConcurrentRows;
            item.SpoolResultsToDisk = spoolResultsToDisk;
            if (_config.Instances.TryGetValue(item.Name, out var central))
            {
                central.MaxParallelRows = maxConcurrentRows;
                central.SpoolResultsToDisk = spoolResultsToDisk;
            }
            _details.Text = $"Saved row execution settings for {item.Name}: max {maxConcurrentRows} concurrent row(s); disk spool {(spoolResultsToDisk ? "enabled" : "disabled")}.";
            UiLog($"Saved bounded row settings. instance={item.Name}; maxConcurrentRows={maxConcurrentRows}; spoolResultsToDisk={spoolResultsToDisk}");
            RefreshDashboardDetails(force: true);
        }
        catch (Exception ex)
        {
            _details.Text = $"Could not save row execution settings: {ex.Message}";
            UiLog($"Could not save bounded row settings for {item.Name}: {ex}");
        }
    }

    private void SaveCentralRunId(InstanceInfo item)
    {
        var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
        root ??= new JsonObject();
        var instances = root["Instances"] as JsonObject ?? new JsonObject();
        var instance = instances[item.Name] as JsonObject ?? new JsonObject();
        instance["RunId"] = item.CurrentRunId;
        instances[item.Name] = instance; root["Instances"] = instances;
        File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private void SaveInstanceReportingSettings(InstanceInfo item, bool _)
    {
        try
        {
            var root = File.Exists(_config.SettingsFile) ? JsonNode.Parse(File.ReadAllText(_config.SettingsFile)) as JsonObject : new JsonObject();
            root ??= new JsonObject();
            var instances = root["Instances"] as JsonObject ?? new JsonObject();
            var instance = instances[item.Name] as JsonObject ?? new JsonObject();
            instance["GenerateResults"] = item.GenerateResults;
            instance["GenerateAggregateHistory"] = item.GenerateAggregateHistory;
            instance["GenerateRowHistory"] = item.GenerateRowHistory;
            instance["InternalLoggingEnabled"] = item.InternalLoggingEnabled;
            instance["SaveRequestBodyToHistory"] = item.SaveRequestBodyToHistory;
            instance["SaveResponseBodyToHistory"] = item.SaveResponseBodyToHistory;
            instance["MaskSensitiveData"] = item.MaskSensitiveData;
            var sheets = new JsonObject();
            foreach (var sheet in item.HistorySheets) sheets[sheet.Key] = sheet.Value;
            instance["HistorySheets"] = sheets;
            instances[item.Name] = instance; root["Instances"] = instances;
            File.WriteAllText(_config.SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            if (_config.Instances.TryGetValue(item.Name, out var central))
            {
                central.GenerateResults = item.GenerateResults;
                central.GenerateAggregateHistory = item.GenerateAggregateHistory;
                central.GenerateRowHistory = item.GenerateRowHistory;
                central.InternalLoggingEnabled = item.InternalLoggingEnabled;
                central.SaveRequestBodyToHistory = item.SaveRequestBodyToHistory;
                central.SaveResponseBodyToHistory = item.SaveResponseBodyToHistory;
                central.MaskSensitiveData = item.MaskSensitiveData;
                central.HistorySheets = new Dictionary<string, bool>(item.HistorySheets, StringComparer.OrdinalIgnoreCase);
            }
            UiLog($"Saved instance reporting settings. instance={item.Name}; generateResults={item.GenerateResults}; internalLogging={item.InternalLoggingEnabled}");
            _details.Text = $"Saved reporting and storage settings for {item.Name}.";
            RefreshDashboardDetails(force: true);
        }
        catch (Exception ex) { UiLog($"Could not save reporting settings for {item.Name}: {ex}"); _details.Text = $"Could not save reporting settings: {ex.Message}"; }
    }

    private void OpenStepWizardForInstance(InstanceInfo item)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item);
        var wizard = new TestStepWizardWindow(step => SaveWizardStep(item, step), templatesFolder: item.TemplatesFolder, instanceName: item.Name);
        wizard.Show(this);
    }

    private void OpenSpreadsheetForInstance(InstanceInfo item)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item);
        OpenSpreadsheetPreview();
    }

    private void EditStepForInstance(InstanceInfo item)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item);
        if (item.TestcaseOptions.Count == 0) { SetStatusForInstance($"No editable steps found for {item.Name}.", true); return; }
        var picker = new StepPickerWindow(item.Name, item.TestcaseOptions, (testcaseIndex, stepName) => OpenExistingStepWizard(item, testcaseIndex, stepName));
        picker.Show(this);
    }

    private void OpenExistingStepWizard(InstanceInfo item, int testcaseIndex, string stepName)
    {
        try
        {
            using var workbookStream = new FileStream(item.WorkbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var workbook = new XLWorkbook(workbookStream);
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase));
            if (sheet is null) { SetStatusForInstance($"No config sheet found in {item.Name}.", true); return; }
            var used = sheet.FirstRowUsed();
            var headers = used?.CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
            var row = sheet.RowsUsed().Skip(1).FirstOrDefault(r => int.TryParse(r.Cell(headers.GetValueOrDefault("testcaseindex")).GetString(), out var index) && index == testcaseIndex && r.Cell(headers.GetValueOrDefault("stepname")).GetString().Trim().Equals(stepName, StringComparison.OrdinalIgnoreCase));
            if (row is null) { SetStatusForInstance($"Could not locate config row for testcase {testcaseIndex}, step {stepName}.", true); return; }
            var values = headers.ToDictionary(pair => pair.Key, pair => row.Cell(pair.Value).GetString(), StringComparer.OrdinalIgnoreCase);
            var wizard = new TestStepWizardWindow(step => SaveWizardStep(item, step), new WizardStepDraft(row.RowNumber(), values), item.TemplatesFolder, item.Name);
            wizard.Show(this);
        }
        catch (Exception ex) { SetStatusForInstance($"Could not open step editor: {ex.Message}", true); }
    }

    private void OpenWorkbookForInstance(InstanceInfo item)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item);
        OpenExcelFile(item.WorkbookPath, "DataEngine workbook");
    }
    private void RunSingleInstance(InstanceInfo item)
    {
        foreach (var other in _instances) other.Selected = false;
        item.Selected = true; _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item); RunSelected(promptForMissingSelection: false);
    }
    private void StopSingleInstance(InstanceInfo item)
    {
        item.Selected = true; _selectedInstance = item; _instanceList.SelectedItem = item; StopSelected(false);
    }
    private void OpenInstanceLog(InstanceInfo item)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item); OpenLatestInternalLog();
    }
    private void OpenSelectedInternalLogForInstance(InstanceInfo item, string testcase, string environment, string result, string run)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item);
        OpenInternalLogForSelection(item, testcase, environment, result, run);
    }
    private void OpenLatestInternalLogForInstance(InstanceInfo item)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item); OpenLatestInternalLog();
    }
    private void OpenAllInternalLogsForInstance(InstanceInfo item)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item); OpenAllInternalLogs();
    }
    private void OpenInstanceHistory(InstanceInfo item)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item); OpenLatestResultWorkbook();
    }
    private void OpenSelectedHistoryForInstance(InstanceInfo item, string testcase, string environment, string result, string row, bool trySpreadsheet)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item);
        if (row.Equals("All rows", StringComparison.OrdinalIgnoreCase))
        {
            OpenHistoryPath(ArtifactCatalog.AggregateHistory(item), trySpreadsheet, "aggregate history");
            return;
        }
        var candidates = ArtifactCatalog.History(item).Where(x => x.Testcase.Equals(testcase, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase));
        if (!result.Equals("All", StringComparison.OrdinalIgnoreCase)) candidates = candidates.Where(x => x.Result.Equals(result, StringComparison.OrdinalIgnoreCase));
        if (!row.Equals("All rows", StringComparison.OrdinalIgnoreCase)) candidates = candidates.Where(x => x.Row.Equals(row, StringComparison.OrdinalIgnoreCase));
        OpenHistoryPath(candidates.OrderByDescending(x => File.GetLastWriteTimeUtc(x.Path)).Select(x => x.Path).FirstOrDefault(), trySpreadsheet, $"history for {testcase} / {environment}");
    }
    private void OpenLatestHistoryForInstance(InstanceInfo item, bool trySpreadsheet)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item);
        var path = ArtifactCatalog.History(item).OrderByDescending(x => File.GetLastWriteTimeUtc(x.Path)).Select(x => x.Path).FirstOrDefault();
        OpenHistoryPath(path, trySpreadsheet, "latest history");
    }
    private void OpenAggregateHistoryForInstance(InstanceInfo item, string result, bool trySpreadsheet)
    {
        _selectedInstance = item; _instanceList.SelectedItem = item; ShowDetails(item);
        var candidates = FindHistoryFiles(item).Where(x => Path.GetFileName(x).StartsWith("Execution_History_", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(item.CurrentRunId)) candidates = candidates.Where(x => Path.GetFileName(x).Contains(item.CurrentRunId, StringComparison.OrdinalIgnoreCase));
        if (!result.Equals("All", StringComparison.OrdinalIgnoreCase)) candidates = candidates.Where(x => FindHistoryResult(x).Equals(result, StringComparison.OrdinalIgnoreCase));
        OpenHistoryPath(candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault(), trySpreadsheet, "aggregate history");
    }
    private void OpenHistoryPath(string? path, bool trySpreadsheet, string description)
    {
        if (path is null)
        {
            _details.Text = $"No {description} workbook was found.";
            UiLog($"Open {description} ignored. reason=file-not-found");
            return;
        }
        UiLog($"Opening {description}. path={path}; method={(trySpreadsheet ? "try-spreadsheet" : "default-excel")}");
        if (trySpreadsheet) LaunchSpreadsheetProcess(path, description);
        else OpenExcelFile(path, description);
    }
    private static string[] FindHistoryFiles(InstanceInfo item) => Directory.Exists(item.HistoryFolder)
        ? Directory.GetFiles(item.HistoryFolder, "*.xlsx", SearchOption.AllDirectories).Where(x => !Path.GetFileName(x).StartsWith("~$", StringComparison.OrdinalIgnoreCase)).ToArray()
        : [];
    private static string FindHistoryResult(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return parts.LastOrDefault(x => x.Equals("passed", StringComparison.OrdinalIgnoreCase) || x.Equals("failed", StringComparison.OrdinalIgnoreCase) || x.Equals("cancelled", StringComparison.OrdinalIgnoreCase)) ?? "unknown";
    }
    private static string FindHistoryEnvironment(string path, InstanceInfo item)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var environment in item.EnvironmentOptions.OrderByDescending(x => x.Length))
            if (parts.Any(x => x.Equals(environment, StringComparison.OrdinalIgnoreCase))) return string.IsNullOrWhiteSpace(environment) ? "default" : environment;
        var resultIndex = Array.FindIndex(parts, x => x.Equals("passed", StringComparison.OrdinalIgnoreCase) || x.Equals("failed", StringComparison.OrdinalIgnoreCase) || x.Equals("cancelled", StringComparison.OrdinalIgnoreCase));
        return resultIndex > 0 ? parts[resultIndex - 1] : "default";
    }
    private static string FindHistoryTestcase(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); var marker = name.IndexOf("_Row", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? name[..marker] : name;
    }
    private static string FindHistoryRow(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); var marker = name.IndexOf("_Row", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return "";
        var end = name.IndexOf('_', marker + 4);
        return end > marker ? name[marker..end] : name[marker..];
    }

    private void SaveWizardStep(InstanceInfo item, WizardStepDraft step)
    {
        try
        {
            using var workbook = new XLWorkbook(item.WorkbookPath);
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase)) ?? workbook.AddWorksheet("config");
            var headers = sheet.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString().Trim(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? [];
            if (headers.Count == 0) { SetStatusForInstance($"Cannot add step to {item.Name}: config sheet has no headers.", true); return; }
            var row = step.Row ?? ((sheet.RangeUsed()?.RangeAddress.LastAddress.RowNumber ?? 1) + 1);
            foreach (var pair in step.Values) if (headers.TryGetValue(pair.Key, out var column)) sheet.Cell(row, column).Value = pair.Value;
            var backup = $"{item.WorkbookPath}.backup.{DateTime.Now:yyyyMMdd_HHmmssfff}.xlsx";
            File.Copy(item.WorkbookPath, backup, overwrite: false); workbook.Save();
            SetStatusForInstance($"Added step '{step.Get("stepname")}' to {item.Name}. Backup: {Path.GetFileName(backup)}");
            RefreshInstances();
        }
        catch (Exception ex) { SetStatusForInstance($"Could not add step: {ex.Message}", true); }
    }

    private void CreateTestcaseForInstance(InstanceInfo item, TestcaseWizardDraft draft)
    {
        try
        {
            using var workbook = new XLWorkbook(item.WorkbookPath);
            var config = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase)) ?? workbook.AddWorksheet("config");
            var configHeaders = EnsureHeaders(config, new[] { "testcaseindex", "testcase", "stepname", "steptype", "stage", "enabled", "timeout", "manualtimeoutminutes", "wait", "targeturl", "verb", "templetsource", "contenttype", "expectedstatus", "assert", "ignoreempty", "assertonlyresponse", "stoponfailure", "stepconfig" });
            var existingRows = config.RowsUsed().Skip(1).Where(row => row.CellsUsed().Any(cell => !string.IsNullOrWhiteSpace(cell.GetString()))).ToList();
            if (existingRows.Any(row => int.TryParse(row.Cell(configHeaders["testcaseindex"]).GetString(), out var index) && index == draft.TestcaseIndex))
            {
                SetStatusForInstance($"Testcase index {draft.TestcaseIndex} already exists in {item.Name}.", true); return;
            }
            if (existingRows.Any(row => row.Cell(configHeaders["testcase"]).GetString().Trim().Equals(draft.TestcaseName, StringComparison.OrdinalIgnoreCase)))
            {
                SetStatusForInstance($"A testcase named '{draft.TestcaseName}' already exists in {item.Name}.", true); return;
            }

            var configRow = (config.LastRowUsed()?.RowNumber() ?? 1) + 1;
            SetCell(config, configRow, configHeaders, "testcaseindex", draft.TestcaseIndex.ToString());
            SetCell(config, configRow, configHeaders, "testcase", draft.TestcaseName);
            SetCell(config, configRow, configHeaders, "stepname", draft.StepName);
            SetCell(config, configRow, configHeaders, "steptype", draft.StepType);
            SetCell(config, configRow, configHeaders, "stage", draft.Stage);
            SetCell(config, configRow, configHeaders, "enabled", draft.Enabled ? "true" : "false");
            SetCell(config, configRow, configHeaders, "timeout", draft.TimeoutSeconds.ToString());
            SetCell(config, configRow, configHeaders, "manualtimeoutminutes", "15");
            SetCell(config, configRow, configHeaders, "wait", draft.WaitMilliseconds.ToString());
            SetCell(config, configRow, configHeaders, "targeturl", draft.TargetUrl);
            SetCell(config, configRow, configHeaders, "verb", draft.Verb);
            SetCell(config, configRow, configHeaders, "templetsource", draft.TemplateSource);
            SetCell(config, configRow, configHeaders, "contenttype", draft.ContentType);
            SetCell(config, configRow, configHeaders, "expectedstatus", draft.ExpectedStatus);
            SetCell(config, configRow, configHeaders, "assert", "true");
            SetCell(config, configRow, configHeaders, "ignoreempty", "false");
            SetCell(config, configRow, configHeaders, "assertonlyresponse", "false");
            SetCell(config, configRow, configHeaders, "stoponfailure", "true");
            SetCell(config, configRow, configHeaders, "stepconfig", "{}");

            if (draft.CreateInitialRow)
            {
                var request = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("request", StringComparison.OrdinalIgnoreCase)) ?? workbook.AddWorksheet("request");
                var requestHeaders = EnsureHeaders(request, new[] { "testcaseindex", "testcase", "dataid" });
                if (request.RowsUsed().Skip(1).Any(row => int.TryParse(row.Cell(requestHeaders["testcaseindex"]).GetString(), out var index) && index == draft.TestcaseIndex && row.Cell(requestHeaders["dataid"]).GetString().Trim().Equals(draft.DataId, StringComparison.OrdinalIgnoreCase)))
                {
                    SetStatusForInstance($"Data ID '{draft.DataId}' already exists for testcase {draft.TestcaseIndex}.", true); return;
                }
                var requestRow = (request.LastRowUsed()?.RowNumber() ?? 1) + 1;
                SetCell(request, requestRow, requestHeaders, "testcaseindex", draft.TestcaseIndex.ToString());
                SetCell(request, requestRow, requestHeaders, "testcase", draft.TestcaseName);
                SetCell(request, requestRow, requestHeaders, "dataid", draft.DataId);

                var response = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("response", StringComparison.OrdinalIgnoreCase)) ?? workbook.AddWorksheet("response");
                var responseHeaders = EnsureHeaders(response, new[] { "testcaseindex", "testcase", "dataid", "extractvariable" });
                var responseRow = (response.LastRowUsed()?.RowNumber() ?? 1) + 1;
                SetCell(response, responseRow, responseHeaders, "testcaseindex", draft.TestcaseIndex.ToString());
                SetCell(response, responseRow, responseHeaders, "testcase", draft.TestcaseName);
                SetCell(response, responseRow, responseHeaders, "dataid", draft.DataId);
                SetCell(response, responseRow, responseHeaders, "extractvariable", "");
            }

            var backup = $"{item.WorkbookPath}.backup.{DateTime.Now:yyyyMMdd_HHmmssfff}.xlsx";
            File.Copy(item.WorkbookPath, backup, overwrite: false);
            workbook.Save();
            RefreshInstances();
            var refreshed = _instances.FirstOrDefault(x => x.FolderPath.Equals(item.FolderPath, StringComparison.OrdinalIgnoreCase));
            if (refreshed is not null) { refreshed.Selected = true; _selectedInstance = refreshed; _instanceList.SelectedItem = refreshed; ShowDetails(refreshed); RefreshRows(); }
            SetStatusForInstance($"Created testcase '{draft.TestcaseName}' with initial step '{draft.StepName}'. Backup created: {Path.GetFileName(backup)}");
            if (refreshed is not null) OpenInstanceConfiguration(refreshed);
        }
        catch (Exception ex) { SetStatusForInstance($"Could not create testcase: {ex.Message}", true); }
    }

    private static Dictionary<string, int> EnsureHeaders(IXLWorksheet sheet, IEnumerable<string> required)
    {
        var headers = sheet.FirstRowUsed()?.CellsUsed().ToDictionary(cell => cell.GetString().Trim(), cell => cell.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var next = Math.Max(1, sheet.LastColumnUsed()?.ColumnNumber() ?? 0) + 1;
        foreach (var name in required)
            if (!headers.ContainsKey(name)) { sheet.Cell(1, next).Value = name; headers[name] = next++; }
        return headers;
    }

    private static void SetCell(IXLWorksheet sheet, int row, IReadOnlyDictionary<string, int> headers, string name, string value)
    {
        if (headers.TryGetValue(name, out var column)) sheet.Cell(row, column).Value = value;
    }

    private void SetStatusForInstance(string message, bool error = false)
    {
        _details.Text = message; _footer.Text = message; UiLog(message);
    }

    private void RefreshRows()
    {
        foreach (var item in _instances)
            if (_rows.TryGetValue(item.FolderPath, out var row)) row.Refresh();
        _readyValue.Text = _instances.Count(item => item.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase)).ToString();
        _runningValue.Text = _instances.Count(item => item.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) || item.Status.Equals("Stopping", StringComparison.OrdinalIgnoreCase)).ToString();
        _passedValue.Text = _instances.Count(item => item.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase)).ToString();
        _failedValue.Text = _instances.Count(item => item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) || item.Status.Equals("Launch failed", StringComparison.OrdinalIgnoreCase)).ToString();
        var latest = _instances.Where(x => x.RequestsPerSecond.HasValue).OrderByDescending(x => x.CompletedAt).FirstOrDefault();
        _rpsValue.Text = latest?.RequestsPerSecond?.ToString("F2") ?? "—";
        _footer.Text = $"{_instances.Count} instances  /  {_instances.Count(x => x.Selected)} selected  /  {DashboardStyle.Build}";
        var selected = _instances.Where(x => x.Selected).ToList();
        var selectedRunning = selected.Any(x => x.Process is { HasExited: false });
        _chooseButton.IsEnabled = !selectedRunning && (_selectedInstance is not null || selected.Count > 0);
        _runButton.IsEnabled = selected.Count > 0 && !selectedRunning && selected.Any(x => x.Process is null || x.Process.HasExited);
        _stopButton.IsEnabled = _forceButton.IsEnabled = _instances.Any(x => x.Selected && x.Process is { HasExited: false });
        if (_page == "Active runs")
        {
            var visible = (_instanceList.ItemsSource as IEnumerable<InstanceInfo>)?.ToList() ?? [];
            if (!visible.SequenceEqual(_instances.Where(x => x.Process is { HasExited: false } && x.Name.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)))) FilterInstances();
        }
    }

    private async void RunSelected(bool promptForMissingSelection = true)
    {
        var selected = _instances.Where(x => x.Selected).ToList();
        UiLog($"Run selected started. selected={selected.Count}; runnable={selected.Count(x => x.Process is null || x.Process.HasExited)}");
        foreach (var item in selected.Where(x => x.Process is null || x.Process.HasExited))
            await LaunchAsync(item, promptForMissingSelection);

        RefreshRows();
        UiLog("Run selected completed.");
    }

    private async Task LaunchAsync(InstanceInfo item, bool promptForMissingSelection, IReadOnlyList<(string Name, string Value)>? overrides = null)
    {
        try
        {
            if (promptForMissingSelection && !item.HasAppliedSelection && !await ChooseRunSelectionAsync(item)) return;
            if (item.GenerateResults) Directory.CreateDirectory(item.ResultsFolder);
            var runner = Path.GetFullPath(_config.RunnerDll);
            var selection = string.IsNullOrWhiteSpace(item.TestcaseSelection) ? "0" : item.TestcaseSelection;
            item.CurrentRunId = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..25];
            item.CurrentRunFolder = item.GenerateResults ? item.ResultsFolder : item.LogsFolder;
            var environments = item.EnvironmentSelection;
            if (!string.IsNullOrWhiteSpace(item.SelectionReadError)) throw new InvalidDataException(item.SelectionReadError);
            var draft = new RunSelectionDraft(item.TestcaseOptions, item.EnvironmentOptions, selection, item.StepSelectionJson, environments, item.EnvironmentSelectionJson);
            if (!draft.TryBuild(out _, out var validationError)) throw new InvalidDataException(validationError);
            var uiLaunchId = $"ui-{Guid.NewGuid():N}";
            var uiLaunchAt = DateTimeOffset.Now;
            SaveCentralRunId(item);
            var runnerIsDll = runner.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            var start = new ProcessStartInfo(runnerIsDll ? "dotnet" : runner)
            {
                WorkingDirectory = item.FolderPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (runnerIsDll) start.ArgumentList.Add(runner);
            void Arg(string name, string value) { start.ArgumentList.Add(name); start.ArgumentList.Add(value); }
            Arg("--config", _config.SettingsFile); Arg("--instance", item.Name);
            Arg("--ui-launch-id", uiLaunchId); Arg("--ui-launch-at", uiLaunchAt.ToString("O"));
            foreach (var (name, value) in overrides ?? []) Arg(name, value);
            UiLog($"Runner launch requested. instance={item.Name}; runId={item.CurrentRunId}; runner={runner}; args={JsonSerializer.Serialize(start.ArgumentList)}");
            item.Process = Process.Start(start);
            if (item.Process is null) throw new InvalidOperationException("The runner process could not be started.");
            item.Process.EnableRaisingEvents = true;
            item.Process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) UiLog($"Runner stdout. instance={item.Name}; runId={item.CurrentRunId}; message={e.Data}"); };
            item.Process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) UiLog($"Runner stderr. instance={item.Name}; runId={item.CurrentRunId}; message={e.Data}"); };
            item.Process.Exited += (_, _) => UiLog($"Runner process exited. instance={item.Name}; runId={item.CurrentRunId}; processId={item.Process?.Id}; exitCode={item.Process?.ExitCode}; hasEvents={item.EventMessages.Count > 0}");
            item.Process.BeginOutputReadLine();
            item.Process.BeginErrorReadLine();
            UiLog($"Runner launch returned. instance={item.Name}; runId={item.CurrentRunId}; uiLaunchId={uiLaunchId}; processId={item.Process?.Id}; returnedAt={DateTimeOffset.Now:O}");
            item.Status = "Running"; item.LastEvent = $"Started with testcases {selection}"; item.EventPosition = 0; item.EventLineIndex = 0; item.EventLinePositions.Clear(); item.Assertions.Clear();
            item.Requests.Clear(); item.ManualActions.Clear(); item.EventMessages.Clear(); item.CompletedRequests = null; item.AverageMs = null;
            item.P95Ms = null; item.RequestsPerSecond = null; item.StartedAt = DateTimeOffset.Now; item.CompletedAt = null;
            item.EventMessages.Add($"{DateTime.Now:HH:mm:ss}  RUN started / testcases {selection}");
            UiLog($"Run state initialized. instance={item.Name}; runId={item.CurrentRunId}; testcases={selection}; mode={item.RowsExecutionMode}; environments={environments}; dataIds={item.DataIdSelection}; internalLog={_enableInternalLog.IsChecked == true}");
        }
        catch (Exception ex)
        {
            UiLog($"Runner launch failed for '{item.Name}': {ex}");
            item.Status = "Launch failed";
            item.LastEvent = ex.Message;
            _details.Text = $"Cannot run {item.Name}: {ex.Message}";
        }
    }


    /// <summary>Runs one testcase (optionally only some data rows) of an instance on behalf of the data editor. Returns why it could not start, or null.</summary>
    internal async Task<string?> RunFromEditorAsync(string instanceName, int testcaseIndex, IReadOnlyList<string> dataIds)
    {
        var item = _instances.FirstOrDefault(x => x.Name == instanceName);
        if (item is null) return $"Instance '{instanceName}' is no longer in the dashboard.";
        if (item.Process is { HasExited: false }) return $"{item.Name} is already running; stop it first.";
        // The selection is passed on the command line for this run only, so the dashboard's own saved selection is untouched.
        var (testcases, steps, dataIdSelection) = (item.TestcaseSelection, item.StepSelectionJson, item.DataIdSelection);
        var ids = dataIds.Count == 0 ? "" : $"{testcaseIndex}:{string.Join(',', dataIds)}";
        List<(string, string)> overrides = [("--testcases", testcaseIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("--steps", "")];
        if (ids.Length > 0) overrides.Add(("--dataids", ids));
        else overrides.Add(("--dataids", "0"));
        item.TestcaseSelection = testcaseIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        item.StepSelectionJson = "";
        item.DataIdSelection = ids;
        try { await LaunchAsync(item, promptForMissingSelection: false, overrides); }
        finally { (item.TestcaseSelection, item.StepSelectionJson, item.DataIdSelection) = (testcases, steps, dataIdSelection); }
        RefreshRows();
        return item.Status == "Launch failed" ? item.LastEvent : null;
    }

    private void StopSelected(bool force)
    {
        foreach (var item in _instances.Where(x => x.Selected && x.Process is { HasExited: false }))
        {
            try
            {
                if (force)
                {
                    item.Process!.Kill(entireProcessTree: true);
                    item.Status = "Force stopped";
                    item.LastEvent = "Process terminated by user";
                }
                else
                {
                    var controlFolder = item.GenerateResults ? item.ResultsFolder : Path.Combine(item.LogsFolder, ".runtime");
                    Directory.CreateDirectory(controlFolder);
                    var stopFile = Path.Combine(controlFolder, $"run_{item.CurrentRunId}.stop");
                    File.WriteAllText(stopFile, "stop requested by UI");
                    item.Status = "Stopping";
                    item.LastEvent = "Graceful stop requested";
                }
                item.EventMessages.Add($"{DateTime.Now:HH:mm:ss}  {(force ? "FORCE STOP" : "STOP")} requested by user");
                UiLog($"Runner stop requested. instance={item.Name}; runId={item.CurrentRunId}; force={force}; processId={item.Process?.Id}");
            }
            catch (Exception ex)
            {
                item.Status = "Stop failed";
                item.LastEvent = ex.Message;
                UiLog($"Runner stop failed. instance={item.Name}; runId={item.CurrentRunId}; force={force}; error={ex}");
            }
        }
        RefreshRows();
    }

    private void PollEvents()
    {
        var pollStarted = Stopwatch.GetTimestamp();
        if (DateTimeOffset.UtcNow >= _nextWorkbookScan)
        {
            _nextWorkbookScan = DateTimeOffset.UtcNow.AddSeconds(1.5);
            if (_instances.Any(WorkbookChangedOnDisk))
            {
                UiLog("Workbook change detected; refreshing instance workbook catalogs automatically.");
                RefreshInstances();
            }
        }
        var filesSeen = 0;
        var eventsApplied = 0;
        foreach (var item in _instances)
        {
            try
            {
                var eventPattern = string.IsNullOrWhiteSpace(item.CurrentRunId) ? "events_*.ndjson" : $"events_{item.CurrentRunId}*.ndjson";
                var files = !string.IsNullOrEmpty(item.CurrentRunFolder) && Directory.Exists(item.CurrentRunFolder) ? Directory.GetFiles(item.CurrentRunFolder, eventPattern, SearchOption.AllDirectories) : [];
                filesSeen += files.Length;
                foreach (var file in files)
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    var lines = text.Split('\n');
                    // Only consume newline-terminated records. The final split entry is empty or incomplete.
                    var completeLineCount = Math.Max(0, lines.Length - 1);
                    var eventKey = Path.GetFileName(file);
                    var lineIndex = item.EventLinePositions.GetValueOrDefault(eventKey);
                    while (lineIndex < completeLineCount)
                    {
                        var line = lines[lineIndex++].TrimEnd('\r');
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try { using var parsed = JsonDocument.Parse(line); ApplyEvent(item, parsed.RootElement); eventsApplied++; } catch (Exception ex) { UiLog($"Invalid event received for '{item.Name}': {ex}"); item.LastEvent = "Invalid event received"; }
                    }
                    item.EventLinePositions[eventKey] = lineIndex;
                }
            }
            catch (IOException) { /* The runner may be rotating or opening the event file; retry on the next tick. */ }
            catch (UnauthorizedAccessException) { /* The file may be temporarily locked; retry on the next tick. */ }
            if (item.Process is { HasExited: true } && (item.Status == "Running" || item.Status == "Stopping"))
            {
                item.CurrentRunFolder = FindRunFolder(item, item.CurrentRunId) ?? item.CurrentRunFolder;
                item.Status = item.Process.ExitCode == 0 ? "Passed" : item.Process.ExitCode == 3 ? "Cancelled" : "Failed";
                item.CompletedAt ??= DateTimeOffset.Now;
                UiLog($"Runner process exited. instance={item.Name}; runId={item.CurrentRunId}; processId={item.Process.Id}; exitCode={item.Process.ExitCode}; observedAt={DateTimeOffset.Now:O}");
            }
        }
        var pollElapsedMs = Stopwatch.GetElapsedTime(pollStarted).TotalMilliseconds;
        if (eventsApplied > 0 || pollElapsedMs >= 100)
            UiLog($"UI poll completed. filesSeen={filesSeen}; eventsApplied={eventsApplied}; elapsedMs={pollElapsedMs:F1}; observedAt={DateTimeOffset.Now:O}");
        RefreshRows();
        if (_instanceList.SelectedItem is InstanceInfo selected)
        {
            _selectedInstance = selected;
            UpdateDetailsText();
        }
    }

    private static bool WorkbookChangedOnDisk(InstanceInfo item)
    {
        try
        {
            var info = new FileInfo(item.WorkbookPath);
            if (!info.Exists) return false;
            var fingerprint = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            return !string.Equals(item.WorkbookFingerprint, fingerprint, StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void ApplyEvent(InstanceInfo item, JsonElement e)
    {
        RecordDashboardEvent(item, e);
        var type = e.GetProperty("eventType").GetString();
        if (type == "run-issue")
        {
            var issue = new RunIssueInfo
            {
                Severity = e.TryGetProperty("severity", out var severity) ? severity.GetString() ?? "Error" : "Error",
                Category = e.TryGetProperty("category", out var category) ? category.GetString() ?? "Runner" : "Runner",
                Message = e.TryGetProperty("message", out var message) ? message.GetString() ?? "" : "",
                Environment = e.TryGetProperty("environment", out var issueEnvironment) ? issueEnvironment.GetString() ?? "" : "",
                Testcase = e.TryGetProperty("testcase", out var issueTestcase) ? issueTestcase.GetString() ?? "" : "",
                DataId = e.TryGetProperty("dataId", out var issueDataId) ? issueDataId.GetString() ?? "" : "",
                StepName = e.TryGetProperty("stepName", out var issueStep) ? issueStep.GetString() ?? "" : "",
                OccurredAt = e.TryGetProperty("occurredAt", out var occurred) && occurred.TryGetDateTimeOffset(out var issueTime) ? issueTime : DateTimeOffset.Now
            };
            if (!item.Issues.Any(x => x.Identity.Equals(issue.Identity, StringComparison.OrdinalIgnoreCase))) item.Issues.Add(issue);
            item.LastEvent = $"{issue.Severity}: {issue.Category} — {issue.Message}";
            UiLog($"Runner issue applied. instance={item.Name}; severity={issue.Severity}; category={issue.Category}; message={issue.Message}");
        }
        if (type == "actionmanual-pending")
        {
            var requestId = e.GetProperty("requestId").GetString() ?? "";
            var environment = e.TryGetProperty("environment", out var manualEnvironment) ? manualEnvironment.GetString() ?? "" : "";
            var testcaseIndex = e.GetProperty("testcaseIndex").GetInt32();
            var testcase = e.GetProperty("testcase").GetString() ?? "";
            var dataId = e.GetProperty("dataId").GetString() ?? "";
            var stepName = e.GetProperty("stepName").GetString() ?? "";
            var existing = item.ManualActions.FirstOrDefault(x => x.TestcaseIndex == testcaseIndex && x.Testcase.Equals(testcase, StringComparison.OrdinalIgnoreCase)
                    && x.DataId.Equals(dataId, StringComparison.OrdinalIgnoreCase) && x.StepName.Equals(stepName, StringComparison.OrdinalIgnoreCase)
                    && x.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase));
            var action = existing ?? new ActionManualInfo
            {
                RequestId = requestId, TestcaseIndex = testcaseIndex, Testcase = testcase,
                DataId = dataId, StepName = stepName, Environment = environment,
                ManualInfo = e.TryGetProperty("manualInfo", out var manualInfo) ? manualInfo.GetString() ?? "" : "",
                TimeoutMinutes = e.TryGetProperty("timeoutMinutes", out var timeoutMinutes) ? timeoutMinutes.GetInt32() : 1,
                StartedAt = e.TryGetProperty("startedAt", out var startedAt) && startedAt.TryGetDateTimeOffset(out var parsedStartedAt) ? parsedStartedAt : DateTimeOffset.Now,
                ControlFolder = e.TryGetProperty("controlFolder", out var controlFolder) ? controlFolder.GetString() ?? "" : ""
            };
            if (existing is null) item.ManualActions.Add(action);
            else { existing.ManualInfo = action.ManualInfo; existing.TimeoutMinutes = action.TimeoutMinutes; existing.ControlFolder = action.ControlFolder; }
            item.ManualActions.RemoveAll(x => x != action && x.Identity.Equals(action.Identity, StringComparison.OrdinalIgnoreCase));
            item.LastEvent = $"ACTION MANUAL PENDING: {requestId} / {action.Environment} / {action.StepName}";
            UiLog($"Runner event applied. type=actionmanual-pending; instance={item.Name}; requestId={requestId}; environment={action.Environment}; step={action.StepName}");
            RefreshActionManualPanel();
        }
        if (type == "actionmanual-completed")
        {
            var requestId = e.GetProperty("requestId").GetString() ?? "";
            var action = item.ManualActions.FirstOrDefault(x => x.RequestId.Equals(requestId, StringComparison.OrdinalIgnoreCase));
            if (action is not null) { action.Status = e.GetProperty("result").GetString() ?? "Failed"; action.Comment = e.TryGetProperty("comment", out var comment) ? comment.GetString() ?? "" : ""; }
            RefreshActionManualPanel();
        }
        if (type == "request-completed")
        {
            var error = e.TryGetProperty("errorMessage", out var errorProperty) ? errorProperty.GetString() : "";
            var stepType = e.TryGetProperty("stepType", out var stepTypeProperty) ? stepTypeProperty.GetString() : "";
            var template = e.TryGetProperty("template", out var templateProperty) ? templateProperty.GetString() : "";
            var targetUrl = e.TryGetProperty("targetUrl", out var urlProperty) ? urlProperty.GetString() : "";
            var status = e.TryGetProperty("httpStatus", out var statusProperty) ? statusProperty.GetInt32().ToString() : "";
            var failureCategory = e.TryGetProperty("failureCategory", out var categoryProperty) ? categoryProperty.GetString() ?? "" : "";
            var runtimeReference = e.TryGetProperty("runtimeReference", out var referenceProperty) ? referenceProperty.GetString() ?? "" : "";
            var failurePrefix = failureCategory.Equals("RuntimeReference", StringComparison.OrdinalIgnoreCase) ? $"RUNTIME REFERENCE FAILED: {runtimeReference} | " : "";
            item.LastEvent = $"{failurePrefix}DataId {e.GetProperty("dataId").GetString()} / Step {e.GetProperty("stepName").GetString()} [{stepType}] {e.GetProperty("result").GetString()} | HTTP {status} | assertions passed: {e.GetProperty("assertionsPassed").GetInt32()}, failed: {e.GetProperty("assertionsFailed").GetInt32()} | error: {error} | template: {template} | url: {targetUrl}";
            UiLog($"Runner event applied. type=request-completed; instance={item.Name}; runId={item.CurrentRunId}; dataId={e.GetProperty("dataId").GetString()}; step={e.GetProperty("stepName").GetString()}; result={e.GetProperty("result").GetString()}; status={status}; failureCategory={failureCategory}; runtimeReference={runtimeReference}; assertionsPassed={e.GetProperty("assertionsPassed").GetInt32()}; assertionsFailed={e.GetProperty("assertionsFailed").GetInt32()}; error={error}");
        }
        if (type == "assertion-completed")
        {
            var assertion = new AssertionInfo
            {
                TestcaseIndex = e.GetProperty("testcaseIndex").GetInt32(), Testcase = e.GetProperty("testcase").GetString() ?? "", DataId = e.GetProperty("dataId").GetString() ?? "", StepName = e.GetProperty("stepName").GetString() ?? "",
                ResponsePath = e.GetProperty("responsePath").GetString() ?? "", ResponseHeader = $"{e.GetProperty("stepName").GetString()}.{e.GetProperty("responsePath").GetString()}", AssertionVerb = e.GetProperty("assertionVerb").GetString() ?? "", AssertionType = e.TryGetProperty("assertionType", out var assertionType) ? assertionType.GetString() ?? "" : "", AssertionMeaning = e.TryGetProperty("assertionMeaning", out var assertionMeaning) ? assertionMeaning.GetString() ?? "" : "", ExpectedValue = e.GetProperty("expectedValue").GetString() ?? "", ActualValue = e.GetProperty("actualValue").GetString() ?? "", Result = e.GetProperty("result").GetString() ?? "", FailureMessage = e.GetProperty("failureMessage").GetString() ?? "", ExtractVariable = "",
                Environment = e.TryGetProperty("environment", out var environment) ? environment.GetString() ?? "" : "",
                ExcelRowNumber = e.TryGetProperty("excelRowNumber", out var row) ? row.GetInt32() : 0, ExpectedValueColumn = e.TryGetProperty("expectedValueColumn", out var col) ? col.GetInt32() : 0
            };
            item.Assertions.Add(assertion);
            if (assertion.Result == "FAIL") item.LastEvent = $"FAILED: {assertion.ResponsePath} expected {assertion.ExpectedValue} actual {assertion.ActualValue}";
            UiLog($"Runner event applied. type=assertion-completed; instance={item.Name}; runId={item.CurrentRunId}; dataId={assertion.DataId}; step={assertion.StepName}; path={assertion.ResponsePath}; result={assertion.Result}; environment={assertion.Environment}");
        }
        if (type == "run-completed")
        {
            item.Status = DashboardStyle.Status(e.GetProperty("result").GetString() ?? "Completed");
            item.CompletedAt = DateTimeOffset.Now;
            var p95 = e.TryGetProperty("p95DurationMs", out var p95Property) ? p95Property.GetInt64() : 0;
            var rps = e.TryGetProperty("requestsPerSecond", out var rpsProperty) ? rpsProperty.GetDouble() : 0;
            if (e.TryGetProperty("historyFile", out var history) && !string.IsNullOrWhiteSpace(history.GetString()))
            {
                var historyPath = history.GetString()!;
                item.CurrentRunFolder = Path.GetDirectoryName(Path.GetFullPath(historyPath)) ?? item.CurrentRunFolder;
                item.LastEvent = $"Run completed: {item.Status}; p95: {p95} ms; RPS: {rps:F2}; history: {historyPath}";
            }
            var completedHistory = e.TryGetProperty("historyFile", out var historyEvent) ? historyEvent.GetString() ?? "" : "";
            UiLog($"Runner event applied. type=run-completed; instance={item.Name}; runId={item.CurrentRunId}; result={item.Status}; history={completedHistory}");
        }
    }

    private void ShowDetails(InstanceInfo? item)
    {
        _selectedInstance = item;
        UiLog($"Details selection changed. instance={item?.Name ?? "<none>"}; assertions={item?.Assertions.Count ?? 0}");
        UpdateEnvironmentFilterOptions();
        UpdateDetailsText();
        RefreshAssertionList();
        BuildConfiguration();
        RefreshDashboardDetails(true);
    }

    private void UpdateDetailsText()
    {
        RefreshDashboardDetails();
    }

    private void OpenLatestInternalLog()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open internal.log ignored. reason=no-selected-instance"); return; }
        var path = FindLatestInternalLog(_selectedInstance);
        if (path is null) { _details.Text = "No internal.log exists for this instance yet. Run it with internal logging enabled."; UiLog($"Open internal.log ignored. instance={_selectedInstance.Name}; reason=file-not-found"); return; }
        UiLog($"Opening latest internal.log. instance={_selectedInstance.Name}; path={path}");
        OpenFile(path, "internal.log");
    }

    private void OpenAllInternalLogs()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open all internal logs ignored. reason=no-selected-instance"); return; }
        var item = _selectedInstance;
        var paths = FindInternalLogs(item);
        if (paths.Count == 0)
        {
            _details.Text = "No internal.log files exist for this instance yet. Run it with internal logging enabled.";
            UiLog($"Open all internal logs ignored. instance={item.Name}; reason=file-not-found");
            return;
        }

        var outputFolder = Directory.Exists(item.CurrentRunFolder) ? item.CurrentRunFolder : item.LogsFolder;
        Directory.CreateDirectory(outputFolder);
        var runLabel = string.IsNullOrWhiteSpace(item.CurrentRunId) ? DateTime.Now.ToString("yyyyMMdd_HHmmss") : item.CurrentRunId;
        var combinedPath = Path.Combine(outputFolder, $"InternalLogs_{runLabel}_all-environments.log");
        try
        {
            using (var writer = new StreamWriter(combinedPath, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("Load Testing Tool - combined internal logs");
                writer.WriteLine($"Instance: {item.Name}");
                writer.WriteLine($"Run: {runLabel}");
                writer.WriteLine($"Generated: {DateTimeOffset.Now:O}");
                writer.WriteLine();
                foreach (var path in paths)
                {
                    writer.WriteLine($"===== ENVIRONMENT: {FindEnvironmentLabel(path, item)} =====");
                    writer.WriteLine($"Source: {path}");
                    writer.WriteLine(File.ReadAllText(path));
                    writer.WriteLine();
                }
            }
            _details.Text = $"Combined internal logs created:\n{combinedPath}\nEnvironments included: {paths.Count}";
            UiLog($"Opening all internal logs. instance={item.Name}; runId={item.CurrentRunId}; files={paths.Count}; output={combinedPath}");
            OpenFile(combinedPath, "combined internal logs");
        }
        catch (Exception ex)
        {
            _details.Text = $"Could not combine internal logs:\n{ex.Message}";
            UiLog($"Could not combine internal logs. instance={item.Name}; error={ex}");
        }
    }

    private void OpenSelectedInternalLog()
    {
        if (_selectedInstance is null)
        {
            _details.Text = "Select an instance first.";
            UiLog("Open selected internal log ignored. reason=no-selected-instance");
            return;
        }

        var testcase = _internalLogTestcase.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(testcase) || testcase.Equals("No testcases found", StringComparison.OrdinalIgnoreCase))
        {
            _details.Text = "Select a testcase first.";
            UiLog($"Open selected internal log ignored. instance={_selectedInstance.Name}; reason=no-testcase-selected");
            return;
        }

        var environment = _internalLogEnvironment.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(environment) || environment.Equals("No environments found", StringComparison.OrdinalIgnoreCase))
        {
            _details.Text = "Select an environment first.";
            UiLog($"Open selected internal log ignored. instance={_selectedInstance.Name}; reason=no-environment-selected");
            return;
        }

        OpenInternalLogForSelection(_selectedInstance, testcase, environment, "All", "Latest run");
    }

    private void OpenInternalLogForSelection(InstanceInfo item, string testcase, string environment, string result, string run)
    {
        var candidates = FindInternalLogs(item)
            .Where(candidate => FindTestcaseLabel(candidate, item).Equals(testcase, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => FindEnvironmentLabel(candidate, item).Equals(environment, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(result) && !result.Equals("All", StringComparison.OrdinalIgnoreCase))
            candidates = candidates.Where(candidate => Path.GetFileName(candidate).Contains($"_{result.ToLowerInvariant()}_", StringComparison.OrdinalIgnoreCase));
        var path = candidates
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (path is null)
        {
            _details.Text = $"No internal log exists for testcase '{testcase}' and environment '{environment}' for {run.ToLowerInvariant()}.";
            UiLog($"Open selected internal log ignored. instance={item.Name}; testcase={testcase}; environment={environment}; result={result}; run={run}; reason=file-not-found");
            return;
        }

        UiLog($"Opening selected internal log. instance={item.Name}; testcase={testcase}; environment={environment}; result={result}; run={run}; path={path}");
        OpenFile(path, $"internal log ({testcase}, {environment})");
    }

    private void OpenSelectedDataEngine()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open DataEngine ignored. reason=no-selected-instance"); return; }
        UiLog($"Opening DataEngine workbook. instance={_selectedInstance.Name}; path={_selectedInstance.WorkbookPath}");
        OpenExcelFile(_selectedInstance.WorkbookPath, "DataEngine workbook");
    }

    private void OpenLatestResultWorkbook()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open result workbook ignored. reason=no-selected-instance"); return; }
        var folder = _selectedInstance.CurrentRunFolder;
        if (Directory.Exists(folder) && Path.GetFileName(folder).Equals("log", StringComparison.OrdinalIgnoreCase))
            folder = Directory.GetParent(folder)?.FullName ?? folder;
        var path = !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.xlsx", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        path ??= Directory.Exists(_selectedInstance.HistoryFolder)
            ? Directory.GetFiles(_selectedInstance.HistoryFolder, "*.xlsx", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (path is null) { _details.Text = "No result Excel workbook exists for the current run yet."; UiLog($"Open result workbook ignored. instance={_selectedInstance.Name}; reason=file-not-found"); return; }
        UiLog($"Opening latest result workbook. instance={_selectedInstance.Name}; path={path}");
        OpenExcelFile(path, "result workbook");
    }

    private async Task GenerateAssertionComparisonAsync()
    {
        if (_selectedInstance is null)
        {
            _details.Text = "Select an instance first.";
            UiLog("Assertion comparison ignored. reason=no-selected-instance");
            return;
        }

        var instance = _selectedInstance;
        var snapshot = instance.Assertions.Select(a => new AssertionSnapshot(
            a.Environment, a.Testcase, a.DataId, a.StepName, a.ResponsePath,
            a.AssertionVerb, a.ExpectedValue, a.ActualValue, a.Result, a.FailureMessage)).ToList();
        if (snapshot.Count == 0)
        {
            _details.Text = "No assertions are available for comparison yet.";
            UiLog($"Assertion comparison ignored. instance={instance.Name}; reason=no-assertions");
            return;
        }

        var outputFolder = Path.Combine(instance.ResultsFolder, "failed");
        var outputPath = Path.Combine(outputFolder, $"AssertionComparison_{instance.CurrentRunId}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        _details.Text = $"Generating assertion comparison...\nAssertions captured: {snapshot.Count}";
        UiLog($"Assertion comparison started. instance={instance.Name}; runId={instance.CurrentRunId}; assertions={snapshot.Count}; output={outputPath}");
        try
        {
            await Task.Run(() => CreateAssertionComparisonWorkbook(snapshot, outputPath));
            _details.Text = $"Assertion comparison created:\n{outputPath}";
            UiLog($"Assertion comparison completed. instance={instance.Name}; runId={instance.CurrentRunId}; output={outputPath}");
        }
        catch (Exception ex)
        {
            _details.Text = $"Could not create assertion comparison:\n{ex.Message}";
            UiLog($"Assertion comparison failed. instance={instance.Name}; runId={instance.CurrentRunId}; error={ex}");
        }
    }

    private void OpenLatestAssertionComparison()
    {
        if (_selectedInstance is null)
        {
            _details.Text = "Select an instance first.";
            UiLog("Open assertion comparison ignored. reason=no-selected-instance");
            return;
        }

        var folder = Path.Combine(_selectedInstance.ResultsFolder, "failed");
        var path = Directory.Exists(folder)
            ? Directory.GetFiles(folder, "AssertionComparison_*.xlsx", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (path is null)
        {
            _details.Text = "No assertion comparison workbook exists yet.";
            UiLog($"Open assertion comparison ignored. instance={_selectedInstance.Name}; reason=file-not-found");
            return;
        }
        UiLog($"Opening latest assertion comparison. instance={_selectedInstance.Name}; path={path}");
        OpenExcelFile(path, "assertion comparison workbook");
    }

    private static void CreateAssertionComparisonWorkbook(IReadOnlyList<AssertionSnapshot> assertions, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var environments = assertions.Select(a => string.IsNullOrWhiteSpace(a.Environment) ? "(unknown)" : a.Environment)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        var grouped = assertions
            .GroupBy(a => new AssertionComparisonKey(a.Testcase, a.DataId, a.StepName, a.ResponsePath, a.AssertionVerb))
            .OrderBy(group => group.Key.Testcase, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.DataId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.StepName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.ResponsePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rows = grouped.Select(group => new ComparisonRow(group.Key, environments.ToDictionary(
            environment => environment,
                environment => group.Where(a => (string.IsNullOrWhiteSpace(a.Environment) ? "(unknown)" : a.Environment).Equals(environment, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Result == "FAIL" ? 0 : 1).FirstOrDefault(), StringComparer.OrdinalIgnoreCase))).ToList();
        var failedRows = rows.Where(row => row.Values.Values.Any(value => value?.Result == "FAIL")).ToList();
        var differentRows = rows.Where(row => row.Values.Values.Select(value => value?.Result ?? "MISSING").Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
            || row.Values.Values.Select(value => value?.ActualValue ?? "MISSING").Distinct(StringComparer.Ordinal).Count() > 1).ToList();

        using var workbook = new XLWorkbook();
        WriteComparisonSheet(workbook.Worksheets.Add("Comparison"), rows, environments);
        WriteComparisonSheet(workbook.Worksheets.Add("FailedOnly"), failedRows, environments);
        WriteComparisonSheet(workbook.Worksheets.Add("Differences"), differentRows, environments);

        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = "Environment";
        summary.Cell(1, 2).Value = "Assertions";
        summary.Cell(1, 3).Value = "Passed";
        summary.Cell(1, 4).Value = "Failed";
        summary.Cell(1, 5).Value = "Missing";
        summary.Cell(1, 6).Value = "Pass rate";
        for (var i = 0; i < environments.Count; i++)
        {
            var environment = environments[i];
            var values = assertions.Where(a => (string.IsNullOrWhiteSpace(a.Environment) ? "(unknown)" : a.Environment).Equals(environment, StringComparison.OrdinalIgnoreCase)).ToList();
            var row = i + 2;
            var passed = values.Count(a => a.Result == "PASS");
            var failed = values.Count(a => a.Result == "FAIL");
            var missing = values.Count(a => a.ActualValue == "<missing>");
            summary.Cell(row, 1).Value = environment;
            summary.Cell(row, 2).Value = values.Count;
            summary.Cell(row, 3).Value = passed;
            summary.Cell(row, 4).Value = failed;
            summary.Cell(row, 5).Value = missing;
            summary.Cell(row, 6).Value = values.Count == 0 ? 0 : (double)passed / values.Count;
            summary.Cell(row, 6).Style.NumberFormat.Format = "0.00%";
        }
        StyleHeader(summary.Row(1));
        summary.Columns().AdjustToContents();
        workbook.SaveAs(outputPath);
    }

    private sealed record ComparisonRow(AssertionComparisonKey Key, Dictionary<string, AssertionSnapshot?> Values);

    private static void WriteComparisonSheet(IXLWorksheet sheet, IReadOnlyList<ComparisonRow> rows, IReadOnlyList<string> environments)
    {
        var headers = new List<string> { "Testcase", "Data ID", "Step", "Response path", "Operator" };
        foreach (var environment in environments)
        {
            headers.Add($"{environment} result");
            headers.Add($"{environment} expected");
            headers.Add($"{environment} actual");
            headers.Add($"{environment} failure");
        }
        headers.Add("Different between environments");
        for (var i = 0; i < headers.Count; i++) sheet.Cell(1, i + 1).Value = headers[i];
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var excelRow = rowIndex + 2;
            sheet.Cell(excelRow, 1).Value = row.Key.Testcase;
            sheet.Cell(excelRow, 2).Value = row.Key.DataId;
            sheet.Cell(excelRow, 3).Value = row.Key.StepName;
            sheet.Cell(excelRow, 4).Value = row.Key.ResponsePath;
            sheet.Cell(excelRow, 5).Value = row.Key.AssertionVerb;
            var column = 6;
            foreach (var environment in environments)
            {
                row.Values.TryGetValue(environment, out var value);
                sheet.Cell(excelRow, column++).Value = value?.Result ?? "MISSING";
                sheet.Cell(excelRow, column++).Value = value?.ExpectedValue ?? "";
                sheet.Cell(excelRow, column++).Value = value?.ActualValue ?? "MISSING";
                sheet.Cell(excelRow, column++).Value = value?.FailureMessage ?? "";
            }
            var resultSet = row.Values.Values.Select(value => value?.Result ?? "MISSING").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var actualSet = row.Values.Values.Select(value => value?.ActualValue ?? "MISSING").Distinct(StringComparer.Ordinal).ToList();
            sheet.Cell(excelRow, column).Value = resultSet.Count > 1 || actualSet.Count > 1 ? "YES" : "NO";
        }
        StyleHeader(sheet.Row(1));
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        foreach (var column in sheet.Columns())
            if (column.Width > 60) column.Width = 60;
    }

    private static void StyleHeader(IXLRow row)
    {
        row.Style.Font.Bold = true;
        row.Style.Fill.BackgroundColor = XLColor.LightBlue;
    }

    private void OpenTemplatesFolder()
    {
        if (_selectedInstance is null) { _details.Text = "Select an instance first."; UiLog("Open Templates folder ignored. reason=no-selected-instance"); return; }
        UiLog($"Opening Templates folder. instance={_selectedInstance.Name}; path={_selectedInstance.TemplatesFolder}");
        OpenFolder(_selectedInstance.TemplatesFolder, "Templates folder");
    }

    private void OpenExcelFile(string path, string description) => OpenFile(path, description);

    private void OpenFile(string path, string description)
    {
        if (!File.Exists(path)) { UiLog($"Cannot open {description}; file not found: {path}"); _details.Text = $"File not found: {path}"; return; }
        try { SystemDefaultFileOpener.Open(path); UiLog($"Requested system-default app for {description}: {path}"); }
        catch (Exception ex)
        {
            UiLog($"Could not open {description} with the system default application: {ex}");
            _details.Text = $"Could not open {description}: {ex.Message}. Set a default app for this file type in your system settings.";
        }
    }

    private void OpenFolder(string path, string description)
    {
        if (!Directory.Exists(path)) { UiLog($"Cannot open {description}; folder not found: {path}"); _details.Text = $"Folder not found: {path}"; return; }
        try { SystemDefaultFileOpener.Open(path); UiLog($"Requested system-default app for {description}: {path}"); }
        catch (Exception ex) { UiLog($"Could not open {description} '{path}': {ex}"); _details.Text = ex.Message; }
    }

    private void UiLog(string message)
    {
        try
        {
            var path = Path.GetFullPath(_config.UiLogFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [UI] {message}{Environment.NewLine}");
        }
        catch { /* UI logging must never prevent the instance manager from operating. */ }
    }

    private static List<string> FindInternalLogs(InstanceInfo item)
    {
        var roots = new[] { item.CurrentRunFolder, item.LogsFolder, item.ResultsFolder }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var all = roots.SelectMany(path => Directory.GetFiles(path, "*.log", SearchOption.AllDirectories))
            .Where(path => Path.GetFileName(path).Equals("internal.log", StringComparison.OrdinalIgnoreCase)
                || (Path.GetFileName(path).Contains("_Run_", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(path).EndsWith("_process.log", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(item.CurrentRunId))
        {
            var shortRunId = item.CurrentRunId.Length <= 8 ? item.CurrentRunId : item.CurrentRunId[^8..];
            var currentRun = all.Where(path => path.Contains(item.CurrentRunId, StringComparison.OrdinalIgnoreCase) || path.Contains($"_Run_{shortRunId}", StringComparison.OrdinalIgnoreCase)).ToList();
            if (currentRun.Count > 0) all = currentRun;
        }
        return all.OrderBy(path => FindEnvironmentLabel(path, item), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string FindEnvironmentLabel(string path, InstanceInfo item)
    {
        var name = Path.GetFileName(path);
        foreach (var environment in item.EnvironmentOptions.OrderByDescending(x => x.Length))
        {
            var safe = SafeFileNamePart(environment);
            if (!string.IsNullOrWhiteSpace(safe) && name.Contains($"_{safe}_", StringComparison.OrdinalIgnoreCase)) return string.IsNullOrWhiteSpace(environment) ? "default" : environment;
        }
        if (name.Contains("_default_", StringComparison.OrdinalIgnoreCase)) return "default";
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var environment in item.EnvironmentOptions.OrderByDescending(x => x.Length))
            if (segments.Any(segment => segment.Equals(environment, StringComparison.OrdinalIgnoreCase))) return environment;
        for (var i = 0; i < segments.Length; i++)
            if (segments[i].Equals("environment", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length) return segments[i + 1];
        return segments.Length > 1 ? segments[^2] : "unknown";
    }

    private static string FindTestcaseLabel(string path, InstanceInfo item)
    {
        var file = Path.GetFileNameWithoutExtension(path);
        var runMarker = file.LastIndexOf("_Run_", StringComparison.OrdinalIgnoreCase);
        if (runMarker < 0) return Path.GetFileNameWithoutExtension(path);
        var prefix = file[..runMarker];
        var resultMarker = prefix.LastIndexOf("_passed_", StringComparison.OrdinalIgnoreCase);
        if (resultMarker < 0) resultMarker = prefix.LastIndexOf("_failed_", StringComparison.OrdinalIgnoreCase);
        if (resultMarker < 0) resultMarker = prefix.LastIndexOf("_cancelled_", StringComparison.OrdinalIgnoreCase);
        if (resultMarker < 0) return prefix;
        var testcaseAndEnvironment = prefix[..resultMarker];
        foreach (var environment in item.EnvironmentOptions.OrderByDescending(x => x.Length))
        {
            var safe = SafeFileNamePart(environment);
            var suffix = "_" + (string.IsNullOrWhiteSpace(safe) ? "default" : safe);
            if (testcaseAndEnvironment.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return testcaseAndEnvironment[..^suffix.Length];
        }
        if (testcaseAndEnvironment.EndsWith("_default", StringComparison.OrdinalIgnoreCase)) return testcaseAndEnvironment[..^8];
        return testcaseAndEnvironment;
    }

    private static string SafeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((value ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "default" : cleaned;
    }

    private static string NormalizeRowsExecutionMode(string? value)
        => value?.Equals("sequential", StringComparison.OrdinalIgnoreCase) == true || value?.Equals("loop", StringComparison.OrdinalIgnoreCase) == true
            ? "sequential"
            : "parallel";

    private static string? FindLatestInternalLog(InstanceInfo item)
    {
        return FindInternalLogs(item)
            .OrderByDescending(path => !string.IsNullOrWhiteSpace(item.CurrentRunId) && (path.Contains(item.CurrentRunId, StringComparison.OrdinalIgnoreCase) || path.Contains($"_Run_{(item.CurrentRunId.Length <= 8 ? item.CurrentRunId : item.CurrentRunId[^8..])}", StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindRunFolder(InstanceInfo item, string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || !Directory.Exists(item.ResultsFolder)) return null;
        var eventFile = Directory.GetFiles(item.ResultsFolder, $"events_{runId}*.ndjson", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains(Path.DirectorySeparatorChar + "pass" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                 || path.Contains(Path.DirectorySeparatorChar + "failed" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (eventFile is null) return null;
        var eventDirectory = Directory.GetParent(eventFile);
        if (eventDirectory is null) return null;
        // Events are stored in <run-folder>/log; the Excel history workbook is in <run-folder>.
        return eventDirectory.Name.Equals("log", StringComparison.OrdinalIgnoreCase)
            ? eventDirectory.Parent?.FullName
            : eventDirectory.FullName;
    }

    private static UiConfig LoadConfig()
    {
        var basePath = AppContext.BaseDirectory;
        var root = new ConfigurationBuilder().SetBasePath(basePath).AddJsonFile("appsettings.json", optional: true).Build();
        var config = new UiConfig(); root.Bind(config);
        config.InstancesRoot = ResolveConfiguredPath(config.InstancesRoot, basePath);
        config.RunnerDll = ResolveConfiguredPath(config.RunnerDll, basePath);
        config.SettingsFile = ResolveConfiguredPath(config.SettingsFile, basePath);
        config.DocumentationFile = ResolveConfiguredPath(config.DocumentationFile, basePath);
        config.UiLogFile = ResolveConfiguredPath(config.UiLogFile, basePath);
        if (File.Exists(config.SettingsFile))
        {
            try
            {
                var central = JsonNode.Parse(File.ReadAllText(config.SettingsFile)) as JsonObject;
                if (central is not null)
                {
                    if (central["InstancesRoot"] is JsonValue instancesRoot && !string.IsNullOrWhiteSpace(instancesRoot.ToString()))
                        config.InstancesRoot = ResolveConfiguredPath(instancesRoot.ToString(), Path.GetDirectoryName(config.SettingsFile)!);
                    if (central["WorkbookName"] is JsonValue workbookName && !string.IsNullOrWhiteSpace(workbookName.ToString()))
                        config.WorkbookName = Path.GetFileName(workbookName.ToString());
                    if (int.TryParse(central["MaxParallelRows"]?.ToString() ?? central["MaxConcurrentRows"]?.ToString(), out var maxParallelRows)) config.MaxParallelRows = Math.Clamp(maxParallelRows, 1, 100);
                    if (int.TryParse(central["Iterations"]?.ToString(), out var iterations)) config.Iterations = Math.Max(1, iterations);
                    if (int.TryParse(central["ParallelRowStartIntervalMs"]?.ToString() ?? central["ThreadIntervalMs"]?.ToString(), out var threadInterval)) config.ParallelRowStartIntervalMs = Math.Max(0, threadInterval);
                    if (int.TryParse(central["IterationIntervalMs"]?.ToString(), out var iterationInterval)) config.IterationIntervalMs = Math.Max(0, iterationInterval);
                    if (bool.TryParse(central["SpoolResultsToDisk"]?.ToString(), out var spoolResultsToDisk)) config.SpoolResultsToDisk = spoolResultsToDisk;
                    config.RowsExecutionMode = NormalizeRowsExecutionMode(central["RowsExecutionMode"]?.ToString() ?? central["ExecutionMode"]?.ToString());
                    if (bool.TryParse(central["RunEnvironmentsParallel"]?.ToString(), out var environmentsParallel)) config.RunEnvironmentsParallel = environmentsParallel;
                    else if (bool.TryParse(central["RunScenariosInParallel"]?.ToString(), out var legacyParallel)) config.RunEnvironmentsParallel = legacyParallel;
                    if (bool.TryParse(central["GenerateResults"]?.ToString(), out var generateResults)) config.GenerateResults = generateResults;
                    var environments = central["Environments"]?.Deserialize<List<string>>();
                    if (environments is not null) config.Environments = environments.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                    var instances = central["Instances"]?.Deserialize<Dictionary<string, UiInstanceDefinition>>();
                    if (instances is not null) config.Instances = new Dictionary<string, UiInstanceDefinition>(instances, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"Could not load central runner settings: {ex.Message}"); }
        }
        return config;
    }

    private static string ResolveConfiguredPath(string configured, string basePath)
    {
        if (string.IsNullOrWhiteSpace(configured)) return configured;
        if (Path.IsPathRooted(configured)) return Path.GetFullPath(configured);

        var direct = Path.GetFullPath(configured, basePath);
        var configuredLeaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(configured));
        var directDirectoryIsUsable = !configuredLeaf.Equals("Instances", StringComparison.OrdinalIgnoreCase)
            || (Directory.Exists(direct) && Directory.EnumerateFiles(direct, "*.xlsx", SearchOption.TopDirectoryOnly).Any());
        if (File.Exists(direct) || (Directory.Exists(direct) && directDirectoryIsUsable)) return direct;

        // Development output is nested below src/LoadTestingTool.UI/bin, while
        // the packaged dashboard keeps appsettings, Instances, and Runner next
        // to the UI directory. Try the same configured path from each ancestor.
        var ancestors = new List<DirectoryInfo>();
        for (var directory = new DirectoryInfo(basePath); directory is not null; directory = directory.Parent) ancestors.Add(directory);
        foreach (var directory in ancestors.AsEnumerable().Reverse())
        {
            var candidate = Path.GetFullPath(configured, directory.FullName);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        }

        var fileName = Path.GetFileName(configured);
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(configured));

        if (fileName.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            foreach (var directory in ancestors.AsEnumerable().Reverse())
                if (File.Exists(Path.Combine(directory.FullName, fileName))) return Path.Combine(directory.FullName, fileName);

        if (leaf.Equals("Instances", StringComparison.OrdinalIgnoreCase))
            foreach (var directory in ancestors.AsEnumerable().Reverse())
                if (Directory.Exists(Path.Combine(directory.FullName, leaf))) return Path.Combine(directory.FullName, leaf);

        if (fileName.Equals("LoadTestingTool.exe", StringComparison.OrdinalIgnoreCase))
            foreach (var directory in ancestors.AsEnumerable().Reverse())
                foreach (var relative in new[] { "bin/Debug/net10.0/LoadTestingTool.dll", "bin/Release/net10.0/LoadTestingTool.dll", "bin/Debug/net10.0/LoadTestingTool.exe", "bin/Release/net10.0/LoadTestingTool.exe" })
                {
                    var candidate = Path.Combine(directory.FullName, relative);
                    if (File.Exists(candidate)) return candidate;
                }

        return direct;
    }
}
