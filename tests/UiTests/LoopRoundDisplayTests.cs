using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace UiTests;

public class LoopRoundDisplayTests
{
    [AvaloniaFact]
    public void The_dashboard_keeps_the_round_a_live_event_reports()
    {
        var item = Activator.CreateInstance(typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        var record = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.MainWindow")!
            .GetMethod("RecordDashboardEvent", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        Assert.NotNull(record);
        var json = JsonSerializer.Serialize(new { eventType = "request-completed", runId = "r", testcase = "T", dataId = "D1", stepName = "Each", stepType = "HTTP", result = "PASS", loopRound = "Loop #2 > Inner #3" });
        using var doc = JsonDocument.Parse(json);
        record!.Invoke(null, [item, doc.RootElement]);
        var requests = ((System.Collections.IEnumerable)item.GetType().GetProperty("Requests")!.GetValue(item)!).Cast<LoadTestingTool.UI.DashboardRequest>().ToList();
        Assert.Equal("Loop #2 > Inner #3", Assert.Single(requests).LoopRound);
    }

    [AvaloniaFact]
    public void The_old_step_wizard_offers_the_control_step_types()
    {
        var source = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.TestStepConfigurationEditor")!;
        var field = source.GetField("_newStepType", BindingFlags.NonPublic | BindingFlags.Instance)!;
        using var dir = new TempDir();
        var item = Activator.CreateInstance(source.Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        item.GetType().GetProperty("Name")!.SetValue(item, "Demo");
        item.GetType().GetProperty("WorkbookPath")!.SetValue(item, Samples.CreateLarge(dir, rows: 3));
        var editor = Activator.CreateInstance(source, item);
        Assert.NotNull(editor);
        var combo = Assert.IsType<ComboBox>(field.GetValue(editor));
        var types = ((System.Collections.IEnumerable)combo.ItemsSource!).Cast<string>().ToList();
        foreach (var type in new[] { "If", "While", "For", "ForEach" }) Assert.Contains(type, types);
    }
}
