using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI;

public sealed class StepDetailsWindow : Window
{
    private readonly string _logPath;
    private readonly string _testcase;
    private readonly string _dataId;
    private readonly string _stepName;
    private readonly string _environment;
    private readonly string _stepType;
    private readonly TextBox _search = new() { Watermark = "Highlight terms: term1 & term2…", Width = 420 };
    private readonly TextBlock _matchCount = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly TabControl _tabs = new();
    private IReadOnlyList<StepSection> _sections = [];
    private readonly Dictionary<string, TextEditor> _viewers = new(StringComparer.OrdinalIgnoreCase);
    private double _fontSize = 12;

    public StepDetailsWindow(string logPath, string testcase, string dataId, string stepName, string environment, string stepType)
    {
        _logPath = logPath;
        _testcase = testcase;
        _dataId = dataId;
        _stepName = stepName;
        _environment = environment;
        _stepType = string.IsNullOrWhiteSpace(stepType) ? "HTTP" : stepType;
        Title = $"{_stepType} step details — {_testcase} / {_stepName} / {_environment}";
        Width = 1120;
        Height = 780;
        MinWidth = 760;
        MinHeight = 500;
        Build();
    }

    private void Build()
    {
        var blocks = LoadMatchingBlocks(out var problem);
        _sections = problem is not null
            ? [new StepSection("Overview", problem)]
            : StepLogView.Build(blocks, _stepType, _environment);

        var heading = DashboardStyle.Text($"{_stepType}  ·  {_testcase} / Row {_dataId} / {_stepName} / {_environment}", 16, true);
        var source = DashboardStyle.Text($"Source: {_logPath}", 11, false, DashboardStyle.Muted);
        source.TextWrapping = TextWrapping.Wrap;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
        {
            _search,
            DashboardStyle.Action("Copy visible", () => CopyVisible()),
            DashboardStyle.Action("−", () => ChangeFontSize(-1)),
            DashboardStyle.Action("+", () => ChangeFontSize(1)),
            DashboardStyle.Text("Text size", 11, false, DashboardStyle.Muted),
            _matchCount
        }};
        _search.TextChanged += (_, _) => RefreshCurrent();
        _tabs.SelectionChanged += (_, _) => RefreshCurrent();
        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Spacing = 7, Children = { heading, source, tools } };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        _tabs.ItemsSource = _sections.Select(section => new TabItem { Header = section.Title, Content = new Border() }).ToList();
        root.Children.Add(_tabs);
        Content = root;
        RefreshCurrent();
    }

    private void RefreshCurrent()
    {
        if (_tabs.SelectedItem is not TabItem tab) return;
        var name = tab.Header?.ToString() ?? "Overview";
        var section = _sections.FirstOrDefault(x => x.Title == name) ?? new StepSection(name, "No data recorded.");
        var value = section.Text;
        if (!_viewers.TryGetValue(name, out var box))
        {
            box = new TextEditor
            {
                IsReadOnly = true,
                ShowLineNumbers = false,
                WordWrap = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = _fontSize,
                Foreground = DashboardStyle.Brush("#E3EDF9"),
                Padding = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 420,
                Background = Brushes.Transparent,
                SearchResultsBrush = DashboardStyle.Brush("#F6A623")
            };
            box.TextArea.TextView.LineTransformers.Add(new StepLogColorizer(section.Kind));
            box.TextArea.SelectionBrush = DashboardStyle.Brush("#F6A623");
            box.TextArea.SelectionForeground = Brushes.Black;
            _viewers[name] = box;
            box.Text = value;
            box.AttachedToVisualTree += (_, _) => ApplySearch(box, _search.Text);
            tab.Content = box;
            ToolTip.SetTip(box, "Text is selectable. Drag with the mouse or press Ctrl+A / Ctrl+C.");
        }
        box.FontSize = _fontSize;
        ApplySearch(box, _search.Text);
        var count = CountMatches(value, _search.Text);
        _matchCount.Text = count == 0 ? "No matches" : $"{count:N0} matching occurrence(s)";
    }

    private static void ApplySearch(TextEditor editor, string? query)
    {
        if (editor.SearchPanel is not { } panel) return;
        var terms = (query ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        panel.UseRegex = true;
        panel.SearchPattern = terms.Count == 0 ? string.Empty : string.Join("|", terms.Select(Regex.Escape));
        panel.SetSearchResultsBrush(DashboardStyle.Brush("#F6A623"));
        if (terms.Count > 0) panel.Open(); else panel.Close();
    }

    private void ChangeFontSize(double delta)
    {
        _fontSize = Math.Clamp(_fontSize + delta, 8, 28);
        RefreshCurrent();
    }

    private async void CopyVisible(object? sender = null, Avalonia.Interactivity.RoutedEventArgs? args = null)
    {
        var text = _tabs.SelectedItem is TabItem tab ? _sections.FirstOrDefault(x => x.Title == tab.Header?.ToString())?.Text ?? string.Empty : string.Empty;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text);
        _matchCount.Text = "Copied visible section.";
    }

    /// <summary>The parts of the log that belong to this step (one per run of it). When there are none, <paramref name="problem"/> says why.</summary>
    private List<string> LoadMatchingBlocks(out string? problem)
    {
        problem = null;
        try
        {
            if (!File.Exists(_logPath)) { problem = $"The internal log file was not found:\n{_logPath}"; return []; }
            // Entries of rows that ran in parallel are interleaved, so first try to pick this step's entries by request id.
            var byRequest = StepLogView.SelectBlocks(File.ReadAllText(_logPath), _dataId, _stepName);
            if (byRequest.Count > 0) return byRequest;
            var lines = File.ReadAllLines(_logPath);
            var starts = lines.Select((line, index) => (line, index))
                .Where(x => x.line.Contains("---- STEP ", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.index).ToList();
            if (starts.Count == 0) starts = lines.Select((line, index) => (line, index)).Where(x => x.line.Contains("Request starting.", StringComparison.OrdinalIgnoreCase)).Select(x => x.index).ToList();
            var exact = new List<string>();
            var relaxed = new List<string>();
            for (var i = 0; i < starts.Count; i++)
            {
                var end = i + 1 < starts.Count ? starts[i + 1] : lines.Length;
                var block = string.Join(Environment.NewLine, lines[starts[i]..end]);
                if (ContainsIdentity(block, true)) exact.Add(block);
                else if (ContainsIdentity(block, false)) relaxed.Add(block);
            }
            var matches = exact.Count > 0 ? exact : relaxed;
            if (matches.Count > 0) return matches;
            problem = $"No matching {_stepType} step section was found for testcase '{_testcase}', row '{_dataId}', step '{_stepName}', environment '{_environment}'.\n\nThe full log is available at:\n{_logPath}";
            return [];
        }
        catch (Exception ex) { problem = $"Could not read the internal log:\n{ex.Message}"; return []; }
    }

    private bool ContainsIdentity(string block, bool requireDataId)
    {
        var step = block.Contains($"stepName={_stepName}", StringComparison.OrdinalIgnoreCase) || block.Contains($"STEP: {_stepName}", StringComparison.OrdinalIgnoreCase) || block.Contains($"| {_stepName} |", StringComparison.OrdinalIgnoreCase);
        var environment = string.IsNullOrWhiteSpace(_environment)
            || block.Contains($"environment={_environment}", StringComparison.OrdinalIgnoreCase)
            || block.Contains($"ENVIRONMENT: {_environment}", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(_logPath).Contains(_environment, StringComparison.OrdinalIgnoreCase);
        var dataId = !requireDataId || string.IsNullOrWhiteSpace(_dataId) || block.Contains($"dataid={_dataId}", StringComparison.OrdinalIgnoreCase) || block.Contains($"dataId={_dataId}", StringComparison.OrdinalIgnoreCase) || block.Contains($"data ID {_dataId}", StringComparison.OrdinalIgnoreCase);
        var type = string.IsNullOrWhiteSpace(_stepType) || block.Contains($"type={_stepType}", StringComparison.OrdinalIgnoreCase) || block.Contains($"TYPE: {_stepType}", StringComparison.OrdinalIgnoreCase);
        return step && environment && dataId && type;
    }

    private static int CountMatches(string value, string? filter)
    {
        var count = 0;
        foreach (var term in (filter ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0))
        {
            var index = 0;
            while ((index = value.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0) { count++; index += term.Length; }
        }
        return count;
    }
}
