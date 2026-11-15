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
