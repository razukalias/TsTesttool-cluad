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

    [AvaloniaFact]
    public void The_exchange_folder_and_the_message_belong_to_the_instance_that_is_selected()
    {
        var root = Path.Combine(Path.GetTempPath(), "twoinst_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var a = new LoadTestingTool.UI.InstanceInfo { Name = "A", FolderPath = Path.Combine(root, "A") };
            var b = new LoadTestingTool.UI.InstanceInfo { Name = "B", FolderPath = Path.Combine(root, "B") };
            Directory.CreateDirectory(a.FolderPath); Directory.CreateDirectory(b.FolderPath);
            var main = new LoadTestingTool.UI.MainWindow();
            var build = typeof(LoadTestingTool.UI.MainWindow).GetMethod("UpkeepControls", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Avalonia.Controls.Control Build(LoadTestingTool.UI.InstanceInfo item) => (Avalonia.Controls.Control)build.Invoke(main, [item, new SqliteHistoryStore(item.HistoryFolder)])!;
            static Avalonia.Controls.TextBox Folder(Avalonia.Controls.Control c) => c.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().Single(t => (string?)t.Tag == "history-exchange-folder");
            static string Message(Avalonia.Controls.Control c) => c.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(t => (string?)t.Tag == "history-upkeep-message").Text ?? "";
            static void Click(Avalonia.Controls.Control c, string label) { c.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(x => x.Content?.ToString() == label).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }

            var first = Build(a);
            var host = new Avalonia.Controls.Window { Content = first }; host.Show();
            Assert.Equal(Path.Combine(a.HistoryFolder, "Exchange"), Folder(first).Text);
            Click(first, "Export shown run");                                                  // nothing to export: says so, for A
            Assert.Contains("no run", Message(first));
            Folder(first).Text = Path.Combine(root, "typed for A");

            // selecting B: its own default folder and no message from A
            host.Content = null;
            var second = Build(b);
            host.Content = second;
            Assert.Equal(Path.Combine(b.HistoryFolder, "Exchange"), Folder(second).Text);
            Assert.Equal("", Message(second));

            // back to A: what was typed for A is still there, and so is its message
            host.Content = null;
            var again = Build(a);
            host.Content = again;
            Assert.Equal(Path.Combine(root, "typed for A"), Folder(again).Text);
            Assert.Contains("no run", Message(again));
            host.Close();
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }

    [AvaloniaFact]
    public async Task The_Artifacts_tab_cleans_up_exports_imports_and_restores_the_workbook_of_a_run()
    {
        var root = Path.Combine(Path.GetTempPath(), "upkeep_" + Guid.NewGuid().ToString("N")[..8]);
        var inst = Path.Combine(root, "Instances", "Demo"); Directory.CreateDirectory(inst);
        using (var dir = new TempDir()) File.Copy(Samples.CreateLarge(dir, rows: 3), Path.Combine(inst, "DataEngine.xlsx"));
        File.WriteAllText(Path.Combine(root, "appsettings.json"), "{\"InstancesRoot\":\"Instances\",\"WorkbookName\":\"DataEngine.xlsx\"}");
        var history = Path.Combine(inst, "History");
        var store = new SqliteHistoryStore(history);
        var config = new AppConfig { HistoryFolder = history, HistoryStorage = HistoryStorage.Database };
        void Save(string runId, DateTimeOffset start, string book)
        {
            var run = new RunResult { RunId = runId, StartedAt = start, EndedAt = start.AddMinutes(1) };
            run.Testcases.Add(new TestcaseResult { TestcaseIndex = 1, Testcase = "Order", DataId = "D1", Iteration = 1, Environment = "qa", StartedAt = start, EndedAt = start.AddSeconds(1) });
            HistoryStores.SaveToDatabase(config, run, new RunValueMemory.Snapshot(), null, System.Text.Encoding.UTF8.GetBytes(book));
        }
        Save("ancient", DateTimeOffset.Now.AddDays(-90), "old book");
        Save("recent", DateTimeOffset.Now.AddHours(-1), "new book");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            var main = new LoadTestingTool.UI.MainWindow();
            var create = typeof(LoadTestingTool.UI.MainWindow).GetMethod("CreateStandaloneEditor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
            var editor = (DataEditorWindow)create.Invoke(main, null)!; editor.Height = 1800; editor.Show();
            var tabs = ((DockPanel)editor.FindControl<Border>("RightHost")!.Child!).Children.OfType<TabControl>().Single();
            await editor.Shell.LoadAsync(editor.Shell.Instances[0]);
            editor.Shell.SelectedNode = editor.Shell.Instances[0];
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var selected = (LoadTestingTool.UI.InstanceInfo)typeof(LoadTestingTool.UI.MainWindow).GetField("_selectedInstance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
            selected.FolderPath = inst; selected.WorkbookPath = Path.Combine(inst, "DataEngine.xlsx"); selected.HistoryStorage = HistoryStorage.Database;
            // saving the retention must go to a settings file of this test, never to the real central one
            var central = Path.Combine(root, "central.json"); File.WriteAllText(central, "{}");
            var appConfig = typeof(LoadTestingTool.UI.MainWindow).GetField("_config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
            appConfig.GetType().GetProperty("SettingsFile")!.SetValue(appConfig, central);
            typeof(LoadTestingTool.UI.MainWindow).GetMethod("RefreshDashboardDetails", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.Invoke(main, [true]);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            tabs.SelectedIndex = tabs.Items.Count - 1;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            string Texts() => string.Join("|", tabs.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text));
            void Click(string label) { tabs.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == label).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            Assert.Contains("Workbook copies: 2 versions", Texts());

            // clean up with keep-for-ever removes nothing
            Click("Clean up now");
            Assert.Contains("0 runs removed", Texts());
            Assert.Equal(2, store.Summary().Runs);

            // 30 days: the 90 day old run goes, with the workbook version only it used
            tabs.GetVisualDescendants().OfType<NumericUpDown>().First(n => (string?)n.Tag == "history-retention-days").Value = 30;
            Click("Save");
            Assert.Equal(30, selected.HistoryRetentionDays);
            Assert.Contains("\"HistoryRetentionDays\": 30", File.ReadAllText(central));
            Click("Clean up now");
            Assert.Contains("1 runs removed", Texts());
            Assert.Equal(["recent"], store.ListRuns(0, 10).Select(r => r.RunId).ToArray());
            Assert.Equal(1, store.SnapshotTotals().Versions);

            // export the shown run (the newest when none is chosen), import it into a second instance's database, restore the workbook next to the live one
            var exchange = Path.Combine(root, "Exchange");
            tabs.GetVisualDescendants().OfType<TextBox>().First(t => (string?)t.Tag == "history-exchange-folder").Text = exchange;
            Click("Export shown run");
            var exported = Path.Combine(exchange, "Run_recent.runhistory.sqlite");
            Assert.True(File.Exists(exported), Texts());
            var other = new SqliteHistoryStore(Path.Combine(root, "other"));
            Assert.Equal((1, 0), other.ImportRunFile(exported));

            store.DeleteRuns(["recent"]);
            Click("Import run files");                                             // bring the file back from the exchange folder
            Assert.Contains("1 runs added", Texts());
            Assert.Equal(["recent"], store.ListRuns(0, 10).Select(r => r.RunId).ToArray());

            Click("Save the workbook as it was in the shown run");
            var restored = Path.Combine(inst, "DataEngine_as_of_recent.xlsx");
            Assert.True(File.Exists(restored), Texts());
            Assert.Equal("new book", File.ReadAllText(restored));
            Click("Save the workbook as it was in the shown run");                  // never over an existing file
            Assert.Contains("already there", Texts());

            // logs: the checkboxes are saved with the instance, old log files are read into the database by their short run id
            var logs = Path.Combine(inst, "Logs"); Directory.CreateDirectory(logs);
            selected.HistoryStorage = HistoryStorage.Database;
            selected.EnvironmentOptions.Clear(); selected.EnvironmentOptions.Add("qa");
            File.WriteAllText(Path.Combine(logs, "Order_qa_passed_20261126_100000_T1_I1_Run_recent.log"), "old log text");   // "recent" is shorter than 8: the whole id is the short id
            tabs.GetVisualDescendants().OfType<CheckBox>().First(c => (string?)c.Tag == "history-delete-log-files").IsChecked = true;
            Assert.True(selected.HistoryDeleteLogFiles);
            Assert.Contains("\"HistoryDeleteLogFiles\": true", File.ReadAllText(central));
            tabs.GetVisualDescendants().OfType<CheckBox>().First(c => (string?)c.Tag == "history-logs-in-db").IsChecked = false;
            Assert.False(selected.HistoryLogsInDatabase);
            Click("Import logs of existing runs");
            Assert.Contains("1 logs stored", Texts());
            Assert.Equal("old log text", store.LoadLog("recent", "Order", "qa"));
            Assert.Contains("Logs: 1 stored", Texts());

            // the step details window takes a finished run's log from the database, into a temporary file named like the original
            var request = new LoadTestingTool.UI.DashboardRequest("Create", "HTTP", "Order", "D1", "qa", "PASS", 10, "", "");
            var fromDatabase = LoadTestingTool.UI.MainWindow.TryLogFromDatabase(selected, request, out var label);
            Assert.NotNull(fromDatabase);
            Assert.Equal("old log text", File.ReadAllText(fromDatabase!));
            Assert.Contains("history database", label);
            Assert.Equal("Order_qa_passed_20261126_100000_T1_I1_Run_recent.log", Path.GetFileName(fromDatabase));
            Directory.Delete(Path.GetDirectoryName(fromDatabase)!, true);
            Assert.Null(LoadTestingTool.UI.MainWindow.TryLogFromDatabase(selected, request with { Testcase = "Other" }, out _));      // no log for another testcase: the caller falls back to the file
            selected.HistoryStorage = HistoryStorage.Files;
            Assert.Null(LoadTestingTool.UI.MainWindow.TryLogFromDatabase(selected, request, out _));                                    // the old way never looks in the database
            selected.HistoryStorage = HistoryStorage.Database;

            // replacing the live workbook needs a second click and keeps the replaced one aside
            File.WriteAllText(Path.Combine(inst, "DataEngine.xlsx"), "edited since");
            Click("Replace the live workbook with the shown run's copy");
            Assert.Contains("Click again to replace the workbook with the copy of recent", Texts());
            Assert.Equal("edited since", File.ReadAllText(Path.Combine(inst, "DataEngine.xlsx")));
            Click("Click again to replace the workbook with the copy of recent");
            Assert.Equal("new book", File.ReadAllText(Path.Combine(inst, "DataEngine.xlsx")));
            Assert.Equal("edited since", File.ReadAllText(Directory.GetFiles(inst, "DataEngine_before_restore_*.xlsx").Single()));

            tabs.GetVisualDescendants().OfType<NumericUpDown>().First(n => (string?)n.Tag == "history-retention-mb").Value = 50;
            Click("Save");
            Assert.Equal(50, selected.HistoryRetentionMaxMb);
            Assert.Contains("\"HistoryRetentionMaxMb\": 50", File.ReadAllText(central));

            Click("Export shown run to Excel");
            Assert.True(File.Exists(Path.Combine(exchange, "Run_recent.xlsx")), Texts());

            // deleting needs a second click, and says what it removes
            Click("Delete shown run");
            Assert.Contains("Click again to delete recent", Texts());
            Assert.Equal(["recent"], store.ListRuns(0, 10).Select(r => r.RunId).ToArray());
            Click("Click again to delete recent");
            Assert.Contains("Run recent deleted.", Texts());
            Assert.Empty(store.ListRuns(0, 10));
            Assert.Equal(0, store.SnapshotTotals().Versions);
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
