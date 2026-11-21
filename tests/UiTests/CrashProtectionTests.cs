using Avalonia.Headless.XUnit;
using LoadTestingTool.Editing;
using LoadTestingTool.UI;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class CrashProtectionTests
{
    private sealed class Isolated : IDisposable
    {
        public readonly TempDir Dir = new();
        private readonly string _logs = CrashGuard.LogFolder, _recovery = RecoveryStore.Folder;
        public string Logs => System.IO.Path.Combine(Dir.Path, "logs");
        public Isolated() { CrashGuard.LogFolder = Logs; RecoveryStore.Folder = System.IO.Path.Combine(Dir.Path, "recovery"); }
        public void Dispose() { CrashGuard.LogFolder = _logs; RecoveryStore.Folder = _recovery; Dir.Dispose(); }
    }

    [AvaloniaFact]
    public void A_failing_handler_is_caught_logged_with_the_last_actions_and_reported()
    {
        using var iso = new Isolated();
        var messages = new List<string>();
        Action<string> hear = messages.Add;
        CrashGuard.Reported += hear;
        try
        {
            var before = CrashGuard.CaughtCount;
            CrashGuard.Trace("opened the wizard for cell 5");
            Assert.False(CrashGuard.Guard(() => throw new InvalidOperationException("stale row"), "the cell wizard"));
            Assert.True(CrashGuard.Guard(() => { }, "nothing"));
            Assert.Equal(before + 1, CrashGuard.CaughtCount);
            Assert.Single(messages);
            Assert.Contains("Something went wrong in the cell wizard: stale row", messages[0]);
            var log = File.ReadAllText(System.IO.Path.Combine(iso.Logs, "ui-errors.log"));
            Assert.Contains("InvalidOperationException", log);
            Assert.Contains("opened the wizard for cell 5", log);
        }
        finally { CrashGuard.Reported -= hear; }
    }

    [AvaloniaFact]   // Notify touches the dispatcher, which must stay on the UI thread of the test session
    public void The_hang_monitor_reports_a_freeze_once_after_the_limit_and_tells_when_the_window_answers_again()
    {
        using var iso = new Isolated();
        var start = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var monitor = new HangMonitor(TimeSpan.FromSeconds(10), start);
        CrashGuard.Trace("moved to the next cell");
        Assert.False(monitor.Check(start.AddSeconds(9)));
        Assert.True(monitor.Check(start.AddSeconds(11)));
        Assert.False(monitor.Check(start.AddSeconds(20)));   // one report per freeze
        Assert.True(monitor.Frozen);
        var log = File.ReadAllText(System.IO.Path.Combine(iso.Logs, "ui-hang.log"));
        Assert.Contains("has not responded for 11 s", log);
        Assert.Contains("moved to the next cell", log);
        monitor.Beat(start.AddSeconds(25));
        Assert.False(monitor.Frozen);
        Assert.False(monitor.Check(start.AddSeconds(30)));
    }

    [AvaloniaFact]
    public void Unsaved_edits_are_copied_to_the_recovery_folder_and_come_back_beside_the_workbook_after_a_crash()
    {
        using var iso = new Isolated();
        var path = Samples.CreateLarge(iso.Dir, rows: 3);
        var original = File.ReadAllBytes(path);
        var window = DataEditorWindow.Open(path, null);
        window.Show();
        var editor = window.Shell.Current!;
        Assert.False(editor.SaveRecoveryNow());   // nothing to recover while nothing is edited
        editor.AddStepOfType("HTTP", null, false, null);
        Assert.True(editor.IsDirty);
        Assert.True(editor.SaveRecoveryNow());
        var copy = RecoveryStore.PathFor(path);
        Assert.True(File.Exists(copy));
        Assert.Equal(original, File.ReadAllBytes(path));   // the workbook itself is untouched
        Assert.True(WorkbookDocument.Load(copy).Config.Rows.Count > WorkbookDocument.Load(path).Config.Rows.Count, "the copy holds the unsaved step");
        window.Close();   // closing with unsaved edits keeps the copy (as after a crash)
        Assert.True(File.Exists(copy));

        var reopened = DataEditorWindow.Open(path, null);
        reopened.Show();
        var notice = reopened.Shell.Current!.StatusMessage;
        Assert.Contains("recovered", notice);
        var recovered = Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!, "*(recovered*");
        Assert.Single(recovered);
        Assert.False(File.Exists(copy));
        Assert.Equal(original, File.ReadAllBytes(path));
        reopened.Close();
    }

    [AvaloniaFact]
    public void The_recovery_copy_is_removed_once_everything_is_saved()
    {
        using var iso = new Isolated();
        var path = Samples.CreateLarge(iso.Dir, rows: 3);
        var window = DataEditorWindow.Open(path, null);
        window.Show();
        var editor = window.Shell.Current!;
        editor.AddStepOfType("HTTP", null, false, null);
        Assert.True(editor.SaveRecoveryNow());
        editor.SaveCommand.Execute(null);
        Assert.False(editor.IsDirty);
        Assert.False(editor.SaveRecoveryNow());
        Assert.False(File.Exists(RecoveryStore.PathFor(path)));
        window.Close();
        var reopened = DataEditorWindow.Open(path, null);
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!, "*(recovered*"));
        reopened.Close();
    }
}
