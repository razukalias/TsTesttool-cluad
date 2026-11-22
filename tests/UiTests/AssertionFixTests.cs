using System.Reflection;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class AssertionFixTests
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [AvaloniaFact]
    public void Run_views_keep_only_the_selected_testcase_or_step()
    {
        var dashboard = new LoadTestingTool.UI.MainWindow();
        var item = Activator.CreateInstance(typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        var type = item.GetType();
        type.GetProperty("Name")!.SetValue(item, "Demo");
        var requests = (System.Collections.IList)type.GetProperty("Requests")!.GetValue(item)!;
        requests.Add(new LoadTestingTool.UI.DashboardRequest("A", "HTTP", "T1", "D1", "", "PASS", 5, "", ""));
        requests.Add(new LoadTestingTool.UI.DashboardRequest("B", "HTTP", "T1", "D1", "", "FAIL", 5, "Assertion", ""));
        requests.Add(new LoadTestingTool.UI.DashboardRequest("C", "HTTP", "T2", "D1", "", "PASS", 5, "", ""));
        var scopeField = typeof(LoadTestingTool.UI.MainWindow).GetField("_scope", Flags)!;
        typeof(LoadTestingTool.UI.MainWindow).GetField("_scopeInstance", Flags)!.SetValue(dashboard, "Demo");
        var scoped = typeof(LoadTestingTool.UI.MainWindow).GetMethod("ScopedRequests", Flags)!;
        List<LoadTestingTool.UI.DashboardRequest> Names() => (List<LoadTestingTool.UI.DashboardRequest>)scoped.Invoke(dashboard, [item])!;

        scopeField.SetValue(dashboard, null);
        Assert.Equal(3, Names().Count);
        scopeField.SetValue(dashboard, new RunScope("T1", 1, null));
        Assert.Equal(["A", "B"], Names().Select(r => r.StepName));
        scopeField.SetValue(dashboard, new RunScope("T1", 1, "B"));
        Assert.Equal(["B"], Names().Select(r => r.StepName));
        scopeField.SetValue(dashboard, new RunScope("T1", 1, "NeverRun"));
        Assert.Empty(Names());
    }
}
