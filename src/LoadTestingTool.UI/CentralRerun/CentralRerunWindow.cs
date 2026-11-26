using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.CentralRerun;

/// <summary>
/// The re-run center: the failed rows of every instance in one list (filters, ticks), the steps to run per testcase, one button that re-runs
/// everything ticked (one runner per instance, all at the same time) and a report (HTML and Excel) when it is done.
/// </summary>
public sealed class CentralRerunWindow : Window
{
    private const string AllInstances = "All instances", AllTestcases = "All testcases", AllEnvironments = "All environments", AllKinds = "All failures";

    private readonly ICentralRerunHost _host;
    private readonly ComboBox _instance = new() { MinWidth = 150, Tag = "central-instance" };
    private readonly ComboBox _testcase = new() { MinWidth = 150, Tag = "central-testcase" };
    private readonly ComboBox _environment = new() { MinWidth = 150, Tag = "central-environment" };
    private readonly ComboBox _kind = new() { MinWidth = 120, Tag = "central-kind", ItemsSource = new[] { AllKinds, "FAIL", "ERROR" }, SelectedIndex = 0 };
    private readonly TextBox _text = new() { MinWidth = 200, Watermark = "Row, step or reason contains…", Tag = "central-text" };
    private readonly ListBox _rows = new() { Height = 300, Background = Brushes.Transparent, Tag = "central-rows" };
    private readonly TextBlock _count = new() { FontSize = 12, Opacity = 0.85 };
    private readonly StackPanel _overview = new() { Spacing = 2, Tag = "central-overview" };
    private readonly StackPanel _steps = new() { Spacing = 6, Tag = "central-steps" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.9, Tag = "central-status" };
    private readonly Button _run = new() { Content = "Re-run ticked rows", Classes = { "primary" }, MinHeight = 34, MinWidth = 150, Tag = "central-run" };
    private readonly StackPanel _reportReady = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Tag = "central-report-ready" };

    private readonly List<RowChoice> _all = [];
    private readonly ObservableCollection<RowChoice> _shown = [];
    private readonly Dictionary<(string Instance, int Testcase), HashSet<string>> _stepState = [];
    private readonly HashSet<(string Instance, int Testcase)> _touched = [];
    private bool _filling, _bulk, _busy;

    /// <summary>True once the failed rows of every instance have been read (it happens in the background so the window opens at once).</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The paths of the last report, or null before a batch has finished.</summary>
    public (string Html, string Excel)? Report { get; private set; }

    /// <summary>The result of the last batch (null before one has finished).</summary>
    public BatchResult? LastBatch { get; private set; }

    public IReadOnlyList<FailedPair> Failed => _all.Select(c => c.Pair).ToList();
    public IReadOnlyList<FailedPair> Ticked => _all.Where(c => c.IsChecked).Select(c => c.Pair).ToList();

    private sealed class RowChoice : ObservableObject
    {
        public RowChoice(FailedPair pair)
        {
            Pair = pair;
            Text = $"{pair.Instance} › {pair.Testcase}   {pair.DataId}   {pair.EnvironmentText}   {pair.Kind}   {pair.Reason}";
        }
        public FailedPair Pair { get; }
        public string Text { get; }
        private bool _isChecked;
        public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
    }

    public CentralRerunWindow(ICentralRerunHost host)
    {
        _host = host;
        Title = "Re-run center — failed rows of all instances";
        Width = 980; Height = 820; MinWidth = 700; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0B1626");

        _rows.ItemsSource = _shown;
        _rows.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<RowChoice>((choice, _) =>
        {
            var box = new CheckBox { FontFamily = new FontFamily("Consolas"), FontSize = 12, Content = choice?.Text };
            if (choice is not null) box.Bind(CheckBox.IsCheckedProperty, new Avalonia.Data.Binding(nameof(RowChoice.IsChecked)) { Mode = Avalonia.Data.BindingMode.TwoWay });
            return box;
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("central-line")) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(4, 0)), new Setter(ListBoxItem.MinHeightProperty, 24d) } });
        _rows.ContainerPrepared += (_, e) => { if (e.Container is ListBoxItem item) item.Classes.Add("central-line"); };
        _instance.SelectionChanged += (_, _) => { if (!_filling) { FillTestcases(); ApplyFilter(); } };
        foreach (var combo in new[] { _testcase, _environment, _kind }) combo.SelectionChanged += (_, _) => { if (!_filling) ApplyFilter(); };
        _text.TextChanged += (_, _) => { if (!_filling) ApplyFilter(); };

        var tickShown = new Button { Content = "Tick all shown", MinHeight = 28, Tag = "central-tick-shown" };
        var untickShown = new Button { Content = "Untick all shown", MinHeight = 28, Tag = "central-untick-shown" };
        var tickNone = new Button { Content = "Untick everything", MinHeight = 28, Tag = "central-tick-none" };
        tickShown.Click += (_, _) => SetShown(true);
        untickShown.Click += (_, _) => SetShown(false);
        tickNone.Click += (_, _) => SetAll(false);
        var refresh = new Button { Content = "Read again", MinHeight = 28, Tag = "central-reload" };
        refresh.Click += (_, _) => Load();
        _run.IsEnabled = false;
        _run.Click += (_, _) => _ = RunTickedAsync();

        var htmlButton = new Button { Content = "Open the HTML report", MinHeight = 30, Tag = "central-report-html" };
        var excelButton = new Button { Content = "Open the Excel report", MinHeight = 30, Tag = "central-report-excel" };
        var folderButton = new Button { Content = "Open the report folder", MinHeight = 30, Tag = "central-report-folder" };
        htmlButton.Click += (_, _) => { if (Report is { } r) _host.Open(r.Html); };
        excelButton.Click += (_, _) => { if (Report is { } r) _host.Open(r.Excel); };
        folderButton.Click += (_, _) => { if (Report is { } r) _host.Open(Path.GetDirectoryName(r.Html)!); };
        _reportReady.Children.Add(new TextBlock { Text = "Report ready", FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#39D39B"), VerticalAlignment = VerticalAlignment.Center });
        _reportReady.Children.Add(htmlButton); _reportReady.Children.Add(excelButton); _reportReady.Children.Add(folderButton);

        var filters = new WrapPanel { Children = { _instance, _testcase, _environment, _kind, _text } };
        foreach (var child in filters.Children) if (child is Control c) c.Margin = new Thickness(0, 0, 8, 6);
        Content = new Grid
        {
            Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto"),
            Children =
            {
                Place(new TextBlock { Text = "Every failed row and environment of every instance, as the table shows them (the newest result of each). Tick what to run again. One runner starts per instance and they run at the same time; the instance's own Overview shows its run.", Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap }, 0),
                Place(Section("Overview", _overview), 1),
                Place(new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Failed rows", FontWeight = FontWeight.SemiBold }, filters, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { tickShown, untickShown, tickNone, refresh, _count } } } }, 2),
                Place(new Grid { RowDefinitions = new RowDefinitions("*"), Children = { _rows } }, 3),
                Place(new ScrollViewer { MaxHeight = 190, Content = Section("Steps to run (per testcase; the failed steps are ticked, the steps that passed are not)", _steps) }, 4),
                Place(new StackPanel { Spacing = 6, Children = { _status, _reportReady } }, 5),
                Place(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _run, CloseButton() } }, 6)
            }
        };
        Load();
    }

    private Button CloseButton() { var b = new Button { Content = "Close", MinHeight = 34 }; b.Click += (_, _) => Close(); return b; }

    private static T Place<T>(T control, int row) where T : Control { Grid.SetRow(control, row); control.Margin = new Thickness(0, 0, 0, 10); return control; }

    private static StackPanel Section(string title, Control body) => new() { Spacing = 4, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, body } };

    private async void Load()
    {
        IsLoaded = false; _run.IsEnabled = false;
        _status.Text = "Reading the results of every instance…";
        var instances = _host.Instances.ToList();
        List<FailedPair> pairs;
        try { pairs = await Task.Run(() => instances.SelectMany(i => CentralRerunSource.Collect(i)).ToList()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = $"Could not read the results: {ex.Message}"; return; }
        var ticked = _all.Where(c => c.IsChecked).Select(c => c.Pair.Key).ToHashSet();
        foreach (var old in _all) old.PropertyChanged -= OnChoiceChanged;
        _all.Clear();
        foreach (var pair in pairs)
        {
            var choice = new RowChoice(pair) { IsChecked = ticked.Contains(pair.Key) };
            choice.PropertyChanged += OnChoiceChanged;
            _all.Add(choice);
        }
        _filling = true;
        try
        {
            SetOptions(_instance, AllInstances, instances.Select(i => i.Name));
            SetOptions(_environment, AllEnvironments, pairs.Select(p => p.EnvironmentText).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
            FillTestcases();
        }
        finally { _filling = false; }
        ApplyFilter();
        RefreshOverview();
        IsLoaded = true;
        _status.Text = pairs.Count == 0 ? "No failed rows in any instance." : $"{pairs.Count} failed {(pairs.Count == 1 ? "row" : "rows")} in {pairs.Select(p => p.Instance).Distinct().Count()} {(pairs.Select(p => p.Instance).Distinct().Count() == 1 ? "instance" : "instances")}.";
        _run.IsEnabled = !_busy && Ticked.Count > 0;
    }

    private static void SetOptions(ComboBox box, string all, IEnumerable<string> values)
    {
        var keep = box.SelectedItem as string;
        var list = new List<string> { all }; list.AddRange(values);
        box.ItemsSource = list;
        box.SelectedItem = keep is not null && list.Contains(keep) ? keep : all;
    }

    private void FillTestcases()
    {
        var chosen = _instance.SelectedItem as string;
        var names = _all.Where(c => chosen is null or AllInstances || c.Pair.Instance == chosen).Select(c => c.Pair.Testcase).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var was = _filling; _filling = true;
        try { SetOptions(_testcase, AllTestcases, names); }
        finally { _filling = was; }
    }

    private void ApplyFilter()
    {
        var instance = _instance.SelectedItem as string; var testcase = _testcase.SelectedItem as string; var environment = _environment.SelectedItem as string; var kind = _kind.SelectedItem as string;
        var terms = (_text.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _shown.Clear();
        foreach (var c in _all)
        {
            var p = c.Pair;
            if (instance is not (null or AllInstances) && p.Instance != instance) continue;
            if (testcase is not (null or AllTestcases) && !p.Testcase.Equals(testcase, StringComparison.OrdinalIgnoreCase)) continue;
            if (environment is not (null or AllEnvironments) && !p.EnvironmentText.Equals(environment, StringComparison.OrdinalIgnoreCase)) continue;
            if (kind is not (null or AllKinds) && p.Kind != kind) continue;
            if (terms.Length > 0 && !terms.All(t => c.Text.Contains(t, StringComparison.OrdinalIgnoreCase))) continue;
            _shown.Add(c);
        }
        _count.Text = $"{_shown.Count} shown of {_all.Count} · {_all.Count(c => c.IsChecked)} ticked";
    }

    private void SetShown(bool value) { _bulk = true; try { foreach (var c in _shown) c.IsChecked = value; } finally { _bulk = false; } OnTickChanged(); }
    private void SetAll(bool value) { _bulk = true; try { foreach (var c in _all) c.IsChecked = value; } finally { _bulk = false; } OnTickChanged(); }

    private void OnChoiceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(RowChoice.IsChecked) && !_bulk) OnTickChanged(); }

    private void OnTickChanged()
    {
        _count.Text = $"{_shown.Count} shown of {_all.Count} · {_all.Count(c => c.IsChecked)} ticked";
        RefreshOverview();
        RefreshSteps();
        _run.IsEnabled = !_busy && IsLoaded && _all.Any(c => c.IsChecked);
    }

    private void RefreshOverview()
    {
        _overview.Children.Clear();
        if (_all.Count == 0) { _overview.Children.Add(new TextBlock { Text = "No failed rows.", Opacity = 0.7 }); return; }
        foreach (var group in _all.GroupBy(c => c.Pair.Instance, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.Select(c => (c.Pair.TestcaseIndex, c.Pair.DataId)).Distinct().Count();
            _overview.Children.Add(new TextBlock { FontSize = 12, Text = $"{group.Key}: {group.Count()} failed {(group.Count() == 1 ? "pair" : "pairs")} ({rows} {(rows == 1 ? "row" : "rows")}, {group.Select(c => c.Pair.EnvironmentText).Distinct().Count()} environments, {group.Count(c => c.Pair.Kind == "ERROR")} errors) · {group.Count(c => c.IsChecked)} ticked" });
        }
    }

    /// <summary>The steps of every testcase that has a ticked row: the steps that failed are ticked, the others are not; a hand change is kept.</summary>
    private void RefreshSteps()
    {
        _steps.Children.Clear();
        var groups = _all.Where(c => c.IsChecked).GroupBy(c => (c.Pair.Instance, c.Pair.TestcaseIndex)).OrderBy(g => g.Key.Instance, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key.TestcaseIndex).ToList();
        foreach (var group in groups)
        {
            var key = group.Key;
            var catalog = _host.Instances.FirstOrDefault(i => i.Name.Equals(key.Instance, StringComparison.OrdinalIgnoreCase))?.Catalog.FirstOrDefault(c => c.Index == key.TestcaseIndex);
            var every = catalog?.Steps.Where(s => s.Enabled).Select(s => s.Name).ToList() ?? [];
            if (!_touched.Contains(key))
            {
                var failed = group.SelectMany(c => c.Pair.FailedSteps).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var chosen = every.Where(failed.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _stepState[key] = chosen.Count == 0 ? every.ToHashSet(StringComparer.OrdinalIgnoreCase) : chosen;   // no step named: run them all
            }
            var panel = new WrapPanel { Tag = "central-steps-" + key.Instance + "-" + key.TestcaseIndex };
            foreach (var step in every)
            {
                var box = new CheckBox { Content = step, IsChecked = _stepState[key].Contains(step), Margin = new Thickness(0, 0, 14, 0), Tag = "step:" + step };
                var (instance, index, name) = (key.Instance, key.TestcaseIndex, step);
                box.IsCheckedChanged += (_, _) =>
                {
                    _touched.Add((instance, index));
                    if (box.IsChecked == true) _stepState[(instance, index)].Add(name); else _stepState[(instance, index)].Remove(name);
                };
                panel.Children.Add(box);
            }
            _steps.Children.Add(new TextBlock { FontSize = 12, Text = $"{key.Instance} › {group.First().Pair.Testcase}  ({group.Count()} {(group.Count() == 1 ? "pair" : "pairs")})", Opacity = 0.85 });
            _steps.Children.Add(panel);
        }
    }

    /// <summary>Runs everything ticked (also what the button does). Returns when the batch and the report are done.</summary>
    internal async Task RunTickedAsync()
    {
        if (_busy) return;
        var ticked = Ticked;
        if (ticked.Count == 0) return;
        _busy = true; _run.IsEnabled = false; _reportReady.IsVisible = false; Report = null;
        try
        {
            var steps = new Dictionary<(string, int), IReadOnlyList<string>>();
            foreach (var key in ticked.Select(p => (p.Instance, p.TestcaseIndex)).Distinct())
                if (_stepState.TryGetValue(key, out var chosen)) steps[key] = [.. chosen];
            _status.Text = $"Running {ticked.Count} {(ticked.Count == 1 ? "pair" : "pairs")} in {ticked.Select(p => p.Instance).Distinct().Count()} {(ticked.Select(p => p.Instance).Distinct().Count() == 1 ? "instance" : "instances")}…";
            var batch = await CentralRerunBatch.RunAsync(_host, ticked, steps, message => Dispatcher.UIThread.Post(() => _status.Text = message));
            LastBatch = batch;
            Report = await Task.Run(() => CentralRerunReport.Write(_host.ReportsFolder, batch));
            _reportReady.IsVisible = true;
            _status.Text = $"Done: {batch.Rows.Count(r => r.After == BatchRow.NowPassed)} now passed, {batch.Rows.Count(r => r.IsStillFailed)} still failed, {batch.Rows.Count(r => r.After == BatchRow.NotRun)} not run." + (batch.Problems.Count > 0 ? " " + string.Join(" ", batch.Problems.Select(p => $"{p.Key}: {p.Value}")) : string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"The batch ended, but the report could not be written: {ex.Message}";
        }
        finally
        {
            _busy = false;
            _touched.Clear();
            Load();   // the lists show the new status
        }
    }
}
