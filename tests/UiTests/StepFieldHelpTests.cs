using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class StepFieldHelpTests
{
    [Theory]
    [InlineData("steptype"), InlineData("stage"), InlineData("enabled"), InlineData("stoponfailure"), InlineData("verb"), InlineData("targeturl"), InlineData("contenttype"),
     InlineData("templetsource"), InlineData("expectedstatus"), InlineData("timeout"), InlineData("wait"), InlineData("assert"), InlineData("ignoreempty"),
     InlineData("assertonlyresponse"), InlineData("manualtimeoutminutes"), InlineData("stepconfig"), InlineData("stepname")]
    public void Every_step_form_field_has_a_label_and_an_explanation(string header)
    {
        Assert.False(string.IsNullOrWhiteSpace(StepFieldHelp.LabelFor(header)));
        Assert.True(StepFieldHelp.TextFor(header)!.Length > 20);
    }

    [Fact]
    public void The_manual_timeout_is_called_that_and_explains_the_default()
    {
        Assert.Equal("Manual timeout (min)", StepFieldHelp.LabelFor("manualtimeoutminutes"));
        Assert.Contains("15", StepFieldHelp.TextFor("manualtimeoutminutes"));
        Assert.Equal(StepFieldHelp.TextFor("manualtimeoutminutes"), StepFieldHelp.TextForLabel("Manual timeout (minutes)"));   // the older wizard's wording
    }

    [AvaloniaFact]
    public void The_info_icon_shows_the_text_on_hover_and_in_a_flyout_that_keyboard_and_click_open()
    {
        var icon = (Button)StepFieldHelp.Icon("Some explanation")!;
        Assert.Equal("Some explanation", ToolTip.GetTip(icon));
        Assert.True(icon.Focusable);
        var flyout = Assert.IsType<Flyout>(icon.Flyout);
        Assert.Equal("Some explanation", ((TextBlock)flyout.Content!).Text);
        Assert.Null(StepFieldHelp.Icon(null));
    }
}
