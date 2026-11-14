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
}
