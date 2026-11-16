using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class FieldValidationTests
{
    private static IEnumerable<string> Texts(Control root) => root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "");

    [AvaloniaFact]
    public void The_step_form_hints_what_a_wait_or_a_bad_status_will_do()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1300; window.Height = 900;
        window.Show();
        var shell = window.Shell;
        shell.Current!.ShowSteps = true;   // these tests are about Config edit mode
        shell.SelectedNode = shell.Instances[0].Testcases[0].StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var step = shell.Current!.FocusedStep!;

        step.Write("wait", "Please approve");
        step.Write("expectedstatus", "20O");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var texts = Texts(window).ToList();
        Assert.Contains(texts, t => t.Contains("Pauses the run for a person") && t.Contains("Please approve"));
        Assert.Contains(texts, t => t.StartsWith("⚠") && t.Contains("expectedstatus '20O'"));

        step.Write("wait", "250");
        step.Write("expectedstatus", "2xx");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        texts = Texts(window).ToList();
        Assert.Contains(texts, t => t.Contains("Waits 250 ms"));
        Assert.DoesNotContain(texts, t => t.Contains("expectedstatus '"));
        window.Close();
    }

    [AvaloniaFact]
    public void Screenshots_of_the_form_hint_and_the_table_mark()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1500; window.Height = 900;
        window.Show();
        var shell = window.Shell;
        shell.Current!.ShowSteps = true;   // these tests are about Config edit mode
        shell.SelectedNode = shell.Instances[0].Testcases[0].StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var editor = shell.Current!;
        editor.FocusedStep!.Write("wait", "Please approve");
        editor.FocusedStep!.Write("expectedstatus", "20O");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "field-hint-form.png"));
        editor.ShowStepTable = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "field-hint-table.png"));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "⚠");
        window.Close();
    }
}
