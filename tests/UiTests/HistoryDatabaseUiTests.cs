using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class HistoryDatabaseUiTests
{
    private static void WriteOldWorkbook(string folder, string runId, DateTimeOffset start)
    {
        Directory.CreateDirectory(folder);
        var run = new RunResult { RunId = runId, StartedAt = start, EndedAt = start.AddMinutes(1) };
        var state = new RunValueMemory.Snapshot();
        foreach (var id in new[] { "D1", "D2", "D3" })
        {
            var t = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = id, Iteration = 1, Environment = "qa", StartedAt = start, EndedAt = start.AddSeconds(1) };
            t.Requests.Add(new RequestResult { Testcase = "Order", StepName = "Create", HttpStatus = id == "D2" ? 500 : 200 });
            run.Testcases.Add(t);
            state.Generated[$"1|{id}|1|qa|guid|1"] = "g-" + id;
        }
        using var book = new XLWorkbook();
        book.AddWorksheet("Summary").Cell(1, 1).Value = "x";
        HistoryRunStateStore.WriteSheet(book, state, run);
        book.SaveAs(Path.Combine(folder, $"Execution_History_{runId}.xlsx"));
    }

    [AvaloniaFact]
    public async Task The_Artifacts_tab_imports_old_history_into_the_instance_database_and_compares_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "probe_" + Guid.NewGuid().ToString("N")[..8]);
        var inst = Path.Combine(root, "Instances", "Demo"); Directory.CreateDirectory(inst);
        using (var dir = new TempDir()) File.Copy(Samples.CreateLarge(dir, rows: 3), Path.Combine(inst, "DataEngine.xlsx"));
        File.WriteAllText(Path.Combine(root, "appsettings.json"), "{\"InstancesRoot\":\"Instances\",\"WorkbookName\":\"DataEngine.xlsx\"}");
        var history = Path.Combine(inst, "History");
        WriteOldWorkbook(history, "20260301_100000_aaaaaaaa", new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));
        WriteOldWorkbook(history, "20260320_100000_bbbbbbbb", new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero));
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            var main = new LoadTestingTool.UI.MainWindow();
            var create = typeof(LoadTestingTool.UI.MainWindow).GetMethod("CreateStandaloneEditor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
            var created = (DataEditorWindow?)create.Invoke(main, null);
            Assert.True(created is not null, ((TextBlock)typeof(LoadTestingTool.UI.MainWindow).GetField("_details", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!).Text);
            var editor = created!; editor.Height = 1500; editor.Show();
            var tabs = ((DockPanel)editor.FindControl<Border>("RightHost")!.Child!).Children.OfType<TabControl>().Single();
            await editor.Shell.LoadAsync(editor.Shell.Instances[0]);
            editor.Shell.SelectedNode = editor.Shell.Instances[0];
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            // the dashboard may list other instances; the one under test is pointed at the temporary folder
            var selected = (LoadTestingTool.UI.InstanceInfo)typeof(LoadTestingTool.UI.MainWindow).GetField("_selectedInstance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
            selected.FolderPath = inst;
            typeof(LoadTestingTool.UI.MainWindow).GetMethod("RefreshDashboardDetails", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.Invoke(main, [true]);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            tabs.SelectedIndex = tabs.Items.Count - 1;          // Artifacts is the last tab
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            string Texts() => string.Join("|", tabs.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text));
            Assert.Contains("HISTORY DATABASE", Texts());
            Assert.Contains("not created yet", Texts());
            Assert.Contains("history workbooks only", Texts());

            var from = tabs.GetVisualDescendants().OfType<CalendarDatePicker>().First(c => c.Watermark == "From (optional)");
            from.SelectedDate = new DateTime(2026, 3, 10);                       // only the run of 20 March is in range
            void Click(string label) => tabs.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == label).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            async Task Settle(Func<bool> done) { for (var i = 0; i < 200 && !done(); i++) { await Task.Delay(50); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } }

            Click("Import runs");
            await Settle(() => Texts().Contains("runs added"));
            Assert.Contains("1 runs added", Texts());
            var store = new SqliteHistoryStore(history);
            Assert.Equal(["20260320_100000_bbbbbbbb"], store.ScanPage(0, 10).Rows.Select(r => r.RunId).Distinct().ToArray());

            Click("Import runs");                                                  // the same period again adds nothing
            await Settle(() => Texts().Contains("0 runs added"));
            Assert.Contains("1 already there", Texts());

            from.SelectedDate = null;
            Click("Import runs");
            await Settle(() => Texts().Contains("1 runs added"));
            Click("Compare with workbooks");
            await Settle(() => Texts().Contains("agree"));
            Assert.Contains("The database and the workbooks agree.", Texts());
            Assert.Equal(2, store.Summary().Runs);
        }
        finally { Directory.SetCurrentDirectory(previous); try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [Fact]
    public void The_dashboard_history_explorer_lists_the_runs_of_the_database_and_loads_bodies_and_assertions_only_for_a_comparison()
    {
        var inst = Path.Combine(Path.GetTempPath(), "explorer_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(inst);
        try
        {
            var start = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
            var config = new AppConfig { HistoryFolder = Path.Combine(inst, "History"), HistoryStorage = HistoryStorage.Database, SaveRequestBodyToHistory = true, SaveResponseBodyToHistory = true };
            foreach (var (runId, failing) in new[] { ("run_a", false), ("run_b", true) })
            {
                var run = new RunResult { RunId = runId, StartedAt = start.AddHours(runId == "run_a" ? 0 : 1), EndedAt = start.AddHours(2) };
                var t = new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = "D1", Iteration = 1, Environment = "qa", StartedAt = start, EndedAt = start.AddSeconds(1) };
                var request = new RequestResult { Testcase = "Order", TestcaseIndex = 1, DataId = "D1", StepName = "Create", Environment = "qa", HttpStatus = failing ? 500 : 200, DurationMs = 40, ResponseBody = failing ? "{\"error\":true}" : "{\"ok\":true}", StartedAt = start };
                request.Assertions.Add(new AssertionResult { RequestId = request.RequestId, Testcase = "Order", DataId = "D1", StepName = "Create", ResponsePath = "ok", AssertionVerb = "eq", ExpectedValue = "true", ActualValue = failing ? "(none)" : "true", Result = failing ? "FAIL" : "PASS" });
                t.Requests.Add(request);
                run.Testcases.Add(t);
                HistoryStores.SaveToDatabase(config, run, null, null);
            }
            var item = Activator.CreateInstance(typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.InstanceInfo")!, nonPublic: true)!;
            item.GetType().GetProperty("FolderPath")!.SetValue(item, inst);
            item.GetType().GetProperty("Name")!.SetValue(item, "Demo");
            var read = typeof(LoadTestingTool.UI.MainWindow).GetMethod("ReadDatabaseEntries", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var entries = ((System.Collections.IEnumerable)read.Invoke(null, [item, 500])!).Cast<object>().ToList();

            string Get(object o, string name) => Convert.ToString(o.GetType().GetProperty(name)!.GetValue(o))!;
            Assert.Equal(["run_b", "run_a"], entries.Select(e => Get(e, "RunId")));        // newest first
            Assert.Equal(["FAILED", "PASSED"], entries.Select(e => Get(e, "Status")));
            Assert.All(entries, e => Assert.Equal("True", Get(e, "InDatabase")));
            var requests = (System.Collections.IList)entries[0].GetType().GetProperty("Requests")!.GetValue(entries[0])!;
            Assert.Single(requests);
            Assert.Equal(string.Empty, Get(requests[0]!, "ResponseBody"));                   // a list does not read bodies
            Assert.Empty((System.Collections.IList)entries[0].GetType().GetProperty("Assertions")!.GetValue(entries[0])!);
            Assert.Contains("#run_b", Get(entries[0], "Path"));                              // an entry is told apart by the run, not by a file
        }
        finally { try { Directory.Delete(inst, true); } catch (IOException) { } }
    }
}
