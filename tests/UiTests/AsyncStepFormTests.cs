using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class AsyncStepFormTests
{
    private static (DataEditorWindow Window, EditorShellViewModel Shell) Open(TempDir dir)
    {
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1300; window.Height = 800;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, window.Shell);
    }

    [AvaloniaFact]
    public void Every_step_form_has_a_Run_asynchronously_box_that_writes_the_async_column()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        shell.SelectedNode = shell.Instances[0];
        shell.Current!.ShowSteps = true;
        shell.SelectedNode = shell.Instances[0].Testcases[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = shell.Current!;
        vm.StepRows[0].IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var form = window.GetVisualDescendants().OfType<StepFormView>().Single();
        var label = form.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Run asynchronously");
        var row = label.FindAncestorOfType<Grid>()!;
        var box = row.GetVisualDescendants().OfType<CheckBox>().Single();
        Assert.False(box.IsChecked);                           // the column does not exist yet: off
        box.IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("True", vm.StepRows[0].Value("async"));
        window.Close();
    }

    [Fact]
    public void An_async_step_is_marked_in_the_tree()
    {
        var item = new TestcaseItem(new LoadTestingTool.Editing.TestcaseInfo(1, "t", ["S1"], [new LoadTestingTool.Editing.StepNesting("S1", 0, false, false, "HTTP", true)]), 1);
        Assert.Contains("⇉ async", item.StepNodes[0].Title);
        var plain = new TestcaseItem(new LoadTestingTool.Editing.TestcaseInfo(1, "t", ["S1"], [new LoadTestingTool.Editing.StepNesting("S1", 0, false, false, "HTTP")]), 1);
        Assert.DoesNotContain("async", plain.StepNodes[0].Title);
    }

    [Theory]
    [InlineData("HTTP")] [InlineData("SQL")] [InlineData("Script")] [InlineData("File")] [InlineData("GraphQL")] [InlineData("ActionManual")]
    [InlineData("If")] [InlineData("While")] [InlineData("For")] [InlineData("ForEach")]
    public void Every_step_type_lists_the_async_field(string type)
    {
        var fields = typeof(StepFormView).GetMethod("FieldsFor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var list = (string[])fields.Invoke(null, [type])!;
        Assert.Contains("async", list);
    }
}
