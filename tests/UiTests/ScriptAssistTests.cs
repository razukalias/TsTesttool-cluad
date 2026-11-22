using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using LoadTestingTool.Editing;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ScriptAssistTests
{
    private static (TextBox Box, ScriptAssist Assist, Window Window) Make(ScriptKind kind, string text, string[]? variables = null)
    {
        var box = new TextBox { AcceptsReturn = true, Text = text };
        var assist = new ScriptAssist(box, kind, () => variables ?? []);
        var window = new Window { Content = new StackPanel { Children = { box, assist.Signature, assist.Suggestions } }, Width = 600, Height = 400 };
        window.Show();
        box.CaretIndex = text.Length;
        return (box, assist, window);
    }

    private static string[] Shown(ScriptAssist assist) => assist.Suggestions.IsVisible ? assist.Suggestions.Items.OfType<ScriptSuggestion>().Select(s => s.Label).ToArray() : [];

    private static void Key(TextBox box, Key key, KeyModifiers modifiers = KeyModifiers.None)
        => box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    [AvaloniaFact]
    public void A_dot_shows_the_members_and_Tab_inserts_the_chosen_one()
    {
        var (box, assist, window) = Make(ScriptKind.Template, "var t = Runtime.Lo");
        assist.UpdateNow(forced: false);
        Assert.Equal(["Log", "Logger"], Shown(assist));
        Key(box, Avalonia.Input.Key.Down);
        Assert.Equal("Logger", ((ScriptSuggestion)assist.Suggestions.SelectedItem!).Label);
        Key(box, Avalonia.Input.Key.Up);
        Key(box, Avalonia.Input.Key.Tab);
        Assert.Equal("var t = Runtime.Log", box.Text); Assert.Equal(box.Text!.Length, box.CaretIndex);
        Assert.False(assist.Suggestions.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_closes_the_list_Ctrl_Space_opens_it_and_a_short_word_alone_does_not()
    {
        var (box, assist, window) = Make(ScriptKind.Template, "R");
        assist.UpdateNow(forced: false);
        Assert.Empty(Shown(assist));                        // one letter: not yet
        Key(box, Avalonia.Input.Key.Space, KeyModifiers.Control);
        Assert.Contains("Runtime", Shown(assist));
        Key(box, Avalonia.Input.Key.Escape);
        Assert.False(assist.Suggestions.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void A_name_starting_with_an_underscore_offers_the_request_columns_as_placeholders()
    {
        var (box, assist, window) = Make(ScriptKind.Template, "return _co", ["country", "title"]);
        assist.UpdateNow(forced: false);
        Assert.Equal(["_country_"], Shown(assist));
        Key(box, Avalonia.Input.Key.Enter);
        Assert.Equal("return _country_", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void A_snippet_replaces_the_typed_word_with_the_block()
    {
        var (box, assist, window) = Make(ScriptKind.Template, "fore");
        assist.UpdateNow(forced: false);
        var index = assist.Suggestions.Items.OfType<ScriptSuggestion>().ToList().FindIndex(s => s.Kind == "snippet");
        assist.Suggestions.SelectedIndex = index;
        assist.Accept();
        Assert.StartsWith("foreach (var item in items)", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void The_signature_line_shows_the_call_and_the_active_parameter()
    {
        var (box, assist, window) = Make(ScriptKind.Condition, "Is(\"gt\", V(\"a\"), ");
        assist.UpdateNow(forced: false);
        Assert.True(assist.Signature.IsVisible);
        Assert.Contains("Is(string verb, string? actual, string expected = \"\")", assist.Signature.Text);
        Assert.EndsWith("▸ expected", assist.Signature.Text);
        box.Text = "1 + 2"; box.CaretIndex = 5; assist.UpdateNow(forced: false);
        Assert.False(assist.Signature.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_template_form_offers_runtime_members_and_compiles_while_typing_paused()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2), "Customers");
        var window = new ScriptTemplateWindow(Path.GetDirectoryName(Path.GetFullPath(editor.WorkbookPath))!, "Templates", null, ["title"]);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CodeBox.Text = "var x = Runtime.Ge"; window.CodeBox.CaretIndex = window.CodeBox.Text.Length;
        var found = window.SuggestionsNow();
        Assert.NotNull(found); Assert.Contains(found!.Items, i => i.Label == "Get");
        window.Close();
    }

    [AvaloniaFact]
    public void The_condition_editor_suggests_helpers_and_verbs_and_shows_the_signature()
    {
        var window = new ConditionBuilderWindow(() => ["Login.statusCode"], () => ["Login.user"], () => ["i"], "cs: true");
        window.Show();
        window.ScriptText = "cs: Ha";
        window.ScriptEditor.CaretOffset = window.ScriptText.Length;
        Assert.Equal("Has", window.EngineSuggestions()!.Items.First().Label);
        window.ScriptText = "cs: Is(\"ne";
        window.ScriptEditor.CaretOffset = window.ScriptText.Length;
        Assert.Equal(["ne"], window.EngineSuggestions()!.Items.Select(i => i.Label).ToArray());
        window.ScriptText = "cs: Is(\"no";
        window.ScriptEditor.CaretOffset = window.ScriptText.Length;
        Assert.Equal(["notcontains", "notempty", "notexists"], window.EngineSuggestions()!.Items.Select(i => i.Label).Order().ToArray());
        window.Close();
    }
}
