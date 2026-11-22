using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LoadTestingTool.UI.DataEditor;

namespace LoadTestingTool.UI;

/// <summary>
/// The text size of the run panels (Overview, Assertions, Bugs, Run Summary, Environments, Artifacts): one shared scale, changed with the A− / A+ buttons
/// or Ctrl + mouse wheel over a panel, remembered between runs. Everything in the panels grows together and wraps to the width that is left.
/// </summary>
internal static class DetailTextSize
{
    private static readonly double[] Steps = [0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0];
    public static readonly ScaleTransform Transform = new(1, 1);
    public static event Action? Changed;

    static DetailTextSize() => Apply(EditorSettings.Load().DetailTextScale);

    public static double Scale => Transform.ScaleX;

    private static void Apply(double scale)
    {
        scale = Steps.OrderBy(s => Math.Abs(s - scale)).First();
        Transform.ScaleX = scale; Transform.ScaleY = scale;
    }

    /// <summary>One step bigger (+1), smaller (-1) or back to normal (0).</summary>
    public static void Step(int direction)
    {
        var index = Array.IndexOf(Steps, Scale);
        index = direction == 0 ? Array.IndexOf(Steps, 1.0) : Math.Clamp(index + direction, 0, Steps.Length - 1);
        Apply(Steps[index]);
        try { var settings = EditorSettings.Load(); settings.DetailTextScale = Scale; settings.Save(); } catch (IOException) { }
        Changed?.Invoke();
    }

    /// <summary>Puts a panel under the shared scale; Ctrl + mouse wheel over it changes the size.</summary>
    public static Control Wrap(Control child)
    {
        var host = new Border { Background = Brushes.Transparent, Child = new LayoutTransformControl { LayoutTransform = Transform, Child = child } };   // a background so the wheel works over empty areas too
        host.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0) return;
            Step(e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return host;
    }

    /// <summary>"A−  100%  A+": smaller, back to normal, bigger.</summary>
    public static Control CreateBar()
    {
        var smaller = new Button { Content = "A−", Tag = "text-smaller", Padding = new Avalonia.Thickness(8, 2), MinWidth = 30 };
        var normal = new Button { Tag = "text-normal", Padding = new Avalonia.Thickness(6, 2), MinWidth = 48 };
        var bigger = new Button { Content = "A+", Tag = "text-bigger", Padding = new Avalonia.Thickness(8, 2), MinWidth = 30 };
        ToolTip.SetTip(smaller, "Smaller text in these panels (Ctrl + mouse wheel down)");
        ToolTip.SetTip(bigger, "Bigger text in these panels (Ctrl + mouse wheel up)");
        ToolTip.SetTip(normal, "Back to the normal text size");
        void Refresh() { normal.Content = $"{Scale * 100:0}%"; smaller.IsEnabled = Scale > Steps[0]; bigger.IsEnabled = Scale < Steps[^1]; }
        smaller.Click += (_, _) => Step(-1);
        bigger.Click += (_, _) => Step(1);
        normal.Click += (_, _) => Step(0);
        var bar = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Tag = "text-size-bar", Children = { smaller, normal, bigger } };
        void OnChanged() => Refresh();
        bar.AttachedToVisualTree += (_, _) => { Changed += OnChanged; Refresh(); };
        bar.DetachedFromVisualTree += (_, _) => Changed -= OnChanged;
        Refresh();
        return bar;
    }
}
