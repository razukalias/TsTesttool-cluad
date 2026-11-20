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

public class StepResponseTimeoutTests
{
    [Fact]
    public void The_responsetimeout_column_is_read_in_milliseconds_next_to_the_seconds_timeout()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir);
        var document = WorkbookDocument.Load(path);
        document.Config.EnsureColumn("responsetimeout");
        document.Config.Rows[0]["timeout"] = "30";
        document.Config.Rows[0]["responsetimeout"] = "1500";
        document.Save(new WorkbookSaveOptions());

        var step = new LoadTestingTool.Excel.WorkbookReader().Read(path).Testcases.SelectMany(t => t.Steps).First();
        Assert.Equal(1500, step.ResponseTimeoutMs);
        Assert.Equal(30, step.TimeoutSeconds);
    }
}

public class StepConfigFilesTests
{
    [Theory]
    [InlineData("{\"source\": \"Queries/Get.graphql\", \"operationName\":\"Get\"}", "Queries/Get.graphql")]
    [InlineData("{\"Source\":\"a\\\\b.sql\"}", "a/b.sql")]
    [InlineData("source=Queries/Get.sql;parameters=x", "Queries/Get.sql")]
    [InlineData("{\"source\":\"Queries/_name_.sql\"}", null)]
    [InlineData("{\"query\":\"{ a }\"}", null)]
    [InlineData("", null)]
    public void The_source_file_is_read_from_json_and_from_key_value_text(string config, string? expected)
        => Assert.Equal(expected, StepConfigFiles.SourceOf(config));

    [Fact]
    public void Only_the_source_value_is_replaced()
    {
        Assert.Equal("{\"source\": \"Q/x copy.graphql\", \"operationName\":\"Get\"}", StepConfigFiles.WithSource("{\"source\": \"Q/x.graphql\", \"operationName\":\"Get\"}", "Q/x copy.graphql"));
        Assert.Equal("a=1;source=Q/x copy.sql;b=2", StepConfigFiles.WithSource("a=1;source=Q/x.sql;b=2", "Q/x copy.sql"));
    }
}
