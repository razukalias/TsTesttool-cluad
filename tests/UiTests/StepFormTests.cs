using Avalonia.Headless;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class TemplateSearchTests
{
    [Fact]
    public void Ampersand_separates_terms_that_are_trimmed_and_deduplicated()
        => Assert.Equal(["id", "NAME"], TemplateSearch.Terms(" id & NAME & Id &  "));

    [Fact]
    public void Every_term_is_matched_case_insensitively()
    {
        var matches = TemplateSearch.Matches("{\"Id\":1,\"name\":\"x\",\"id2\":2}", TemplateSearch.Terms("id & name"));
        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public void Empty_search_matches_nothing() => Assert.Empty(TemplateSearch.Matches("abc", TemplateSearch.Terms("  ")));
}

public class StepFormTests
{
    private static (DataEditorWindow Window, EditorShellViewModel Shell) Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        var window = DataEditorWindow.Open(path, null);
        window.Width = 1300; window.Height = 800;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, window.Shell);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Choosing_a_testcase_lists_collapsed_steps_and_opening_one_shows_the_form()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        shell.SelectedNode = shell.Instances[0];
        shell.Current!.ShowSteps = true;   // these tests are about Config edit mode
        shell.SelectedNode = shell.Instances[0].Testcases[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = shell.Current!;
        Assert.True(vm.ShowSteps);
        Assert.Null(vm.FocusedStep);
        Assert.NotEmpty(vm.StepRows);
        Assert.Empty(window.GetVisualDescendants().OfType<StepFormView>());
        vm.StepRows[0].IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Single(window.GetVisualDescendants().OfType<StepFormView>());
        vm.CollapseAllStepsCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Null(vm.StepRows[0].ExpandedContent);
        Assert.Empty(window.GetVisualDescendants().OfType<StepFormView>());
        window.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Choosing_a_step_shows_one_form_with_search_and_orange_terms()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var testcase = shell.Instances[0].Testcases[0];
        shell.Current!.ShowSteps = true;   // these tests are about Config edit mode
        shell.SelectedNode = testcase.StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var form = Assert.Single(window.GetVisualDescendants().OfType<StepFormView>());
        form.TemplateText = "{\"id\":1,\"name\":\"a\"}";
        form.SearchText = "id & name";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(["id", "name"], form.HighlightedTerms);
        Assert.NotEmpty(form.MatchSummary);
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "step-form.png"));
        window.Close();
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_long_template_scrolls_inside_its_editor_and_typing_at_the_bottom_keeps_the_caret_and_selection()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var testcase = shell.Instances[0].Testcases[0];
        shell.Current!.ShowSteps = true;
        shell.SelectedNode = testcase.StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var form = Assert.Single(window.GetVisualDescendants().OfType<StepFormView>());
        form.TemplateText = string.Join("\n", Enumerable.Range(1, 300).Select(n => "line " + n + " value"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var editor = form.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First();
        Assert.True(editor.Bounds.Height <= 560, $"the editor must not grow with the text (was {editor.Bounds.Height})");

        editor.CaretOffset = editor.Document.TextLength;
        foreach (var ch in "xyz") { editor.TextArea.PerformTextInput(ch.ToString()); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        window.UpdateLayout();
        Assert.Equal(0, editor.SelectionLength);
        Assert.Equal(editor.Document.TextLength, editor.CaretOffset);
        Assert.EndsWith("xyz", editor.Text);
        window.Close();
    }
}

public class StepFormTabsTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_step_form_has_a_Config_tab_and_a_Template_tab_and_only_one_shows()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var window = DataEditorWindow.Open(path, null);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var shell = window.Shell;
        shell.Current!.ShowSteps = true;
        shell.SelectedNode = shell.Instances[0].Testcases[0].StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var form = window.GetVisualDescendants().OfType<StepFormView>().First();
        var config = form.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => (string?)b.Tag == "step-tab-config");
        var template = form.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => (string?)b.Tag == "step-tab-template");
        Assert.True(template.IsVisible);                       // an HTTP step has a template
        Assert.False(form.ShowingTemplate);                    // the config fields come first
        Assert.True(config.IsChecked);
        var fields = form.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().First(t => t.IsEffectivelyVisible);
        Assert.NotNull(fields);

        form.ShowTab(true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(form.ShowingTemplate);
        Assert.True(template.IsChecked);
        Assert.False(config.IsChecked);
        Assert.Contains(form.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>(), e => e.IsEffectivelyVisible);   // the template editor shows

        form.ShowTab(false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(form.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>(), e => e.IsEffectivelyVisible);
        window.Close();
    }
}
