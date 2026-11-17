using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class TreeRunTickedRowsTests
{
    private static (EditorShellViewModel Shell, DataEditorViewModel Editor, List<RunRequest> Requests) Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using (var workbook = new XLWorkbook(path))
        {
            var config = workbook.Worksheet("config");
            config.Cell(3, 1).Value = 2; config.Cell(3, 2).Value = "Orders"; config.Cell(3, 3).Value = "CreateOrder"; config.Cell(3, 4).Value = "HTTP";
            config.Cell(3, 5).Value = "order.json"; config.Cell(3, 6).Value = "https://api.example.test/orders"; config.Cell(3, 7).Value = "POST";
            workbook.Worksheet("request").Cell(1, 8).Value = "CreateOrder.sku";
            workbook.Worksheet("response").Cell(1, 7).Value = "{eq}CreateOrder.statusCode";
            workbook.Save();
        }
        var shell = new EditorShellViewModel([("Sample", path)]);
        shell.LoadAsync(shell.Instances[0]).GetAwaiter().GetResult();
        var editor = shell.Instances[0].Editor!;
        var requests = new List<RunRequest>();
        editor.RunHandler = r => { requests.Add(r); return Task.FromResult<string?>(null); };
        return (shell, editor, requests);
    }

    [Fact]
    public async Task Testcase_play_runs_only_its_ticked_rows_and_shows_how_many()
    {
        using var dir = new TempDir();
        var (shell, editor, requests) = Open(dir);
        var first = shell.Instances[0].Testcases[0];
        var second = shell.Instances[0].Testcases[1];
        editor.SelectedTestcase = first;
        editor.VisibleRows[0].IsTicked = true;
        editor.VisibleRows[2].IsTicked = true;
        editor.NotifyTickChanged();

        Assert.Equal("2", first.TickedRowsText);
        Assert.True(first.HasTickedRows);
        Assert.False(second.HasTickedRows);
        Assert.Equal("2", shell.Instances[0].TickedRowsText);

        await shell.RunNodeAsync(first);
        var request = Assert.Single(requests);
        Assert.Equal([first.Index], request.Testcases);
        Assert.Equal(2, request.RowsByTestcase![first.Index].Count);
    }

    [Fact]
    public async Task A_testcase_with_no_ticked_rows_still_runs_everything_even_if_another_testcase_has_ticks()
    {
        using var dir = new TempDir();
        var (shell, editor, requests) = Open(dir);
        var first = shell.Instances[0].Testcases[0];
        var second = shell.Instances[0].Testcases[1];
        editor.SelectedTestcase = first;
        editor.VisibleRows[0].IsTicked = true;
        editor.NotifyTickChanged();

        await shell.RunNodeAsync(second);

        var request = Assert.Single(requests);
        Assert.Equal([second.Index], request.Testcases);
        Assert.Null(request.RowsByTestcase);               // all of its rows, as before
        Assert.Empty(request.DataIds);
    }

    [Fact]
    public async Task Step_play_runs_that_step_on_the_ticked_rows_of_its_testcase()
    {
        using var dir = new TempDir();
        var (shell, editor, requests) = Open(dir);
        var first = shell.Instances[0].Testcases[0];
        editor.SelectedTestcase = first;
        editor.VisibleRows[1].IsTicked = true;
        editor.NotifyTickChanged();
        var step = first.StepNodes[0];

        await shell.RunNodeAsync(step);

        var request = Assert.Single(requests);
        Assert.Equal([first.Index], request.Testcases);
        Assert.Equal([step.Name], request.Steps![first.Index]);
        Assert.Equal([editor.VisibleRows[1].CurrentDataId], request.RowsByTestcase![first.Index]);
    }

    [Fact]
    public async Task Instance_play_runs_only_the_testcases_that_have_ticked_rows_and_only_those_rows()
    {
        using var dir = new TempDir();
        var (shell, editor, requests) = Open(dir);
        var first = shell.Instances[0].Testcases[0];
        editor.SelectedTestcase = first;
        editor.VisibleRows[0].IsTicked = true;
        editor.NotifyTickChanged();

        await shell.RunNodeAsync(shell.Instances[0]);

        var request = Assert.Single(requests);
        Assert.Equal([first.Index], request.Testcases);
        Assert.Single(request.RowsByTestcase![first.Index]);
    }

    [Fact]
    public async Task With_no_ticks_the_play_buttons_work_as_before_and_show_no_number()
    {
        using var dir = new TempDir();
        var (shell, _, requests) = Open(dir);
        var first = shell.Instances[0].Testcases[0];
        Assert.Equal(string.Empty, first.TickedRowsText);
        Assert.False(shell.Instances[0].HasTickedRows);

        await shell.RunNodeAsync(first);
        await shell.RunNodeAsync(shell.Instances[0]);

        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Null(r.RowsByTestcase));
        Assert.Empty(requests[1].Testcases);               // the whole instance
    }
}
