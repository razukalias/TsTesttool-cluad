using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace UiTests;

// Overview step timeline: the exclude box hides steps that contain every term; clearing it brings them back.
public class OverviewExcludeTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly Type Window = typeof(LoadTestingTool.UI.MainWindow);

    private static LoadTestingTool.UI.DashboardRequest Step(string name, string testcase = "Customers", string result = "PASS")
        => new(name, "HTTP", testcase, "row_a", "dev", result, 10, "", "");

    private static (object Dashboard, TextBox Exclude, Func<LoadTestingTool.UI.DashboardRequest, bool> Shown, Func<LoadTestingTool.UI.DashboardRequest, bool> ShownIgnoringExclude) Open()
    {
        var dashboard = new LoadTestingTool.UI.MainWindow();
        var exclude = (TextBox)Window.GetField("_overviewStepExclude", Flags)!.GetValue(dashboard)!;
        var match = Window.GetMethod("MatchesOverviewStep", Flags)!;
        return (dashboard, exclude, r => (bool)match.Invoke(dashboard, [r, true])!, r => (bool)match.Invoke(dashboard, [r, false])!);
    }

    [AvaloniaFact]
    public void A_step_that_contains_every_exclude_term_is_hidden_and_clearing_the_box_brings_it_back()
    {
        var (_, exclude, shown, _) = Open();
        var health = Step("Health check");
        var ping = Step("Ping");
        Assert.True(shown(health));

        exclude.Text = "health";
        Assert.False(shown(health));
        Assert.True(shown(ping));

        exclude.Text = "";
        Assert.True(shown(health));
    }

    [AvaloniaFact]
    public void Several_terms_must_all_match_to_hide_a_step_and_they_can_hit_other_fields()
    {
        var (_, exclude, shown, _) = Open();
        exclude.Text = "health & dev";
        Assert.False(shown(Step("Health check")));         // environment dev is in the step's text too
        exclude.Text = "health & prod";
        Assert.True(shown(Step("Health check")));
        exclude.Text = "FAIL";
        Assert.False(shown(Step("Login", result: "FAIL")));
        Assert.True(shown(Step("Login", result: "PASS")));
    }

    [AvaloniaFact]
    public void The_exclude_box_has_its_own_hint_and_does_not_touch_the_include_box()
    {
        var (dashboard, exclude, shown, ignoring) = Open();
        var include = (TextBox)Window.GetField("_overviewStepFilter", Flags)!.GetValue(dashboard)!;
        Assert.Contains("Hide steps", exclude.Watermark);
        include.Text = "login";
        exclude.Text = "row_a";
        Assert.False(shown(Step("Login")));                // matches include, but excluded
        Assert.True(ignoring(Step("Login")));              // without the exclude box it would be shown
        Assert.False(ignoring(Step("Ping")));              // not in the include filter either way
    }
}
