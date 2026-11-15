using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class BackupsWindowTests
{
    private static (string Workbook, string Backups, DataEditorViewModel Editor) Setup(TempDir dir)
    {
        File.WriteAllText(dir.Combine("appsettings.json"), "{\"DataEngineBackupFolder\":\"backup\"}");
        var instance = Path.Combine(dir.Combine("Instances"), "Demo");
        Directory.CreateDirectory(instance);
        var workbook = Path.Combine(instance, "Demo.xlsx");
        File.Move(Samples.CreateLarge(dir, rows: 3), workbook);
        var backups = Path.Combine(dir.Combine("backup"), "Demo");
        Directory.CreateDirectory(backups);
        return (workbook, backups, new DataEditorViewModel(workbook));
    }

    [Fact]
    public void Lists_only_this_workbooks_backups_newest_first_and_filters_by_time()
    {
        using var dir = new TempDir();
        var (workbook, backups, _) = Setup(dir);
        foreach (var stamp in new[] { "20260101_100000000", "20260103_100000000", "20260102_100000000" })
            File.WriteAllText(Path.Combine(backups, $"Demo_{stamp}.backup.xlsx"), stamp);
        File.WriteAllText(Path.Combine(backups, "Demo_2_20260104_100000000.backup.xlsx"), "other workbook");
        File.WriteAllText(Path.Combine(backups, "Other_20260105_100000000.backup.xlsx"), "other");

        var list = WorkbookBackups.List(workbook);

        Assert.Equal(new[] { new DateTime(2026, 1, 3, 10, 0, 0), new DateTime(2026, 1, 2, 10, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0) }, list.Select(b => b.Taken));
    }

    [AvaloniaFact]
    public async Task Restore_puts_the_chosen_backup_in_place_keeps_the_current_one_and_reloads_the_editor()
    {
        using var dir = new TempDir();
        var (workbook, backups, editor) = Setup(dir);
        var original = File.ReadAllBytes(workbook);
        File.WriteAllBytes(Path.Combine(backups, "Demo_20260101_100000000.backup.xlsx"), original);
        editor.PasteBlock(0, 0, ClipboardTable.Parse("edited"));
        editor.SaveCommand.Execute(null);                       // the file now differs from the backup
        var edited = File.ReadAllBytes(workbook);
        Assert.NotEqual(original, edited);

        var window = new BackupsWindow(editor) { Confirm = _ => Task.FromResult(true) };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.Contains(list.Items.OfType<ListBoxItem>(), i => ((BackupFile)i.Tag!).Path.EndsWith("Demo_20260101_100000000.backup.xlsx"));
        list.SelectedItem = list.Items.OfType<ListBoxItem>().First(i => ((BackupFile)i.Tag!).Path.EndsWith("Demo_20260101_100000000.backup.xlsx"));

        await window.RestoreAsync();

        Assert.Equal(original, File.ReadAllBytes(workbook));
        Assert.Contains(Directory.GetFiles(backups, "*.backup.xlsx"), f => File.ReadAllBytes(f).SequenceEqual(edited));
        Assert.False(editor.IsDirty);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Restore_is_refused_while_the_workbook_has_unsaved_edits_or_the_app_says_no()
    {
        using var dir = new TempDir();
        var (workbook, backups, editor) = Setup(dir);
        File.WriteAllText(Path.Combine(backups, "Demo_20260101_100000000.backup.xlsx"), "x");
        var before = File.ReadAllBytes(workbook);

        var window = new BackupsWindow(editor, () => "it is running") { Confirm = _ => Task.FromResult(true) };
        window.Show();
        await window.RestoreAsync();
        Assert.Equal(before, File.ReadAllBytes(workbook));

        var window2 = new BackupsWindow(editor) { Confirm = _ => Task.FromResult(true) };
        window2.Show();
        editor.PasteBlock(0, 0, ClipboardTable.Parse("unsaved"));
        await window2.RestoreAsync();
        Assert.Equal(before, File.ReadAllBytes(workbook));
        window.Close(); window2.Close();
    }
}

public class BackupsScreenshotTests
{
    [AvaloniaFact]
    public void Screenshot_of_the_backups_window()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("appsettings.json"), "{\"DataEngineBackupFolder\":\"backup\"}");
        var instance = Path.Combine(dir.Combine("Instances"), "large");
        Directory.CreateDirectory(instance);
        var workbook = Path.Combine(instance, "large.xlsx");
        File.Move(Samples.CreateLarge(dir, rows: 3), workbook);
        var backups = Path.Combine(dir.Combine("backup"), "large");
        Directory.CreateDirectory(backups);
        foreach (var stamp in new[] { "20261113_093011250", "20261114_171502004", "20261115_101244871" })
            File.Copy(workbook, Path.Combine(backups, $"large_{stamp}.backup.xlsx"));
        var editor = DataEditorWindow.Open(workbook, null);
        editor.Width = 1500; editor.Height = 900; editor.Show();
        var window = new BackupsWindow(editor.Shell.Current!);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(Path.Combine(shots, "backups-window.png"));
        editor.CaptureRenderedFrame()!.Save(Path.Combine(shots, "backups-toolbar.png"));
        window.Close(); editor.Close();
    }
}
