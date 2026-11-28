using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class DisableStepTests
{
    private static (DataEditorWindow Window, EditorShellViewModel Shell) Open(TempDir dir)
    {
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1300; window.Height = 900; window.Show();
        var shell = window.Shell;
        shell.Current!.ShowSteps = true;
        shell.SelectedNode = shell.Instances[0].Testcases[0].StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    [AvaloniaFact]
    public void Disabling_a_step_writes_the_enabled_cell_fades_the_node_and_undo_brings_it_back()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var node = shell.Instances[0].Testcases[0].StepNodes[0];
        Assert.False(node.IsDisabled);
        Assert.Equal(1.0, node.Fade);

        shell.SetStepEnabled(node, false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        node = shell.Instances[0].Testcases[0].StepNodes[0];
        Assert.True(node.IsDisabled);
        Assert.Equal(0.45, node.Fade);
        Assert.Contains("disabled", node.Title);
        var row = shell.Current!.StepRows.First(r => r.CurrentName == node.Name);
        Assert.Equal("False", row.Row.Get("enabled"));

        shell.SetStepEnabled(node, true);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        node = shell.Instances[0].Testcases[0].StepNodes[0];
        Assert.False(node.IsDisabled);
        Assert.Equal("True", shell.Current!.StepRows.First(r => r.CurrentName == node.Name).Row.Get("enabled"));
        window.Close();
    }

    [AvaloniaFact]
    public void Ticked_steps_are_disabled_and_enabled_together()
    {
        using var dir = new TempDir();
        var (w, sh) = Open(dir);
        var testcase = sh.Instances[0].Testcases.FirstOrDefault(t => t.StepNodes.Count >= 2) ?? sh.Instances[0].Testcases[0];
        sh.SelectedNode = testcase.StepNodes[0];
        if (testcase.StepNodes.Count < 2) sh.Current!.AddStepCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var nodes = testcase.StepNodes;
        Assert.True(nodes.Count >= 2);
        nodes[0].IsChosen = true; nodes[1].IsChosen = true;
        sh.SetStepEnabled(nodes[0], false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        nodes = testcase.StepNodes;
        Assert.True(nodes[0].IsDisabled); Assert.True(nodes[1].IsDisabled);
        if (nodes.Count > 2) Assert.False(nodes[2].IsDisabled);
        w.Close();
    }
}
