using System.Reflection;
using Xunit;

namespace UiTests;

// The "passed validation" line is not a problem: it must not turn into an error on the instance.
public class ValidationIssuesTests
{
    [Fact]
    public void Only_errors_and_warnings_become_issues_not_the_pass_or_info_lines()
    {
        var assembly = typeof(LoadTestingTool.UI.MainWindow).Assembly;
        var instanceType = assembly.GetType("LoadTestingTool.UI.InstanceInfo")!;
        var issueType = assembly.GetType("LoadTestingTool.UI.ValidationIssue")!;
        var resultType = assembly.GetType("LoadTestingTool.UI.InstanceValidationResult")!;
        object Issue(string severity, string problem) => Activator.CreateInstance(issueType, severity, "X", problem, "Fix it.")!;

        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(issueType))!;
        list.Add(Issue("PASS", "Workbook passed validation."));
        list.Add(Issue("INFO", "Nothing to check."));
        list.Add(Issue("WARNING", "A warning."));
        list.Add(Issue("ERROR", "An error."));
        var result = Activator.CreateInstance(resultType, "x", list)!;
        var instance = Activator.CreateInstance(instanceType, nonPublic: true)!;

        typeof(LoadTestingTool.UI.MainWindow).GetMethod("ApplyValidationIssues", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [instance, result]);

        var issues = ((System.Collections.IEnumerable)instanceType.GetProperty("Issues")!.GetValue(instance)!).Cast<object>().ToList();
        Assert.Equal(["Warning", "Error"], issues.Select(i => (string)i.GetType().GetProperty("Severity")!.GetValue(i)!).ToArray());
    }
}
