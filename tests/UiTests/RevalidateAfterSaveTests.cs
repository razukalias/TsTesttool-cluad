using System.Collections;
using System.Reflection;
using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class RevalidateAfterSaveTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [Fact]
    public void Saving_in_the_editor_tells_the_dashboard_which_workbook_was_saved()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var shell = new EditorShellViewModel([("Sample", path)]);
        Assert.True(shell.Instances[0].LoadNow());
        var saved = new List<string>();
        shell.WorkbookSaved += saved.Add;

        var vm = shell.Instances[0].Editor!;
        vm.PasteBlock(0, 0, ClipboardTable.Parse("changed"));
        vm.SaveCommand.Execute(null);

        Assert.Equal([path], saved);
    }

    [AvaloniaFact]
    public async Task Revalidating_after_a_save_replaces_that_instances_old_validation_issues_and_keeps_run_issues()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var dashboard = new LoadTestingTool.UI.MainWindow();
        var dashboardType = typeof(LoadTestingTool.UI.MainWindow);

        var instances = (IList)dashboardType.GetField("_instances", Any)!.GetValue(dashboard)!;
        var infoType = instances.GetType().GetGenericArguments()[0];
        var instance = Activator.CreateInstance(infoType)!;
        infoType.GetProperty("Name")!.SetValue(instance, "TempInstance");
        infoType.GetProperty("FolderPath")!.SetValue(instance, dir.Path);
        infoType.GetProperty("WorkbookPath")!.SetValue(instance, path);
        instances.Add(instance);

        var issues = (IList)infoType.GetProperty("Issues")!.GetValue(instance)!;
        var issueType = issues.GetType().GetGenericArguments()[0];
        object Issue(string category, string message)
        {
            var item = Activator.CreateInstance(issueType)!;
            issueType.GetProperty("Severity")!.SetValue(item, "Error");
            issueType.GetProperty("Category")!.SetValue(item, category);
            issueType.GetProperty("Message")!.SetValue(item, message);
            return item;
        }
        issues.Add(Issue("Validation", "stale: step X has a wrong value"));
        issues.Add(Issue("Run", "a failed step from the last run"));

        var revalidate = dashboardType.GetMethod("RevalidateAfterSaveAsync", Any)!;
        await (Task)revalidate.Invoke(dashboard, [path])!;

        var messages = string.Join("|", issues.Cast<object>().Select(i => $"{issueType.GetProperty("Category")!.GetValue(i)}:{issueType.GetProperty("Message")!.GetValue(i)}"));
        Assert.DoesNotContain("stale", messages);
        Assert.Contains("Run:a failed step from the last run", messages);
    }
}
