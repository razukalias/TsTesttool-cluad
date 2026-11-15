using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class RerunCheckboxTests
{
    private static (DataEditorViewModel Vm, List<RunRequest> Requests) Editor(TempDir dir)
    {
        var vm = new DataEditorViewModel(Samples.Copy(dir, Samples.IntegrationScenarios));
        var requests = new List<RunRequest>();
        vm.RunHandler = r => { requests.Add(r); return Task.FromResult<string?>(null); };
        return (vm, requests);
    }

    [Fact]
    public async Task A_run_is_fresh_unless_the_box_is_ticked_and_the_box_clears_when_that_run_starts()
    {
        using var dir = new TempDir();
        var (vm, requests) = Editor(dir);

        await vm.RunTestcaseCommand.ExecuteAsync(null);
        vm.ReuseLastValues = true;
        await vm.RunTestcaseCommand.ExecuteAsync(null);
        await vm.RunTestcaseCommand.ExecuteAsync(null);

        Assert.Equal([false, true, false], requests.Select(r => r.Rerun));
        Assert.False(vm.ReuseLastValues);
    }

    [Fact]
    public async Task The_box_also_clears_when_the_run_is_refused_and_when_the_workbook_is_reloaded()
    {
        using var dir = new TempDir();
        var (vm, _) = Editor(dir);
        vm.RunHandler = _ => Task.FromResult<string?>("already running");
        vm.ReuseLastValues = true;
        await vm.RunTestcaseCommand.ExecuteAsync(null);
        Assert.False(vm.ReuseLastValues);

        vm.ReuseLastValues = true;
        vm.ReloadCommand.Execute(null);
        Assert.False(vm.ReuseLastValues);
    }
}
