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
