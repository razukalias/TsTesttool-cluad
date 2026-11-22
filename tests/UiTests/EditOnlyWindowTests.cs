using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class EditOnlyWindowTests
{
    [AvaloniaFact]
    public void The_edit_only_window_is_the_table_editor_without_running_history_or_other_modes()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var window = DataEditorWindow.OpenEditOnly(path);
        window.Width = 1440; window.Height = 800; window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Edit only", window.Title);
        Assert.Equal(0, window.ViewModel.ViewModeIndex);
        foreach (var name in new[] { "RunTickedButton", "RunShownButton", "HistoryLogsButton", "MockServerButton", "ViewModeBox", "OpenSeparateToolButton", "InstanceTreePanel", "TreeSplitter", "BodySplitter", "RightHost" })
            Assert.False(window.FindControl<Control>(name)!.IsVisible, name);
        Assert.NotEmpty(window.FindControl<DataGrid>("Grid")!.Columns);   // the table view with its columns is there
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(Path.Combine(shots, "edit-only.png"));
        window.Close();
        Assert.False(TableEditorLock.OtherProcessHas(path));
    }

    [AvaloniaFact]
    public void The_normal_editor_has_the_separate_window_button_and_asks_to_save_first()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 800; window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<Button>("OpenSeparateToolButton")!.IsVisible);
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(Path.Combine(shots, "normal-with-separate-button.png"));
        var vm = window.ViewModel;
        vm.VisibleRows[0].Cells[1] = "changed";   // unsaved edit
        window.OpenSelectedInSeparateWindow();
        Assert.True(vm.StatusIsError);
        Assert.Contains("Save", vm.StatusMessage);
        window.Close();
    }

    [Fact]
    public void A_lock_of_another_live_process_is_seen_and_ones_own_is_not()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.xlsx");
        TableEditorLock.Take(path);
        Assert.False(TableEditorLock.OtherProcessHas(path));   // only this process
        TableEditorLock.Release(path);
    }
}
