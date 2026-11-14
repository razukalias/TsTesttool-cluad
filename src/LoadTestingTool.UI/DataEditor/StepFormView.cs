using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
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
        ["templetsource"] = "Template", ["stoponfailure"] = "Stop on failure", ["contenttype"] = "Content type", ["timeout"] = "Timeout (s)",
        ["wait"] = "Wait before (ms)", ["expectedstatus"] = "Expected status", ["assert"] = "Assertions", ["ignoreempty"] = "Ignore empty",
        ["assertonlyresponse"] = "Only response", ["manualtimeoutminutes"] = "Wait limit (min)",
    };

    private static readonly string[] Formats = ["JSON", "XML", "SQL", "GraphQL", "C# script", "Plain text"];
    private static readonly string[] ContentTypeChoices = ["application/json", "application/xml", "text/plain", "application/graphql", "application/x-www-form-urlencoded", "multipart/form-data", "application/octet-stream"];
    private static readonly HashSet<string> Known = new(["steptype", "stage", "enabled", "stoponfailure", "verb", "targeturl", "contenttype", "templetsource", "expectedstatus", "timeout", "wait", "assert", "ignoreempty", "assertonlyresponse", "manualtimeoutminutes", "stepconfig"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The fields each step type uses, in the order they are shown.</summary>
    private static string[] FieldsFor(string type) => type.Trim().ToLowerInvariant() switch
    {
        "graphql" or "caseql" => ["steptype", "stage", "enabled", "stoponfailure", "targeturl", "templetsource", "expectedstatus", "timeout", "wait", "assert", "ignoreempty", "assertonlyresponse"],
        "sql" => ["steptype", "stage", "enabled", "stoponfailure", "templetsource", "timeout", "wait", "assert", "ignoreempty", "assertonlyresponse"],
        "file" => ["steptype", "stage", "enabled", "stoponfailure", "templetsource", "timeout", "wait", "assert", "ignoreempty", "assertonlyresponse"],
        "script" => ["steptype", "stage", "enabled", "stoponfailure", "timeout", "wait"],
        "actionmanual" => ["steptype", "stage", "enabled", "stoponfailure", "manualtimeoutminutes"],
        _ => ["steptype", "stage", "enabled", "stoponfailure", "verb", "targeturl", "contenttype", "templetsource", "expectedstatus", "timeout", "wait", "assert", "ignoreempty", "assertonlyresponse"],
    };

    private static bool UsesTemplate(string type) => type.Trim().ToLowerInvariant() is not ("script" or "actionmanual");

    private readonly StepRowViewModel _step;
    private readonly List<Action> _cleanup = [];
    private readonly List<Action> _fieldCleanup = [];
    private readonly StackPanel _settings = new() { Spacing = 8 };
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
        var others = (_step.Editor?.StepColumns ?? []).Where(c => !Known.Contains(c.Title) && !shown.Contains(c.Title)).ToList();
        if (others.Count > 0)
        {
            _settings.Children.Add(Text("OTHER COLUMNS", 11, Muted, bold: true));
            foreach (var column in others) _settings.Children.Add(Field(column.Title));
        }
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
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        row.Children.Add(Text("Name", 12, Muted, vertical: VerticalAlignment.Center));
        var box = new TextBox { Text = _step.Cells[0], HorizontalAlignment = HorizontalAlignment.Stretch };
        void Commit() { var value = box.Text ?? string.Empty; if (value != _step.Cells[0]) _step.Cells[0] = value; }
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
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        row.Children.Add(Text(label, 12, Muted, vertical: VerticalAlignment.Center));
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
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        return row;
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
            body.Children.Add(Text($"{type.ToUpperInvariant()} SETTINGS", 11, Muted, bold: true));
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
        var save = new Button { Content = "Save", Classes = { "accent" } };
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
                _editor.IsReadOnly = true;
                SetStatus("No template file set. Enter a path in the Template field, for example requests/create.json.", false);
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
                    SetStatus("New file: it will be created when you save.", false);
                }
            }
            catch (Exception ex) { _editor.IsReadOnly = true; _editor.Text = string.Empty; SetStatus(ex.Message, true); }
        }
        finally { _loading = false; }
        UpdateSearch();
    }

    private void UpdateDirty()
    {
        if (_editor.IsReadOnly) return;
        var changed = (_editor.Text ?? string.Empty) != _loaded;
        if (changed) SetStatus("● Unsaved template changes", false, "#F6C76B");
        else if (_isNew) SetStatus("New file: it will be created when you save.", false);
        else SetStatus("", false);
    }

    private void Validate()
    {
        try { TemplateEditorWindow.ValidateContent(_editor.Text ?? string.Empty, _format.SelectedItem?.ToString() ?? "Plain text"); SetStatus("Template is valid.", false, "#39D39B"); }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }

    /// <summary>Validates and writes the file. The workbook is not touched; a new file only needs its path in the Template field.</summary>
    internal bool Save()
    {
        if (_editor.IsReadOnly || _step.Editor is not { } editor) return false;
        try
        {
            var text = _editor.Text ?? string.Empty;
            TemplateEditorWindow.ValidateContent(text, _format.SelectedItem?.ToString() ?? "Plain text");
            var path = TemplateEditorWindow.ResolveInside(editor.TemplatesFolder, Source);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            _loaded = text;
            _isNew = false;
            SetStatus($"Saved {Source}", false, "#39D39B");
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
