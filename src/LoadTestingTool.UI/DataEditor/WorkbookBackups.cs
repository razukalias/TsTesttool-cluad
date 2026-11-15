using System.Globalization;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One stored copy of a workbook.</summary>
public sealed record BackupFile(string Path, DateTime Taken, long Size)
{
    public string Label => $"{Taken:yyyy-MM-dd HH:mm:ss}   ·   {Size / 1024.0:N0} KB   ·   {System.IO.Path.GetFileName(Path)}";
}

/// <summary>Finds the backups of one workbook — in the central backup folder and the older ones beside the file — and puts one back.</summary>
public static class WorkbookBackups
{
    private static readonly Regex Stamp = new(@"(?<d>\d{8})_(?<t>\d{6})(?<ms>\d{3})?", RegexOptions.Compiled);

    public static IReadOnlyList<BackupFile> List(string workbookPath)
    {
        var full = Path.GetFullPath(workbookPath);
        var directory = Path.GetDirectoryName(full)!;
        var name = Path.GetFileNameWithoutExtension(full);
        var found = new List<string>();

        if (DataEngineBackup.ConfiguredFolder(full) is { } central)
        {
            var folder = Path.Combine(central, Path.GetFileName(directory));
            if (Directory.Exists(folder))
                found.AddRange(Directory.EnumerateFiles(folder, $"{name}_*.backup.xlsx").Where(f => IsCentralNameOf(f, name)));
        }
        if (Directory.Exists(directory))
            found.AddRange(Directory.EnumerateFiles(directory, $"{Path.GetFileName(full)}.backup.*xlsx"));

        return found.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => new BackupFile(f, TakenAt(f), new FileInfo(f).Length))
            .OrderByDescending(b => b.Taken)
            .ToList();
    }

    // "My_Flow_20260101_120000123.backup.xlsx" belongs to "My_Flow" but "My_Flow_2_20260101_….backup.xlsx" belongs to "My_Flow_2".
    private static bool IsCentralNameOf(string file, string name)
        => Regex.IsMatch(Path.GetFileName(file), $@"^{Regex.Escape(name)}_\d{{8}}_\d{{6,9}}(_\d+)?\.backup\.xlsx$", RegexOptions.IgnoreCase);

    /// <summary>When the backup was taken: the stamp in its name, else the file's own time.</summary>
    public static DateTime TakenAt(string path)
    {
        var match = Stamp.Match(Path.GetFileName(path));
        if (match.Success && DateTime.TryParseExact(match.Groups["d"].Value + match.Groups["t"].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            return match.Groups["ms"].Success ? at.AddMilliseconds(int.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture)) : at;
        return File.GetLastWriteTime(path);
    }

    /// <summary>Backs up the current workbook, then replaces it with the chosen backup. Returns the safety copy's path (null when there was no workbook).</summary>
    public static string? Restore(string backupPath, string workbookPath)
    {
        string? safety = File.Exists(workbookPath) ? DataEngineBackup.CreateBackup(workbookPath) : null;
        var temp = workbookPath + ".restore-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(backupPath, temp);
            File.Move(temp, workbookPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
            throw;
        }
        return safety;
    }
}
