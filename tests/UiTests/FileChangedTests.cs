using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Only at save: a file with another modified time than the one the editor opened or saved asks before it is overwritten.
public class FileChangedTests
{
    private static void Edit(DataEditorViewModel vm) => vm.VisibleRows[0].Cells[1] = "edited";

    [Fact]
    public void Nothing_is_checked_until_saving()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        Assert.False(vm.FileChangedVisible);                       // no background check
        Edit(vm);
        Assert.False(vm.FileChangedVisible);
    }

    [Fact]
    public void Saving_a_file_with_another_modified_time_asks_first_and_saves_nothing()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        Edit(vm);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        var before = File.ReadAllBytes(path);
        vm.SaveCommand.Execute(null);
        Assert.True(vm.FileChangedVisible);
        Assert.Contains("changed on disk", vm.FileChangedText);
        Assert.True(vm.IsDirty);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Cancel_keeps_the_edits_and_the_next_save_asks_again()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        Edit(vm);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        vm.SaveCommand.Execute(null);
        vm.CancelSaveOfChangedFileCommand.Execute(null);
        Assert.False(vm.FileChangedVisible);
        Assert.True(vm.IsDirty);
        vm.SaveCommand.Execute(null);
        Assert.True(vm.FileChangedVisible);
    }

    [Fact]
    public void Overwrite_saves_over_the_changed_file()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        Edit(vm);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        vm.SaveCommand.Execute(null);
        vm.OverwriteChangedFileCommand.Execute(null);
        Assert.False(vm.IsDirty);
        Assert.False(vm.FileChangedVisible);
        vm.VisibleRows[0].Cells[1] = "edited again";
        vm.SaveCommand.Execute(null);                              // the saved time is the known one now: no question
        Assert.False(vm.FileChangedVisible);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Reload_takes_the_new_version_and_a_normal_save_does_not_ask()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        Edit(vm);
        vm.SaveCommand.Execute(null);                              // own save: no question
        Assert.False(vm.FileChangedVisible);
        Edit(vm);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        vm.SaveCommand.Execute(null);
        vm.ReloadChangedFileCommand.Execute(null);
        Assert.False(vm.FileChangedVisible);
        Assert.False(vm.IsDirty);
    }
}
