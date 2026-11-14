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
        ["stepname"] = "Name", ["steptype"] = "Type", ["stage"] = "Stage", ["enabled"] = "Enabled", ["verb"] = "Verb",
        ["targeturl"] = "Target URL", ["templetsource"] = "Template", ["stoponfailure"] = "Stop on failure",
    };

    private static readonly string[] Formats = ["JSON", "XML", "SQL", "GraphQL", "C# script", "Plain text"];

    private readonly StepRowViewModel _step;
    private readonly List<Action> _cleanup = [];
    private readonly int _templateCell;
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
        var columns = editor?.StepColumns ?? [];
        _templateCell = columns.FirstOrDefault(c => c.Title.Equals("templetsource", StringComparison.OrdinalIgnoreCase))?.CellIndex ?? -1;

        var fields = new StackPanel { Spacing = 8 };
        fields.Children.Add(Text("STEP SETTINGS", 11, Muted, bold: true));
        fields.Children.Add(Field("Name", 0, null));
        foreach (var column in columns) fields.Children.Add(Field(Labels.GetValueOrDefault(column.Title, column.Title), column.CellIndex, StepOptions.For(column.Title)));
        if (editor is not null)
        {
            var data = new Button { Content = "Data columns of this step →", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(data, "Open the Data tab narrowed to this step's request and response columns");
            data.Click += (_, _) => editor.SelectStepNamed(step.CurrentName, showSettings: false);
            fields.Children.Add(data);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 230 });
        grid.Children.Add(CardOf(fields));
        if (_templateCell >= 0 && editor is not null)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1.3, GridUnitType.Star) { MinWidth = 250 });
            var template = BuildTemplatePanel();
            template.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(template, 1);
            grid.Children.Add(template);
        }
        Content = grid;
        DetachedFromVisualTree += (_, _) => { foreach (var undo in _cleanup) undo(); _cleanup.Clear(); };
    }

    // ---- step settings -------------------------------------------------------------------------

    private Control Field(string label, int cell, IReadOnlyList<string>? options)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        row.Children.Add(Text(label, 12, Muted, vertical: VerticalAlignment.Center));
        Control input;
        if (options is not null)
        {
            var box = new ComboBox { ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch };
            box.Bind(ComboBox.SelectedItemProperty, new Binding($"Cells[{cell}]") { Source = _step, Mode = BindingMode.TwoWay });
            input = box;
        }
        else
        {
            var box = new TextBox { Text = _step.Cells[cell], HorizontalAlignment = HorizontalAlignment.Stretch };
            void Commit()
            {
                var value = box.Text ?? string.Empty;
                if (value != _step.Cells[cell]) _step.Cells[cell] = value;
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
                else if (e.Key == Key.Escape) { box.Text = _step.Cells[cell]; e.Handled = true; }
            };
            NotifyCollectionChangedEventHandler follow = (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Replace && e.NewStartingIndex == cell && box.Text != _step.Cells[cell]) box.Text = _step.Cells[cell];
                if (e.Action == NotifyCollectionChangedAction.Replace && e.NewStartingIndex == _templateCell && cell == _templateCell) LoadTemplate();
            };
            _step.Cells.CollectionChanged += follow;
            _cleanup.Add(() => _step.Cells.CollectionChanged -= follow);
            input = box;
        }
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        return row;
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

    private string Source => _templateCell < 0 ? string.Empty : (_step.Cells[_templateCell] ?? string.Empty).Trim();

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
