using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>A small dialog that asks "how many?" for a row or column menu entry. Returns null when cancelled.</summary>
public sealed class CountPromptWindow : Window
{
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 500, Value = 1, Increment = 1, FormatString = "0", Width = 140 };
    private int? _result;

    public CountPromptWindow(string question)
    {
        Title = question;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var ok = new Button { Content = "OK", Classes = { "primary" }, MinWidth = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) => { _result = (int)Math.Clamp(_count.Value ?? 1, 1, 500); Close(_result); };
        cancel.Click += (_, _) => Close((int?)null);
        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = question, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _count,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } },
            },
        };
        Opened += (_, _) => _count.Focus();
    }

    public static async Task<int?> AskAsync(Window owner, string question)
        => await new CountPromptWindow(question).ShowDialog<int?>(owner);
}
