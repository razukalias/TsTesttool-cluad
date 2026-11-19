using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The dialog behind "Script" in the wizard's + menu: pick an existing template and fill its parameters (text, or references such as &lt;From_request_header_r&gt;),
/// or write a new template. Returns the <c>&lt;runscript(…)&gt;</c> part, or null when cancelled.
/// </summary>
public sealed class ScriptPartWindow : Window
{
    private readonly DataEditorViewModel _editor;
    private readonly ComboBox _template = new() { MinWidth = 280, Tag = "script-template" };
    private readonly StackPanel _rows = new() { Spacing = 6, Tag = "script-parameters" };
    private readonly TextBlock _preview = new() { FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12, Foreground = Brush.Parse("#8CC1FF"), TextWrapping = TextWrapping.Wrap, Tag = "script-preview" };
    private readonly TextBlock _note = new() { FontSize = 12, Foreground = Brush.Parse("#F6C76B"), TextWrapping = TextWrapping.Wrap, Tag = "script-note" };
    private readonly List<(TextBox Name, TextBox Value)> _parameters = [];
    private ValuePart? _result;
    private bool _filling;

    public ScriptPartWindow(DataEditorViewModel editor, string? existingToken)
    {
        _editor = editor;
        Title = "Script";
        Width = 720; SizeToContent = SizeToContent.Height; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var edit = new Button { Content = "Edit template…", Tag = "script-edit" };
        var create = new Button { Content = "New template…", Tag = "script-new" };
        edit.Click += async (_, _) => { if (_template.SelectedItem is string name) { await ScriptTemplateWindow.OpenAsync(this, WorkspaceRoot, "Templates", name, RequestNames()); FillParametersFromTemplate(); } };
        create.Click += async (_, _) =>
        {
            var saved = await ScriptTemplateWindow.OpenAsync(this, WorkspaceRoot, "Templates", null, RequestNames());
            LoadTemplates(saved);
            if (saved is not null) FillParametersFromTemplate();
        };
        var add = new Button { Content = "＋ Parameter", Tag = "script-add-parameter" };
        add.Click += (_, _) => AddRow(string.Empty, string.Empty);
        var ok = new Button { Content = "OK", Classes = { "primary" }, MinWidth = 90, IsDefault = true, Tag = "script-ok" };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) => { if (Build() is { } part) { _result = part; Close(); } };
        cancel.Click += (_, _) => Close();
        _template.SelectionChanged += (_, _) => { if (!_filling) FillParametersFromTemplate(); UpdatePreview(); };

        Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children =
        {
            new TextBlock { Text = "Run a script and use what it returns", FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = "The script (a .csx template) gets the parameters below as variables and returns the value for the cell. Parameters can be text or references to other cells.", FontSize = 12, Foreground = Brush.Parse("#8EA4BC"), TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Template", VerticalAlignment = VerticalAlignment.Center }, _template, edit, create } },
            new TextBlock { Text = "Parameters (name and value)", FontWeight = FontWeight.SemiBold },
            _rows, add, _note,
            new TextBlock { Text = "Stored in the cell as", FontSize = 11, Foreground = Brush.Parse("#8EA4BC") }, _preview,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
        } };

        var parsed = existingToken is null ? null : ScriptTemplates.ParseToken(existingToken);
        LoadTemplates(parsed?.Template);
        if (parsed is { } p) { foreach (var (name, value) in p.Parameters) AddRow(name, value); }
        else FillParametersFromTemplate();
        UpdatePreview();
    }

    private string WorkspaceRoot => Path.GetDirectoryName(Path.GetFullPath(_editor.WorkbookPath)) ?? ".";

    private IReadOnlyList<string> RequestNames() => _editor.MenuSources().RequestHeaders;

    private void LoadTemplates(string? select)
    {
        _filling = true;
        var names = ScriptTemplates.List(WorkspaceRoot, "Templates").ToList();
        if (select is not null && !names.Contains(select, StringComparer.OrdinalIgnoreCase)) names.Insert(0, select);   // a token that names a template that is gone is still shown
        _template.ItemsSource = names;
        _template.SelectedItem = select is null ? names.FirstOrDefault() : names.FirstOrDefault(n => n.Equals(select, StringComparison.OrdinalIgnoreCase));
        _filling = false;
        _note.Text = names.Count == 0 ? "There is no script template yet. Press “New template…” to write one." : string.Empty;
        UpdatePreview();
    }

    /// <summary>Adds a row for each variable the chosen template reads and that has no row yet, so the call starts with the right names.</summary>
    private void FillParametersFromTemplate()
    {
        if (_template.SelectedItem is not string name || ScriptTemplates.PathOf(name, WorkspaceRoot, "Templates") is not { } path) { UpdatePreview(); return; }
        var wanted = ScriptTemplates.ParametersUsed(File.ReadAllText(path));
        foreach (var parameter in wanted)
            if (!_parameters.Any(r => string.Equals(r.Name.Text?.Trim(), parameter, StringComparison.OrdinalIgnoreCase))) AddRow(parameter, string.Empty);
        UpdatePreview();
    }

    private void AddRow(string name, string value)
    {
        var nameBox = new TextBox { Text = name, Watermark = "name", Width = 170, Tag = "script-parameter-name" };
        var valueBox = new TextBox { Text = value, Watermark = "value, or a reference like <From_request_header_r>", MinWidth = 300, Tag = "script-parameter-value" };
        var reference = new Button { Content = "＋ From…", Tag = "script-parameter-reference" };
        reference.Flyout = ReferenceFlyout(valueBox);
        var remove = new Button { Content = "✕", Tag = "script-parameter-remove" };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        row.Children.Add(nameBox);
        Grid.SetColumn(valueBox, 1); row.Children.Add(valueBox); valueBox.Margin = new Thickness(6, 0);
        Grid.SetColumn(reference, 2); row.Children.Add(reference);
        Grid.SetColumn(remove, 3); row.Children.Add(remove); remove.Margin = new Thickness(6, 0, 0, 0);
        var pair = (nameBox, valueBox);
        remove.Click += (_, _) => { _rows.Children.Remove(row); _parameters.Remove(pair); UpdatePreview(); };
        nameBox.TextChanged += (_, _) => UpdatePreview();
        valueBox.TextChanged += (_, _) => UpdatePreview();
        _parameters.Add(pair);
        _rows.Children.Add(row);
        UpdatePreview();
    }

    /// <summary>A short list of what a parameter value can refer to: the request values of the row, earlier responses, and generated values.</summary>
    private Flyout ReferenceFlyout(TextBox target)
    {
        var sources = _editor.MenuSources();
        var items = new List<string>();
        items.AddRange(sources.RequestHeaders.Select(h => $"<From_request_{h}_r>"));
        items.AddRange(sources.ResponseFields.Select(f => $"<From_response_{f}_r>"));
        items.AddRange(["<datum>", "<currentdatetime>", "<currenttimestamp>", "<guid>", "<randomnumber>"]);
        var filter = new TextBox { Watermark = "Type to filter…", Tag = "reference-filter" };
        var list = new ListBox { ItemsSource = items, MaxHeight = 240, MinWidth = 340, Tag = "reference-list" };
        filter.TextChanged += (_, _) => list.ItemsSource = items.Where(i => string.IsNullOrWhiteSpace(filter.Text) || i.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var flyout = new Flyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight, Content = new StackPanel { Spacing = 6, Children = { filter, list } } };
        list.DoubleTapped += (_, _) => { if (list.SelectedItem is string token) { target.Text = (target.Text ?? string.Empty) + token; flyout.Hide(); } };
        return flyout;
    }

    private IEnumerable<(string, string)> Pairs() => _parameters.Select(p => ((p.Name.Text ?? string.Empty).Trim(), (p.Value.Text ?? string.Empty).Trim())).Where(p => p.Item1.Length > 0);

    private void UpdatePreview()
    {
        _preview.Text = _template.SelectedItem is string name ? ScriptTemplates.Token(name, Pairs()) : "(choose a template)";
    }

    private ValuePart? Build()
    {
        if (_template.SelectedItem is not string name) { _note.Text = "Choose a template, or write a new one."; return null; }
        var bad = _parameters.FirstOrDefault(p => (p.Name.Text ?? string.Empty).Trim().Length == 0 && (p.Value.Text ?? string.Empty).Trim().Length > 0);
        if (bad.Name is not null) { _note.Text = "A parameter has a value but no name."; return null; }
        var invalid = Pairs().FirstOrDefault(p => p.Item1.Contains(':') || p.Item1.Contains(',') || p.Item1.Contains(' '));
        if (invalid.Item1 is not null) { _note.Text = $"The parameter name '{invalid.Item1}' cannot contain ':', ',' or spaces."; return null; }
        if (Pairs().Any(p => p.Item2.Contains(',') && !p.Item2.Contains('<'))) _note.Text = "Note: a comma inside a plain value ends the parameter; use a reference or a template for values with commas.";
        return ValuePart.Script(name, Pairs());
    }

    public static async Task<ValuePart?> AskAsync(Window owner, DataEditorViewModel editor, string? existingToken)
    {
        var window = new ScriptPartWindow(editor, existingToken);
        await window.ShowDialog(owner);
        return window._result;
    }

    internal ComboBox TemplateBox => _template;
    internal IReadOnlyList<(TextBox Name, TextBox Value)> Parameters => _parameters;
    internal ValuePart? Result => _result;
    internal ValuePart? BuildNow() => Build();
}
