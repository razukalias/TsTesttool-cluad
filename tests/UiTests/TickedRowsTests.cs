using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class TickedRowsTests
{
    private static (DataEditorViewModel Vm, List<RunRequest> Requests) Open(TempDir dir)
    {
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 5));
        var requests = new List<RunRequest>();
        vm.RunHandler = r => { requests.Add(r); return Task.FromResult<string?>(null); };
        return (vm, requests);
    }

    [Fact]
    public async Task Run_ticked_runs_only_the_ticked_rows_of_the_testcase()
    {
        using var dir = new TempDir();
        var (vm, requests) = Open(dir);
        Assert.False(vm.CanRunTicked);                       // nothing ticked yet
        vm.VisibleRows[0].IsTicked = true;
        vm.VisibleRows[3].IsTicked = true;
        vm.NotifyTickChanged();
        Assert.True(vm.CanRunTicked);
        Assert.Equal("▶ Run ticked (2)", vm.TickText);

        await vm.RunTickedCommand.ExecuteAsync(null);

        var request = Assert.Single(requests);
        Assert.Equal([vm.SelectedTestcase!.Index], request.Testcases);
        Assert.Equal([vm.VisibleRows[0].CurrentDataId, vm.VisibleRows[3].CurrentDataId], request.DataIds);
    }

    [Fact]
    public void Ticks_survive_filtering_and_tick_all_and_clear_work_on_what_is_shown()
    {
        using var dir = new TempDir();
        var (vm, _) = Open(dir);
        var first = vm.VisibleRows[0].CurrentDataId;
        vm.VisibleRows[0].IsTicked = true;
        vm.FilterText = "zzz-matches-nothing";                // rows rebuilt / hidden
        vm.FilterText = "";
        Assert.True(vm.VisibleRows.First(r => r.CurrentDataId == first).IsTicked || vm.TickedCount == 1);
        Assert.Equal(1, vm.TickedCount);

        vm.TickAllShownCommand.Execute(null);
        Assert.Equal(vm.VisibleRows.Count, vm.TickedCount);

        vm.ClearTicksCommand.Execute(null);
        Assert.Equal(0, vm.TickedCount);
        Assert.False(vm.CanRunTicked);
    }

    [Fact]
    public async Task Run_ticked_with_nothing_ticked_says_so_and_does_not_run()
    {
        using var dir = new TempDir();
        var (vm, requests) = Open(dir);
        await vm.RunTickedCommand.ExecuteAsync(null);
        Assert.Empty(requests);
    }

    [AvaloniaFact]
    public void The_grid_has_a_tick_column_whose_boxes_drive_the_ticks()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<DataGrid>("Grid")!;
        Assert.Equal("Run", Assert.IsType<TextBlock>(grid.Columns[0].Header).Text);

        var editor = window.Shell.Current!;
        editor.VisibleRows[1].IsTicked = true;
        editor.NotifyTickChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, editor.TickedCount);
        window.Close();
    }
}

public class TickedAcrossTestcasesTests
{
    [Fact]
    public void Ticks_in_the_whole_instance_view_are_passed_per_testcase()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        vm.ShowWholeInstance();
        var rows = vm.VisibleRows.Take(2).ToList();
        foreach (var row in rows) row.IsTicked = true;
        var (request, _) = vm.TickedRun();
        Assert.NotNull(request);
        Assert.NotNull(request!.RowsByTestcase);
        Assert.Equal(rows.Count, request.RowsByTestcase!.Values.Sum(v => v.Count));
        Assert.All(rows, r => Assert.Contains(r.CurrentDataId, request.RowsByTestcase[r.TestcaseIndex]));
    }
}

public class RunShowsOverviewTests
{
    [AvaloniaFact]
    public async Task Starting_a_run_raises_RunStarted_so_the_window_can_show_the_overview()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 2));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.Shell.Current!;
        vm.RunHandler = _ => Task.FromResult<string?>(null);
        var raised = 0;
        vm.RunStarted += (_, _) => raised++;
        await vm.RunRowCommand.ExecuteAsync(null);
        Assert.Equal(1, raised);
        window.Close();
    }
}
