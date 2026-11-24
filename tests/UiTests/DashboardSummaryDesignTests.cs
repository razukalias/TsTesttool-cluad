using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Avalonia.Headless;
using Xunit;
namespace UiTests;
public class DashboardSummaryDesignTests
{
    [AvaloniaFact]
    public async Task Overview_and_Run_Summary_show_the_figures_as_tiles_a_bar_and_a_result_table()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "probe_" + Guid.NewGuid().ToString("N")[..8]);
        var inst = System.IO.Path.Combine(root, "Instances", "Demo"); Directory.CreateDirectory(inst);
        using (var dir = new TempDir()) File.Copy(Samples.CreateLarge(dir, rows: 3), System.IO.Path.Combine(inst, "DataEngine.xlsx"));
        File.WriteAllText(System.IO.Path.Combine(root, "appsettings.json"), "{\"InstancesRoot\":\"Instances\",\"WorkbookName\":\"DataEngine.xlsx\"}");
        var prev = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(root);
            var d = new LoadTestingTool.UI.MainWindow();
            var m = typeof(LoadTestingTool.UI.MainWindow).GetMethod("CreateStandaloneEditor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
            var editor = (DataEditorWindow)m.Invoke(d, null)!; editor.Height = 1500; editor.Show();
            var tabs = ((DockPanel)editor.FindControl<Border>("RightHost")!.Child!).Children.OfType<TabControl>().Single();
            await editor.Shell.LoadAsync(editor.Shell.Instances[0]);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var wt = typeof(LoadTestingTool.UI.MainWindow);
            editor.Shell.SelectedNode = editor.Shell.Instances[0];
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var sel = (LoadTestingTool.UI.InstanceInfo)wt.GetField("_selectedInstance", flags)!.GetValue(d)!;
            sel.Requests.Clear(); sel.Assertions.Clear();
            string[] envs = ["dev", "test", "prod"];
            for (var i = 0; i < 40; i++)
            {
                var env = envs[i % 3]; var res = i % 7 == 0 ? "FAIL" : "PASS"; var tc = i % 2 == 0 ? "OrderBlank" : "OrderAsserted";
                sel.Requests.Add(new LoadTestingTool.UI.DashboardRequest("GetOrder", "HTTP", tc, "B" + (i % 5 + 1), env, res, 40 + i * 7, res == "FAIL" ? "Assertion" : "None", res == "FAIL" ? "orderid expected O1 got O2" : ""));
                sel.Assertions.Add(new LoadTestingTool.UI.AssertionInfo { Testcase = tc, Environment = env, DataId = "B" + (i % 5 + 1), StepName = "GetOrder", ResponsePath = "orderid", AssertionVerb = "equals", ExpectedValue = "O1", ActualValue = res == "PASS" ? "O1" : "O2", Result = res });
            }
            sel.Status = "Failed"; sel.StartedAt = DateTimeOffset.Now.AddSeconds(-83); sel.CompletedAt = DateTimeOffset.Now;
            sel.CompletedRequests = 40; sel.AverageMs = 171.4; sel.P95Ms = 310; sel.RequestsPerSecond = 0.5;
            wt.GetMethod("RefreshDashboardDetails", flags)!.Invoke(d, [true]);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
            Directory.CreateDirectory(shots);
            tabs.SelectedIndex = 1; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            editor.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "dashboard-overview-run.png"));
            tabs.SelectedIndex = 3; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            editor.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "dashboard-run-summary.png"));
            string Texts(int tab) { tabs.SelectedIndex = tab; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return string.Join("|", tabs.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text)); }
            var overview = Texts(1);
            Assert.Contains("171 ms", overview); Assert.Contains("310 ms", overview); Assert.Contains("34 passed", overview); Assert.Contains("6 failed", overview);
            Assert.Contains("Environment", overview); Assert.Contains("Started", overview);
            var summary = Texts(3);
            Assert.Contains("Testcases", summary); Assert.Contains("Skipped (dontrun)", summary); Assert.Contains("Unexpected error", summary); Assert.Contains("Passed", summary); Assert.Contains("Failed", summary);
            var assertions = Texts(4);
            Assert.Contains("Known bugs", assertions); Assert.Contains("Expected", assertions); Assert.Contains("Actual", assertions); Assert.Contains("orderid equals", assertions);
            editor.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "dashboard-assertions.png"));
            var environments = Texts(6);
            Assert.Contains("Compared", environments); Assert.Contains("Differ", environments);
            editor.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "dashboard-environments.png"));
            var artifacts = Texts(7);
            Assert.Contains("Locations", artifacts.Replace("LOCATIONS", "Locations")); Assert.Contains("Workbook", artifacts);
            editor.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "dashboard-artifacts.png"));
            editor.Close();
        } finally { Directory.SetCurrentDirectory(prev); }
    }
}
