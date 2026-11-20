using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ControlStepTreeTests
{
    private static (EditorShellViewModel Shell, TestcaseItem Testcase) Open(TempDir dir)
    {
        var shell = new EditorShellViewModel([("Sample", Samples.CreateLarge(dir, rows: 3))]);
        var testcase = shell.Instances[0].Testcases.FirstOrDefault(t => t.StepNodes.Count >= 2) ?? shell.Instances[0].Testcases[0];
        shell.SelectedNode = testcase;
        shell.Current!.ShowSteps = true;
        if (testcase.StepNodes.Count < 2) shell.Current.AddStepCommand.Execute(null);
        Assert.True(testcase.StepNodes.Count >= 2);
        return (shell, testcase);
    }

    [Fact]
    public void A_step_put_inside_a_control_step_is_indented_in_the_tree_and_comes_out_again()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        var editor = shell.Current!;
        var first = testcase.StepNodes[0].Name;
        var second = testcase.StepNodes[1].Name;
        editor.StepRows.First(r => r.CurrentName == first).Write("steptype", "For");
        Assert.True(testcase.StepNodes[0].IsControl);
        Assert.Contains("(For)", testcase.StepNodes[0].Title);

        shell.PutStepInside(testcase.StepNodes.First(n => n.Name == second), first);

        var node = testcase.StepNodes.First(n => n.Name == second);
        Assert.Equal(1, node.Depth);
        Assert.Equal(16, node.Indent.Left);
        Assert.Equal(0, testcase.StepNodes.First(n => n.Name == first).Depth);
        Assert.Same(node, shell.SelectedNode);
        Assert.Contains(editor.Document.Config.Rows, r => r.Get("stepname") == second && r.Get("parent") == first);
        editor.UndoCommand.Execute(null);
        Assert.Equal(0, testcase.StepNodes.First(n => n.Name == second).Depth);

        shell.PutStepInside(testcase.StepNodes.First(n => n.Name == second), first);
        shell.PutStepInside(testcase.StepNodes.First(n => n.Name == second), null, oneLevelOut: true);
        Assert.Equal(0, testcase.StepNodes.First(n => n.Name == second).Depth);
    }

    [Fact]
    public void The_record_view_names_the_control_steps_of_the_testcase()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        Assert.Equal(string.Empty, testcase.ControlSummary);
        var first = testcase.StepNodes[0].Name;
        shell.Current!.StepRows.First(r => r.CurrentName == first).Write("steptype", "For");
        Assert.Contains($"{first} (For)", testcase.ControlSummary);
    }

    [Fact]
    public void A_move_that_is_not_allowed_says_why_and_changes_nothing()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        var editor = shell.Current!;
        var first = testcase.StepNodes[0];
        var second = testcase.StepNodes[1];
        var undoBefore = editor.UndoTip;

        shell.PutStepInside(second, first.Name);   // the first step is plain HTTP, not a control step

        Assert.Equal(0, testcase.StepNodes.First(n => n.Name == second.Name).Depth);
        Assert.Contains("not an If, While, For or ForEach", editor.StatusMessage);
        Assert.Equal(undoBefore, editor.UndoTip);   // nothing was recorded
    }
}

public class ResponseValuePickerTests
{
    [AvaloniaFact]
    public void The_picker_lists_status_codes_and_expected_fields_of_the_other_steps_and_builds_the_reference()
    {
        using var dir = new TempDir();
        var shell = new EditorShellViewModel([("Sample", Samples.CreateLarge(dir, rows: 3))]);
        var testcase = shell.Instances[0].Testcases.FirstOrDefault(t => t.StepNodes.Count >= 2) ?? shell.Instances[0].Testcases[0];
        shell.SelectedNode = testcase;
        var editor = shell.Current!;
        if (testcase.StepNodes.Count < 2) editor.AddStepCommand.Execute(null);
        var first = testcase.StepNodes[0].Name;
        var second = testcase.StepNodes[1].Name;
        var targets = editor.ReferenceTargets(second);
        Assert.Contains($"{first}.statusCode", targets);
        Assert.DoesNotContain($"{second}.statusCode", targets);
        Assert.Equal($"<From_response_{first}.statusCode_r>", ResponseValuePicker.ReferenceFor($"{first}.statusCode"));
        var picker = new ResponseValuePicker(editor, second);
        Assert.Contains($"{first}.statusCode", picker.Shown);
    }
}
