using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class TickedRowsTests
{
    private static (DataEditorViewModel Vm, EditorShellViewModel Shell) Open(TempDir dir, int rows = 5)
    {
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: rows));
        return (window.Shell.Current!, window.Shell);
    }

    [AvaloniaFact]
    public void Ticked_rows_take_precedence_over_the_tree_ticks_and_run_only_those_rows()
    {
        using var dir = new TempDir();
        var (vm, shell) = Open(dir);
        foreach (var testcase in vm.Testcases) testcase.IsChosen = true;      // the tree ticks everything by default in a real instance
        vm.VisibleRows[0].IsTicked = true;
        vm.VisibleRows[3].IsTicked = true;
        vm.NotifyTickChanged();

        var (request, label) = vm.BuildTickedRequest();

        Assert.NotNull(request);
        Assert.Equal([vm.SelectedTestcase!.Index], request!.Testcases);
        Assert.Equal([vm.VisibleRows[0].CurrentDataId, vm.VisibleRows[3].CurrentDataId], request.RowsByTestcase![vm.SelectedTestcase.Index]);
        Assert.Equal("2 ticked rows", label);
        Assert.Equal(2, shell.TickedCount);                      // the badge counts the rows that will run, not the testcases
        Assert.Equal("(2)", shell.TickedBadge);
        Assert.Equal("2 rows ticked", shell.TickedText);
        Assert.True(shell.CanRunTicked);
    }

    [AvaloniaFact]
    public void With_no_rows_ticked_the_tree_ticks_decide_as_before()
    {
        using var dir = new TempDir();
        var (vm, shell) = Open(dir);
        foreach (var testcase in vm.Testcases) testcase.IsChosen = true;
        var (request, _) = vm.BuildTickedRequest();
        Assert.Equal(vm.Testcases.Select(t => t.Index), request!.Testcases);
        Assert.Null(request.RowsByTestcase);
        Assert.Equal(vm.Testcases.Count, shell.TickedCount);
        Assert.Equal($"({vm.Testcases.Count})", shell.TickedBadge);
    }

    [AvaloniaFact]
    public void Run_ticked_stays_enabled_for_ticked_rows_even_when_no_testcase_is_ticked_in_the_tree()
    {
        using var dir = new TempDir();
        var (vm, shell) = Open(dir);
        foreach (var testcase in vm.Testcases) testcase.IsChosen = false;
        Assert.False(shell.CanRunTicked);
        vm.VisibleRows[1].IsTicked = true;
        vm.NotifyTickChanged();
        Assert.True(shell.CanRunTicked);
        vm.SetShownTicks(false);
        Assert.False(shell.CanRunTicked);
    }

    [AvaloniaFact]
    public void Ticks_survive_filtering_and_the_select_all_acts_on_the_rows_shown()
    {
        using var dir = new TempDir();
        var (vm, _) = Open(dir);
        var first = vm.VisibleRows[0].CurrentDataId;
        vm.VisibleRows[0].IsTicked = true;
        vm.NotifyTickChanged();
        Assert.Null(vm.ShownTickState);                          // some, not all

        vm.FilterText = "zzz-matches-nothing";
        vm.FilterText = "";
        Assert.Equal(1, vm.TickedRowCount);
        Assert.True(vm.VisibleRows.First(r => r.CurrentDataId == first).IsTicked);

        vm.SetShownTicks(true);
        Assert.Equal(true, vm.ShownTickState);
        Assert.Equal(vm.VisibleRows.Count, vm.TickedRowCount);

        vm.SetShownTicks(false);
        Assert.Equal(false, vm.ShownTickState);
        Assert.Equal(0, vm.TickedRowCount);
    }

    [AvaloniaFact]
    public void Run_ticked_across_testcases_passes_each_testcases_rows()
    {
        using var dir = new TempDir();
        var (vm, _) = Open(dir, rows: 3);
        vm.ShowWholeInstance();
        var rows = vm.VisibleRows.Take(2).ToList();
        foreach (var row in rows) row.IsTicked = true;
        vm.NotifyTickChanged();
        var (request, _) = vm.BuildTickedRequest();
        Assert.NotNull(request!.RowsByTestcase);
        Assert.Equal(rows.Count, request.RowsByTestcase!.Values.Sum(v => v.Count));
        Assert.All(rows, r => Assert.Contains(r.CurrentDataId, request.RowsByTestcase[r.TestcaseIndex]));
    }

    [AvaloniaFact]
    public void The_Run_column_header_box_ticks_and_clears_every_row_shown_and_shows_some()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 4));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.Shell.Current!;
        var grid = window.FindControl<DataGrid>("Grid")!;
        var panel = Assert.IsType<StackPanel>(grid.Columns[0].Header);
        Assert.Equal("Run", panel.Children.OfType<TextBlock>().Single().Text);
        var header = panel.Children.OfType<CheckBox>().Single();
        Assert.False(header.IsChecked);

        header.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));     // tick all shown
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(vm.VisibleRows.Count, vm.TickedRowCount);
        Assert.True(header.IsChecked);

        vm.VisibleRows[0].IsTicked = false;
        vm.NotifyTickChanged();
        Assert.Null(header.IsChecked);                           // indeterminate: some

        header.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));     // some -> all
        Assert.Equal(true, header.IsChecked);
        header.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Primitives.ToggleButton.ClickEvent));     // all -> none
        Assert.Equal(0, vm.TickedRowCount);
        Assert.False(header.IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void The_grid_toolbar_has_no_run_ticked_buttons_and_no_reuse_or_rerun_checkboxes()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 2));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var all = window.GetVisualDescendants().ToList();
        var buttons = all.OfType<Button>().Select(b => b.Content as string).Where(c => c is not null).ToList();
        Assert.DoesNotContain("Tick all shown", buttons);
        Assert.DoesNotContain("Clear ticks", buttons);
        Assert.DoesNotContain(all.OfType<CheckBox>(), c => (c.Content as string) is "Reuse last values" or "↻");
        Assert.Single(all.OfType<Button>(), b => b.Name == "RunTickedButton");      // the one in the tree
        window.Close();
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

    [AvaloniaFact]
    public void Ticks_are_all_kept_when_the_rows_are_rebuilt_with_the_tick_boxes_on_screen()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 6));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.Shell.Current!;
        vm.SetShownTicks(true);
        Dispatcher.UIThread.RunJobs();
        var all = vm.TickedRowCount;
        Assert.True(all > 1);

        // a click on a row's own box is counted at once (not one click late)
        var box = window.GetVisualDescendants().OfType<CheckBox>().First(b => b.Tag as string == "tick" && b.IsChecked == true);
        box.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(all - 1, vm.TickedRowCount);
        Assert.Equal(all - 1, vm.VisibleRows.Count(r => r.IsTicked));
        box.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(all, vm.TickedRowCount);

        vm.ShowWholeInstance();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(all, vm.TickedRowCount);
        Assert.Equal(all, vm.VisibleRows.Count(r => r.IsTicked));
        Assert.Equal(all, window.Shell.TickedRows);
        Assert.All(vm.Testcases, t => Assert.Equal(vm.VisibleRows.Count(r => r.IsTicked && r.TestcaseIndex == t.Index), t.TickedRows));
    }
}
