using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia;
using Avalonia.VisualTree;
using Xunit;

namespace UiTests;

public class AssertionTabLayoutTests
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [AvaloniaFact]
    public void The_pager_sits_under_the_assertion_list_and_there_is_no_workbench_button()
    {
        var dashboard = new LoadTestingTool.UI.MainWindow();
        dashboard.Width = 1500; dashboard.Height = 900; dashboard.Show();
        Dispatcher.UIThread.RunJobs();
        var item = Activator.CreateInstance(typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        item.GetType().GetProperty("Name")!.SetValue(item, "Demo");
        var list = (System.Collections.IList)item.GetType().GetProperty("Assertions")!.GetValue(item)!;
        for (var i = 0; i < 36; i++)
            list.Add(new LoadTestingTool.UI.AssertionInfo { Testcase = "OrderBlank", DataId = "B1", StepName = "getorder", ResponsePath = $"field{i}", AssertionVerb = "eq", ExpectedValue = $"E{i}", ActualValue = $"A{i}", Result = "FAIL", Environment = "production" });
        typeof(LoadTestingTool.UI.MainWindow).GetField("_selectedInstance", Flags)!.SetValue(dashboard, item);
        var tabs = (TabControl)typeof(LoadTestingTool.UI.MainWindow).GetField("_detailTabs", Flags)!.GetValue(dashboard)!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(i => i.Header as string == "Assertions");
        typeof(LoadTestingTool.UI.MainWindow).GetMethod("RefreshAssertionList", Flags)!.Invoke(dashboard, null);
        Dispatcher.UIThread.RunJobs();

        var labels = dashboard.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
        Assert.DoesNotContain("Open Assertion Workbench", labels);
        var scroll = dashboard.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Content is StackPanel p && ReferenceEquals(p, typeof(LoadTestingTool.UI.MainWindow).GetField("_assertionItems", Flags)!.GetValue(dashboard)));
        var bar = dashboard.GetVisualDescendants().OfType<LoadTestingTool.UI.PagerBar>().First(b => b.IsVisible && b.Bounds.Width > 0 && b.GetVisualAncestors().Contains(tabs));
        var scrollTop = scroll.TranslatePoint(new Avalonia.Point(0, 0), dashboard)!.Value;
        var barTop = bar.TranslatePoint(new Avalonia.Point(0, 0), dashboard)!.Value;
        Assert.True(barTop.Y >= scrollTop.Y + scroll.Bounds.Height - 1, $"the pager (y={barTop.Y}) must be below the list (y={scrollTop.Y}, h={scroll.Bounds.Height})");
        Assert.True(bar.Bounds.Width <= tabs.Bounds.Width + 1, "the pager must fit the panel width");

        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        dashboard.CaptureRenderedFrame()!.Save(Path.Combine(shots, "assertion-tab.png"));
        dashboard.Close();
    }

    [AvaloniaFact]
    public void Texts_in_the_panels_can_be_selected_with_the_mouse()
    {
        Assert.IsType<SelectableTextBlock>(LoadTestingTool.UI.DashboardStyle.Text("x"));
    }
}
