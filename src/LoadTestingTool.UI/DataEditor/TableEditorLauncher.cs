using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Starts the edit-only table editor as its own process (<c>--table-editor path</c>).</summary>
internal static class TableEditorLauncher
{
    public static void Open(string workbookPath)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The application process path is unavailable.");
        var start = new ProcessStartInfo { FileName = processPath, UseShellExecute = false, CreateNoWindow = true };
        var assembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (!string.IsNullOrWhiteSpace(assembly) && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--table-editor"); start.ArgumentList.Add(Path.GetFullPath(workbookPath));
        Process.Start(start);
    }
}

/// <summary>Remembers which workbooks an edit-only window has open (a small file in the temp folder per workbook), so a second window can warn.</summary>
internal static class TableEditorLock
{
    private static string FileFor(string workbookPath, int processId)
    {
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(workbookPath).ToLowerInvariant())))[..16];
        var folder = Path.Combine(Path.GetTempPath(), "TsTableEditor");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{key}.{processId}.lock");
    }

    public static void Take(string workbookPath) { try { File.WriteAllText(FileFor(workbookPath, Environment.ProcessId), workbookPath); } catch (IOException) { } }

    public static void Release(string workbookPath) { try { File.Delete(FileFor(workbookPath, Environment.ProcessId)); } catch (IOException) { } }

    /// <summary>True when another live process has an edit-only window on this workbook.</summary>
    public static bool OtherProcessHas(string workbookPath)
    {
        try
        {
            var mine = Path.GetFileName(FileFor(workbookPath, Environment.ProcessId));
            var prefix = mine[..mine.IndexOf('.')] + ".";
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(FileFor(workbookPath, 0))!, prefix + "*.lock"))
            {
                var parts = Path.GetFileName(file).Split('.');
                if (parts.Length < 3 || !int.TryParse(parts[1], out var pid) || pid == Environment.ProcessId) continue;
                try { using var process = Process.GetProcessById(pid); if (!process.HasExited) return true; }
                catch (ArgumentException) { try { File.Delete(file); } catch (IOException) { } }   // the process is gone: a stale file
            }
        }
        catch (IOException) { }
        return false;
    }
}
