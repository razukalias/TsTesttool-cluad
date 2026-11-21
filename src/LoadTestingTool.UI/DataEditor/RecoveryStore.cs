using System.Security.Cryptography;
using System.Text;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// Recovery copies of unsaved edits: while a workbook has unsaved changes the editor writes them to a copy in the Recovery folder every
/// half minute. If the app dies, the next time the workbook is opened that copy (when newer than the workbook) is placed beside it as
/// "name (recovered yyyyMMdd-HHmm).xlsx"; the workbook itself is never touched.
/// </summary>
public static class RecoveryStore
{
    public static string Folder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Recovery");

    public static string PathFor(string workbookPath)
    {
        var full = Path.GetFullPath(workbookPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..8];
        return Path.Combine(Folder, $"{Path.GetFileNameWithoutExtension(full)}-{hash}.recovery.xlsx");
    }

    public static void Delete(string workbookPath)
    {
        try { var path = PathFor(workbookPath); if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>The recovered copy placed beside the workbook, or null when there is nothing newer than the workbook.</summary>
    public static string? Recover(string workbookPath)
    {
        try
        {
            var recovery = PathFor(workbookPath);
            if (!File.Exists(recovery) || !File.Exists(workbookPath)) return null;
            if (File.GetLastWriteTimeUtc(recovery) <= File.GetLastWriteTimeUtc(workbookPath)) { File.Delete(recovery); return null; }   // saved after the copy was made
            var stamp = File.GetLastWriteTime(recovery).ToString("yyyyMMdd-HHmm");
            var folder = Path.GetDirectoryName(Path.GetFullPath(workbookPath))!;
            var target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(workbookPath)} (recovered {stamp}).xlsx");
            for (var n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(workbookPath)} (recovered {stamp} {n}).xlsx");
            File.Move(recovery, target);
            return target;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
