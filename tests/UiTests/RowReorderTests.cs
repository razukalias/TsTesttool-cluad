using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Saving puts the rows of each testcase together in testcase order on every sheet, keeps references pointing at the same data, and keeps formatting.
public class RowReorderTests
{
    private static string TwoTestcases(TempDir dir)
    {
        var path = dir.Combine("two.xlsx");
        using var workbook = new XLWorkbook();
        var config = workbook.AddWorksheet("config");
        var request = workbook.AddWorksheet("request");
        var response = workbook.AddWorksheet("response");
        string[] ch = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb"];
        for (var i = 0; i < ch.Length; i++) config.Cell(1, i + 1).Value = ch[i];
        void Step(int row, int index, string tc, string step, string url)
        {
            string[] v = [index.ToString(), tc, step, "HTTP", step + ".json", url, "POST"];
            for (var i = 0; i < v.Length; i++) config.Cell(row, i + 1).Value = v[i];
        }
        Step(2, 1, "First", "A", "https://x/<From_request_A.name_3>");   // points at request row 3 = the second row of testcase 1... see below
        Step(3, 2, "Second", "B", "https://x/b");
        string[] rh = ["testcaseindex", "testcase", "dataid", "dontrun", "A.name", "B.name"];
        for (var i = 0; i < rh.Length; i++) { request.Cell(1, i + 1).Value = rh[i]; }
        string[] sh = ["testcaseindex", "testcase", "dataid", "{eq}A.id", "{eq}B.id"];
        for (var i = 0; i < sh.Length; i++) { response.Cell(1, i + 1).Value = sh[i]; }
        void Data(int row, int index, string tc, string id, string a, string b)
        {
            request.Cell(row, 1).Value = index; request.Cell(row, 2).Value = tc; request.Cell(row, 3).Value = id; request.Cell(row, 5).Value = a; request.Cell(row, 6).Value = b;
            response.Cell(row, 1).Value = index; response.Cell(row, 2).Value = tc; response.Cell(row, 3).Value = id; response.Cell(row, 4).Value = "ok-" + id;
        }
        Data(2, 1, "First", "F1", "a1", "");
        Data(3, 2, "Second", "S1", "", "b1");
        Data(4, 2, "Second", "S2", "", "b2");
        request.Cell(3, 5).Value = "<From_request_B.name_4>";                           // a reference that must follow S2 when rows move
        request.Cell(4, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFCC00");      // formatting travels with its row
        workbook.SaveAs(path);
        return path;
    }

    private static List<string> Ids(IXLWorksheet sheet, int column = 3) => sheet.RowsUsed().Skip(1).Select(r => r.Cell(column).GetString()).ToList();

    [Fact]
    public void A_row_added_to_the_first_testcase_after_the_second_moves_up_on_save_with_references_following()
    {
        using var dir = new TempDir();
        var path = TwoTestcases(dir);
        var vm = new DataEditorViewModel(path);
        vm.SelectedTestcase = vm.Testcases.First(t => t.Index == 1);
        vm.AddRows(1);                                         // lands at the bottom of both sheets, after testcase 2
        var added = vm.VisibleRows.Last();
        added.Cells[vm.Columns.First(c => c.Title == "A.name").CellIndex] = "a2";
        vm.SaveCommand.Execute(null);

        Assert.True(!vm.StatusIsError, vm.StatusMessage);
        using var workbook = new XLWorkbook(path);
        var request = workbook.Worksheet("request");
        Assert.Equal(["F1", "F2", "S1", "S2"], Ids(request));
        Assert.Equal(["F1", "F2", "S1", "S2"], Ids(workbook.Worksheet("response")));
        Assert.Equal(["1", "1", "2", "2"], Ids(request, 1));
        // S1's reference to S2's name followed S2 from row 4 to row 5
        Assert.Equal("<From_request_B.name_5>", request.Cell(4, 5).GetString());
        // S2's yellow highlight moved with the row; the new row has none
        Assert.Equal("#FFCC00", "#" + request.Cell(5, 5).Style.Fill.BackgroundColor.Color.R.ToString("X2") + request.Cell(5, 5).Style.Fill.BackgroundColor.Color.G.ToString("X2") + request.Cell(5, 5).Style.Fill.BackgroundColor.Color.B.ToString("X2"));
        Assert.Contains("testcase order", vm.StatusMessage);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void A_workbook_already_in_order_is_not_touched()
    {
        using var dir = new TempDir();
        var path = TwoTestcases(dir);
        var vm = new DataEditorViewModel(path);
        vm.SelectedTestcase = vm.Testcases.First(t => t.Index == 2);
        vm.AddRows(1);                                         // at the bottom, which is where testcase 2 belongs
        vm.SaveCommand.Execute(null);
        Assert.DoesNotContain("testcase order", vm.StatusMessage);
        using var workbook = new XLWorkbook(path);
        Assert.Equal(["F1", "S1", "S2"], Ids(workbook.Worksheet("request")).Take(3).ToList());
    }
}
