using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
}
