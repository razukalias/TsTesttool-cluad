using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class OpenCleanTests
{
    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("FileHttpScenarios")]
    [InlineData("DataEngine_IntegrationScenarios")]
    public void Opening_a_shipped_instance_and_visiting_both_tabs_does_not_mark_it_changed(string instance)
    {
        var root = AppContext.BaseDirectory;
        var path = Directory.EnumerateFiles(FindRepoRoot(root), "*.xlsx", SearchOption.AllDirectories).FirstOrDefault(f => f.Contains(instance) && !f.Contains("bin") && !f.Contains("obj") && !f.Contains("~$"));
        Assert.NotNull(path);
        using var dir = new TempDir();
        var copy = dir.Combine(Path.GetFileName(path!));
        File.Copy(path!, copy);
        var window = DataEditorWindow.Open(copy, null);
        window.Width = 1440; window.Height = 860;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = window.ViewModel;
        foreach (var testcase in vm.Testcases.ToList())
        {
            vm.SelectedTestcase = testcase;
            vm.ShowSteps = true;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsDirty, $"dirty on Steps tab of testcase {testcase.Title}: {vm.StatusMessage}");
            vm.ShowSteps = false;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsDirty, $"dirty on Data tab of testcase {testcase.Title}: {vm.StatusMessage}");
        }
        window.Close();
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LoadTestingTool.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
