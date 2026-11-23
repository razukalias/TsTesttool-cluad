using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>A small window that says why something was refused, so it cannot be missed in the status line. Not modal: the editor stays usable.</summary>
internal sealed class NoticeWindow : Window
{
    public string Message { get; }

    public NoticeWindow(string title, string message)
    {
        Message = message;
        Title = title;
        Width = 460; SizeToContent = SizeToContent.Height; MinHeight = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.Parse("#0E1B2C"));
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90, Tag = "notice-ok" };
        ok.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(22), Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#FF8A8A")) },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E3EDF9")) },
                ok
            }
        };
    }
}
