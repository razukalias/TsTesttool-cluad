using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// For If, For, ForEach and While steps: <c>Step.output</c> (and <c>Step.index</c> for loops) are always available to later steps; this adds the response-sheet
/// columns to check them, only when you click.
/// </summary>
public sealed class ControlOutputView : StackPanel
{
    public ControlOutputView(StepRowViewModel step, bool loop)
    {
        Spacing = 4;
        Children.Add(new TextBlock { Text = "OUTPUT", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#8A94A6")) });
        Children.Add(new TextBlock
        {
            Text = loop
                ? "Later steps can always use <From_response_" + step.CurrentName + ".output_r> (the loop value) and <From_response_" + step.CurrentName + ".index_r> (the round, from 0). Add the columns only if you want to check them in the response sheet."
                : "Later steps can always use <From_response_" + step.CurrentName + ".output_r> (true or false). Add the column only if you want to check it in the response sheet.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#8A94A6"))
        });
        var button = new Button { Content = loop ? "Add output and index columns" : "Add output column", HorizontalAlignment = HorizontalAlignment.Left, Tag = "add-output-columns" };
        button.Click += (_, _) => step.Editor?.AddOutputColumns(step.CurrentName, loop);
        Children.Add(button);
    }
}
