using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>A guided window with only the settings of one step's type; it edits the same step as the form, so both stay in step.</summary>
public sealed class StepWizardWindow : Window
{
    public static void Open(Control from, StepRowViewModel step)
    {
        var window = new StepWizardWindow(step);
        if (TopLevel.GetTopLevel(from) is Window owner) window.Show(owner); else window.Show();
    }

    private StepWizardWindow(StepRowViewModel step)
    {
        var type = step.TypeText;
        Title = $"Configure {type} step · {step.CurrentName}";
        Width = 560; Height = 640; MinWidth = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0B1626");

        var body = new StackPanel { Spacing = 14, Margin = new Thickness(22) };
        body.Children.Add(new TextBlock { Text = $"{type} step", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#E3EDF9") });
        body.Children.Add(new TextBlock { Text = step.CurrentName, FontSize = 13, Foreground = Brush.Parse("#8EA4BC") });
        body.Children.Add(new TextBlock { Text = "Changes are applied to the step as you make them. Save the workbook from the editor to keep them; Ctrl+Z there undoes them.", FontSize = 12, Foreground = Brush.Parse("#8EA4BC"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new StepTypeSection(step));
        var done = new Button { Content = "Done", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        done.Click += (_, _) => Close();
        body.Children.Add(done);
        Content = new ScrollViewer { Content = body };
    }
}
