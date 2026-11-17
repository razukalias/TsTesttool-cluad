using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Record view: the dontrun column gets help text, a step picker and warnings for names that are no step.
public class DontRunRecordTests
{
    private static DataEditorViewModel Open(TempDir dir)
    {
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3), "Customers");
        editor.OpenRecordTabCommand.Execute(null);
        return editor;
    }

    [Fact]
    public void The_dontrun_line_lists_steps_warns_about_names_that_are_no_step_and_about_listing_every_step()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var line = editor.RequestLines.First(l => l.IsDontRun);
        Assert.Empty(line.Warnings);

        line.SetRaw("Typo, CreateCustomer ");
        Assert.Equal(["Typo", "CreateCustomer"], line.ListedSteps);
        Assert.Contains(line.Warnings, w => w.Contains("Typo is not a step") && w.Contains("skips nothing"));
        Assert.Contains(line.Warnings, w => w.Contains("Every step is listed"));

        line.SetListedSteps(["CreateCustomer"]);
        Assert.Equal("CreateCustomer", line.Raw);
        line.SetRaw("");
        Assert.Empty(line.Warnings);
    }

    [AvaloniaFact]
    public void The_dontrun_line_shows_its_help_and_a_choose_steps_button_and_other_lines_do_not()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var dontRun = new RecordLineView(editor.RequestLines.First(l => l.IsDontRun), editor);
        var other = new RecordLineView(editor.RequestLines.First(l => !l.IsDontRun), editor);
        var window = new Window { Content = new StackPanel { Children = { dontRun, other } } };
        window.Show();

        var help = dontRun.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Tag as string == "line-help");
        Assert.Contains("skipped for this row only", help.Text);
        Assert.Single(dontRun.GetVisualDescendants().OfType<Button>(), b => b.Tag as string == "dontrun-steps");
        Assert.DoesNotContain(other.GetVisualDescendants().OfType<Button>(), b => b.Tag as string == "dontrun-steps");
        Assert.DoesNotContain(other.GetVisualDescendants().OfType<TextBlock>(), t => t.Tag as string == "line-help");
        window.Close();
    }
}
