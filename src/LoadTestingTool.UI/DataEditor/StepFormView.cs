using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>How a step appears wherever its form is shown (alone from the tree, or expanded in a testcase's list).</summary>
public static class StepFormTemplates
{
    public static readonly IDataTemplate Form = new FuncDataTemplate<StepRowViewModel>((step, _) => step is null ? null : new StepFormView(step), false);
}

/// <summary>
/// One step as a vertical form: a labelled field for each config column, and beside it the step's template file with a
/// search box that marks every match orange ("id &amp; name" marks both).
/// </summary>
public sealed class StepFormView : UserControl
{
    private static readonly IBrush Card = Brush.Parse("#13233A");
    private static readonly IBrush Line = Brush.Parse("#22364F");
    private static readonly IBrush Muted = Brush.Parse("#8EA4BC");
    private static readonly IBrush Orange = Brush.Parse("#F6A623");
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["steptype"] = "Type", ["stage"] = "Stage", ["enabled"] = "Enabled", ["verb"] = "Verb", ["targeturl"] = "Target URL",
        ["templetsource"] = "Template", ["stoponfailure"] = "Stop on failure", ["contenttype"] = "Content type", ["timeout"] = "Timeout (s)", ["timeoutms"] = "Timeout (ms)",
        ["wait"] = "Wait before (ms or message)", ["expectedstatus"] = "Expected status", ["assert"] = "Assertions", ["ignoreempty"] = "Ignore empty",
        ["assertonlyresponse"] = "Only response", ["manualtimeoutminutes"] = "Manual timeout (min)",
    };

    private static readonly string[] Formats = ["JSON", "XML", "SQL", "GraphQL", "C# script", "Plain text"];
    private static readonly string[] ContentTypeChoices = ["application/json", "application/xml", "text/plain", "application/graphql", "application/x-www-form-urlencoded", "multipart/form-data", "application/octet-stream"];
    private static readonly HashSet<string> Known = new(["steptype", "stage", "enabled", "stoponfailure", "verb", "targeturl", "contenttype", "templetsource", "expectedstatus", "timeout", "timeoutms", "wait", "assert", "ignoreempty", "assertonlyresponse", "manualtimeoutminutes", "stepconfig"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The fields each step type uses, in the order they are shown.</summary>
    private static string[] FieldsFor(string type) => type.Trim().ToLowerInvariant() switch
    {
        "graphql" or "caseql" => ["steptype", "stage", "enabled", "stoponfailure", "targeturl", "templetsource", "expectedstatus", "timeout", "timeoutms", "wait", "assert", "ignoreempty", "assertonlyresponse"],
        "sql" => ["steptype", "stage", "enabled", "stoponfailure", "templetsource", "timeout", "wait", "assert", "ignoreempty", "assertonlyresponse"],
        "file" => ["steptype", "stage", "enabled", "stoponfailure", "templetsource", "timeout", "wait", "assert", "ignoreempty", "assertonlyresponse"],
        "script" => ["steptype", "stage", "enabled", "stoponfailure", "templetsource", "timeout", "wait"],
        "actionmanual" => ["steptype", "stage", "enabled", "stoponfailure", "manualtimeoutminutes"],
        _ => ["steptype", "stage", "enabled", "stoponfailure", "verb", "targeturl", "contenttype", "templetsource", "expectedstatus", "timeout", "timeoutms", "wait", "assert", "ignoreempty", "assertonlyresponse"],
    };

    private static bool UsesTemplate(string type) => !type.Trim().Equals("ActionManual", StringComparison.OrdinalIgnoreCase);
    private static bool UsesHeaders(string type) => type.Trim().ToLowerInvariant() is not ("sql" or "file" or "script" or "actionmanual");

    private readonly StepRowViewModel _step;
    private readonly List<Action> _cleanup = [];
    private readonly List<Action> _fieldCleanup = [];
    private readonly StackPanel _settings = new() { Spacing = 8 };
    private TextBox? _templateBox;
    private readonly Border _typeCard = new();
    private Control? _templatePanel;
    private readonly TextEditor _editor = new();
    private readonly TermHighlighter _highlighter = new();
    private readonly TextBox _search = new() { Watermark = "Search: term1 & term2 …", MinWidth = 90 };
    private readonly TextBlock _matches = Text("", 11, Muted);
    private readonly TextBlock _title = Text("TEMPLATE", 11, Muted, bold: true);
    private readonly TextBlock _status = Text("", 11, Muted);
    private readonly ComboBox _format = new() { ItemsSource = Formats, MinWidth = 90 };
    private string _loaded = string.Empty;
    private bool _isNew, _loading;

    public StepFormView(StepRowViewModel step)
    {
        _step = step;
        var editor = step.Editor;
        BuildSettings();

        var left = new StackPanel { Spacing = 12 };
        left.Children.Add(CardOf(_settings));
        _typeCard.Child = null;
        left.Children.Add(_typeCard);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(1.25, GridUnitType.Star) { MinWidth = 260 });
        grid.Children.Add(left);
        if (editor is not null)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 250 });
            _templatePanel = BuildTemplatePanel();
            _templatePanel.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(_templatePanel, 1);
            grid.Children.Add(_templatePanel);
        }
        Content = grid;
        UpdateTypeParts();

        NotifyCollectionChangedEventHandler follow = (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Replace) return;
            if (e.NewStartingIndex == _step.CellOf("steptype")) Avalonia.Threading.Dispatcher.UIThread.Post(() => { BuildSettings(); UpdateTypeParts(); });
            else if (e.NewStartingIndex == _step.CellOf("templetsource") && _step.Editor is not null) LoadTemplate();
        };
        _step.Cells.CollectionChanged += follow;
        _cleanup.Add(() => _step.Cells.CollectionChanged -= follow);
        if (editor is not null)
        {
            // saving the workbook also writes a template that was named or edited here but not saved yet
            Action withWorkbook = () =>
            {
                if (_templatePanel?.IsVisible == true && Source.Length > 0 && (_isNew || (_editor.Text ?? string.Empty) != _loaded)) Save();
            };
            editor.SavingWorkbook += withWorkbook;
            _cleanup.Add(() => editor.SavingWorkbook -= withWorkbook);
        }
        DetachedFromVisualTree += (_, _) => { foreach (var undo in _cleanup.Concat(_fieldCleanup)) undo(); _cleanup.Clear(); _fieldCleanup.Clear(); };
    }

    // ---- step settings -------------------------------------------------------------------------

    private void BuildSettings()
    {
        foreach (var undo in _fieldCleanup) undo();
        _fieldCleanup.Clear();
        _settings.Children.Clear();
        var type = _step.TypeText;

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(Text("STEP SETTINGS", 11, Muted, bold: true, vertical: VerticalAlignment.Center));
        if (StepTypeSection.HasSettings(type))
        {
            var wizard = new Button { Content = "Configure…", Classes = { "accent" } };
            ToolTip.SetTip(wizard, "A guided window with just the settings of this step type");
            wizard.Click += (_, _) => StepWizardWindow.Open(this, _step);
            Grid.SetColumn(wizard, 1);
            head.Children.Add(wizard);
        }
        _settings.Children.Add(head);
        _settings.Children.Add(NameField());

        foreach (var title in FieldsFor(type)) _settings.Children.Add(Field(title));
        var shown = new HashSet<string>(FieldsFor(type), StringComparer.OrdinalIgnoreCase);
        var others = (_step.Editor?.StepColumns ?? []).Where(c => !Known.Contains(c.Title) && !shown.Contains(c.Title) && !c.Title.StartsWith("header_", StringComparison.OrdinalIgnoreCase) && !LegacyColumns.IsLegacy(c.Title)).ToList();
        if (others.Count > 0)
        {
            _settings.Children.Add(Text("OTHER COLUMNS", 11, Muted, bold: true));
            foreach (var column in others) _settings.Children.Add(Field(column.Title));
        }
        if (UsesHeaders(type)) _settings.Children.Add(HeadersSection());
        _settings.Children.Add(RawConfig());
        if (_step.Editor is { } owner)
        {
            var data = new Button { Content = "Data columns of this step →", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(data, "Open the Data tab narrowed to this step's request and response columns");
            data.Click += (_, _) => owner.SelectStepNamed(_step.CurrentName, showSettings: false);
            _settings.Children.Add(data);
        }
    }

    private Control NameField()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        row.Children.Add(StepFieldHelp.Labelled(Text("Name", 12, Muted, vertical: VerticalAlignment.Center), StepFieldHelp.TextFor("stepname")));
        var box = new TextBox { Text = _step.Cells[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        var committing = false;
        void Commit()
        {
            if (committing) return;   // Enter and the focus loss that follows must not rename twice
            var value = (box.Text ?? string.Empty).Trim();
            if (value == _step.Cells[0]) { if (box.Text != value) box.Text = value; return; }
            committing = true;
            try { _step.Cells[0] = value; }
            finally { committing = false; }
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } else if (e.Key == Key.Escape) { box.Text = _step.Cells[0]; e.Handled = true; } };
        NotifyCollectionChangedEventHandler follow = (_, e) => { if (e.Action == NotifyCollectionChangedAction.Replace && e.NewStartingIndex == 0 && box.Text != _step.Cells[0]) box.Text = _step.Cells[0]; };
        _step.Cells.CollectionChanged += follow;
        _fieldCleanup.Add(() => _step.Cells.CollectionChanged -= follow);
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        return row;
    }

    /// <summary>One config column by header: a check box, a drop-down, or a text box depending on what the column holds.</summary>
    private Control Field(string title)
    {
        var label = Labels.GetValueOrDefault(title, title.StartsWith("header_", StringComparison.OrdinalIgnoreCase) ? "Header " + title[7..] : title);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        row.Children.Add(StepFieldHelp.Labelled(Text(label, 12, Muted, vertical: VerticalAlignment.Center), StepFieldHelp.TextFor(title)));
        Control input;
        var options = title.Equals("contenttype", StringComparison.OrdinalIgnoreCase) ? ContentTypeChoices : StepOptions.For(title)?.ToArray();
        var current = _step.Value(title);
        if (title.ToLowerInvariant() is "enabled" or "stoponfailure" or "assert" or "assertonlyresponse" or "ignoreempty")
        {
            var check = new CheckBox { IsChecked = !(current.Trim().ToLowerInvariant() is "false" or "0" or "no" or "off") && (current.Length > 0 || title is "enabled" or "stoponfailure" or "assert") };
            check.IsCheckedChanged += (_, _) => _step.Write(title, check.IsChecked == true ? "True" : "False");
            input = check;
        }
        else if (options is not null)
        {
            var choices = options.ToList();
            if (title.Equals("steptype", StringComparison.OrdinalIgnoreCase) && !current.Trim().Equals("ActionManual", StringComparison.OrdinalIgnoreCase)) choices.RemoveAll(c => c.Equals("ActionManual", StringComparison.OrdinalIgnoreCase));
            if (current.Length > 0 && !choices.Contains(current, StringComparer.OrdinalIgnoreCase)) choices.Add(current);
            var box = new ComboBox { ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedItem = choices.FirstOrDefault(c => string.Equals(c, current, StringComparison.OrdinalIgnoreCase)) };
            box.SelectionChanged += (_, _) => { if (box.SelectedItem is string chosen && !string.Equals(chosen, _step.Value(title), StringComparison.Ordinal)) _step.Write(title, chosen); };
            input = box;
            Follow(title, v => { if (v != (box.SelectedItem as string ?? string.Empty)) box.SelectedItem = choices.FirstOrDefault(c => string.Equals(c, v, StringComparison.OrdinalIgnoreCase)); });
        }
        else
        {
            var box = new TextBox { Text = current, HorizontalAlignment = HorizontalAlignment.Stretch };
            void Commit() { var value = box.Text ?? string.Empty; if (value != _step.Value(title)) _step.Write(title, value); }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } else if (e.Key == Key.Escape) { box.Text = _step.Value(title); e.Handled = true; } };
            input = box;
            Follow(title, v => { if (box.Text != v) box.Text = v; });
        }
        if (title.Equals("templetsource", StringComparison.OrdinalIgnoreCase) && _step.Editor is not null && input is TextBox templateBox)
        {
            _templateBox = templateBox;
            var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(browse, "Pick a template from the Templates folder");
            browse.Click += async (_, _) => await BrowseTemplateAsync(templateBox);
            var create = new Button { Content = "New…", Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(create, "Create a new template and choose its name");
            create.Click += async (_, _) => await NewTemplateAsync(templateBox);
            var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            pair.Children.Add(templateBox);
            Grid.SetColumn(browse, 1);
            pair.Children.Add(browse);
            Grid.SetColumn(create, 2);
            pair.Children.Add(create);
            input = pair;
        }
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        if (LoadTestingTool.Editing.StepFieldRules.Covers(title)) AddFieldHint(row, input, title, current);
        return row;
    }

    /// <summary>Marks a numeric or status field that the runner would misread and says in a line what it will do with the value.</summary>
    private void AddFieldHint(Grid row, Control input, string title, string initial)
    {
        row.RowDefinitions = new RowDefinitions("Auto,Auto");
        var hint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 2, 0, 0) };
        Grid.SetRow(hint, 1);
        Grid.SetColumn(hint, 1);
        row.Children.Add(hint);
        void Refresh(string value)
        {
            var check = LoadTestingTool.Editing.StepFieldRules.Check(title, value);
            hint.Text = (check.State == LoadTestingTool.Editing.FieldState.Warning ? "⚠ " : "ⓘ ") + check.Message;
            hint.IsVisible = check.State != LoadTestingTool.Editing.FieldState.Ok;
            hint.Foreground = Brush.Parse(check.State == LoadTestingTool.Editing.FieldState.Warning ? "#F6C76B" : "#8FA3BF");
            if (input is TextBox box)
            {
                if (check.State == LoadTestingTool.Editing.FieldState.Warning) box.BorderBrush = Brush.Parse("#F6C76B");
                else box.ClearValue(TextBox.BorderBrushProperty);
            }
        }
        Refresh(initial);
        if (input is TextBox typed) typed.TextChanged += (_, _) => Refresh(typed.Text ?? string.Empty);
        Follow(title, Refresh);
    }

    /// <summary>Asks for a name (a folder like requests/create.json is fine), points the step at it and opens the editor ready to write it.</summary>
    private async Task NewTemplateAsync(TextBox box)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner || _step.Editor is not { } editor) return;
        var extension = NewTemplatePath()[(NewTemplatePath().LastIndexOf('.'))..];
        var suggestion = Source.Length > 0 ? Source : NewTemplatePath();
        var name = await AskNameAsync(owner, suggestion, extension);
        if (name is null) return;
        try { TemplateEditorWindow.ResolveInside(editor.TemplatesFolder, name); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { SetStatus(ex.Message, true); return; }
        box.Text = name;
        _step.Write("templetsource", name);
        SetStatus(File.Exists(Path.Combine(editor.TemplatesFolder, name)) ? $"{name} already exists and is now this step's template." : $"New template {name}: write it, then Save to create the file.", false, "#39D39B");
    }

    private static async Task<string?> AskNameAsync(Window owner, string suggestion, string extension)
    {
        var input = new TextBox { Text = suggestion, MinWidth = 340 };
        var error = new TextBlock { FontSize = 11, Foreground = Brush.Parse("#FF7A7A") };
        var ok = new Button { Content = "Create", Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        var dialog = new Window
        {
            Title = "New template", SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = Brush.Parse("#0B1626"),
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 8,
                Children =
                {
                    Text("Template name", 14, Brush.Parse("#E3EDF9"), bold: true),
                    Text("Relative to the Templates folder. Use a folder to organise, for example requests/create.json. A missing extension is added.", 11, Muted),
                    input, error,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } }
                }
            }
        };
        string? result = null;
        void Accept()
        {
            var name = (input.Text ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
            if (name.Length == 0 || name.EndsWith('/') || name.Split('/').Any(part => part is "" or "." or ".."))
            { error.Text = "Enter a file name, optionally inside folders."; return; }
            if (name.IndexOfAny(Path.GetInvalidPathChars()) >= 0) { error.Text = "That name has characters a file cannot have."; return; }
            if (!Path.HasExtension(name)) name += extension;
            result = name;
            dialog.Close();
        }
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => dialog.Close();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Opens the picker in the Templates folder; a file from elsewhere is copied into it so the runner can find it.</summary>
    private async Task BrowseTemplateAsync(TextBox box)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || _step.Editor is not { } editor) return;
        try
        {
            var folder = editor.TemplatesFolder;
            Directory.CreateDirectory(folder);
            var start = await top.StorageProvider.TryGetFolderFromPathAsync(folder);
            var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a template", SuggestedStartLocation = start, AllowMultiple = false });
            if (picked.Count == 0 || picked[0].TryGetLocalPath() is not { } path) return;
            var relative = Path.GetRelativePath(folder, path);
            var note = string.Empty;
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                var name = Path.GetFileName(path);
                var target = Path.Combine(folder, name);
                for (var n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}{n}{Path.GetExtension(name)}");
                File.Copy(path, target);
                relative = Path.GetFileName(target);
                note = $"Copied into the Templates folder as {relative}.";
            }
            relative = relative.Replace('\\', '/');
            box.Text = relative;
            _step.Write("templetsource", relative);
            if (note.Length > 0) SetStatus(note, false, "#39D39B");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus(ex.Message, true); }
    }

    /// <summary>The HTTP headers this step sends: those with a value in its row. Empty ones stay hidden until added.</summary>
    private Control HeadersSection()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(Text("HEADERS", 11, Muted, bold: true));
        var headers = (_step.Editor?.StepColumns ?? []).Where(c => c.Title.StartsWith("header_", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var column in headers.Where(c => _step.Value(c.Title).Trim().Length > 0))
        {
            var title = column.Title;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*,Auto") };
            row.Children.Add(Text(title[7..], 12, Muted, vertical: VerticalAlignment.Center));
            var box = new TextBox { Text = _step.Value(title), HorizontalAlignment = HorizontalAlignment.Stretch };
            void Commit()
            {
                var value = box.Text ?? string.Empty;
                if (value == _step.Value(title)) return;
                _step.Write(title, value);
                if (value.Trim().Length == 0) Avalonia.Threading.Dispatcher.UIThread.Post(BuildSettings);
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            Follow(title, v => { if (box.Text != v) box.Text = v; });
            var remove = new Button { Content = "✕", Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(remove, "Remove this header from the step (the value is cleared; the column stays for other steps)");
            remove.Click += (_, _) => { _step.Write(title, string.Empty); Avalonia.Threading.Dispatcher.UIThread.Post(BuildSettings); };
            Grid.SetColumn(box, 1); row.Children.Add(box);
            Grid.SetColumn(remove, 2); row.Children.Add(remove);
            panel.Children.Add(row);
        }
        if (!headers.Any(c => _step.Value(c.Title).Trim().Length > 0)) panel.Children.Add(Text("No headers on this step.", 11, Muted));

        var unused = headers.Where(c => _step.Value(c.Title).Trim().Length == 0).Select(c => c.Title[7..]).ToList();
        var name = new AutoCompleteBox { ItemsSource = unused, MinimumPrefixLength = 0, Watermark = "Header name", HorizontalAlignment = HorizontalAlignment.Stretch };
        name.GotFocus += (_, _) => { if (unused.Count > 0) name.IsDropDownOpen = true; };
        var value = new TextBox { Watermark = "Value", HorizontalAlignment = HorizontalAlignment.Stretch };
        var add = new Button { Content = "＋ Add", Margin = new Thickness(6, 0, 0, 0) };
        var message = Text("", 11, Brush.Parse("#FF7A7A"));
        message.TextWrapping = TextWrapping.Wrap;
        add.Click += (_, _) =>
        {
            var wanted = (name.Text ?? string.Empty).Trim();
            if (wanted.StartsWith("header_", StringComparison.OrdinalIgnoreCase)) wanted = wanted[7..];
            if (wanted.Length == 0 || wanted.Any(char.IsWhiteSpace) || wanted.Contains(':')) { message.Text = "Enter a header name without spaces, for example Authorization."; return; }
            if ((value.Text ?? string.Empty).Trim().Length == 0) { message.Text = "Enter a value: a header without a value is not sent."; return; }
            var existing = headers.FirstOrDefault(c => c.Title[7..].Equals(wanted, StringComparison.OrdinalIgnoreCase))?.Title;
            _step.Write(existing ?? "header_" + wanted, value.Text!);
            Avalonia.Threading.Dispatcher.UIThread.Post(BuildSettings);
        };
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };
        value.Margin = new Thickness(6, 0, 0, 0);
        addRow.Children.Add(name); Grid.SetColumn(value, 1); addRow.Children.Add(value); Grid.SetColumn(add, 2); addRow.Children.Add(add);
        panel.Children.Add(addRow);
        panel.Children.Add(message);
        return panel;
    }

    private void Follow(string title, Action<string> apply)
    {
        NotifyCollectionChangedEventHandler follow = (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Replace && e.NewStartingIndex == _step.CellOf(title)) apply(_step.Value(title));
        };
        _step.Cells.CollectionChanged += follow;
        _fieldCleanup.Add(() => _step.Cells.CollectionChanged -= follow);
    }

    /// <summary>The raw StepConfig text, for anything the structured fields do not cover.</summary>
    private Control RawConfig()
    {
        var box = new TextBox { Text = _step.Value("stepconfig"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 11 };
        box.LostFocus += (_, _) => { if ((box.Text ?? string.Empty) != _step.Value("stepconfig")) _step.Write("stepconfig", box.Text ?? string.Empty); };
        Follow("stepconfig", v => { if (box.Text != v) box.Text = v; });
        return new Expander { Header = "Raw StepConfig", Content = box, Margin = new Thickness(0, 4, 0, 0) };
    }

    /// <summary>Shows the type's own settings card and hides the template editor for types that have no template.</summary>
    private void UpdateTypeParts()
    {
        var type = _step.TypeText;
        var hasSection = StepTypeSection.HasSettings(type) || type.Trim().Equals("ActionManual", StringComparison.OrdinalIgnoreCase);
        _typeCard.IsVisible = hasSection;
        if (hasSection)
        {
            _typeCard.Child = null;
            var body = new StackPanel { Spacing = 8 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Children = { Text($"{type.ToUpperInvariant()} SETTINGS", 11, Muted, bold: true) } };
            if (StepTypeGuide.Icon(type, () => _step.Value("stepconfig"), example => _step.Write("stepconfig", example)) is { } guide) heading.Children.Add(guide);
            body.Children.Add(heading);
            body.Children.Add(new StepTypeSection(_step));
            _typeCard.Child = CardOf(body);
        }
        if (_templatePanel is not null) _templatePanel.IsVisible = UsesTemplate(type);
        if (_step.Editor is not null) LoadTemplate();
    }

    // ---- template panel ------------------------------------------------------------------------

    private Control BuildTemplatePanel()
    {
        _editor.ShowLineNumbers = true;
        _editor.FontFamily = new FontFamily("Consolas,Menlo,monospace");
        _editor.FontSize = 12;
        _editor.Foreground = Brush.Parse("#E3EDF9");
        _editor.Background = Brush.Parse("#08111F");
        _editor.MinHeight = 230;
        _editor.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _editor.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _editor.TextArea.SelectionBrush = Brush.Parse("#C98A1B");
        _editor.TextArea.SelectionForeground = Brushes.Black;
        _editor.TextArea.TextView.LineTransformers.Add(_highlighter);
        _editor.TextChanged += (_, _) => { if (!_loading) { UpdateDirty(); UpdateSearch(); } };
        _editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
        };

        _search.TextChanged += (_, _) => UpdateSearch();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { JumpToNextMatch(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _search.Text = string.Empty; e.Handled = true; }
        };
        var next = new Button { Content = "Next ↓" };
        ToolTip.SetTip(next, "Jump to the next match (Enter)");
        next.Click += (_, _) => JumpToNextMatch();

        var validate = new Button { Content = "Validate" };
        validate.Click += (_, _) => Validate();
        var save = new Button { Content = "Save template", Classes = { "accent" } };
        ToolTip.SetTip(save, "Writes the template file now (Ctrl+S). The workbook is saved separately.");
        save.Click += (_, _) => Save();

        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        searchRow.Children.Add(_search);
        next.Margin = new Thickness(8, 0); Grid.SetColumn(next, 1); searchRow.Children.Add(next);
        Grid.SetColumn(_matches, 2); _matches.VerticalAlignment = VerticalAlignment.Center; searchRow.Children.Add(_matches);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        footer.Children.Add(_format);
        _status.Margin = new Thickness(8, 0); Grid.SetColumn(_status, 1); _status.VerticalAlignment = VerticalAlignment.Center; _status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(_status);
        Grid.SetColumn(validate, 2); footer.Children.Add(validate);
        save.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(save, 3); footer.Children.Add(save);

        var panel = new DockPanel { LastChildFill = true };
        var head = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        head.Children.Add(_title);
        head.Children.Add(searchRow);
        DockPanel.SetDock(head, Dock.Top);
        footer.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(head);
        panel.Children.Add(footer);
        panel.Children.Add(new Border { Child = _editor, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true });

        LoadTemplate();
        return CardOf(panel);
    }

    private string Source => _step.Value("templetsource").Trim();

    private void LoadTemplate()
    {
        _loading = true;
        try
        {
            var source = Source;
            _title.Text = source.Length == 0 ? "TEMPLATE" : $"TEMPLATE · {source}";
            _format.SelectedIndex = TemplateEditorWindow.FormatIndex(source, _step.TypeText);
            _isNew = false;
            if (source.Length == 0)
            {
                _loaded = string.Empty;
                _editor.Text = string.Empty;
                _editor.IsReadOnly = false;
                SetStatus($"No template yet. Write one here and Save to create Templates/{NewTemplatePath()}.", false);
                return;
            }
            _editor.IsReadOnly = false;
            try
            {
                var path = TemplateEditorWindow.ResolveInside(_step.Editor!.TemplatesFolder, source);
                if (File.Exists(path)) { _loaded = File.ReadAllText(path); _editor.Text = _loaded; SetStatus("", false); }
                else
                {
                    _loaded = string.Empty;
                    _isNew = true;
                    _editor.Text = TemplateEditorWindow.DefaultContent(_format.SelectedItem?.ToString() ?? "Plain text");
                    SetStatus("New file: not created yet. Click Save template (or save the workbook) to create it.", false, "#F6C76B");
                }
            }
            catch (Exception ex) { _editor.Text = string.Empty; SetStatus(ex.Message, true); }
        }
        finally { _loading = false; }
        UpdateSearch();
    }

    private void UpdateDirty()
    {
        var changed = (_editor.Text ?? string.Empty) != _loaded;
        if (changed) SetStatus("● Unsaved template changes", false, "#F6C76B");
        else if (Source.Length == 0) SetStatus($"No template yet. Write one here and Save to create Templates/{NewTemplatePath()}.", false);
        else if (_isNew) SetStatus("New file: not created yet. Click Save template (or save the workbook) to create it.", false);
        else SetStatus("", false);
    }

    private void Validate()
    {
        try { TemplateEditorWindow.ValidateContent(_editor.Text ?? string.Empty, _format.SelectedItem?.ToString() ?? "Plain text"); SetStatus("Template is valid.", false, "#39D39B"); }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    /// <summary>Validates and writes the file. The workbook is not touched; a new file only needs its path in the Template field.</summary>
    /// <summary>The file a template with no path gets: the step's name plus an extension for the chosen format.</summary>
    private string NewTemplatePath()
    {
        var extension = (_format.SelectedItem?.ToString() ?? "Plain text") switch { "JSON" => "json", "XML" => "xml", "SQL" => "sql", "GraphQL" => "graphql", "C# script" => "cs", _ => "txt" };
        var name = string.Concat(_step.CurrentName.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) ? '_' : c));
        return $"{(name.Length == 0 ? "template" : name)}.{extension}";
    }

    public bool SaveTemplate() => Save();

    internal bool Save()
    {
        if (_step.Editor is not { } editor) return false;
        try
        {
            // a name typed into the Template field counts even if the field has not lost focus yet
            if (_templateBox is { } typed && (typed.Text ?? string.Empty).Trim() is { } pending && pending != _step.Value("templetsource").Trim())
            {
                var text0 = _editor.Text ?? string.Empty;
                _step.Write("templetsource", pending);
                if (_editor.Text != text0 && text0.Trim().Length > 0) _editor.Text = text0;
            }
            var text = _editor.Text ?? string.Empty;
            if (text.Trim().Length > 0) TemplateEditorWindow.ValidateContent(text, _format.SelectedItem?.ToString() ?? "Plain text"); // an empty file may be created and filled in later
            var source = Source;
            var created = source.Length == 0;
            if (created) source = NewTemplatePath();
            var path = TemplateEditorWindow.ResolveInside(editor.TemplatesFolder, source);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            _loaded = text;
            _isNew = false;
            if (created) _step.Write("templetsource", source); // the cell change reloads this panel from the file just written
            SetStatus(created ? $"Created Templates/{source} and set it as this step's template." : $"Saved {source}", false, "#39D39B");
            editor.RefreshPreview();
            return true;
        }
        catch (Exception ex) { SetStatus(ex.Message, true); return false; }
    }

    public string TemplateText { get => _editor.Text ?? string.Empty; set => _editor.Text = value; }
    public string SearchText { get => _search.Text ?? string.Empty; set => _search.Text = value; }
    public string MatchSummary => _matches.Text ?? string.Empty;
    public IReadOnlyList<string> HighlightedTerms => _highlighter.Terms;
    public string StatusText => _status.Text ?? string.Empty;

    // ---- search --------------------------------------------------------------------------------

    private void UpdateSearch()
    {
        var terms = TemplateSearch.Terms(_search.Text);
        _highlighter.Terms = terms;
        _editor.TextArea.TextView.Redraw();
        var summary = TemplateSearch.Summary(_editor.Text ?? string.Empty, terms);
        _matches.Text = summary;
        _matches.Foreground = summary.Contains("not found") ? Brush.Parse("#F6C76B") : Orange;
    }

    private void JumpToNextMatch()
    {
        var matches = TemplateSearch.Matches(_editor.Text ?? string.Empty, TemplateSearch.Terms(_search.Text));
        if (matches.Count == 0) return;
        var from = _editor.SelectionLength > 0 ? _editor.SelectionStart + 1 : _editor.CaretOffset;
        var target = matches.FirstOrDefault(m => m.Start >= from);
        if (target == default) target = matches[0];
        _editor.Select(target.Start, target.Length);
        _editor.TextArea.Caret.BringCaretToView();
        _editor.ScrollTo(_editor.Document.GetLineByOffset(target.Start).LineNumber, 0);
    }

    private void SetStatus(string text, bool error, string? color = null)
    {
        _status.Text = text;
        _status.FontWeight = error ? FontWeight.SemiBold : FontWeight.Normal;
        _status.FontSize = error ? 12 : 11;
        _status.Foreground = Brush.Parse(color ?? (error ? "#FF7A7A" : "#8EA4BC"));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static Border CardOf(Control child) => new()
    {
        Background = Card,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16, 14),
        Child = child
    };

    private static TextBlock Text(string text, double size, IBrush color, bool bold = false, VerticalAlignment vertical = VerticalAlignment.Top)
        => new() { Text = text, FontSize = size, Foreground = color, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = vertical };

    /// <summary>Paints every occurrence of the search terms orange.</summary>
    private sealed class TermHighlighter : DocumentColorizingTransformer
    {
        public IReadOnlyList<string> Terms { get; set; } = [];

        protected override void ColorizeLine(DocumentLine line)
        {
            if (Terms.Count == 0) return;
            var text = CurrentContext.Document.GetText(line);
            foreach (var term in Terms)
            {
                var at = 0;
                while ((at = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    ChangeLinePart(line.Offset + at, line.Offset + at + term.Length, element =>
                    {
                        element.TextRunProperties.SetBackgroundBrush(Orange);
                        element.TextRunProperties.SetForegroundBrush(Brushes.Black);
                    });
                    at += Math.Max(1, term.Length);
                }
            }
        }
    }
}
