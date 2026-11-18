using System.Reflection;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Xunit;

namespace UiTests;

// Template editor: typing "_" lists the step's variables, typing on narrows the list, Enter inserts _name_.
public class TemplateAutocompleteTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Type EditorType = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.TemplateEditorWindow")!;

    private static (Avalonia.Controls.Window Window, TextEditor Content) Open(string text, Func<IReadOnlyList<string>>? variables)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tpl_" + Guid.NewGuid().ToString("N"));
        var window = (Avalonia.Controls.Window)Activator.CreateInstance(EditorType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance, null,
            [folder, "t.json", "HTTP", (Action<string>)(_ => { }), "inst", "tc", "step", variables], null)!;
        var content = (TextEditor)EditorType.GetField("_content", Flags)!.GetValue(window)!;
        content.Text = text;
        window.Show();
        content.TextArea.Focus();
        content.CaretOffset = text.Length;
        return (window, content);
    }

    private static CompletionWindow? Popup(Avalonia.Controls.Window window) => (CompletionWindow?)EditorType.GetField("_completion", Flags)!.GetValue(window);

    [AvaloniaFact]
    public void Typing_underscore_lists_the_variables_and_typing_on_filters_them()
    {
        var (window, content) = Open("{ \"a\": ", () => ["name", "email", "age"]);
        window.KeyTextInput("_");

        var popup = Popup(window);
        Assert.NotNull(popup);
        Assert.Equal(["_age_", "_email_", "_name_"], popup!.CompletionList.CompletionData.Select(d => d.Text).ToArray());

        window.KeyTextInput("e");
        Assert.Equal(["_email_"], popup.CompletionList.ListBox!.ItemsSource!.Cast<ICompletionData>().Select(d => d.Text).ToArray());
        popup.CompletionList.RequestInsertion(EventArgs.Empty);
        Assert.Equal("{ \"a\": _email_", content.Text);
    }

    [AvaloniaFact]
    public void No_list_in_the_middle_of_a_word_or_without_variables()
    {
        var (window, _) = Open("abc", () => ["name"]);
        window.KeyTextInput("_");
        Assert.Null(Popup(window));

        var (other, _) = Open("x ", null);
        other.KeyTextInput("_");
        Assert.Null(Popup(other));
    }
}
