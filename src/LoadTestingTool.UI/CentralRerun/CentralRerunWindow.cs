using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.CentralRerun;

/// <summary>
/// The re-run center: the failed rows of every instance as cards (filters, ticks), each card with its own steps (the step that failed runs again with its old data,
/// the steps after it with fresh data, the steps before it do not run; every step can be changed by hand), one button that re-runs everything ticked
/// (one runner per instance, all at the same time) and a report (HTML and Excel) when it is done.
/// </summary>
public sealed class CentralRerunWindow : Window
{
    private const string AllInstances = "All instances", AllTestcases = "All testcases", AllEnvironments = "All environments", AllKinds = "All failures";
    private const string OldDataText = "Old data", FreshDataText = "Fresh data";

    private readonly ICentralRerunHost _host;
    private readonly ComboBox _instance = new() { MinWidth = 150, Tag = "central-instance" };
    private readonly ComboBox _testcase = new() { MinWidth = 150, Tag = "central-testcase" };
    private readonly ComboBox _environment = new() { MinWidth = 150, Tag = "central-environment" };
    private readonly ComboBox _kind = new() { MinWidth = 120, Tag = "central-kind", ItemsSource = new[] { AllKinds, "FAIL", "ERROR" }, SelectedIndex = 0 };
    private readonly TextBox _text = new() { MinWidth = 200, Watermark = "Row, step or reason contains…", Tag = "central-text" };
    private readonly ListBox _rows = new() { Background = Brushes.Transparent, MinHeight = 220, Tag = "central-rows" };
    private readonly TextBlock _count = new() { FontSize = 12, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _overview = new() { Spacing = 8, Tag = "central-overview" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.9, Tag = "central-status" };
    private readonly Button _run = new() { Content = "Re-run ticked rows", Classes = { "primary" }, MinHeight = 34, MinWidth = 150, Tag = "central-run" };
    private readonly StackPanel _reportReady = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false, Tag = "central-report-ready" };

    private readonly List<RowChoice> _all = [];
    private readonly ObservableCollection<RowChoice> _shown = [];
    /// <summary>The steps a person changed by hand, kept while the list is read again (until a batch has run).</summary>
    private readonly Dictionary<string, List<RowStep>> _remembered = [];
    private bool _filling, _bulk, _busy;

    /// <summary>True once the failed rows of every instance have been read (it happens in the background so the window opens at once).</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The paths of the last report, or null before a batch has finished.</summary>
    public (string Html, string Excel)? Report { get; private set; }

    /// <summary>The result of the last batch (null before one has finished).</summary>
    public BatchResult? LastBatch { get; private set; }

    public IReadOnlyList<FailedPair> Failed => _all.Select(c => c.Pair).ToList();
    public IReadOnlyList<FailedPair> Ticked => _all.Where(c => c.IsChecked).Select(c => c.Pair).ToList();

    /// <summary>The steps of a failed row as the window has them now (what the run would use).</summary>
    public IReadOnlyList<RowStep> StepsOf(FailedPair pair) => _all.First(c => c.Pair.Key == pair.Key).CurrentSteps();

    private sealed class StepChoice(string name, bool failed, bool run, bool oldData) : ObservableObject
    {
        public string Name { get; } = name;
        /// <summary>The step failed in the run this row comes from.</summary>
        public bool Failed { get; } = failed;
        private bool _run = run;
        public bool Run { get => _run; set => SetProperty(ref _run, value); }
        private bool _oldData = oldData;
        public bool OldData { get => _oldData; set => SetProperty(ref _oldData, value); }
    }

    private sealed class RowChoice : ObservableObject
    {
        public RowChoice(FailedPair pair, IReadOnlyList<RowStep> steps, IReadOnlySet<string> failedSteps)
        {
            Pair = pair;
            Steps = [.. steps.Select(s => new StepChoice(s.Name, failedSteps.Contains(s.Name), s.Run, s.OldData))];
            Text = $"{pair.Instance} › {pair.Testcase}   {pair.DataId}   {pair.EnvironmentText}   {pair.Kind}   {pair.Reason}";
        }
        public FailedPair Pair { get; }
        public string Text { get; }
        public List<StepChoice> Steps { get; }
        private bool _isChecked;
        public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }
        public List<RowStep> CurrentSteps() => [.. Steps.Select(s => new RowStep(s.Name, s.Run, s.OldData))];
    }

    public CentralRerunWindow(ICentralRerunHost host)
    {
        _host = host;
        Title = "Re-run center — failed rows of all instances";
        Width = 1040; Height = 860; MinWidth = 760; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0B1626");

        _rows.ItemsSource = _shown;
        _rows.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<RowChoice>((choice, _) => choice is null ? new Border() : Card(choice));
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("central-line")) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0, 0, 0, 0)), new Setter(ListBoxItem.MarginProperty, new Thickness(0, 0, 0, 8)), new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent) } });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("central-line").Class(":selected").Template().OfType<ContentPresenter>()) { Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) } });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("central-line").Class(":pointerover").Template().OfType<ContentPresenter>()) { Setters = { new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent) } });
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
        _reportReady.Children.Add(new TextBlock { Text = "Report ready", FontWeight = FontWeight.SemiBold, Foreground = DashboardStyle.Green, VerticalAlignment = VerticalAlignment.Center });
        _reportReady.Children.Add(htmlButton); _reportReady.Children.Add(excelButton); _reportReady.Children.Add(folderButton);

        var filters = new WrapPanel { Children = { _instance, _testcase, _environment, _kind, _text } };
        foreach (var child in filters.Children) if (child is Control c) c.Margin = new Thickness(0, 0, 8, 6);
        var tickBar = new WrapPanel { Children = { tickShown, untickShown, tickNone, refresh, _count } };
        foreach (var child in tickBar.Children) if (child is Control c) c.Margin = new Thickness(0, 0, 8, 6);
        var intro = DashboardStyle.Text("Tick the failed rows to run again. Under each row are its steps: the step that failed runs again with its old data, the steps after it with fresh data, and the steps before it do not run (their old values are put back for the later steps). Change any step by hand. One runner starts per instance and they run at the same time.", 12, false, DashboardStyle.Muted);
        intro.TextWrapping = TextWrapping.Wrap;
        Content = new Grid
        {
            Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"),
            Children =
            {
                Place(new StackPanel { Spacing = 4, Children = { DashboardStyle.Text("Re-run center", 20, true), intro } }, 0),
                Place(_overview, 1),
                Place(new StackPanel { Spacing = 2, Children = { filters, tickBar } }, 2),
                Place(new Grid { RowDefinitions = new RowDefinitions("*"), Children = { _rows } }, 3),
                Place(new StackPanel { Spacing = 6, Children = { _status, _reportReady } }, 4),
                Place(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _run, CloseButton() } }, 5)
            }
        };
        Load();
    }

    private Button CloseButton() { var b = new Button { Content = "Close", MinHeight = 34 }; b.Click += (_, _) => Close(); return b; }

    private static T Place<T>(T control, int row) where T : Control { Grid.SetRow(control, row); control.Margin = new Thickness(0, 0, 0, 10); return control; }

    /// <summary>One failed row as a card: the tick, what failed and where, why, and the steps as chips with their data choice.</summary>
    private Control Card(RowChoice row)
    {
        var pair = row.Pair;
        var tick = new CheckBox { IsChecked = row.IsChecked, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Tag = "row-tick" };
        tick.IsCheckedChanged += (_, _) => { if (tick.IsChecked != row.IsChecked) row.IsChecked = tick.IsChecked == true; };
        var kind = pair.Kind == "ERROR" ? ("#FFB36B", "#4A3220") : ("#FF7588", "#432333");
        var kindBadge = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), Background = DashboardStyle.Brush(kind.Item2), VerticalAlignment = VerticalAlignment.Center, Child = DashboardStyle.Text(pair.Kind, 10, true, DashboardStyle.Brush(kind.Item1)) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
        {
            kindBadge,
            DashboardStyle.Text(pair.Instance, 13, true), DashboardStyle.Text("›", 13, false, DashboardStyle.Muted), DashboardStyle.Text(pair.Testcase, 13, true),
            DashboardStyle.Text(pair.DataId, 12, false, DashboardStyle.Brush("#72B7FF")),
            new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), Background = DashboardStyle.Brush("#17283B"), VerticalAlignment = VerticalAlignment.Center, Child = DashboardStyle.Text(pair.EnvironmentText, 11, false, DashboardStyle.Muted) }
        } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        header.Children.Add(tick); Grid.SetColumn(title, 1); header.Children.Add(title);
        var reason = DashboardStyle.Text(pair.Reason, 11, false, DashboardStyle.Muted); reason.TextWrapping = TextWrapping.Wrap; reason.Margin = new Thickness(26, 0, 0, 0);
        var chips = new WrapPanel { Margin = new Thickness(26, 2, 0, 0) };
        foreach (var step in row.Steps) chips.Children.Add(Chip(step));
        var body = new StackPanel { Spacing = 4, Children = { header, reason } };
        if (row.Steps.Count > 0) body.Children.Add(chips);
        var card = DashboardStyle.Card(body, new Thickness(12, 10));
        void Paint() { card.Opacity = row.IsChecked ? 1 : 0.55; card.BorderBrush = row.IsChecked ? DashboardStyle.Brush("#3A5F8A") : DashboardStyle.Line; if (tick.IsChecked != row.IsChecked) tick.IsChecked = row.IsChecked; }
        System.ComponentModel.PropertyChangedEventHandler follow = (_, e) => { if (e.PropertyName == nameof(RowChoice.IsChecked)) Paint(); };
        row.PropertyChanged += follow;
        card.DetachedFromVisualTree += (_, _) => row.PropertyChanged -= follow;
        card.AttachedToVisualTree += (_, _) => { row.PropertyChanged -= follow; row.PropertyChanged += follow; Paint(); };
        Paint();
        return card;
    }

    /// <summary>One step: a tick (does it run again), and for a ticked step the choice between the old data of the failed run and fresh data.</summary>
    private Control Chip(StepChoice step)
    {
        var box = new CheckBox { Content = step.Name, IsChecked = step.Run, FontSize = 12, MinHeight = 26, Tag = "step:" + step.Name };
        var data = new ComboBox { ItemsSource = new[] { OldDataText, FreshDataText }, SelectedIndex = step.OldData ? 0 : 1, FontSize = 11, MinHeight = 26, MinWidth = 108, Padding = new Thickness(8, 2), Tag = "data:" + step.Name };
        var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (step.Failed) inner.Children.Add(new TextBlock { Text = "✕", Foreground = DashboardStyle.Red, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        inner.Children.Add(box); inner.Children.Add(data);
        var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 2, 6, 2), Margin = new Thickness(0, 0, 8, 6), BorderThickness = new Thickness(1), Child = inner };
        if (step.Failed) ToolTip.SetTip(chip, "This step failed in the run the row comes from.");
        void Paint()
        {
            chip.Background = step.Run ? DashboardStyle.Brush("#193B61") : DashboardStyle.Brush("#142235");
            chip.BorderBrush = step.Failed ? DashboardStyle.Brush("#8A3B4B") : step.Run ? DashboardStyle.Brush("#3A6EA8") : DashboardStyle.Line;
            data.IsVisible = step.Run;
            if (box.IsChecked != step.Run) box.IsChecked = step.Run;
            var wanted = step.OldData ? 0 : 1; if (data.SelectedIndex != wanted) data.SelectedIndex = wanted;
        }
        box.IsCheckedChanged += (_, _) => { if (step.Run != (box.IsChecked == true)) { step.Run = box.IsChecked == true; OnStepsChanged(); } };
        data.SelectionChanged += (_, _) => { var old = data.SelectedIndex == 0; if (data.SelectedIndex >= 0 && step.OldData != old) { step.OldData = old; OnStepsChanged(); } };
        System.ComponentModel.PropertyChangedEventHandler follow = (_, _) => Paint();
        chip.AttachedToVisualTree += (_, _) => { step.PropertyChanged -= follow; step.PropertyChanged += follow; Paint(); };
        chip.DetachedFromVisualTree += (_, _) => step.PropertyChanged -= follow;
        Paint();
        return chip;
    }

    private void OnStepsChanged()
    {
        foreach (var row in _all) _remembered[row.Pair.Key] = row.CurrentSteps();
        _run.IsEnabled = !_busy && IsLoaded && CanRun();
    }

    private bool CanRun() => _all.Any(c => c.IsChecked && c.Steps.Any(s => s.Run));

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
            var every = instances.FirstOrDefault(i => i.Name.Equals(pair.Instance, StringComparison.OrdinalIgnoreCase))?.Catalog.FirstOrDefault(c => c.Index == pair.TestcaseIndex)?.Steps.Where(s => s.Enabled).Select(s => s.Name).ToList() ?? [];
            var steps = RowStepDefaults.For(pair, every);
            // a change made by hand survives a re-read as long as the testcase has the same steps
            if (_remembered.TryGetValue(pair.Key, out var kept) && kept.Select(s => s.Name).SequenceEqual(every, StringComparer.OrdinalIgnoreCase)) steps = kept;
            var choice = new RowChoice(pair, steps, pair.FailedSteps.ToHashSet(StringComparer.OrdinalIgnoreCase)) { IsChecked = ticked.Contains(pair.Key) };
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
        _run.IsEnabled = !_busy && CanRun();
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
            if (terms.Length > 0 && !terms.All(t => c.Text.Contains(t, StringComparison.OrdinalIgnoreCase) || c.Steps.Any(s => s.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))) continue;
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
        _run.IsEnabled = !_busy && IsLoaded && CanRun();
    }

    /// <summary>Figures on top (failed pairs, errors, ticked) and one line per instance.</summary>
    private void RefreshOverview()
    {
        _overview.Children.Clear();
        if (_all.Count == 0) { _overview.Children.Add(DashboardStyle.Text("No failed rows.", 12, false, DashboardStyle.Muted)); return; }
        var tiles = new WrapPanel
        {
            Children =
            {
                DashboardStyle.Tile("Failed rows", _all.Count(c => c.Pair.Kind == "FAIL").ToString(), DashboardStyle.Red, width: 120),
                DashboardStyle.Tile("Errors", _all.Count(c => c.Pair.Kind == "ERROR").ToString(), DashboardStyle.Brush("#FFB36B"), width: 120),
                DashboardStyle.Tile("Instances", _all.Select(c => c.Pair.Instance).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(), null, width: 120),
                DashboardStyle.Tile("Ticked", _all.Count(c => c.IsChecked).ToString(), DashboardStyle.Blue, width: 120)
            }
        };
        _overview.Children.Add(tiles);
        foreach (var group in _all.GroupBy(c => c.Pair.Instance, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.Select(c => (c.Pair.TestcaseIndex, c.Pair.DataId)).Distinct().Count();
            _overview.Children.Add(DashboardStyle.Text($"{group.Key}: {group.Count()} failed {(group.Count() == 1 ? "pair" : "pairs")} ({rows} {(rows == 1 ? "row" : "rows")}, {group.Select(c => c.Pair.EnvironmentText).Distinct().Count()} environments, {group.Count(c => c.Pair.Kind == "ERROR")} errors) · {group.Count(c => c.IsChecked)} ticked", 12, false, DashboardStyle.Muted));
        }
    }

    /// <summary>Runs everything ticked (also what the button does). Returns when the batch and the report are done.</summary>
    internal async Task RunTickedAsync()
    {
        if (_busy) return;
        var ticked = _all.Where(c => c.IsChecked).ToList();
        if (ticked.Count == 0) return;
        var rows = ticked.Where(c => c.Steps.Any(s => s.Run)).Select(c => (c.Pair, (IReadOnlyList<RowStep>)c.CurrentSteps())).ToList();
        if (rows.Count == 0) { _status.Text = "Tick at least one step of a ticked row."; return; }
        var skipped = ticked.Count - rows.Count;
        _busy = true; _run.IsEnabled = false; _reportReady.IsVisible = false; Report = null;
        try
        {
            var instances = rows.Select(r => r.Pair.Instance).Distinct().Count();
            _status.Text = $"Running {rows.Count} {(rows.Count == 1 ? "pair" : "pairs")} in {instances} {(instances == 1 ? "instance" : "instances")}…" + (skipped > 0 ? $" ({skipped} ticked without a step are left out.)" : string.Empty);
            var batch = await CentralRerunBatch.RunAsync(_host, rows, message => Dispatcher.UIThread.Post(() => _status.Text = message));
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
            _remembered.Clear();
            Load();   // the lists show the new status
        }
    }
}
