using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Config edit mode: the buttons live on each step's card; Table view: the sheet buttons sit in the tab bar.
public class StepCardButtonsTests
{
    private static (DataEditorWindow Window, DataEditorViewModel Editor) Open(TempDir dir, int mode)
    {
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1400; window.Height = 900;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.SelectedTestcase = editor.Testcases[0];
        if (editor.StepRows.Count < 2) editor.AddStepCommand.Execute(null);
        editor.ViewModeIndex = mode;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, editor);
    }

    private static IEnumerable<Button> Visible(Control root) => root.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible);

    [AvaloniaFact]
    public void Config_mode_toolbar_no_longer_has_the_step_buttons()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir, 1);
        editor.ExpandAllStepsCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var texts = Visible(window).Select(b => b.Content as string ?? "").ToList();
        foreach (var gone in new[] { "Add step", "Move step up", "Move step down", "Remove step", "Find & replace", "Show its columns", "Table view" })
            Assert.DoesNotContain(gone, texts);
        window.Close();
    }

    [AvaloniaFact]
    public void Each_card_has_up_down_duplicate_remove_and_they_act_on_that_card()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(editor.StepRows.Count >= 2);
        var ups = Visible(window).Where(b => Equals(b.Tag, "step-up")).ToList();
        Assert.Equal(editor.StepRows.Count, ups.Count);
        Assert.Equal(editor.StepRows.Count, Visible(window).Count(b => Equals(b.Tag, "step-duplicate")));
        Assert.Equal(editor.StepRows.Count, Visible(window).Count(b => Equals(b.Tag, "step-remove")));

        var second = editor.StepRows[1].CurrentName;
        var first = editor.StepRows[0].CurrentName;
        editor.StepRows[1].MoveUpCommand.Execute(null);        // not the selected step: the card's own step moves
        Assert.Equal(second, editor.StepRows[0].CurrentName);
        Assert.Equal(first, editor.StepRows[1].CurrentName);

        var count = editor.StepRows.Count;
        editor.StepRows[0].DuplicateCommand.Execute(null);      // opens the same panel as the tree menu
        Assert.True(editor.TestcasePanelVisible);
        Assert.StartsWith("DUPLICATE STEP", editor.TestcasePanelTitle);

        var name = editor.StepRows[1].CurrentName;
        editor.StepRows[1].RemoveCommand.Execute(null);
        Assert.Equal(count - 1, editor.StepRows.Count);
        Assert.DoesNotContain(editor.StepRows, r => r.CurrentName == name);
        window.Close();
    }

    [AvaloniaFact]
    public void The_card_shows_the_step_type_icon_and_it_follows_the_type()
    {
        using var dir = new TempDir();
        var (_, editor) = Open(dir, 1);
        var step = editor.StepRows[0];
        Assert.Equal(TreeIcons.ForStep(step.TypeText), step.IconData);
    }

    [AvaloniaFact]
    public void Table_view_has_the_sheet_buttons_in_the_tab_bar_and_not_in_the_toolbar()
    {
        using var dir = new TempDir();
        var (window, _) = Open(dir, 0);
        var bar = window.GetVisualDescendants().OfType<StackPanel>().Single(p => Equals(p.Tag, "sheet-tab-bar"));
        var inBar = bar.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Contains(inBar, b => Equals(b.Tag, "add-sheet"));
        Assert.Contains(inBar, b => b.Name == "SheetFilterButton");
        var texts = Visible(window).Select(b => b.Content as string ?? "").ToList();
        Assert.DoesNotContain("＋ Sheet", texts);
        window.Close();
    }
}

public class StepCardScreenshots
{
    [AvaloniaFact]
    public void Screenshots()
    {
        var outDir = Environment.GetEnvironmentVariable("SHOT_DIR");
        if (string.IsNullOrEmpty(outDir)) return;
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1400; window.Height = 900; window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.SelectedTestcase = editor.Testcases[0];
        if (editor.StepRows.Count < 2) editor.AddStepCommand.Execute(null);
        editor.ViewModeIndex = 0; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(outDir, "table.png"));
        editor.ViewModeIndex = 1; editor.StepRows[0].IsExpanded = false; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(outDir, "config.png"));
        window.Close();
    }
}
