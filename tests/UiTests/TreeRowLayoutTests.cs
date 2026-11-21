using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class TreeRowLayoutTests
{
    [AvaloniaFact]
    public void An_instance_shows_its_lines_only_while_expanded_and_the_checkboxes_sit_at_the_right_end()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1400; window.Height = 800; window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var tree = window.GetVisualDescendants().OfType<TreeView>().First();
        var item = tree.GetVisualDescendants().OfType<TreeViewItem>().First(i => i.DataContext is InstanceNode);
        TextBlock Subtitle() => item.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "1 testcase");
        item.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(Subtitle().IsEffectivelyVisible);
        window.CaptureRenderedFrame()!.Save("/tmp/tree_expanded.png");
        item.IsExpanded = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(Subtitle().IsEffectivelyVisible);
        var name = item.GetVisualDescendants().OfType<TextBlock>().First(t => t.FontWeight == Avalonia.Media.FontWeight.Bold);
        Assert.True(name.IsEffectivelyVisible);
        window.CaptureRenderedFrame()!.Save("/tmp/tree_collapsed.png");
        item.IsExpanded = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        // every checkbox of an instance, testcase or step row lies right of its title
        foreach (var row in tree.GetVisualDescendants().OfType<TreeViewItem>())
        {
            var check = row.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.Classes.Contains("treecheck"));
            var title = row.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text));
            if (check is null || title is null) continue;
            Assert.True(check.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X > title.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.X, $"{row.DataContext}");
        }
        window.Close();
    }
}
