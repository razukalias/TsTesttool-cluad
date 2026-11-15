using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Xunit;

namespace UiTests;

public class StorageCleanupTests
{
    [AvaloniaFact]
    public void Preview_lists_backups_without_deleting_anything()
    {
        var root = Path.Combine(Path.GetTempPath(), "cleanup-" + Guid.NewGuid().ToString("N"));
        var backups = Path.Combine(root, "backup", "DataEngine_HttpOnly_4x100Sample_Long");
        Directory.CreateDirectory(backups);
        File.WriteAllText(Path.Combine(backups, "Demo_20260101_000000000.backup.xlsx"), "a");
        File.WriteAllText(Path.Combine(backups, "Demo_20260102_000000000.backup.xlsx"), "b");
        try
        {
            var type = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.StorageCleanupWindow")!;
            var window = (Window)Activator.CreateInstance(type, Path.Combine(root, "backup"), new[] { Path.Combine(root, "DataEngine_HttpOnly_4x100Sample_Long") })!;
            window.Show();
            type.GetMethod("Preview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var summary = (TextBlock)type.GetField("_summary", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.Contains("Matching files: 2", summary.Text);
            var status = (TextBlock)type.GetField("_statusText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.DoesNotContain("Could not scan", status.Text);
            var preview = (TextBox)type.GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.Contains("DELETE", preview.Text);
            Assert.Equal(2, Directory.GetFiles(backups).Length);
            window.Close();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public class StorageCleanupOpenTests
{
    [AvaloniaFact]
    public void The_caret_line_resolves_to_its_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "cleanup-" + Guid.NewGuid().ToString("N"));
        var backups = Path.Combine(root, "backup", "Demo");
        Directory.CreateDirectory(backups);
        var first = Path.Combine(backups, "Demo_20260101_000000000.backup.xlsx");
        var second = Path.Combine(backups, "Demo_20260102_000000000.backup.xlsx");
        File.WriteAllText(first, "a"); File.WriteAllText(second, "b");
        try
        {
            var type = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.StorageCleanupWindow")!;
            var window = (Window)Activator.CreateInstance(type, Path.Combine(root, "backup"), new[] { Path.Combine(root, "Demo") })!;
            window.Show();
            type.GetMethod("Preview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var preview = (TextBox)type.GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var resolve = type.GetMethod("FileOnCurrentLine", BindingFlags.Instance | BindingFlags.NonPublic)!;
            preview.CaretIndex = preview.Text!.IndexOf("20260102", StringComparison.Ordinal);
            Assert.Equal(second, resolve.Invoke(window, null));
            preview.CaretIndex = preview.Text.IndexOf("20260101", StringComparison.Ordinal);
            Assert.Equal(first, resolve.Invoke(window, null));
            preview.CaretIndex = 0; // the header line is not a file
            Assert.Null(resolve.Invoke(window, null));
            window.Close();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public class StorageRestoreTests
{
    private static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    [AvaloniaFact]
    public async Task Restore_replaces_the_workbook_keeps_a_safety_copy_and_tells_the_app()
    {
        var root = Path.Combine(Path.GetTempPath(), "restore-" + Guid.NewGuid().ToString("N"));
        var instance = Path.Combine(root, "Instances", "Demo");
        var backups = Path.Combine(root, "backup", "Demo");
        Directory.CreateDirectory(instance); Directory.CreateDirectory(backups);
        File.WriteAllText(Path.Combine(root, "appsettings.json"), "{\"DataEngineBackupFolder\":\"backup\"}");
        var workbook = Path.Combine(instance, "Demo.xlsx");
        File.WriteAllText(workbook, "current");
        var backup = Path.Combine(backups, "Demo_20260101_000000000.backup.xlsx");
        File.WriteAllText(backup, "old version");
        try
        {
            var type = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.StorageCleanupWindow")!;
            var window = (Window)Activator.CreateInstance(type, Path.Combine(root, "backup"), new[] { instance })!;
            window.Show();
            string? restored = null; string? blocked = "running";
            type.GetProperty("RestoreGuard", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new Func<string, string?>(_ => blocked));
            type.GetProperty("Restored", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new Action<string>(p => restored = p));
            type.GetProperty("Confirm", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new Func<string, string, Task<bool>>((_, _) => Task.FromResult(true)));
            type.GetMethod("Preview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var preview = Get<TextBox>(window, "_preview");
            preview.CaretIndex = preview.Text!.IndexOf("20260101", StringComparison.Ordinal);
            var restore = type.GetMethod("RestoreSelectedForTest", BindingFlags.Instance | BindingFlags.NonPublic)!;

            restore.Invoke(window, null);   // blocked: nothing changes
            Assert.Equal("current", File.ReadAllText(workbook));
            Assert.Contains("running", Get<TextBlock>(window, "_statusText").Text);

            blocked = null;
            restore.Invoke(window, null);
            for (var i = 0; i < 50 && restored is null; i++) { await Task.Delay(50); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }

            Assert.Equal(workbook, restored);
            Assert.Equal("old version", File.ReadAllText(workbook));
            Assert.Contains(Directory.GetFiles(backups, "*.backup.xlsx"), f => File.ReadAllText(f) == "current");
            Assert.Empty(Directory.GetFiles(instance).Where(f => f.Contains(".restore-")));
            window.Close();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [AvaloniaFact]
    public void The_workbook_is_found_from_both_backup_name_styles()
    {
        var root = Path.Combine(Path.GetTempPath(), "restore-" + Guid.NewGuid().ToString("N"));
        var instance = Path.Combine(root, "Demo");
        var type = typeof(LoadTestingTool.UI.MainWindow).Assembly.GetType("LoadTestingTool.UI.StorageCleanupWindow")!;
        var window = (Window)Activator.CreateInstance(type, Path.Combine(root, "backup"), new[] { instance })!;
        var find = type.GetMethod("WorkbookFor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(Path.Combine(instance, "My_Flow.xlsx"), find.Invoke(window, [Path.Combine(root, "backup", "Demo", "My_Flow_20260101_120000123.backup.xlsx")]));
        Assert.Equal(Path.Combine(instance, "My_Flow.xlsx"), find.Invoke(window, [Path.Combine(instance, "My_Flow.xlsx.backup.20260101_120000123.xlsx")]));
        Assert.Null(find.Invoke(window, [Path.Combine(root, "backup", "Other", "X_20260101_120000123.backup.xlsx")]));
        Assert.Null(find.Invoke(window, [Path.Combine(root, "backup", "Demo", "notes.xlsx")]));
    }
}
