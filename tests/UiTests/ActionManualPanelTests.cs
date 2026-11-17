using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace UiTests;

// One pending manual action is one card, even when the panel's filter lists are filled while it builds.
public class ActionManualPanelTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [AvaloniaFact]
    public void A_single_pending_action_shows_one_card()
    {
        var type = typeof(LoadTestingTool.UI.MainWindow);
        var dashboard = new LoadTestingTool.UI.MainWindow();
        var infoType = type.Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!;
        var actionType = type.Assembly.GetType("LoadTestingTool.UI.ActionManualInfo")!;
        var instance = Activator.CreateInstance(infoType, nonPublic: true)!;
        var action = Activator.CreateInstance(actionType, nonPublic: true)!;
        void Set(string name, object value) => actionType.GetProperty(name)!.SetValue(action, value);
        Set("RequestId", "MANUAL-1"); Set("Testcase", "HttpFlow"); Set("DataId", "HTTP001"); Set("StepName", "FetchUser"); Set("Environment", "staging"); Set("ManualInfo", "test"); Set("TimeoutMinutes", 1);
        ((System.Collections.IList)infoType.GetProperty("ManualActions")!.GetValue(instance)!).Add(action);
        type.GetField("_selectedInstance", Flags)!.SetValue(dashboard, instance);

        type.GetMethod("RefreshActionManualPanel", Flags)!.Invoke(dashboard, null);

        var items = (StackPanel)type.GetField("_manualItems", Flags)!.GetValue(dashboard)!;
        Assert.Single(items.Children);
    }
}
