using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI;

/// <summary>
/// "History &amp; logs": one form for what a run writes to history and to the internal log, with profiles (Functional, Debug, Load test).
/// Changing any single option turns the profile label into Custom. Applies from the next run; existing history files are not changed.
/// </summary>
public sealed class HistoryLogsWindow : Window
{
    private readonly ComboBox _profile = new() { MinWidth = 200 };
    private readonly CheckBox _internalLog = new() { Content = "Internal log" };
    private readonly CheckBox _errorsOnly = new() { Content = "Errors and warnings only", Margin = new Thickness(22, 0, 0, 0) };
    private readonly CheckBox _aggregate = new() { Content = "Aggregate history (one workbook for the whole run)" };
    private readonly CheckBox _rowHistory = new() { Content = "Row history (one workbook per data row)" };
    private readonly CheckBox _requestBodies = new() { Content = "Save request bodies" };
    private readonly CheckBox _responseBodies = new() { Content = "Save response bodies" };
    private readonly CheckBox _mask = new() { Content = "Mask sensitive data" };
    private readonly ComboBox _detail = new() { ItemsSource = new[] { "Full detail for every request", "Full detail for failed requests and a sample of passing ones" }, MinWidth = 330 };
    private readonly NumericUpDown _sample = new() { Minimum = 1, Maximum = 1_000_000, Increment = 10, FormatString = "0", Width = 120 };
    private readonly Dictionary<string, CheckBox> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#E8B04A"), FontSize = 12 };
    private bool _loading;
    private readonly bool _largeRun;

    /// <summary>The settings chosen, or null when the window was cancelled.</summary>
    public HistoryOptions? Result { get; private set; }

    public HistoryLogsWindow(string instanceName, HistoryOptions initial, bool largeRun)
    {
        _largeRun = largeRun;
        Title = $"History & logs — {instanceName}";
        Width = 640; Height = 760; MinWidth = 520; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0B1626");

        _profile.ItemsSource = new[] { HistoryProfiles.Functional, HistoryProfiles.Debug, HistoryProfiles.LoadTest, HistoryProfiles.Custom };
        _profile.SelectionChanged += (_, _) => { if (!_loading && _profile.SelectedItem is string name && name != HistoryProfiles.Custom) Load(HistoryProfiles.For(name, (int)(_sample.Value ?? HistoryProfiles.DefaultSampleEvery))); };

        var sheetPanel = new StackPanel { Spacing = 2 };
        foreach (var name in UiHistorySheetCatalog.Names)
        {
            var box = new CheckBox { Content = name };
            _sheets[name] = box;
            sheetPanel.Children.Add(box);
        }
        sheetPanel.Children.Add(new CheckBox { Content = "ResolvedValues (always written: a re-run with old data reads it)", IsChecked = true, IsEnabled = false });

        Control Row(CheckBox box, string help) => StepFieldHelp.Labelled(box, help);
        var options = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Row(_internalLog, "Writes a detailed text log of the run (one file per run in the instance's Logs folder). Useful for finding why something failed; it slows a very large run down."),
                Row(_errorsOnly, "Keeps only warnings and errors in the internal log. The default for load tests."),
                Row(_aggregate, "Writes one history workbook for the whole run, with the summary sheets."),
                Row(_rowHistory, "Writes one history workbook for every data row (testcase, row, iteration). Turn it off for big load tests."),
                Row(_requestBodies, "Puts the request body of each request into RequestHistory."),
                Row(_responseBodies, "Puts the response body of each request into RequestHistory."),
                Row(_mask, "Hides passwords, tokens and other sensitive values in logs and history."),
                new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "How much detail history keeps", FontWeight = FontWeight.SemiBold }, _detail,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Sample: one passing request in", VerticalAlignment = VerticalAlignment.Center }, _sample } } } },
            }
        };
        foreach (var box in new[] { _internalLog, _errorsOnly, _aggregate, _rowHistory, _requestBodies, _responseBodies, _mask }.Concat(_sheets.Values))
            box.IsCheckedChanged += (_, _) => Changed();
        _detail.SelectionChanged += (_, _) => Changed();
        _sample.ValueChanged += (_, _) => Changed();

        var save = new Button { Content = "Save", Classes = { "primary" }, MinHeight = 34, MinWidth = 96 };
        var cancel = new Button { Content = "Cancel", MinHeight = 34 };
        save.Click += (_, _) => { Result = Read(); Close(); };
        cancel.Click += (_, _) => Close();
        Content = new Grid
        {
            Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            Children =
            {
                Place(new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Profile", FontWeight = FontWeight.SemiBold }, _profile, _hint,
                    new TextBlock { Text = "Functional and Debug keep everything. Load test keeps the summary and the full detail of failed requests only, so big runs stay fast. The profile is chosen by hand, never automatically.", Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap } } }, 0),
                Place(new ScrollViewer { Content = new StackPanel { Spacing = 14, Children = { options, new TextBlock { Text = "History sheets", FontWeight = FontWeight.SemiBold }, sheetPanel } } }, 1),
                Place(new TextBlock { Text = "Applies from the next run; existing history files are not changed.", Opacity = 0.7, FontSize = 12 }, 2),
                Place(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel } }, 3),
            }
        };
        Load(initial);
    }

    private static T Place<T>(T control, int row) where T : Control { Grid.SetRow(control, row); control.Margin = new Thickness(0, 0, 0, 10); return control; }

    private void Load(HistoryOptions options)
    {
        _loading = true;
        _internalLog.IsChecked = options.InternalLog; _errorsOnly.IsChecked = options.InternalLogErrorsOnly;
        _aggregate.IsChecked = options.AggregateHistory; _rowHistory.IsChecked = options.RowHistory;
        _requestBodies.IsChecked = options.SaveRequestBodies; _responseBodies.IsChecked = options.SaveResponseBodies; _mask.IsChecked = options.MaskSensitive;
        _detail.SelectedIndex = options.Detail.Equals(HistoryProfiles.DetailFailedAndSample, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _sample.Value = options.SampleEvery;
        foreach (var (name, box) in _sheets) box.IsChecked = (!options.Sheets.TryGetValue(name, out var on) || on);
        _loading = false;
        Changed();
    }

    private HistoryOptions Read() => new(
        _internalLog.IsChecked == true, _internalLog.IsChecked == true && _errorsOnly.IsChecked == true, _aggregate.IsChecked == true, _rowHistory.IsChecked == true,
        _requestBodies.IsChecked == true, _responseBodies.IsChecked == true, _mask.IsChecked == true,
        _detail.SelectedIndex == 1 ? HistoryProfiles.DetailFailedAndSample : HistoryProfiles.DetailAll, (int)Math.Max(1, _sample.Value ?? HistoryProfiles.DefaultSampleEvery),
        _sheets.ToDictionary(p => p.Key, p => p.Value.IsChecked == true, StringComparer.OrdinalIgnoreCase));

    private void Changed()
    {
        if (_loading) return;
        _errorsOnly.IsEnabled = _internalLog.IsChecked == true;
        var detail = Read();
        _sample.IsEnabled = detail.Detail == HistoryProfiles.DetailFailedAndSample;
        var name = HistoryProfiles.Detect(detail);
        _loading = true; _profile.SelectedItem = name; _loading = false;
        _hint.IsVisible = _largeRun && name is HistoryProfiles.Functional or HistoryProfiles.Debug;
        _hint.Text = "This run is large (many iterations and parallel rows). Functional and Debug keep every request in full, which makes the run slow and the history very big. Consider the Load test profile.";
    }

    /// <summary>The profile the window currently shows (for tests).</summary>
    public string ProfileName => _profile.SelectedItem as string ?? string.Empty;
}
