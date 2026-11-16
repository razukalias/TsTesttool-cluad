using System.Text.RegularExpressions;

namespace LoadTestingTool.Execution;

/// <summary>
/// <c>&lt;from_filepath&gt;path</c> puts the content of a file into a request cell; <c>&lt;from_filepath_base64&gt;path</c> puts the file's bytes there as base64.
/// The token is the whole cell. A bare file name is read from the instance's <c>Files</c> folder; a relative path with folders is tried in
/// <c>Files</c> and then in the instance folder; an absolute path is used as it is. The file is read when the step runs and used exactly as it is.
/// </summary>
public static class FileTokens
{
    private static readonly Regex Pattern = new(@"^\s*<from_filepath(?<b64>_base64)?>(?<path>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public const string FilesFolder = "Files";

    public static bool TryParse(string? cellText, out bool base64, out string path)
    {
        base64 = false; path = string.Empty;
        if (string.IsNullOrWhiteSpace(cellText)) return false;
        var match = Pattern.Match(cellText);
        if (!match.Success) return false;
        base64 = match.Groups["b64"].Success;
        path = match.Groups["path"].Value.Trim();
        return path.Length > 0;
    }

    /// <summary>The existing file the path points at, or null. Names are matched without regard to letter case.</summary>
    public static string? Find(string instanceFolder, string path)
    {
        var normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) return File.Exists(normalized) ? normalized : null;
        var filesFolder = Path.Combine(instanceFolder, FilesFolder);
        var bare = normalized.IndexOf(Path.DirectorySeparatorChar) < 0;
        foreach (var baseFolder in bare ? new[] { filesFolder } : new[] { filesFolder, instanceFolder })
        {
            var found = FindIn(baseFolder, normalized);
            if (found is not null) return found;
        }
        return null;
    }

    private static string? FindIn(string folder, string relative)
    {
        var exact = Path.Combine(folder, relative);
        if (File.Exists(exact)) return exact;
        // match each part without regard to letter case
        var current = folder;
        var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!Directory.Exists(current)) return null;
            var last = i == parts.Length - 1;
            var entry = (last ? Directory.EnumerateFiles(current) : Directory.EnumerateDirectories(current))
                .FirstOrDefault(e => string.Equals(Path.GetFileName(e), parts[i], StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;
            current = entry;
        }
        return File.Exists(current) ? current : null;
    }

    /// <summary>Reads the file for a cell: its text as it is, or its bytes as base64. Throws <see cref="FileNotFoundException"/> when the file is missing.</summary>
    public static async Task<(string Value, int Bytes, string FileName)> LoadAsync(string instanceFolder, bool base64, string path, CancellationToken cancellationToken)
    {
        var file = Find(instanceFolder, path) ?? throw new FileNotFoundException($"The file '{path}' was not found (looked in the instance's {FilesFolder} folder{(Path.IsPathRooted(path) ? " and at that path" : " and the instance folder")}).", path);
        var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
        if (base64) return (Convert.ToBase64String(bytes), bytes.Length, Path.GetFileName(file));
        var text = new System.Text.UTF8Encoding(false).GetString(bytes);
        return (text.Length > 0 && text[0] == '﻿' ? text[1..] : text, bytes.Length, Path.GetFileName(file));
    }
}
