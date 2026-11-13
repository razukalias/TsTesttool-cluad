using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClosedXML.Excel;
using System.Text.Json;

namespace LoadTestingTool.UI;

internal sealed class RunSelectionEditor : UserControl
{
    private readonly InstanceInfo _item;
    private readonly RunSelectionDraft _draft;
    private readonly IReadOnlyDictionary<int, List<string>> _dataOptions;
    private readonly Dictionary<int, List<CheckBox>> _dataChecks = [];
    private readonly StackPanel _caseItems = new() { Spacing = 10 };
    private readonly StackPanel _stepItems = new() { Spacing = 10 };
    private readonly StackPanel _environmentItems = new() { Spacing = 10 };
    private readonly StackPanel _dataItems = new() { Spacing = 10 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = DashboardStyle.Red };
    private readonly ComboBox _executionMode = new() { ItemsSource = new[] { "parallel", "sequential" }, Width = 150 };
    private readonly ComboBox _scenarioOrder = new() { ItemsSource = new[] { "Sequential environments", "Parallel environments" }, Width = 180 };
    private readonly TextBox _maxParallelRows = new() { Width = 120 };
    private readonly TextBox _iterations = new() { Width = 120 };
    private readonly TextBox _threadIntervalMs = new() { Width = 150 };
    private readonly TextBox _iterationIntervalMs = new() { Width = 170 };
    private bool _rebuilding;
    private string _currentDataSelection = "";

    public RunSelectionEditor(InstanceInfo item)
    {
        _item = item;
        _draft = new RunSelectionDraft(item.TestcaseOptions, item.EnvironmentOptions, item.TestcaseSelection, item.StepSelectionJson, item.EnvironmentSelection, item.EnvironmentSelectionJson);
        _executionMode.SelectedItem = item.RowsExecutionMode.Equals("sequential", StringComparison.OrdinalIgnoreCase) ? "sequential" : "parallel";
        _scenarioOrder.SelectedIndex = item.RunEnvironmentsParallel ? 1 : 0;
        _maxParallelRows.Text = item.MaxParallelRows.ToString();
        _iterations.Text = item.Iterations.ToString();
        _threadIntervalMs.Text = item.ParallelRowStartIntervalMs.ToString();
        _iterationIntervalMs.Text = item.IterationIntervalMs.ToString();
        _dataOptions = item.DataIdOptions;
        _currentDataSelection = item.DataIdSelection;

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*,215,260") };
        var cases = Panel("1  Testcases", _caseItems, () => SetCases(true), () => SetCases(false));
        var steps = Panel("2  Test steps", _stepItems, () => SetSteps(true), () => SetSteps(false));
        var environments = Panel("3  Environments", _environmentItems, () => { _draft.SelectAllEnvironments(); BuildEnvironments(); UpdateSummary(); }, () => { _draft.ClearEnvironments(); BuildEnvironments(); UpdateSummary(); });
        var data = Panel("4  Data IDs", _dataItems, () => SetAllData(true), () => SetAllData(false));
        cases.Margin = new Thickness(0, 0, 8, 0);
        steps.Margin = new Thickness(0, 0, 8, 0);
        environments.Margin = new Thickness(0, 0, 8, 0);
        columns.Children.Add(cases);
        Grid.SetColumn(steps, 1); columns.Children.Add(steps);
        Grid.SetColumn(environments, 2); columns.Children.Add(environments);
        Grid.SetColumn(data, 3); columns.Children.Add(data);

        var runOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Field("Row execution mode", _executionMode), Field("Environments and testcases execution mode", _scenarioOrder) } };
        var executionOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Field("MaxParallelRows", _maxParallelRows), Field("Iterations", _iterations), Field("Parallel-row start interval (ms)", _threadIntervalMs), Field("Iteration interval (ms)", _iterationIntervalMs) } };
        var status = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12), Children = { runOptions, executionOptions, _summary, _error, DashboardStyle.Text("MaxParallelRows and iterations apply to the complete instance run and are saved in central appsettings.json, not in the workbook config sheet.", 11, false, DashboardStyle.Muted), DashboardStyle.Text("Dependencies are not auto-selected. Include setup/dependency steps when your chosen step needs them.", 11, false, DashboardStyle.Muted) } };
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        var scroll = new ScrollViewer { Content = columns, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(scroll);
        Grid.SetRow(status, 1); layout.Children.Add(status);
        Content = layout;
        BuildCases(); BuildSteps(); BuildEnvironments(); BuildDataIds(); UpdateSummary();
    }

    public bool TryBuild(out RunSelectionResult? result, out string error)
    {
        result = null;
        if (!_draft.TryBuild(out var selection, out error)) return false;
        if (!int.TryParse(_maxParallelRows.Text, out var maxParallelRows) || maxParallelRows < 1 ||
            !int.TryParse(_iterations.Text, out var iterations) || iterations < 1 ||
            !int.TryParse(_threadIntervalMs.Text, out var threadInterval) || threadInterval < 0 ||
            !int.TryParse(_iterationIntervalMs.Text, out var iterationInterval) || iterationInterval < 0)
        {
            error = "MaxParallelRows and iterations must be at least 1; intervals must be 0 or greater.";
            _error.Text = error;
            return false;
        }
        result = selection! with
        {
            DataIds = SerializeDataIds(),
            RowsExecutionMode = _executionMode.SelectedItem?.ToString() ?? "parallel",
            ScenariosParallel = _scenarioOrder.SelectedIndex == 1,
            MaxParallelRows = maxParallelRows,
            Iterations = iterations,
            ParallelRowStartIntervalMs = threadInterval,
            IterationIntervalMs = iterationInterval
        };
        return true;
    }

    public bool EnableSelectedDisabledSteps(RunSelectionResult selection, out string error)
    {
        error = "";
        try
        {
            var selected = JsonSerializer.Deserialize<Dictionary<int, string[]>>(selection.StepsJson) ?? [];
            using var workbook = new XLWorkbook(_item.WorkbookPath);
            var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals("config", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("The workbook has no config sheet.");
            var headers = sheet.FirstRowUsed()?.CellsUsed().ToDictionary(x => x.GetString().Trim(), x => x.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? throw new InvalidDataException("The config sheet has no headers.");
            if (!headers.TryGetValue("enabled", out var enabledColumn)) return true;
            var changed = false;
            foreach (var row in sheet.RowsUsed().Skip(1))
            {
                if (!int.TryParse(row.Cell(headers.GetValueOrDefault("testcaseindex")).GetString(), out var index) || !selected.TryGetValue(index, out var names)) continue;
                var name = row.Cell(headers.GetValueOrDefault("stepname")).GetString();
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase) || !row.Cell(enabledColumn).GetString().Equals("false", StringComparison.OrdinalIgnoreCase)) continue;
                row.Cell(enabledColumn).Value = "true"; changed = true;
            }
            if (!changed) return true;
            var backup = $"{_item.WorkbookPath}.backup.{DateTime.Now:yyyyMMdd_HHmmssfff}.xlsx";
            File.Copy(_item.WorkbookPath, backup, false); workbook.Save();
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static Border Panel(string title, Control items, Action all, Action clear)
    {
        var header = new StackPanel { Spacing = 8, Children = { DashboardStyle.Text(title, 15, true), new StackPanel { Orientation = Orientation.Horizontal, Children = { DashboardStyle.Action("All", all), DashboardStyle.Action("Clear", clear) } } } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(header);
        var scroll = new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        return DashboardStyle.Card(grid, new Thickness(10));
    }

    private static CheckBox Check(string text, bool value, bool enabled = true, object? tag = null)
        => new() { Content = DashboardStyle.Text(text, 12), Tag = tag, IsChecked = value, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Stretch };

    private void BuildCases()
    {
        _rebuilding = true; _caseItems.Children.Clear();
        foreach (var item in _draft.Catalog)
        {
            var enabled = item.Steps.Any(s => s.Enabled);
            var check = Check($"{item.Index}  {item.Name}" + (enabled ? "" : " (no enabled steps)"), enabled && _draft.Cases.Contains(item.Index), enabled);
            check.IsCheckedChanged += (_, _) => { if (_rebuilding) return; _draft.SetCase(item.Index, check.IsChecked == true); BuildSteps(); BuildEnvironments(); BuildDataIds(); UpdateSummary(); };
            _caseItems.Children.Add(check);
        }
        _rebuilding = false;
    }

    private void BuildSteps()
    {
        _rebuilding = true; _stepItems.Children.Clear();
        foreach (var testcase in _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index)))
        {
            _stepItems.Children.Add(DashboardStyle.Text($"{testcase.Index}  {testcase.Name}", 12, true, DashboardStyle.Muted));
            foreach (var step in testcase.Steps)
            {
                var check = Check($"{step.Name}  [{step.Type}]" + (step.Enabled ? "" : " — disabled in workbook"), _draft.Steps[testcase.Index].Contains(step.Name), true);
                ToolTip.SetTip(check, step.Enabled ? "Selected steps run for each selected environment." : "This step is disabled in the workbook. Check it to enable it for this run and save the workbook configuration.");
                check.IsCheckedChanged += (_, _) => { if (_rebuilding) return; _draft.SetStep(testcase.Index, step.Name, check.IsChecked == true); BuildEnvironments(); UpdateSummary(); };
                _stepItems.Children.Add(check);
            }
        }
        if (_stepItems.Children.Count == 0) _stepItems.Children.Add(DashboardStyle.Text("Choose a testcase first.", color: DashboardStyle.Muted));
        _rebuilding = false;
    }

    private void BuildEnvironments()
    {
        _rebuilding = true; _environmentItems.Children.Clear();
        foreach (var environment in _draft.AvailableEnvironments)
        {
            var check = Check(string.IsNullOrEmpty(environment) ? "Default (untagged steps)" : environment, _draft.Environments.Contains(environment));
            check.IsCheckedChanged += (_, _) => { if (_rebuilding) return; _draft.SetEnvironment(environment, check.IsChecked == true); UpdateSummary(); };
            _environmentItems.Children.Add(check);
        }
        _rebuilding = false;
    }

    private void BuildDataIds()
    {
        _rebuilding = true; _dataItems.Children.Clear(); _dataChecks.Clear();
        foreach (var testcase in _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index)))
        {
            if (!_dataOptions.TryGetValue(testcase.Index, out var ids) || ids.Count == 0) continue;
            var checks = new List<CheckBox>(); var panel = new StackPanel { Spacing = 4 };
            foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var selected = string.IsNullOrWhiteSpace(_currentDataSelection) || (ParseDataIds().TryGetValue(testcase.Index, out var chosen) && chosen.Contains(id));
                var check = Check(id, selected, tag: id);
                check.IsCheckedChanged += (_, _) => { if (!_rebuilding) { _currentDataSelection = SerializeDataIds(); UpdateSummary(); } };
                checks.Add(check); panel.Children.Add(check);
            }
            _dataChecks[testcase.Index] = checks;
            _dataItems.Children.Add(new StackPanel { Spacing = 5, Children = { DashboardStyle.Text($"{testcase.Index}  {testcase.Name}", 12, true, DashboardStyle.Muted), panel } });
        }
        if (_dataItems.Children.Count == 0) _dataItems.Children.Add(DashboardStyle.Text("No request data IDs found. All rows will run.", color: DashboardStyle.Muted));
        _rebuilding = false;
    }

    private Dictionary<int, HashSet<string>> ParseDataIds() => DataIdSelectionCodec.Parse(_currentDataSelection);
    private void SetAllData(bool value) { foreach (var check in _dataChecks.Values.SelectMany(x => x)) check.IsChecked = value; _currentDataSelection = SerializeDataIds(); UpdateSummary(); }
    private string SerializeDataIds() => DataIdSelectionCodec.Serialize(_dataChecks.Select(x => new KeyValuePair<int, IEnumerable<string>>(x.Key, x.Value.Where(c => c.IsChecked == true).Select(c => c.Tag?.ToString() ?? ""))));
    private void SetCases(bool value) { foreach (var c in _draft.Catalog) _draft.SetCase(c.Index, value && c.Steps.Any(s => s.Enabled)); BuildCases(); BuildSteps(); BuildEnvironments(); BuildDataIds(); UpdateSummary(); }
    private void SetSteps(bool value) { foreach (var c in _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index))) foreach (var s in c.Steps.Where(s => s.Enabled)) _draft.SetStep(c.Index, s.Name, value); BuildSteps(); BuildEnvironments(); UpdateSummary(); }

    private void UpdateSummary()
    {
        var count = _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index)).Sum(c => c.Steps.Count(s => s.Enabled && _draft.Steps[c.Index].Contains(s.Name)));
        var env = _draft.AvailableEnvironments.Where(e => _draft.Environments.Contains(e)).Select(e => string.IsNullOrEmpty(e) ? "Default" : e).ToList();
        var chosen = _draft.AvailableEnvironments.Where(e => _draft.Environments.Contains(e)).ToList();
        var matching = _draft.Catalog.Where(c => _draft.Cases.Contains(c.Index)).SelectMany(c => chosen.Select(_ => c.Steps.Count(s => s.Enabled && _draft.Steps[c.Index].Contains(s.Name)))).ToList();
        var data = SerializeDataIds();
        _summary.Text = $"Selected: {_draft.Cases.Count} testcase(s), {count} enabled step(s), environments: {(env.Count == 0 ? "none" : string.Join(", ", env))}.\nExecutable: {matching.Count(n => n > 0)} testcase/environment pair(s), {matching.Sum()} step execution(s) per data row / iteration.\nData IDs: {(string.IsNullOrWhiteSpace(data) ? "all rows" : data)}";
        _error.Text = _draft.TryBuild(out _, out var error) ? "" : error;
    }

    private static Control Field(string label, Control control) => new StackPanel { Spacing = 4, Children = { DashboardStyle.Text(label, 11, false, DashboardStyle.Muted), control } };
}
