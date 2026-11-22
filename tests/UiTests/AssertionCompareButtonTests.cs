using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Xunit;

namespace UiTests;

public class AssertionCompareButtonTests
{
    [AvaloniaFact]
    public void The_assertions_tab_has_one_Compare_button_instead_of_Export_and_Open_comparison()
    {
        var dashboard = new LoadTestingTool.UI.MainWindow();
        dashboard.Width = 1500; dashboard.Height = 900; dashboard.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tabs = (TabControl)typeof(LoadTestingTool.UI.MainWindow).GetField("_detailTabs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dashboard)!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(i => i.Header as string == "Assertions");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var labels = dashboard.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).Where(s => s is not null).ToList();
        Assert.Contains("Compare", labels);
        Assert.DoesNotContain("Export comparison", labels);
        Assert.DoesNotContain("Open comparison", labels);
        dashboard.Close();
    }
}
