using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// C# completion for a plain text box that holds a script: a list of suggestions (after a dot, while typing a name, inside <c>Is("</c>), the signature of the call the caret is in,
/// and a callback when typing paused. Up / Down / Tab / Enter / Esc work while the list is open; Ctrl+Space opens it on demand. The list and the signature line are controls the caller places.
/// </summary>
public sealed class ScriptAssist
{
    private readonly TextBox _box;
    private readonly ScriptKind _kind;
    private readonly Func<IReadOnlyList<string>> _variables;
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private int _version;
    private int _start;
    private bool _inserting;

    public ListBox Suggestions { get; }
    public TextBlock Signature { get; } = new() { FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), FontFamily = new FontFamily("Consolas,Menlo,monospace"), TextWrapping = TextWrapping.Wrap, IsVisible = false, Tag = "script-signature" };

    /// <summary>Raised when typing paused for a moment (the caller can compile and show errors).</summary>
    public event Action? Idle;

    public ScriptAssist(TextBox box, ScriptKind kind, Func<IReadOnlyList<string>>? variables = null, string suggestionsTag = "script-suggestions")
    {
        _box = box; _kind = kind; _variables = variables ?? (() => []);
        Suggestions = new ListBox { MaxHeight = 190, MinWidth = 380, Tag = suggestionsTag, IsVisible = false, ItemTemplate = new FuncDataTemplate<ScriptSuggestion>((item, _) => item is null ? null : Line(item)) };
        Suggestions.DoubleTapped += (_, _) => Accept();
        box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        box.TextChanged += (_, _) => { if (_inserting) return; _idle.Stop(); _idle.Start(); _ = RefreshAsync(forced: false); };
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.CaretIndexProperty && !_inserting) UpdateSignature(); };
        box.LostFocus += (_, _) => { if (!Suggestions.IsPointerOver) Suggestions.IsVisible = false; };
        _idle.Tick += (_, _) => { _idle.Stop(); Idle?.Invoke(); };
        Task.Run(ScriptCompletion.WarmUp);
    }

    private static Control Line(ScriptSuggestion item) => new StackPanel
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10,
        Children =
        {
            new TextBlock { Text = Icon(item.Kind), Width = 14, Foreground = Brush.Parse("#8CC1FF") },
            new TextBlock { Text = item.Label, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontWeight = FontWeight.SemiBold },
            new TextBlock { Text = item.Detail, FontSize = 11, Foreground = Brush.Parse("#8EA4BC"), MaxWidth = 520, TextTrimming = TextTrimming.CharacterEllipsis },
        },
    };

    private static string Icon(string kind) => kind switch { "method" => "ƒ", "property" => "◆", "field" => "◇", "local" => "x", "type" => "T", "namespace" => "{}", "keyword" => "k", "snippet" => "✂", "verb" => "=", "variable" => "_", _ => "·" };

    /// <summary>Computes the suggestions for the caret now (the typing path does this in the background).</summary>
    public void UpdateNow(bool forced)
    {
        var text = _box.Text ?? string.Empty;
        Show(Compute(text, _box.CaretIndex, forced), text);
        UpdateSignature();
    }

    private ScriptSuggestions? Compute(string text, int caret, bool forced)
    {
        var found = ScriptCompletion.Suggest(_kind, text, caret, forced, _variables());
        // request columns as _name_: not C# symbols, so they are added by hand while a name starting with "_" is typed
        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        var word = text[start..caret];
        if (_kind == ScriptKind.Template && word.StartsWith('_') && word.Length >= 1 && (start == 0 || text[start - 1] != '.'))
        {
            var items = _variables().Where(v => $"_{v}_".StartsWith(word, StringComparison.OrdinalIgnoreCase)).Select(v => new ScriptSuggestion($"_{v}_", $"_{v}_", $"the value of {v} (a parameter of the call)", "variable")).ToList();
            if (items.Count > 0) return new ScriptSuggestions(start, items);
        }
        return found;
    }

    private async Task RefreshAsync(bool forced)
    {
        var version = ++_version;
        var text = _box.Text ?? string.Empty; var caret = _box.CaretIndex;
        var found = await Task.Run(() => Compute(text, caret, forced));
        if (version != _version) return;   // typed on meanwhile
        Show(found, text);
        UpdateSignature();
    }

    private void Show(ScriptSuggestions? found, string text)
    {
        if (found is null || found.Items.Count == 0) { Suggestions.IsVisible = false; return; }
        _start = found.Start;
        Suggestions.ItemsSource = found.Items;
        Suggestions.SelectedIndex = 0;
        Suggestions.IsVisible = true;
    }

    private void UpdateSignature()
    {
        var sig = ScriptCompletion.Signature(_kind, _box.Text ?? string.Empty, _box.CaretIndex);
        Signature.IsVisible = sig is not null;
        Signature.Text = sig is null ? string.Empty : sig.Text + (sig.Parameters.Count > 0 ? $"   ▸ {sig.Parameters[Math.Min(sig.Active, sig.Parameters.Count - 1)]}" : "");
    }

    /// <summary>Replaces what was typed of the name with the chosen suggestion.</summary>
    public void Accept()
    {
        if (Suggestions.SelectedItem is not ScriptSuggestion item) return;
        var text = _box.Text ?? string.Empty;
        var caret = Math.Min(_box.CaretIndex, text.Length);
        var start = Math.Clamp(_start, 0, caret);
        _inserting = true;
        try
        {
            _box.Text = text[..start] + item.Insert + text[caret..];
            _box.CaretIndex = start + item.Insert.Length;
        }
        finally { _inserting = false; }
        Suggestions.IsVisible = false;
        _version++;
        UpdateSignature();
        _box.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { UpdateNow(forced: true); e.Handled = true; return; }
        if (!Suggestions.IsVisible) return;
        switch (e.Key)
        {
            case Key.Down: Suggestions.SelectedIndex = Math.Min(Suggestions.ItemCount - 1, Suggestions.SelectedIndex + 1); e.Handled = true; break;
            case Key.Up: Suggestions.SelectedIndex = Math.Max(0, Suggestions.SelectedIndex - 1); e.Handled = true; break;
            case Key.Tab or Key.Enter: Accept(); e.Handled = true; break;
            case Key.Escape: Suggestions.IsVisible = false; e.Handled = true; break;
        }
    }
}
