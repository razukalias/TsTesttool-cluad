using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
    private readonly Dictionary<string, string> _sections = new(StringComparer.OrdinalIgnoreCase);
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
        var text = LoadMatchingLog();
        AddCommonSections(text);
        AddTypeSections(text);

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
        _tabs.ItemsSource = _sections.Keys.Select(name => new TabItem { Header = name, Content = new Border() }).ToList();
        root.Children.Add(_tabs);
        Content = root;
        RefreshCurrent();
    }

    private void AddCommonSections(string text)
    {
        _sections["Overview"] = Extract(text, "---- STEP", "REQUEST COMPLETED", "STEP COMPLETED");
        _sections["Complete log"] = text;
        _sections["Execution"] = Extract(text, "Request starting.", "STEP TEMPLATE LOAD", "STEP REQUEST", "STEP RESPONSE", "STEP COMPLETED", "REQUEST COMPLETED");
        _sections["Assertions"] = LinesContaining(text, "ASSERTIONS", "ASSERTION FAILED", "PASSED:", "FAILED:");
        _sections["Errors"] = LinesContaining(text, "ERROR", "FAILED", "error=");
        if (string.IsNullOrWhiteSpace(_sections["Errors"])) _sections["Errors"] = "No error lines were recorded for this step.";
        _sections["Timing"] = LinesContaining(text, "durationMs=", "durationMs:", "thread=", "iteration=");
    }

    private void AddTypeSections(string text)
    {
        var type = _stepType.Trim().ToUpperInvariant();
        if (type is "HTTP")
        {
            _sections["HTTP request"] = Extract(text, "REQUEST SENT", "METHOD:", "URL:", "REQUEST:", "STEP REQUEST");
            _sections["HTTP headers"] = ExtractBlocks(text, "HEADERS:");
            _sections["HTTP request body"] = Extract(text, "BODY:", "REQUEST PAYLOAD:", "REQUEST:");
            _sections["HTTP response"] = Extract(text, "RESPONSE RECEIVED", "STATUS:", "STEP RESPONSE");
            _sections["HTTP response body"] = Extract(text, "RESPONSE PAYLOAD:", "BODY:");
        }
        else if (type is "GRAPHQL" or "CASEQL")
        {
            _sections["GraphQL query"] = Extract(text, "REQUEST PAYLOAD:", "REQUEST:", "STEP REQUEST", "TEMPLATE:");
            _sections["GraphQL variables"] = LinesContaining(text, "variables", "operationName", "METADATA:");
            _sections["GraphQL response"] = Extract(text, "RESPONSE PAYLOAD:", "STEP RESPONSE", "RESPONSE RECEIVED");
            _sections["GraphQL errors"] = LinesContaining(text, "errors", "ERROR", "error=");
        }
        else if (type is "SCRIPT")
        {
            _sections["Script source"] = Extract(text, "STEP REQUEST", "REQUEST:", "STEP TEMPLATE LOAD", "template=");
            _sections["Script input"] = Extract(text, "REQUEST PAYLOAD:", "METADATA:", "parameters", "variables");
            _sections["Script output"] = Extract(text, "RESPONSE PAYLOAD:", "STEP RESPONSE", "INLINE SCRIPT END", "OUTPUT");
            _sections["Script errors"] = LinesContaining(text, "SCRIPT", "exception", "ERROR", "error=");
        }
        else if (type is "FILE")
        {
            _sections["File operation"] = Extract(text, "STEP REQUEST", "ACTION", "path", "template=");
            _sections["File input"] = Extract(text, "REQUEST PAYLOAD:", "REQUEST:", "CONTENT", "SOURCE");
            _sections["File result"] = Extract(text, "RESPONSE PAYLOAD:", "STEP RESPONSE", "OUTPUT", "path=");
            _sections["File errors"] = LinesContaining(text, "FILE", "FileNotFound", "ERROR", "error=");
        }
        else if (type is "SQL")
        {
            _sections["SQL query"] = Extract(text, "REQUEST PAYLOAD:", "REQUEST:", "commandText", "CommandText", "STEP REQUEST");
            _sections["SQL parameters"] = LinesContaining(text, "parameters", "parameter", "METADATA:");
            _sections["SQL result set"] = Extract(text, "RESPONSE PAYLOAD:", "STEP RESPONSE", "rows", "RESULT");
            _sections["SQL errors"] = LinesContaining(text, "SQL", "SqlException", "ERROR", "error=");
        }
        else
        {
            _sections["Step input"] = Extract(text, "REQUEST:", "REQUEST PAYLOAD:", "STEP REQUEST");
            _sections["Step output"] = Extract(text, "RESPONSE:", "RESPONSE PAYLOAD:", "STEP RESPONSE");
        }
    }

    private void RefreshCurrent()
    {
        if (_tabs.SelectedItem is not TabItem tab) return;
        var name = tab.Header?.ToString() ?? "Overview";
        var value = _sections.GetValueOrDefault(name, "No data recorded.");
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
        var text = _tabs.SelectedItem is TabItem tab && _sections.TryGetValue(tab.Header?.ToString() ?? "", out var value) ? value : "";
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text);
        _matchCount.Text = "Copied visible section.";
    }

    private string LoadMatchingLog()
    {
        try
        {
            if (!File.Exists(_logPath)) return $"The internal log file was not found:\n{_logPath}";
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
            if (matches.Count > 0) return string.Join(Environment.NewLine + Environment.NewLine + "==================== NEXT MATCH ====================" + Environment.NewLine, matches);
            return $"No matching {_stepType} step section was found for testcase '{_testcase}', row '{_dataId}', step '{_stepName}', environment '{_environment}'.\n\nThe full log is available at:\n{_logPath}";
        }
        catch (Exception ex) { return $"Could not read the internal log:\n{ex.Message}"; }
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

    private static string Extract(string source, params string[] markers)
    {
        if (string.IsNullOrWhiteSpace(source)) return "No data recorded.";
        var lines = source.Split('\n');
        var start = Array.FindIndex(lines, line => markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        if (start < 0) return "No matching data recorded.";
        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++) if (lines[i].Contains("---- STEP ", StringComparison.OrdinalIgnoreCase) || lines[i].Contains("REQUEST COMPLETED", StringComparison.OrdinalIgnoreCase) || lines[i].Contains("STEP COMPLETED", StringComparison.OrdinalIgnoreCase)) { end = i; break; }
        return string.Join(Environment.NewLine, lines[start..end]);
    }

    private static string ExtractBlocks(string source, params string[] markers)
    {
        var lines = source.Split('\n');
        var selected = lines.Where(line => markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase))).ToArray();
        return selected.Length == 0 ? "No matching data recorded." : string.Join(Environment.NewLine, selected);
    }

    private static string LinesContaining(string source, params string[] terms)
    {
        var selected = source.Split('\n').Where(line => terms.Any(term => line.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        return selected.Length == 0 ? "No matching data recorded." : string.Join(Environment.NewLine, selected);
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

    private static IEnumerable<Inline> BuildHighlightedInlines(string value, string? filter)
    {
        var terms = (filter ?? string.Empty)
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (terms.Count == 0) return [new Run { Text = value }];

        var highlighted = new bool[value.Length];
        foreach (var term in terms)
        {
            var start = 0;
            while ((start = value.IndexOf(term, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                for (var i = start; i < Math.Min(value.Length, start + term.Length); i++) highlighted[i] = true;
                start += Math.Max(1, term.Length);
            }
        }

        var result = new List<Inline>();
        var index = 0;
        while (index < value.Length)
        {
            var marked = highlighted[index];
            var end = index + 1;
            while (end < value.Length && highlighted[end] == marked) end++;
            var run = new Run { Text = value[index..end] };
            if (marked)
            {
                run.Background = DashboardStyle.Brush("#F6A623");
                run.Foreground = Brushes.Black;
            }
            result.Add(run);
            index = end;
        }
        return result;
    }
}
