using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class DetailTextSizeTests
{
    [AvaloniaFact]
    public void The_text_size_steps_up_down_and_back_to_normal_within_limits()
    {
        try
        {
            DetailTextSize.Step(0);
            Assert.Equal(1.0, DetailTextSize.Scale);
            DetailTextSize.Step(1); Assert.Equal(1.1, DetailTextSize.Scale);
            DetailTextSize.Step(-1); DetailTextSize.Step(-1); Assert.Equal(0.9, DetailTextSize.Scale);
            for (var i = 0; i < 20; i++) DetailTextSize.Step(1);
            Assert.Equal(2.0, DetailTextSize.Scale);                    // upper limit
            for (var i = 0; i < 20; i++) DetailTextSize.Step(-1);
            Assert.Equal(0.8, DetailTextSize.Scale);                    // lower limit
        }
        finally { DetailTextSize.Step(0); }
    }

    [AvaloniaFact]
    public void The_panels_have_the_size_buttons_and_the_buttons_change_the_scale_and_the_label()
    {
        try
        {
            var dashboard = new Window { Width = 1500, Height = 900, Content = new StackPanel { Children = { DetailTextSize.CreateBar(), DetailTextSize.Wrap(new TextBlock { Text = "x" }) } } };
            dashboard.Show();
            Dispatcher.UIThread.RunJobs();
            Button Find(string tag) => dashboard.GetVisualDescendants().OfType<Button>().First(b => b.Tag as string == tag);
            Assert.Equal("100%", Find("text-normal").Content);
            Find("text-bigger").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.1, DetailTextSize.Scale);
            Assert.Equal("110%", Find("text-normal").Content);
            Find("text-normal").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1.0, DetailTextSize.Scale);
            Assert.NotEmpty(dashboard.GetVisualDescendants().OfType<LayoutTransformControl>());   // the panels sit under the shared scale
            dashboard.Close();
        }
        finally { DetailTextSize.Step(0); }
    }

    [AvaloniaFact]
    public void Ctrl_and_the_mouse_wheel_change_the_size_over_a_panel()
    {
        try
        {
            var host = DetailTextSize.Wrap(new TextBlock { Text = "x" });
            var window = new Window { Content = host, Width = 300, Height = 200 };
            window.Show();
            window.MouseWheel(new Avalonia.Point(50, 50), new Avalonia.Vector(0, 1), Avalonia.Input.RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.1, DetailTextSize.Scale);
            window.MouseWheel(new Avalonia.Point(50, 50), new Avalonia.Vector(0, -1));    // without Ctrl: only scrolling, no size change
            Assert.Equal(1.1, DetailTextSize.Scale);
            window.Close();
        }
        finally { DetailTextSize.Step(0); }
    }
}
