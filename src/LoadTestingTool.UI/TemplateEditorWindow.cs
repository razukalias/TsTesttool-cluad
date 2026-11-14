using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;

namespace LoadTestingTool.UI;

internal sealed class TemplateEditorWindow : Window
{
    private readonly string _templatesFolder;
    private readonly TextBox _path;
    private readonly ComboBox _format;
    private readonly TextEditor _content;
    private readonly TextBox _search = new() { Watermark = "Highlight terms: term1 & term2…", Width = 360 };
    private readonly TextBlock _matchCount = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly TextBlock _status = DashboardStyle.Text("", 11, false, DashboardStyle.Muted);
    private readonly Action<string> _saved;
    private readonly string _instanceName;
    private readonly string _testcaseName;
    private readonly string _stepName;
    private double _fontSize = 12;

    public TemplateEditorWindow(string templatesFolder, string sourcePath, string stepType, Action<string> saved, string? instanceName = null, string? testcaseName = null, string? stepName = null)
    {
        _templatesFolder = Path.GetFullPath(templatesFolder);
        Directory.CreateDirectory(_templatesFolder);
        _saved = saved;
        _instanceName = Display(instanceName); _testcaseName = Display(testcaseName); _stepName = Display(stepName);
        Title = $"Template editor — Instance: {_instanceName} · Testcase: {_testcaseName} · Test step: {_stepName}"; Width = 900; Height = 700; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _path = new TextBox { Text = sourcePath, Watermark = "relative path inside Templates", MinWidth = 320 };
        _format = new ComboBox { ItemsSource = new[] { "JSON", "XML", "SQL", "GraphQL", "C# script", "Plain text" }, SelectedIndex = FormatIndex(sourcePath, stepType), Width = 150 };
        _content = new TextEditor { IsReadOnly = false, ShowLineNumbers = true, WordWrap = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = _fontSize, Foreground = DashboardStyle.Brush("#E3EDF9"), MinHeight = 360, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, SearchResultsBrush = DashboardStyle.Brush("#F6A623") };
        _content.TextArea.SelectionBrush = DashboardStyle.Brush("#F6A623");
        _content.TextArea.SelectionForeground = Brushes.Black;
        _content.AttachedToVisualTree += (_, _) => ApplySearch();
        _search.TextChanged += (_, _) => UpdateMatchCount();
        LoadFile();
        var validate = DashboardStyle.Action("Validate", Validate, "primary");
        var save = DashboardStyle.Action("Save template", Save, "primary");
        var close = DashboardStyle.Action("Close", Close);
        var help = DashboardStyle.Action("Help", () => ContextHelpWindow.Show(this, "Template editor help", "Template files are stored under the instance Templates folder. Use relative paths such as requests/create-user.json or sql/find-user.sql.\n\nTemplates use underscore variables only, for example _userId_, _token_, or _requestId_. The editor does not suggest or generate <guid>, <randomnumber>, or <From_...> syntax.\n\nJSON and XML are syntax-checked. SQL and GraphQL receive basic required-content checks. C# script templates must contain content and require trusted scripts at runtime."));
        var meta = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,Auto") };
        meta.Children.Add(DashboardStyle.Text("File", 12, false, DashboardStyle.Muted)); Grid.SetColumn(_path, 1); meta.Children.Add(_path); Grid.SetColumn(_format, 2); meta.Children.Add(_format);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { help, validate, save, close } };
        var textTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _search, DashboardStyle.Action("−", () => ChangeFontSize(-1)), DashboardStyle.Action("+", () => ChangeFontSize(1)), DashboardStyle.Text("Text size", 11, false, DashboardStyle.Muted), _matchCount } };
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        body.Children.Add(DashboardStyle.Text($"Edit {stepType} template — Instance: {_instanceName} · Testcase: {_testcaseName} · Test step: {_stepName}", 19, true));
        Grid.SetRow(textTools, 1); body.Children.Add(textTools);
        Grid.SetRow(meta, 2); body.Children.Add(meta);
        Grid.SetRow(_content, 3); body.Children.Add(_content);
        Grid.SetRow(_status, 4); body.Children.Add(_status);
        var shell = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(actions, Dock.Top); shell.Children.Add(actions); shell.Children.Add(body);
        Content = new Border { Padding = new Thickness(18), Background = DashboardStyle.Panel, Child = shell };
        UpdateMatchCount();
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private void LoadFile()
    {
        try
        {
            var path = ResolvePath(_path.Text ?? "");
            if (File.Exists(path)) _content.Text = File.ReadAllText(path);
            else _content.Text = DefaultContent(_format.SelectedItem?.ToString() ?? "Plain text");
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.Foreground = DashboardStyle.Red; }
    }

    private void Validate()
    {
        try { ValidateContent(_content.Text ?? "", _format.SelectedItem?.ToString() ?? "Plain text"); _status.Text = "Template is valid."; _status.Foreground = DashboardStyle.Green; }
        catch (Exception ex) { _status.Text = ex.Message; _status.Foreground = DashboardStyle.Red; }
    }

    private void Save()
    {
        try
        {
            ValidateContent(_content.Text ?? "", _format.SelectedItem?.ToString() ?? "Plain text");
            var path = ResolvePath(_path.Text ?? "");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _content.Text ?? "");
            var relative = Path.GetRelativePath(_templatesFolder, path).Replace(Path.DirectorySeparatorChar, '/');
            _saved(relative); _status.Text = $"Saved {relative}"; _status.Foreground = DashboardStyle.Green;
        }
        catch (Exception ex) { _status.Text = ex.Message; _status.Foreground = DashboardStyle.Red; }
    }

    private void ChangeFontSize(double delta)
    {
        _fontSize = Math.Clamp(_fontSize + delta, 8, 28);
        _content.FontSize = _fontSize;
    }

    private void UpdateMatchCount()
    {
        var text = _content?.Text ?? string.Empty;
        var terms = (_search?.Text ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ApplySearch(terms);
        var count = terms.Sum(term => CountOccurrences(text, term));
        _matchCount.Text = count == 0 ? (string.IsNullOrWhiteSpace(_search?.Text) ? "" : "No matches") : $"{count:N0} matching occurrence(s)";
    }

    private void ApplySearch()
    {
        var terms = (_search.Text ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ApplySearch(terms);
    }

    private void ApplySearch(IReadOnlyCollection<string> terms)
    {
        if (_content.SearchPanel is not { } panel) return;
        panel.UseRegex = true;
        panel.SearchPattern = terms.Count == 0 ? string.Empty : string.Join("|", terms.Select(Regex.Escape));
        panel.SetSearchResultsBrush(DashboardStyle.Brush("#F6A623"));
        if (terms.Count > 0) panel.Open(); else panel.Close();
    }

    private static int CountOccurrences(string value, string term)
    {
        var count = 0; var index = 0;
        while ((index = value.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0) { count++; index += Math.Max(1, term.Length); }
        return count;
    }

    internal static string ResolveInside(string templatesFolder, string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("Template source path is required.");
        var folder = Path.GetFullPath(templatesFolder);
        var path = Path.GetFullPath(Path.Combine(folder, source.Replace('/', Path.DirectorySeparatorChar)));
        var root = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Template path must remain inside the Templates folder.");
        return path;
    }

    private string ResolvePath(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException("Template source path is required.");
        var path = Path.GetFullPath(Path.Combine(_templatesFolder, source.Replace('/', Path.DirectorySeparatorChar)));
        var root = _templatesFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Template path must remain inside the Templates folder.");
        return path;
    }

    internal static void ValidateContent(string content, string format)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("Template content cannot be empty.");
        foreach (Match match in Regex.Matches(content, @"_([\p{L}\p{N}._-]+)_"))
            if (match.Groups[1].Value.Contains('<') || match.Groups[1].Value.Contains('>')) throw new InvalidDataException("Template variables must use _variable_ syntax.");
        if (format.Equals("JSON", StringComparison.OrdinalIgnoreCase)) using (JsonDocument.Parse(NormalizeRuntimeVariables(content))) { }
        else if (format.Equals("XML", StringComparison.OrdinalIgnoreCase)) XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        else if (format.Equals("SQL", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(content, @"\b(select|insert|update|delete|merge|exec|execute|declare|with)\b", RegexOptions.IgnoreCase)) throw new InvalidDataException("SQL template should contain a SQL command such as SELECT, INSERT, UPDATE, DELETE, EXEC, or WITH.");
        else if (format.Equals("GraphQL", StringComparison.OrdinalIgnoreCase) && !content.Contains('{')) throw new InvalidDataException("GraphQL template must contain a selection set enclosed by braces.");
    }

    internal static int FormatIndex(string path, string stepType)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch { ".json" => 0, ".xml" => 1, ".sql" => 2, ".graphql" or ".gql" => 3, ".cs" or ".csx" => 4, _ when stepType.Equals("SQL", StringComparison.OrdinalIgnoreCase) => 2, _ when stepType.Equals("GraphQL", StringComparison.OrdinalIgnoreCase) => 3, _ when stepType.Equals("Script", StringComparison.OrdinalIgnoreCase) => 4, _ => 5 };
    }

    internal static string DefaultContent(string format) => format switch
    {
        "JSON" => "{\n  \"id\": \"_id_\"\n}",
        "XML" => "<request><id>_id_</id></request>",
        "SQL" => "select * from Users where Id = _id_",
        "GraphQL" => "query Example { user(id: \"_id_\") { id } }",
        "C# script" => "return _data_;",
        _ => "_value_"
    };

    private static string NormalizeRuntimeVariables(string content) => Regex.Replace(content, "(?<![\\\"\\w])_[\\p{L}\\p{N}._-]+_(?![\\\"\\w])", "0");
}
