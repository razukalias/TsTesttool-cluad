using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace UiTests;

public class ActionManualTabHeaderTests
{
    [AvaloniaFact]
    public void The_ActionManual_title_takes_the_tab_colour_so_it_is_gray_when_the_tab_is_not_active()
    {
        var method = typeof(LoadTestingTool.UI.MainWindow).GetMethod("BuildActionManualTabHeader", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var header = (StackPanel)method.Invoke(null, [0])!;
        var title = (TextBlock)header.Children[0];
        Assert.Equal("ActionManual", title.Text);
        Assert.False(title.IsSet(TextBlock.ForegroundProperty));   // no fixed bright colour of its own

        var tabs = new TabControl { Items = { new TabItem { Header = "Overview", Content = new TextBlock() }, new TabItem { Header = header, Content = new TextBlock() } }, SelectedIndex = 0 };
        var window = new Window { Content = tabs };
        window.Show();
        var overview = (TabItem)tabs.Items[0]!;
        var manual = (TabItem)tabs.Items[1]!;
        static Avalonia.Media.Color Colour(Avalonia.Media.IBrush? brush) => ((Avalonia.Media.ISolidColorBrush)brush!).Color;

        // Not selected: the title shows the same dimmed colour as the tab itself, which is not the selected tab's colour.
        Assert.Equal(Colour(manual.Foreground), Colour(title.Foreground));
        var activeColour = Colour(overview.Foreground);   // overview is the selected tab
        Assert.NotEqual(activeColour, Colour(title.Foreground));

        // Selected: it follows the tab to the active colour.
        tabs.SelectedIndex = 1;
        Assert.Equal(Colour(manual.Foreground), Colour(title.Foreground));
        Assert.Equal(activeColour, Colour(title.Foreground));
        window.Close();
    }
}
