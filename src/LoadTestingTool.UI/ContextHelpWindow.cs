using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

internal sealed class ContextHelpWindow : Window
{
    private ContextHelpWindow(string title, string text)
    {
        Title = title; Width = 720; Height = 620; MinWidth = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = DashboardStyle.Brush("#E3EDF9") };
        var close = DashboardStyle.Action("Close", Close, "primary");
        Content = new Border { Padding = new Thickness(22), Background = DashboardStyle.Panel, Child = new DockPanel { LastChildFill = true, Children = { close, new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } } } };
        DockPanel.SetDock(close, Dock.Bottom);
    }

    public static void Show(Window owner, string title, string text)
    {
        var window = new ContextHelpWindow(title, text);
        window.Show(owner);
    }
}
