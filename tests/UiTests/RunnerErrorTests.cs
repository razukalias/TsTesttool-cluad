using System.Reflection;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Errors that the runner can only print (startup failures, crashes) must reach the Run issues and the status icons.
public class RunnerErrorTests
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [AvaloniaFact]
    public void Runner_errors_are_added_once_and_count_as_errors_in_the_status()
    {
        var dashboard = new LoadTestingTool.UI.MainWindow();
        var item = Activator.CreateInstance(typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
        var add = typeof(LoadTestingTool.UI.MainWindow).GetMethod("AddRunnerIssue", Flags)!;
        add.Invoke(dashboard, [item, "Error", "Runner", "Instance 'x' was not found.", "", "", false]);
        add.Invoke(dashboard, [item, "Error", "Runner", "Instance 'x' was not found.", "", "", false]);              // the same text twice
        add.Invoke(dashboard, [item, "Error", "Validation", "Instance 'x' was not found.", "", "", false]);          // the same text from another source
        add.Invoke(dashboard, [item, "Warning", "Runner", "History generation failed.", "", "", false]);
        var issues = ((System.Collections.IEnumerable)item.GetType().GetProperty("Issues")!.GetValue(item)!).Cast<object>().ToList();
        Assert.Equal(2, issues.Count);
        Assert.Equal("Error", issues[0].GetType().GetProperty("Severity")!.GetValue(issues[0]));
    }

    [Theory]
    [InlineData("Validation", RunState.ValidationError)]
    [InlineData("Runner", RunState.UnexpectedError)]
    [InlineData("Launch", RunState.UnexpectedError)]
    [InlineData("Unexpected error", RunState.UnexpectedError)]
    public void Issue_categories_map_to_a_state(string category, RunState expected)
        => Assert.Equal(expected, RunClassifier.FromCategory(category, "message"));
}
