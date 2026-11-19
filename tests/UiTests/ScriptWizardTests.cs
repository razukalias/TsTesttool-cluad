using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LoadTestingTool.Editing;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Inline scripts in the wizard: the Script entries of the + menu, the Script dialog with its parameters, and the template form.
public class ScriptWizardTests
{
    private static DataEditorViewModel Open(TempDir dir) => new(Samples.CreateLarge(dir, rows: 2), "Customers");
    private static string Root(DataEditorViewModel editor) => Path.GetDirectoryName(Path.GetFullPath(editor.WorkbookPath))!;

    [Fact]
    public void The_plus_menu_has_a_Script_folder_with_use_and_new_entries_when_the_hooks_are_given()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var hooks = new RecordMenu.ScriptHooks(_ => { }, _ => { });
        var script = Assert.Single(RecordMenu.Root(editor.MenuSources(), hooks), n => n.Title == "Script");
        Assert.All(script.Children!(), n => Assert.NotNull(n.Open));
        Assert.Equal(2, script.Children!().Count);
        Assert.DoesNotContain(RecordMenu.Root(editor.MenuSources()), n => n.Title == "Script");   // no hooks, no entry
    }

    [AvaloniaFact]
    public void The_script_dialog_lists_templates_starts_the_parameters_from_the_script_and_builds_the_part()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        Directory.CreateDirectory(Path.Combine(Root(editor), "Templates"));
        File.WriteAllText(Path.Combine(Root(editor), "Templates", "mail.csx"), "return _first_ + \".\" + Runtime.Get(\"last\") + \"@x.com\";");
        var window = new ScriptPartWindow(editor, null);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("mail", (IEnumerable<string>)window.TemplateBox.ItemsSource!);
        Assert.Equal("mail", window.TemplateBox.SelectedItem);
        Assert.Equal(["first", "last"], window.Parameters.Select(p => p.Name.Text!).OrderBy(n => n));   // read from the script
        window.Parameters.First(p => p.Name.Text == "first").Value.Text = "Ann";
        window.Parameters.First(p => p.Name.Text == "last").Value.Text = "<From_request_title_r>";
        var part = window.BuildNow()!;
        Assert.Equal(ValuePartKind.Script, part.Kind);
        Assert.Equal("<runscript(template:mail, first:Ann, last:<From_request_title_r>)>", part.Raw);
        window.Close();
    }

    [AvaloniaFact]
    public void An_existing_token_opens_with_its_template_and_values()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        Directory.CreateDirectory(Path.Combine(Root(editor), "Templates"));
        File.WriteAllText(Path.Combine(Root(editor), "Templates", "s.csx"), "return \"1\";");
        var window = new ScriptPartWindow(editor, "<runscript(template:s, env:staging)>");
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("s", window.TemplateBox.SelectedItem);
        var row = Assert.Single(window.Parameters);
        Assert.Equal(("env", "staging"), (row.Name.Text, row.Value.Text));
        window.Close();
    }

    [AvaloniaFact]
    public void The_template_form_validates_without_running_and_saves_into_the_Templates_folder()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var window = new ScriptTemplateWindow(Root(editor), "Templates", null, ["title", "country"]);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.NameBox.Text = "env_status";
        window.CodeBox.Text = "return \"200;";                      // broken on purpose
        Assert.False(window.Validate());
        window.SaveNow();
        var path = Path.Combine(Root(editor), "Templates", "env_status.csx");
        Assert.True(File.Exists(path));                              // saved even so; the form says it does not compile yet
        Assert.Equal("env_status", window.SavedName);
        window.CodeBox.Text = "return _title_;";
        Assert.True(window.Validate());
        window.Close();
    }

    [AvaloniaFact]
    public void The_template_form_refuses_a_name_that_is_taken_or_not_a_file_name()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        Directory.CreateDirectory(Path.Combine(Root(editor), "Templates"));
        File.WriteAllText(Path.Combine(Root(editor), "Templates", "taken.csx"), "return 1;");
        var window = new ScriptTemplateWindow(Root(editor), "Templates", null, []);
        window.Show();
        window.NameBox.Text = "taken"; window.SaveNow();
        Assert.Null(window.SavedName);
        window.NameBox.Text = "../evil"; window.SaveNow();
        Assert.Null(window.SavedName);
        Assert.False(File.Exists(Path.Combine(Root(editor), "evil.csx")));
        window.Close();
    }
}
