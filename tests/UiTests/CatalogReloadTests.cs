using Avalonia.Headless.XUnit;
using ClosedXML.Excel;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class CatalogReloadTests
{
    [AvaloniaFact]
    public void A_testcase_saved_after_the_app_started_is_known_to_the_run_check()
    {
        var folder = Path.Combine(Path.GetTempPath(), "catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "w.xlsx");
        void Write(int cases)
        {
            using var book = new XLWorkbook();
            var config = book.AddWorksheet("config");
            string[] header = ["testcaseindex", "testcase", "stepname", "steptype", "enabled"];
            for (var c = 0; c < header.Length; c++) config.Cell(1, c + 1).Value = header[c];
            for (var i = 1; i <= cases; i++) { config.Cell(i + 1, 1).Value = i; config.Cell(i + 1, 2).Value = "case" + i; config.Cell(i + 1, 3).Value = "Step1"; config.Cell(i + 1, 4).Value = "HTTP"; config.Cell(i + 1, 5).Value = "true"; }
            var request = book.AddWorksheet("request");
            request.Cell(1, 1).Value = "testcaseindex"; request.Cell(1, 2).Value = "testcase"; request.Cell(1, 3).Value = "dataid";
            for (var i = 1; i <= cases; i++) { request.Cell(i + 1, 1).Value = i; request.Cell(i + 1, 2).Value = "case" + i; request.Cell(i + 1, 3).Value = "R" + i; }
            book.AddWorksheet("response");
            book.SaveAs(path);
        }
        try
        {
            Write(2);
            var window = new MainWindow();
            var item = new InstanceInfo { Name = "x", FolderPath = folder, WorkbookPath = path };
            window.ReloadWorkbookCatalog(item);
            Assert.DoesNotContain(item.TestcaseOptions, o => o.Index == 3);
            Write(3);   // saved in the editor afterwards
            window.ReloadWorkbookCatalog(item);
            Assert.Contains(item.TestcaseOptions, o => o.Index == 3);
            Assert.Contains("R3", item.DataIdOptions[3]);
        }
        finally { Directory.Delete(folder, true); }
    }
}
