using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

// The ⓘ button of a step type: how to fill the step, and an example Step config that never overwrites an existing one.
public class StepTypeGuideTests
{
    [Fact]
    public void Every_step_type_has_a_guide_and_the_examples_are_valid_json()
    {
        foreach (var type in new[] { "HTTP", "GraphQL", "CaseQL", "SQL", "File", "Script", "ActionManual" })
        {
            var guide = StepTypeGuide.For(type);
            Assert.NotNull(guide);
            Assert.NotEmpty(guide!.Steps);
            if (guide.ExampleConfig is { } example) using (System.Text.Json.JsonDocument.Parse(example)) { }
        }
        Assert.Null(StepTypeGuide.For("Unknown"));
        Assert.Equal("SQL step", StepTypeGuide.For(" sql ")!.Title);
    }

    [AvaloniaFact]
    public void Use_this_example_fills_an_empty_step_config_and_leaves_a_filled_one_alone()
    {
        var config = string.Empty;
        var icon = (Button)StepTypeGuide.Icon("SQL", () => config, text => config = text)!;
        var flyout = (Flyout)icon.Flyout!;
        var use = FindButton(flyout.Content!);

        use.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("ConnectionProfile", config);

        config = "{\"mine\":1}";
        use.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("{\"mine\":1}", config);
        Assert.NotNull(StepTypeGuide.Icon("ActionManual", () => config, _ => { }));   // the guide exists even without an example
    }

    private static Button FindButton(object content)
    {
        var stack = (StackPanel)((ScrollViewer)content).Content!;
        return stack.Children.OfType<Button>().Single(b => b.Tag as string == "use-example");
    }
}
