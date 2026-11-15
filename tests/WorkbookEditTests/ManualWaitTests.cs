using LoadTestingTool.Editing;
using LoadTestingTool.Excel;
using Xunit;

namespace WorkbookEditTests;

public class ManualWaitTests
{
    [Fact]
    public void A_text_Wait_is_a_manual_message_and_is_neither_flagged_nor_rejected_by_the_runner()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);
        var step = document.Config.Rows.First(r => r.Get("stepname").Length > 0);
        step.Set("wait", "Please approve the release");
        document.Save();

        var reloaded = WorkbookDocument.Load(path);
        Assert.DoesNotContain(reloaded.Validate(), p => p.Message.Contains("wait", StringComparison.OrdinalIgnoreCase));
        var read = new WorkbookReader().Read(path);
        var parsed = read.Testcases.SelectMany(t => t.Steps).First(s => s.StepName == step.Get("stepname"));
        Assert.Equal(0, parsed.WaitMs);
    }
}

public class CentralBackupTests
{
    [Fact]
    public void A_backup_folder_receives_the_copy_instead_of_the_workbook_folder()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var original = File.ReadAllBytes(path);
        var central = dir.Combine("central-backups");
        var document = WorkbookDocument.Load(path);
        document.Config.Rows[0]["timeout"] = "45";

        var result = document.Save(new WorkbookSaveOptions { BackupFolder = central });

        Assert.NotNull(result.BackupPath);
        Assert.StartsWith(Path.GetFullPath(central), result.BackupPath);
        Assert.EndsWith(".backup.xlsx", result.BackupPath);
        Assert.Equal(original, File.ReadAllBytes(result.BackupPath!));
        Assert.DoesNotContain(Directory.GetFiles(Path.GetDirectoryName(path)!), f => f.Contains(".backup."));
    }
}
