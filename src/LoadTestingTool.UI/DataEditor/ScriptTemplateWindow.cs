using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The form for writing a script template (<c>.csx</c>): the code, a help panel with how it works and examples, autocomplete for what a script can use
/// (and the variables of this workbook), a Validate button that compiles without running, and Save into the Templates folder.
/// Returns the template name when it was saved, otherwise null.
/// </summary>
public sealed class ScriptTemplateWindow : Window
{
    private readonly string _workspaceRoot;
    private readonly string _templatesFolder;
    private readonly TextBox _name = new() { Watermark = "template name, e.g. env_status", MinWidth = 260, Tag = "script-name" };
    private readonly TextBox _code = new() { AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 13, MinHeight = 280, TextWrapping = TextWrapping.NoWrap, Tag = "script-code" };
    private readonly ListBox _problems = new() { MaxHeight = 120, Tag = "script-problems", IsVisible = false };
    private readonly TextBlock _status = new() { FontSize = 12, Foreground = Brush.Parse("#8EA4BC"), TextWrapping = TextWrapping.Wrap, Tag = "script-status" };
    private readonly ScriptAssist _assist;
    private Avalonia.Controls.Templates.IDataTemplate _insertTemplate = null!;
    private readonly TextBlock _signature;
    private readonly IReadOnlyList<ScriptHelpItem> _items;
    private readonly bool _existing;
    private string? _saved;

    /// <param name="variables">Names of the workbook's request columns; offered as <c>_name_</c>.</param>
    public ScriptTemplateWindow(string workspaceRoot, string templatesFolder, string? existingName, IReadOnlyList<string> variables)
    {
        _workspaceRoot = workspaceRoot;
        _templatesFolder = templatesFolder;
        _existing = existingName is not null;
        _items = [.. variables.Select(v => new ScriptHelpItem($"_{v}_", $"_{v}_", $"the request value {v} of this row (when the call passes it as a parameter)")), .. ScriptTemplates.Help];
        Title = _existing ? $"Script template: {existingName}" : "New script template";
        Width = 1040; Height = 720; MinWidth = 760; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (existingName is not null)
        {
            _name.Text = existingName; _name.IsEnabled = false;
            var path = ScriptTemplates.PathOf(existingName, workspaceRoot, templatesFolder);
            _code.Text = path is null ? string.Empty : File.ReadAllText(path);
        }
        else _code.Text = ScriptTemplates.Starter("my_script");
        if (!_existing) _name.TextChanged += (_, _) => { };

        var validate = new Button { Content = "Validate", Tag = "script-validate" };
        validate.Click += (_, _) => Validate();
        var addVariable = new Button { Content = "Add variable…", Tag = "script-add-variable" };
        ToolTip.SetTip(addVariable, "Adds a line like  var orderId = \"_orderId_\";  at the top of the script, filled from a request column of the row");
        addVariable.Click += async (_, _) =>
        {
            var chosen = await new ScriptVariableWindow(variables, [], _code.Text ?? string.Empty).ShowDialog<(string Name, string Placeholder)?>(this);
            if (chosen is { } pick) _code.Text = ScriptVariableLines.AddToTop(_code.Text, pick.Name, pick.Placeholder);
        };
        var save = new Button { Content = "Save", Classes = { "primary" }, Tag = "script-save", MinWidth = 90 };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Close", Tag = "script-close", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close();

        _insertTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ScriptHelpItem>((item, _) => item is null ? null : new StackPanel { Children =
        {
            new TextBlock { Text = item.Name, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = item.Detail, FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
        } });
        _assist = new ScriptAssist(_code, ScriptKind.Template, () => _items.Where(x => x.Insert.StartsWith('_') && x.Insert.EndsWith('_') && x.Insert.Length > 2).Select(x => x.Insert[1..^1]).ToList());
        _signature = _assist.Signature;
        _assist.Idle += () => _ = LiveCheckAsync();
        _problems.DoubleTapped += (_, _) => JumpToProblem();
        _problems.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ScriptDiagnostic>((d, _) => d is null ? null : new TextBlock { Text = $"{(d.IsError ? "✕" : "⚠")} line {d.Line}, column {d.Column}: {d.Message}", Foreground = Brush.Parse(d.IsError ? "#FF7588" : "#F6C76B"), TextWrapping = TextWrapping.Wrap });

        var left = new DockPanel { LastChildFill = true };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8), Children = { new TextBlock { Text = "Name", VerticalAlignment = VerticalAlignment.Center }, _name, new TextBlock { Text = ".csx in the Templates folder", Foreground = Brush.Parse("#8EA4BC"), VerticalAlignment = VerticalAlignment.Center } } };
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0), Children =
        {
            _problems, _status,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { addVariable, validate, cancel, save } },
        } };
        DockPanel.SetDock(bottom, Dock.Bottom); left.Children.Add(bottom);
        var editor = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        editor.Children.Add(_code);
        var assistPanel = new StackPanel { Spacing = 4, Children = { _assist.Signature, _assist.Suggestions } };
        Grid.SetRow(assistPanel, 1); editor.Children.Add(assistPanel);
        left.Children.Add(editor);

        var help = new ScrollViewer { Content = new StackPanel { Spacing = 10, Children =
        {
            new TextBlock { Text = "Help and examples", FontWeight = FontWeight.SemiBold },
            new TextBox { Text = ScriptTemplates.HelpText, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Tag = "script-help" },
            new TextBlock { Text = "Insert (double-click, or press Ctrl+Space in the code)", FontWeight = FontWeight.SemiBold },
            InsertList(),
        } } };
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,360"), Margin = new Thickness(16) };
        split.Children.Add(left);
        var splitter = new GridSplitter { Width = 6, Background = Brushes.Transparent }; Grid.SetColumn(splitter, 1); split.Children.Add(splitter);
        var side = new Border { Child = help, BorderBrush = Brush.Parse("#22364F"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10) };
        Grid.SetColumn(side, 2); split.Children.Add(side);
        Content = split;
        _status.Text = "Press Validate to compile the script without running it.";
        Opened += (_, _) => (_existing ? _code : _name).Focus();
    }

    private Control InsertList()
    {
        var list = new ListBox { ItemsSource = _items, MaxHeight = 360, Tag = "script-insert-list" };
        list.ItemTemplate = _insertTemplate;
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is ScriptHelpItem item) InsertText(item.Insert, replaceTyped: 0); };
        return list;
    }

    private void InsertText(string text, int replaceTyped)
    {
        var caret = _code.CaretIndex;
        var current = _code.Text ?? string.Empty;
        var start = Math.Max(0, caret - replaceTyped);
        _code.Text = current[..start] + text + current[caret..];
        _code.CaretIndex = start + text.Length;
        _code.Focus();
    }

    // ---- Autocomplete: ScriptAssist (members after a dot, names, keywords, snippets, signature help) -------------------------

    /// <summary>The suggestions for the code before the caret, for tests: what the list would show now.</summary>
    internal ScriptSuggestions? SuggestionsNow(bool forced = false)
    {
        _assist.UpdateNow(forced);
        return _assist.Suggestions.IsVisible ? new ScriptSuggestions(0, _assist.Suggestions.Items.OfType<ScriptSuggestion>().ToList()) : null;
    }

    internal ScriptAssist Assist => _assist;

    /// <summary>Compiles while typing paused, so errors show without pressing Validate (the compile runs in the background).</summary>
    private async Task LiveCheckAsync()
    {
        var code = _code.Text ?? string.Empty;
        var found = await Task.Run(() => ScriptTemplates.Validate(code));
        if (!string.Equals(code, _code.Text ?? string.Empty, StringComparison.Ordinal)) return;
        _problems.ItemsSource = found;
        _problems.IsVisible = found.Count > 0;
        var errors = found.Count(d => d.IsError);
        _status.Text = errors > 0 ? $"{errors} {(errors == 1 ? "error" : "errors")}. Double-click a line to go to it." : found.Count > 0 ? "No errors; there are warnings." : "The script compiles.";
        _status.Foreground = Brush.Parse(errors > 0 ? "#FF7588" : "#39D39B");
    }

    // ---- Validate and save -------------------------------------------------------------------

    /// <summary>Compiles the script and lists the problems; returns true when there is no error.</summary>
    public bool Validate()
    {
        var found = ScriptTemplates.Validate(_code.Text ?? string.Empty);
        _problems.ItemsSource = found;
        _problems.IsVisible = found.Count > 0;
        var errors = found.Count(d => d.IsError);
        _status.Text = errors > 0 ? $"{errors} {(errors == 1 ? "error" : "errors")}. Double-click a line to go to it." : found.Count > 0 ? "No errors; there are warnings." : "The script compiles. (It was not run.)";
        _status.Foreground = Brush.Parse(errors > 0 ? "#FF7588" : "#39D39B");
        return errors == 0;
    }

    private void JumpToProblem()
    {
        if (_problems.SelectedItem is not ScriptDiagnostic d) return;
        var lines = (_code.Text ?? string.Empty).Split('\n');
        var index = 0;
        for (var i = 0; i < Math.Min(d.Line - 1, lines.Length); i++) index += lines[i].Length + 1;
        _code.CaretIndex = Math.Min(index + Math.Max(0, d.Column - 1), (_code.Text ?? string.Empty).Length);
        _code.Focus();
    }

    private void Save()
    {
        var name = (_name.Text ?? string.Empty).Trim();
        if (name.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (!ScriptTemplates.IsValidName(name)) { Say("Give the template a name of letters, digits, '-', '_' or '.' (no folders).", true); return; }
        var existsElsewhere = !_existing && ScriptTemplates.PathOf(name, _workspaceRoot, _templatesFolder) is not null;
        if (existsElsewhere) { Say($"A template named {name} already exists. Pick another name, or open it from the list to change it.", true); return; }
        var valid = Validate();
        var path = _existing ? ScriptTemplates.PathOf(name, _workspaceRoot, _templatesFolder)! : ScriptTemplates.NewTemplatePath(_workspaceRoot, name);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _code.Text ?? string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say($"Could not save {path}: {ex.Message}", true); return; }
        _saved = name;
        Say(valid ? $"Saved {path}" : $"Saved {path} — but it does not compile yet; a run would fail with the errors above.", !valid);
    }

    private void Say(string text, bool problem)
    {
        _status.Text = text;
        _status.Foreground = Brush.Parse(problem ? "#F6C76B" : "#39D39B");
    }

    /// <summary>For tests: the code box, the name box and the saved name.</summary>
    internal TextBox CodeBox => _code;
    internal TextBox NameBox => _name;
    internal string? SavedName => _saved;
    internal void SaveNow() => Save();

    public static async Task<string?> OpenAsync(Window owner, string workspaceRoot, string templatesFolder, string? existingName, IReadOnlyList<string> variables)
    {
        var window = new ScriptTemplateWindow(workspaceRoot, templatesFolder, existingName, variables);
        await window.ShowDialog(owner);
        return window._saved;   // also set when the window is closed with the X after a save
    }
}
