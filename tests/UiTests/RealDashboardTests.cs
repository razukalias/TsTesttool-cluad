using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class RealDashboardTests
{
    [AvaloniaFact]
    public async Task Real_dashboard_tabs_are_hosted_in_the_editor()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dash_" + Guid.NewGuid().ToString("N")[..8]);
        var inst = System.IO.Path.Combine(root, "Instances", "Demo");
        Directory.CreateDirectory(inst);
        using (var dir = new TempDir())
            File.Copy(Samples.CreateLarge(dir, rows: 3), System.IO.Path.Combine(inst, "DataEngine.xlsx"));
        File.WriteAllText(System.IO.Path.Combine(root, "appsettings.json"), "{\"InstancesRoot\":\"Instances\",\"WorkbookName\":\"DataEngine.xlsx\"}");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            var dashboard = new LoadTestingTool.UI.MainWindow();
            var method = typeof(LoadTestingTool.UI.MainWindow).GetMethod("CreateStandaloneEditor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
            var editor = (DataEditorWindow?)method.Invoke(dashboard, null);
            Assert.NotNull(editor);
            editor!.Show();
            var tabs = editor.FindControl<Border>("RightHost")!.Child is DockPanel framed ? framed.Children.OfType<TabControl>().Single() : null;
            Assert.NotNull(tabs);
            var headers = tabs!.Items.Cast<TabItem>().Select(t => t.Header?.ToString() ?? "?").ToList();
            Assert.Equal("Workbook", headers[0]);
            Assert.Contains("Overview", headers);
            Assert.Contains("Assertions", headers);
            Assert.Contains("Run Summary", headers);
            var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
            Directory.CreateDirectory(shots);
            var shell = editor.Shell;
            var instance = shell.Instances[0];
            await shell.LoadAsync(instance);
            var step = instance.Testcases[0].StepNodes[0];
            shell.SelectedNode = step;           // narrows Overview, Assertions, Artifacts and Run Summary to this step
            tabs.SelectedIndex = headers.IndexOf("Overview");
            editor.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-real-overview.png"));
            tabs.SelectedIndex = headers.IndexOf("Run Summary");
            editor.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-real-summary.png"));
            editor.Close();
        }
        finally { Directory.SetCurrentDirectory(previous); }
    }
}

public class OverviewFollowsEditorSelectionTests
{
    [AvaloniaFact]
    public async Task The_poll_keeps_the_instance_chosen_in_the_editor_instead_of_the_hidden_list_selection()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "follow_" + Guid.NewGuid().ToString("N")[..8]);
        foreach (var name in new[] { "Alpha", "Beta" })
        {
            var inst = System.IO.Path.Combine(root, "Instances", name);
            Directory.CreateDirectory(inst);
            using var dir = new TempDir();
            File.Copy(Samples.CreateLarge(dir, rows: 2), System.IO.Path.Combine(inst, "DataEngine.xlsx"));
        }
        File.WriteAllText(System.IO.Path.Combine(root, "appsettings.json"), "{\"InstancesRoot\":\"Instances\",\"WorkbookName\":\"DataEngine.xlsx\"}");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var dashboard = new LoadTestingTool.UI.MainWindow();
            var editor = (DataEditorWindow)typeof(LoadTestingTool.UI.MainWindow).GetMethod("CreateStandaloneEditor", flags)!.Invoke(dashboard, null)!;
            editor.Show();
            var shell = editor.Shell;
            var beta = shell.Instances.Last();   // not the first one, which the hidden list would select by default
            Assert.True(shell.Instances.Count > 1);
            await shell.LoadAsync(beta);
            shell.SelectedNode = beta.Testcases[0];

            typeof(LoadTestingTool.UI.MainWindow).GetMethod("PollEvents", flags)!.Invoke(dashboard, null);   // what the 500 ms timer does

            var selected = typeof(LoadTestingTool.UI.MainWindow).GetField("_selectedInstance", flags)!.GetValue(dashboard)!;
            Assert.Equal(beta.Name, selected.GetType().GetProperty("Name")!.GetValue(selected));
            editor.Close();
        }
        finally { Directory.SetCurrentDirectory(previous); }
    }
}
