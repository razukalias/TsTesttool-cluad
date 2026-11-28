using Avalonia.Headless.XUnit;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

/// <summary>Item 8: deleting a testcase and saving hung and crashed the app. Every case below must finish (a hang fails by timeout).</summary>
public class DeleteTestcaseCrashTests
{
    private static void Within(Action action, int seconds = 20)
    {
        var task = Task.Run(() => { });   // placeholder to keep the signature simple; the UI thread work runs inline below
        var done = new ManualResetEventSlim();
        Exception? failure = null;
        var timer = new System.Threading.Timer(_ => { if (!done.IsSet) Environment.FailFast("delete testcase hung"); }, null, seconds * 1000, Timeout.Infinite);
        try { action(); } catch (Exception ex) { failure = ex; } finally { done.Set(); timer.Dispose(); }
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [AvaloniaTheory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Delete_each_testcase_of_a_multi_testcase_workbook_through_the_shell_then_save(int index)
    {
        using var dir = new TempDir();
        var (shell, paths) = EditorShellTests.OpenForInstanceView(dir);
        Within(() =>
        {
            var instance = shell.Instances[0];
            var editor = instance.Editor!;
            var testcase = instance.Testcases.FirstOrDefault(t => t.Index == index) ?? instance.Testcases[0];
            shell.SelectedNode = testcase;
            editor.OpenDeleteTestcaseCommand.Execute(null);
            editor.ConfirmTestcaseActionCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            editor.SaveCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(editor.IsDirty);
        });
    }

    [AvaloniaFact]
    public void Delete_in_a_real_window_then_save_and_close()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var window = DataEditorWindow.Open(path, "Scenarios");
        window.Width = 1440; window.Height = 860;
        window.Show();
        Within(() =>
        {
            var vm = window.ViewModel;
            vm.SelectedTestcase = vm.Testcases[1];
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            vm.OpenDeleteTestcaseCommand.Execute(null);
            vm.ConfirmTestcaseActionCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            vm.SaveCommand.Execute(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.Close();
        });
    }

    [AvaloniaFact]
    public void Delete_every_testcase_one_after_another_down_to_none()
    {
        using var dir = new TempDir();
        var (shell, _) = EditorShellTests.OpenForInstanceView(dir);
        Within(() =>
        {
            var editor = shell.Instances[0].Editor!;
            while (editor.Testcases.Count > 0)
            {
                shell.SelectedNode = shell.Instances[0].Testcases[0];
                editor.OpenDeleteTestcaseCommand.Execute(null);
                editor.ConfirmTestcaseActionCommand.Execute(null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                editor.SaveCommand.Execute(null);
            }
            Assert.Empty(editor.Testcases);
        });
    }
}

public class DeleteTestcaseSweepTests
{
    [Theory]
    [InlineData("DataEngine-all-parameters.xlsx")]
    [InlineData(Samples.IntegrationScenarios)]
    [InlineData(Samples.FileHttpScenarios)]
    public void Deleting_each_testcase_then_saving_leaves_a_file_the_runner_can_read(string workbook)
    {
        using var probe = new TempDir();
        var count = new DataEditorViewModel(Samples.Copy(probe, workbook)).Testcases.Count;
        for (var n = 0; n < count; n++)
        {
            using var dir = new TempDir();
            var path = Samples.Copy(dir, workbook);
            var vm = new DataEditorViewModel(path);
            var before = vm.Testcases.Count;
            vm.SelectedTestcase = vm.Testcases[n];
            vm.OpenDeleteTestcaseCommand.Execute(null);
            vm.ConfirmTestcaseActionCommand.Execute(null);
            if (vm.Testcases.Count == before) continue;   // refused: another testcase refers to it (a message, not a crash)
            vm.SaveCommand.Execute(null);
            var read = new LoadTestingTool.Excel.WorkbookReader().Read(path);
            Assert.Equal(before - 1, read.Testcases.Select(t => t.TestcaseIndex).Distinct().Count());
            var reopened = new DataEditorViewModel(path);   // and the editor can open the saved file again
            Assert.Equal(before - 1, reopened.Testcases.Count);
        }
    }
}

public class DeleteTestcaseFuzzTests
{
    [Theory]
    [InlineData(Samples.IntegrationScenarios)]
    [InlineData(Samples.FileHttpScenarios)]
    [InlineData("DataEngine-all-parameters.xlsx")]
    public void Random_mixes_of_edits_and_deletes_followed_by_save_never_throw(string workbook)
    {
        for (var seed = 1; seed <= 25; seed++)
        {
            using var dir = new TempDir();
            var path = Samples.Copy(dir, workbook);
            var vm = new DataEditorViewModel(path);
            var random = new Random(seed);
            var log = new List<string>();
            try
            {
                for (var step = 0; step < 14 && vm.Testcases.Count > 0; step++)
                {
                    vm.SelectedTestcase = vm.Testcases[random.Next(vm.Testcases.Count)];
                    var what = random.Next(10);
                    log.Add($"{what}@{vm.SelectedTestcase.Index}");
                    switch (what)
                    {
                        case 0 or 1 or 2: vm.OpenDeleteTestcaseCommand.Execute(null); vm.ConfirmTestcaseActionCommand.Execute(null); break;
                        case 3: vm.MoveTestcaseUpCommand.Execute(null); break;
                        case 4: vm.MoveTestcaseDownCommand.Execute(null); break;
                        case 5: vm.AddRowCommand.Execute(null); break;
                        case 6: vm.DuplicateRowCommand.Execute(null); break;
                        case 7: vm.OpenCopyTestcaseCommand.Execute(null); vm.TestcasePanelName = "Copy" + step; vm.ConfirmTestcaseActionCommand.Execute(null); break;
                        case 8: vm.UndoCommand.Execute(null); break;
                        case 9: vm.SaveCommand.Execute(null); break;
                    }
                    vm.SaveCommand.Execute(null);   // read the file back after every operation, to name the one that breaks it
                    log[^1] += " " + vm.StatusMessage;
                    _ = new LoadTestingTool.Excel.WorkbookReader().Read(path);
                }
                vm.SaveCommand.Execute(null);
                var read = new LoadTestingTool.Excel.WorkbookReader().Read(path);
                _ = new DataEditorViewModel(path);
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                throw new Xunit.Sdk.XunitException($"seed {seed} ({string.Join(" ", log)}): {ex}");
            }
        }
    }
}

public class SavedWorkbookReadableTests
{
    /// <summary>
    /// Item 8 root cause: every save removed the legacy central-config columns (environments, threads, iterations…) by clearing
    /// their contents, which left empty "used" header cells. The runner's reader then hit several columns with the same empty
    /// name and threw, so the saved workbook could not be loaded (and the app crashed on its next reload).
    /// </summary>
    [Fact]
    public void Saving_a_workbook_with_legacy_central_config_columns_leaves_no_empty_header_cells_and_it_still_loads()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, "DataEngine-all-parameters.xlsx");
        var vm = new DataEditorViewModel(path);

        vm.SaveCommand.Execute(null);

        using (var book = new ClosedXML.Excel.XLWorkbook(path))
            foreach (var name in new[] { "config", "request", "response" })
                Assert.DoesNotContain(book.Worksheet(name).FirstRowUsed()!.CellsUsed(), c => string.IsNullOrWhiteSpace(c.GetString()));
        Assert.NotEmpty(new LoadTestingTool.Excel.WorkbookReader().Read(path).Testcases);
        Assert.NotEmpty(new DataEditorViewModel(path).Testcases);
    }

    [Fact]
    public void The_reader_ignores_empty_header_cells_that_other_tools_leave_behind()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, "DataEngine-all-parameters.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook(path))
        {
            var config = book.Worksheet("config");
            config.Cell(1, 2).Value = "";     // two used-but-empty headers, as Excel can leave after a column is cleared
            config.Cell(1, 14).Value = "";
            book.Save();
        }

        Assert.NotEmpty(new LoadTestingTool.Excel.WorkbookReader().Read(path).Testcases);
    }

    [Fact]
    public void Delete_then_save_leaves_the_workbook_loadable_in_the_editor_and_the_reader()
    {
        using var dir = new TempDir();
        var path = Samples.Copy(dir, Samples.IntegrationScenarios);
        var vm = new DataEditorViewModel(path);
        vm.SelectedTestcase = vm.Testcases[1];
        vm.OpenDeleteTestcaseCommand.Execute(null);
        vm.ConfirmTestcaseActionCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        Assert.Equal(vm.Testcases.Count, new LoadTestingTool.Excel.WorkbookReader().Read(path).Testcases.Select(t => t.TestcaseIndex).Distinct().Count());
        Assert.Equal(vm.Testcases.Count, new DataEditorViewModel(path).Testcases.Count);
    }
}

public class ControlOutputColumnTests
{
    [Fact]
    public void The_output_and_index_columns_are_added_only_on_request_and_only_once()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.Copy(dir, "DataEngine-all-parameters.xlsx"));
        Assert.False(vm.ResponseColumnExists("Poll", "output"));
        Assert.Equal(2, vm.AddOutputColumns("Poll", true));
        Assert.True(vm.ResponseColumnExists("Poll", "output")); Assert.True(vm.ResponseColumnExists("Poll", "index"));
        Assert.Equal(0, vm.AddOutputColumns("Poll", true));
        Assert.Equal(1, vm.AddOutputColumns("Gate", false));
        Assert.False(vm.ResponseColumnExists("Gate", "index"));
    }
}

public class FileStepColumnTests
{
    [Fact]
    public void A_file_step_can_take_its_file_from_a_request_column_once()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.Copy(dir, "DataEngine-all-parameters.xlsx"));
        Assert.True(vm.AddFileColumn("ReadPdf"));
        Assert.False(vm.AddFileColumn("ReadPdf"));
    }
}
