using Avalonia.Styling;
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
    private List<RunHistoryRow> _failedOfRun = [];
    private StackPanel _environmentSection = new();
    private readonly ListBox _rows = new() { Height = 300, Background = Brushes.Transparent, SelectionMode = SelectionMode.Single, Tag = "rerun-rows" };   // virtualized: only the lines in view are built
    private readonly TextBlock _count = new() { FontSize = 12, Opacity = 0.8 };
    private readonly Button _more = new() { Content = "Load older runs", IsVisible = false, MinHeight = 30, Tag = "rerun-more" };
    private readonly List<RunHistoryRow> _scanned = [];
    private int _filesScanned;
    private List<RowChoice> _choices = [];

    /// <summary>History workbooks read per page; the newest are read first and older ones only on request.</summary>
    public const int PageSize = 40;

    /// <summary>True once the first page of the history has been read (it is read in the background so the window opens at once).</summary>
    public bool IsLoaded { get; private set; }

    private sealed class RowChoice : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public RowChoice(RunHistoryRow row) { Row = row; Text = $"{row.DataId}   iteration {row.Iteration}{(row.Environment.Length > 0 ? "   " + row.Environment : string.Empty)}   {row.Result}"; }
        public RunHistoryRow Row { get; }
        public string Text { get; }
        private bool _isChecked;
        public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
    }
    private readonly StackPanel _steps = new() { Spacing = 2 };
    private readonly StackPanel _environments = new() { Spacing = 2 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
    private readonly Button _run = new() { Content = "Re-run", Classes = { "primary" }, MinHeight = 34, MinWidth = 96 };
    private IReadOnlyList<RunHistoryRow> _history = [];
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

        Section("Earlier run", out var runsPanel, _runs, _more);
        var all = new Button { Content = "Tick all", MinHeight = 28, Tag = "rerun-all" };
        var none = new Button { Content = "Tick none", MinHeight = 28, Tag = "rerun-none" };
        all.Click += (_, _) => SetAll(true);
        none.Click += (_, _) => SetAll(false);
        _more.Click += async (_, _) => { _more.IsEnabled = false; _status.Text = "Reading older history…"; await LoadPageAsync(); };
        // the list takes the room the window has (about ten lines at the opening size) instead of a fixed four lines, and its lines are compact
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("rerun-line")) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(4, 0)), new Setter(ListBoxItem.MinHeightProperty, 26d) } });
        _rows.ContainerPrepared += (_, e) => { if (e.Container is ListBoxItem item) item.Classes.Add("rerun-line"); };
        SizeChanged += (_, _) => FitRows();
        FitRows();
        _rows.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<RowChoice>((choice, _) =>
        {
            var box = new CheckBox { FontFamily = new FontFamily("Consolas"), Content = choice?.Text };
            if (choice is not null) box.Bind(CheckBox.IsCheckedProperty, new Avalonia.Data.Binding(nameof(RowChoice.IsChecked)) { Mode = Avalonia.Data.BindingMode.TwoWay });
            return box;
        });
        Section("Failed rows", out var rowsPanel, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { all, none, _count } }, _rows);
        Section("Steps and data", out var stepsPanel, new TextBlock { Text = "Old data = the values (random numbers, GUIDs, dates, extracted responses) that run used. Fresh = made new.", Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap }, _steps);
        Section("Environments (untick to hide their failed rows and leave them out)", out var envPanel, _environments);
        _environmentSection = envPanel;
        Content = new Grid
        {
            Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Children =
            {
                Place(new TextBlock { Text = "Pick an earlier run of this testcase. The new run is saved in history as a re-run of it; nothing is overwritten.", Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap }, 0),
                Place(new ScrollViewer { Content = new StackPanel { Spacing = 14, Children = { runsPanel, envPanel, rowsPanel, stepsPanel } } }, 1),
                Place(_status, 2),
                Place(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _run, cancel } }, 3)
            }
        };
        BuildSteps();
        Load();
    }

    private static T Place<T>(T control, int row) where T : Control { Grid.SetRow(control, row); control.Margin = new Thickness(0, 0, 0, 10); return control; }

    private static void Section(string title, out StackPanel panel, params Control[] children)
    {
        panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        foreach (var child in children) panel.Children.Add(child);
    }

    private async void Load()
    {
        _run.IsEnabled = false;
        _status.Text = "Reading the history…";
        await LoadPageAsync();
        IsLoaded = true;
    }

    /// <summary>Reads the next page of history workbooks off the UI thread and refreshes the run list.</summary>
    private async Task LoadPageAsync()
    {
        var store = HistoryStores.Reader(_context.HistoryFolder, _context.Storage); var skip = _filesScanned;
        var (rows, files) = await Task.Run(() => store.ScanPage(skip, PageSize));
        _filesScanned += files;
        _scanned.AddRange(rows);
        _history = _scanned.Where(r => r.TestcaseIndex == _testcase.Index).ToList();
        _more.IsVisible = files >= PageSize;
        _more.IsEnabled = true;
        var keep = SelectedRunId;
        var runs = RunHistoryIndex.Runs(_history);
        _runs.ItemsSource = runs.Select(r => new ComboBoxItem
        {
            Tag = r.RunId, FontFamily = new FontFamily("Consolas"),
            Content = $"{r.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {r.Rows} rows, {r.Failed} failed{(r.RerunOf is null ? string.Empty : "  (re-run)")}"
        }).ToList();
        if (runs.Count == 0)
        {
            _status.Text = _more.IsVisible
                ? $"No run of this testcase in the {_filesScanned} newest history files. Use \"Load older runs\" to look further back."
                : "No earlier run of this testcase was found in the history. Run it first (history must be on), then come back.";
            _run.IsEnabled = false;
            return;
        }
        _run.IsEnabled = true;
        // keep the run picked before; else the newest run that has failed rows, else the newest run
        var index = keep is null ? -1 : runs.ToList().FindIndex(r => r.RunId == keep);
        if (index < 0) index = runs.ToList().FindIndex(r => r.Failed > 0);
        _runs.SelectedIndex = index >= 0 ? index : 0;
        if (_more.IsVisible) _status.Text = $"Showing runs from the {_filesScanned} newest history files; \"Load older runs\" reads {PageSize} more.";
    }

    private void BuildSteps()
    {
        foreach (var step in _testcase.StepNodes.Select(n => n.Name))
        {
            var use = new CheckBox { Content = step, IsChecked = true, MinWidth = 260 };
            var data = new ComboBox { ItemsSource = new[] { "Old data", "Fresh data" }, SelectedIndex = 0, MinWidth = 120 };
            _stepBoxes.Add((use, data, step));
            _steps.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { use, data } });
        }
    }

    private string? SelectedRunId => (_runs.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>The run changed: its failed rows are the candidates, and its environments become the filter at the top.</summary>
    private void ShowRows()
    {
        _failedOfRun = SelectedRunId is { } runId ? RerunPlanner.RowsOf(_history, runId, _testcase.Index).Where(r => r.Failed).ToList() : [];
        _environments.Children.Clear();
        _environmentBoxes.Clear();
        var names = _failedOfRun.Select(r => r.Environment).Where(e => !string.IsNullOrEmpty(e)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => { var i = _context.Environments.ToList().FindIndex(x => x.Equals(e, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; }).ThenBy(e => e).ToList();
        foreach (var name in names)
        {
            var box = new CheckBox { Content = name, IsChecked = true, Tag = "rerun-env" };
            box.IsCheckedChanged += (_, _) => ApplyEnvironmentFilter();
            _environmentBoxes.Add(box);
            _environments.Children.Add(box);
        }
        _environmentSection.IsVisible = names.Count > 0;
        // Steps that failed in these rows are ticked; steps that passed everywhere are listed but unticked (tick them to run them again too). Without step results in the history, all stay ticked.
        if (_failedOfRun.Count > 0 && _failedOfRun.All(r => r.FailedSteps is not null))
        {
            var failed = _failedOfRun.SelectMany(r => r.FailedSteps!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (failed.Count > 0) foreach (var (use, _, step) in _stepBoxes) use.IsChecked = failed.Contains(step);
        }
        ApplyEnvironmentFilter();
    }

    /// <summary>Only failed rows of the ticked environments are listed (and so only they run again).</summary>
    private void ApplyEnvironmentFilter()
    {
        var hidden = _environmentBoxes.Where(b => b.IsChecked != true).Select(b => b.Content?.ToString() ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _choices = [];
        foreach (var row in _failedOfRun.Where(r => !hidden.Contains(r.Environment)))
        {
            var choice = new RowChoice(row) { IsChecked = true };
            choice.PropertyChanged += (_, _) => UpdateCount();
            _choices.Add(choice);
        }
        _status.Text = SelectedRunId is null ? string.Empty
            : _failedOfRun.Count == 0 ? "This run has no failed rows."
            : "Each ticked row runs again only in the environment where it failed.";
        _rows.ItemsSource = _choices;
        FitRows();
        UpdateCount();
    }

    private const double LineHeight = 26;
    /// <summary>Up to 400 rows are all shown at once (the window scrolls, not the list); a run with more keeps a list that scrolls and builds only the lines in view.</summary>
    private void FitRows() => _rows.Height = _choices.Count is > 0 and <= 400 ? _choices.Count * LineHeight + 4 : Math.Max(220, Height - 400);

    private void SetAll(bool on) { foreach (var choice in _choices) choice.IsChecked = on; }

    private void UpdateCount() => _count.Text = $"{_choices.Count(c => c.IsChecked)} of {_choices.Count} ticked";

    private void Accept()
    {
        if (SelectedRunId is not { } runId) return;
        var rows = _choices.Where(c => c.IsChecked).Select(c => c.Row).ToList();
        if (rows.Count == 0) { _status.Text = "Tick at least one row to re-run."; return; }
        var steps = _stepBoxes.Where(s => s.Use.IsChecked == true).ToDictionary(s => s.Step, s => s.Data.SelectedIndex == 0);
        if (steps.Count == 0) { _status.Text = "Tick at least one step to run."; return; }
        var request = RerunPlanner.Build(_testcase.Index, runId, rows, _testcase.StepNodes.Select(n => n.Name).ToList(), steps);
        var dataIds = rows.Select(r => r.DataId).Distinct().Count();
        Result = (request, $"a re-run of {_testcase.Name} ({dataIds} data row{(dataIds == 1 ? "" : "s")})");
        Close();
    }
}
