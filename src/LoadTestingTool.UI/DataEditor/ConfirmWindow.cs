using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>A small question with two buttons; the result is true for the confirming one.</summary>
public sealed class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string text, string confirm)
    {
        Title = title; Width = 460; SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner; CanResize = false;
        var yes = new Button { Content = confirm, Classes = { "accent" }, Tag = "confirm-yes" };
        var no = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), Tag = "confirm-no" };
        yes.Click += (_, _) => Close(true);
        no.Click += (_, _) => Close(false);
        Content = new StackPanel { Margin = new Thickness(16), Spacing = 14, Children =
        {
            new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } },
        } };
    }

    public static async Task<bool> AskAsync(Window owner, string title, string text, string confirm) => await new ConfirmWindow(title, text, confirm).ShowDialog<bool>(owner);
}
