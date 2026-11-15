using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class BackupOnSaveTests
{
    [Fact]
    public void Saving_in_the_editor_copies_the_previous_file_into_the_central_backup_folder()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("appsettings.json"), "{\"DataEngineBackupFolder\":\"backup\"}");
        var instance = Path.Combine(dir.Combine("Instances"), "Demo");
        Directory.CreateDirectory(instance);
        var path = Path.Combine(instance, "Demo.xlsx");
        File.Move(Samples.CreateLarge(dir, rows: 3), path);
        var before = File.ReadAllBytes(path);

        var vm = new DataEditorViewModel(path);
        vm.PasteBlock(0, 0, ClipboardTable.Parse("changed"));
        vm.SaveCommand.Execute(null);

        var backups = Directory.GetFiles(Path.Combine(dir.Combine("backup"), "Demo"), "*.backup.xlsx");
        Assert.Single(backups);
        Assert.Equal(before, File.ReadAllBytes(backups[0]));
        Assert.Equal(backups[0], vm.LastBackupPath);
        Assert.DoesNotContain(Directory.GetFiles(instance), f => f.Contains(".backup."));
    }

    [Fact]
    public void Without_an_appsettings_file_the_backup_stays_beside_the_workbook()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var vm = new DataEditorViewModel(path);
        vm.PasteBlock(0, 0, ClipboardTable.Parse("changed"));
        vm.SaveCommand.Execute(null);
        Assert.Contains(Directory.GetFiles(dir.Path), f => f.Contains(".backup."));
    }
}
