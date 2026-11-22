using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class FileChangedTests
{
    [Fact]
    public void A_file_with_a_different_modified_time_asks_to_reload_and_reload_takes_the_new_version()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        Assert.False(vm.CheckFileChanged());                       // nothing changed since opening
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        Assert.True(vm.CheckFileChanged());
        Assert.True(vm.FileChangedVisible);
        Assert.Contains("changed on disk", vm.FileChangedText);
        vm.ReloadChangedFileCommand.Execute(null);
        Assert.False(vm.FileChangedVisible);
        Assert.False(vm.CheckFileChanged());                       // the new time is the known one
    }

    [Fact]
    public void Keep_what_I_see_accepts_the_new_time_and_an_own_save_does_not_trigger_the_question()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        Assert.True(vm.CheckFileChanged());
        vm.KeepMyVersionCommand.Execute(null);
        Assert.False(vm.FileChangedVisible);
        Assert.False(vm.CheckFileChanged());

        vm.VisibleRows[0].Cells[1] = "edited";
        vm.SaveCommand.Execute(null);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CheckFileChanged());                       // our own save is not "someone else"
    }

    [Fact]
    public void Unsaved_edits_are_mentioned_in_the_question()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        vm.VisibleRows[0].Cells[1] = "edited";
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        Assert.True(vm.CheckFileChanged());
        Assert.Contains("unsaved edits", vm.FileChangedText);
    }

    [AvaloniaFact]
    public void The_window_checks_when_activated_and_shows_the_banner()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var window = DataEditorWindow.Open(path, "Customers");
        window.Width = 1200; window.Height = 700; window.Show();
        Dispatcher.UIThread.RunJobs();
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        window.CheckFilesChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ViewModel.FileChangedVisible);
        window.Close();
    }
}
