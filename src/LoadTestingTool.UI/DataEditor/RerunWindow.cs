using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// "Re-run from history": pick an earlier run of the testcase (by date and time), the rows (failed ones by default), the steps with old or fresh data
/// for each, and the environments. The run that starts is linked to the one it re-runs.
/// </summary>
public sealed class RerunWindow : Window
{
    private readonly TestcaseItem _testcase;
    private readonly RerunContext _context;
    private readonly ComboBox _runs = new() { MinWidth = 420 };
    private readonly CheckBox _failedOnly = new() { Content = "Failed rows only", IsChecked = true };
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly StackPanel _steps = new() { Spacing = 2 };
    private readonly StackPanel _environments = new() { Spacing = 2 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
    private readonly Button _run = new() { Content = "Re-run", Classes = { "primary" }, MinHeight = 34, MinWidth = 96 };
    private IReadOnlyList<RunHistoryRow> _history = [];
    private readonly List<(CheckBox Box, RunHistoryRow Row)> _rowBoxes = [];
    private readonly List<(CheckBox Use, ComboBox Data, string Step)> _stepBoxes = [];
    private readonly List<CheckBox> _environmentBoxes = [];

    /// <summary>What the dialog chose, or null when it was cancelled.</summary>
    public (RunRequest Request, string Label)? Result { get; private set; }

    public RerunWindow(TestcaseItem testcase, RerunContext context)
    {
        _testcase = testcase; _context = context;
        Title = $"Re-run from history — {testcase.Name}";
        Width = 760; Height = 700; MinWidth = 560; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0B1626");

        var cancel = new Button { Content = "Cancel", MinHeight = 34 };
        cancel.Click += (_, _) => Close();
        _run.Click += (_, _) => Accept();
        _runs.SelectionChanged += (_, _) => ShowRows();
        _failedOnly.IsCheckedChanged += (_, _) => ShowRows();

        Section("Earlier run", out var runsPanel, _runs);
        Section("Rows", out var rowsPanel, _failedOnly, new ScrollViewer { MaxHeight = 170, Content = _rows });
        Section("Steps and data", out var stepsPanel, new TextBlock { Text = "Old data = the values (random numbers, GUIDs, dates, extracted responses) that run used. Fresh = made new.", Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap }, _steps);
        Section("Environments", out var envPanel, _environments);
        Content = new Grid
        {
            Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Children =
            {
                Place(new TextBlock { Text = "Pick an earlier run of this testcase. The new run is saved in history as a re-run of it; nothing is overwritten.", Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap }, 0),
                Place(new ScrollViewer { Content = new StackPanel { Spacing = 14, Children = { runsPanel, rowsPanel, stepsPanel, envPanel } } }, 1),
                Place(_status, 2),
                Place(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _run, cancel } }, 3)
            }
        };
        BuildStepsAndEnvironments();
        Load();
    }

    private static T Place<T>(T control, int row) where T : Control { Grid.SetRow(control, row); control.Margin = new Thickness(0, 0, 0, 10); return control; }

    private static void Section(string title, out StackPanel panel, params Control[] children)
    {
        panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        foreach (var child in children) panel.Children.Add(child);
    }

    private void Load()
    {
        _history = RunHistoryIndex.Scan(_context.HistoryFolder).Where(r => r.TestcaseIndex == _testcase.Index).ToList();
        var runs = RunHistoryIndex.Runs(_history);
        _runs.ItemsSource = runs.Select(r => new ComboBoxItem
        {
            Tag = r.RunId, FontFamily = new FontFamily("Consolas"),
            Content = $"{r.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {r.Rows} rows, {r.Failed} failed{(r.RerunOf is null ? string.Empty : "  (re-run)")}"
        }).ToList();
        if (runs.Count == 0)
        {
            _status.Text = "No earlier run of this testcase was found in the history. Run it first (history must be on), then come back.";
            _run.IsEnabled = false;
            return;
        }
        // newest run that has failed rows, else the newest run
        var pick = runs.ToList().FindIndex(r => r.Failed > 0);
        _runs.SelectedIndex = pick >= 0 ? pick : 0;
    }

    private void BuildStepsAndEnvironments()
    {
        foreach (var step in _testcase.StepNodes.Select(n => n.Name))
        {
            var use = new CheckBox { Content = step, IsChecked = true, MinWidth = 260 };
            var data = new ComboBox { ItemsSource = new[] { "Old data", "Fresh data" }, SelectedIndex = 0, MinWidth = 120 };
            _stepBoxes.Add((use, data, step));
            _steps.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { use, data } });
        }
        foreach (var environment in _context.Environments)
        {
            var box = new CheckBox { Content = environment, IsChecked = true };
            _environmentBoxes.Add(box);
            _environments.Children.Add(box);
        }
        if (_context.Environments.Count == 0) _environments.Children.Add(new TextBlock { Text = "This instance has no environments: the run uses its default.", Opacity = 0.7, FontSize = 12 });
    }

    private string? SelectedRunId => (_runs.SelectedItem as ComboBoxItem)?.Tag as string;

    private void ShowRows()
    {
        _rows.Children.Clear(); _rowBoxes.Clear();
        if (SelectedRunId is not { } runId) return;
        var rows = RerunPlanner.RowsOf(_history, runId, _testcase.Index);
        foreach (var row in rows)
        {
            var box = new CheckBox
            {
                IsChecked = !_failedOnly.IsChecked.GetValueOrDefault() || row.Failed,
                IsEnabled = true,
                Content = $"{row.DataId}   iteration {row.Iteration}{(row.Environment.Length > 0 ? "   " + row.Environment : string.Empty)}   {row.Result}",
                FontFamily = new FontFamily("Consolas")
            };
            _rowBoxes.Add((box, row));
            _rows.Children.Add(box);
        }
        _status.Text = "Every iteration and environment of a ticked data row runs again; the runner picks rows by data id.";
    }

    private void Accept()
    {
        if (SelectedRunId is not { } runId) return;
        var rows = _rowBoxes.Where(b => b.Box.IsChecked == true).Select(b => b.Row).ToList();
        if (rows.Count == 0) { _status.Text = "Tick at least one row to re-run."; return; }
        var steps = _stepBoxes.Where(s => s.Use.IsChecked == true).ToDictionary(s => s.Step, s => s.Data.SelectedIndex == 0);
        if (steps.Count == 0) { _status.Text = "Tick at least one step to run."; return; }
        var environments = _environmentBoxes.Where(b => b.IsChecked == true).Select(b => b.Content?.ToString() ?? string.Empty).ToList();
        if (_environmentBoxes.Count > 0 && environments.Count == 0) { _status.Text = "Tick at least one environment."; return; }
        var request = RerunPlanner.Build(_testcase.Index, runId, rows, _testcase.StepNodes.Select(n => n.Name).ToList(), steps, _context.Environments, environments);
        var dataIds = rows.Select(r => r.DataId).Distinct().Count();
        Result = (request, $"a re-run of {_testcase.Name} ({dataIds} data row{(dataIds == 1 ? "" : "s")})");
        Close();
    }
}
