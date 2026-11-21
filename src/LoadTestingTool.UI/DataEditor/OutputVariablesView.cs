using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// "Save values as variables": rows of name + source on a step. The source is a field of the step's own response (items[0].id), its status code or a
/// header; after the step runs the value is a variable <c>_name_</c> (also <c>&lt;From_response_name_r&gt;</c>) for conditions, loops and later steps,
/// without naming the step. Stored as <c>outputs=name=source,name=source</c> in the StepConfig.
/// </summary>
public sealed class OutputVariablesView : StackPanel
{
    private readonly StepRowViewModel _step;
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly TextBlock _message = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#F6C76B"), IsVisible = false };
    private readonly List<(TextBox Name, TextBox Source)> _fields = [];
    private bool _loading;

    public int RowCount => _fields.Count;

    public OutputVariablesView(StepRowViewModel step)
    {
        _step = step;
        Spacing = 4;
        Margin = new Thickness(0, 6, 0, 0);
        Tag = "output-variables";
        var add = new Button { Content = "＋ Add output variable", Tag = "output-add", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => { AddRow(string.Empty, string.Empty); };
        Children.Add(new TextBlock { Text = "SAVE VALUES AS VARIABLES", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#8EA4BC") });
        Children.Add(new TextBlock { Text = "After this step runs, each value is a variable _name_ for conditions, loops and later steps. The source is a response field such as items[0].id, statusCode or header.Location.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#8EA4BC") });
        Children.Add(_rows);
        Children.Add(add);
        Children.Add(_message);
        Load();
        NotifyCollectionChangedEventHandler follow = (_, e) =>
        {
            if (!_loading && e.Action == NotifyCollectionChangedAction.Replace && e.NewStartingIndex == _step.CellOf("stepconfig") && Text() != StepOutputs.Parse(_step.Value("stepconfig")).Format()) Load();
        };
        _step.Cells.CollectionChanged += follow;
        DetachedFromVisualTree += (_, _) => _step.Cells.CollectionChanged -= follow;
    }

    private string Text() => string.Join(",", _fields.Select(f => ((f.Name.Text ?? string.Empty).Trim(), (f.Source.Text ?? string.Empty).Trim())).Where(p => p.Item1.Length > 0 || p.Item2.Length > 0).Select(p => $"{p.Item1}={p.Item2}"));

    private void Load()
    {
        _loading = true;
        _rows.Children.Clear();
        _fields.Clear();
        foreach (var (name, source) in StepOutputs.Parse(_step.Value("stepconfig"))) AddRow(name, source);
        _loading = false;
        Check();
    }

    /// <summary>Adds a row (the ＋ button does this).</summary>
    public void AddRow(string name, string source)
    {
        var nameBox = new TextBox { Text = name, Watermark = "name", Width = 150, Tag = "output-name" };
        var sourceBox = new TextBox { Text = source, Watermark = "response field, statusCode or header.Name", Tag = "output-source" };
        var pick = new Button { Content = "⋯", Tag = "output-pick", Padding = new Thickness(8, 2), Margin = new Thickness(4, 0, 0, 0) };
        ToolTip.SetTip(pick, "Choose a field of this step's response");
        var remove = new Button { Content = "✕", Tag = "output-remove", Padding = new Thickness(6, 2), Margin = new Thickness(4, 0, 0, 0) };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto") };
        var eq = new TextBlock { Text = "=", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        Control[] parts = [nameBox, eq, sourceBox, pick, remove];
        for (var i = 0; i < parts.Length; i++) { Grid.SetColumn(parts[i], i); row.Children.Add(parts[i]); }
        _rows.Children.Add(row);
        _fields.Add((nameBox, sourceBox));
        // written when a box is left (adding the StepConfig column rebuilds the form, which must not happen in the middle of typing)
        nameBox.LostFocus += (_, _) => Commit();
        sourceBox.LostFocus += (_, _) => Commit();
        nameBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Check(); };
        sourceBox.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Check(); };
        remove.Click += (_, _) => { _rows.Children.Remove(row); _fields.RemoveAll(f => ReferenceEquals(f.Name, nameBox)); Commit(); };
        pick.Click += async (_, _) =>
        {
            if (_step.Editor is not { } editor || TopLevel.GetTopLevel(this) is not Window owner) return;
            var own = editor.ReferenceTargets(null).Where(t => t.StartsWith(_step.CurrentName + ".", StringComparison.OrdinalIgnoreCase)).Select(t => t[(_step.CurrentName.Length + 1)..]).ToList();
            var chosen = await new ResponseValuePicker(own, t => t.StartsWith(AssertionEngine.ResponseHeaderPrefix, StringComparison.OrdinalIgnoreCase) ? "header." + t[AssertionEngine.ResponseHeaderPrefix.Length..] : t, "Field of this step's response", "Fields come from the expected columns and the last stored response of this step.").ShowDialog<string?>(owner);
            if (chosen is null) return;
            sourceBox.Text = chosen;
            if (string.IsNullOrWhiteSpace(nameBox.Text)) nameBox.Text = SuggestName(chosen);
        };
        if (!_loading) Check();
    }

    private static string SuggestName(string source)
    {
        var last = source.Split('.', '[', ']').LastOrDefault(p => p.Length > 0) ?? "value";
        var letters = new string(last.Where(char.IsLetterOrDigit).ToArray());
        return letters.Length == 0 || !char.IsLetter(letters[0]) ? "value" : letters;
    }

    /// <summary>Writes the rows into the step's StepConfig now (this happens by itself when a box is left).</summary>
    public void Apply() => Commit();

    private void Commit()
    {
        if (_loading) return;
        var written = StepOutputs.Parse(_step.Value("stepconfig")).Format();
        var text = Text();
        Check();
        if (text == written) return;
        _loading = true;
        try { _step.Write("stepconfig", ControlStepSettings.WithKey(_step.Value("stepconfig"), "outputs", text)); }
        finally { _loading = false; }
    }

    private void Check()
    {
        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, s) in _fields.Select(f => ((f.Name.Text ?? string.Empty).Trim(), (f.Source.Text ?? string.Empty).Trim())))
        {
            if (n.Length == 0 && s.Length == 0) continue;
            if (StepOutputs.Check(n, s) is { } problem) problems.Add(problem);
            else if (!names.Add(n)) problems.Add($"'{n}' is saved twice; the later one wins.");
        }
        _message.Text = string.Join("\n", problems);
        _message.IsVisible = problems.Count > 0;
    }

    /// <summary>The problems shown under the rows (empty when all is well).</summary>
    public string Problems => _message.IsVisible ? _message.Text ?? string.Empty : string.Empty;
}
