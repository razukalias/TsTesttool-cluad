using System.Text.RegularExpressions;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

/// <summary>
/// <c>&lt;from_filepath&gt;path</c> puts the content of a file into a request cell; <c>&lt;from_filepath_base64&gt;path</c> puts the file's bytes there as base64.
/// The token is the whole cell. A bare file name is read from the instance's <c>Files</c> folder; a relative path with folders is tried in
/// <c>Files</c> and then in the instance folder; a path starting with <c>../</c> is relative to the instance folder (files elsewhere in the application); an absolute path is used as it is. The file is read when the step runs and used exactly as it is.
/// </summary>
public static class FileTokens
{
    private static readonly Regex Pattern = new(@"^\s*<from_filepath(?<m>_base64|_binary|_json)?>(?<path>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public const string FilesFolder = "Files";

    /// <summary>How the file is read: text as it is, Base64 of its bytes, a binary reference that streams, or JSON (optionally <c>file.json|$.path</c>).</summary>
    public enum Mode { Text, Base64, Binary, Json }

    public static string Prefix(Mode mode) => mode switch { Mode.Base64 => "<from_filepath_base64>", Mode.Binary => "<from_filepath_binary>", Mode.Json => "<from_filepath_json>", _ => "<from_filepath>" };

    public static bool TryParse(string? cellText, out Mode mode, out string path, out string selector)
    {
        mode = Mode.Text; path = string.Empty; selector = string.Empty;
        if (string.IsNullOrWhiteSpace(cellText)) return false;
        var match = Pattern.Match(cellText);
        if (!match.Success) return false;
        mode = match.Groups["m"].Value.ToLowerInvariant() switch { "_base64" => Mode.Base64, "_binary" => Mode.Binary, "_json" => Mode.Json, _ => Mode.Text };
        path = match.Groups["path"].Value.Trim();
        if (mode == Mode.Json && path.IndexOf('|') is var bar and > 0) { selector = path[(bar + 1)..].Trim(); path = path[..bar].Trim(); }
        return path.Length > 0;
    }

    /// <summary>A binary reference to the file (path, size, SHA-256, content type from the extension); nothing is read into memory.</summary>
    public static async Task<BinaryFileReference> ReferenceAsync(string instanceFolder, string path, CancellationToken cancellationToken)
    {
        var file = Find(instanceFolder, path) ?? throw new FileNotFoundException(NotFound(path));
        var info = new FileInfo(file);
        await using var stream = File.OpenRead(file);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        var type = Path.GetExtension(file).ToLowerInvariant() switch { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".json" => "application/json", ".xml" => "application/xml", ".txt" => "text/plain", ".zip" => "application/zip", _ => "application/octet-stream" };
        return new BinaryFileReference { Path = file, SizeBytes = info.Length, Sha256 = Convert.ToHexString(hash).ToLowerInvariant(), ContentType = type };
    }

    private static string NotFound(string path) => $"The file '{path}' was not found (looked in the instance's {FilesFolder} folder{(Path.IsPathRooted(path) ? " and at that path" : " and the instance folder")}).";

    /// <summary>The existing file the path points at, or null. Names are matched without regard to letter case.</summary>
    public static string? Find(string instanceFolder, string path)
    {
        var normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) return File.Exists(normalized) ? normalized : null;
        var filesFolder = Path.Combine(instanceFolder, FilesFolder);
        var bare = normalized.IndexOf(Path.DirectorySeparatorChar) < 0;
        // A path that starts with ".." is relative to the instance folder only (that is how the file picker writes files outside the instance, so the app can be moved).
        var instanceRelative = normalized == ".." || normalized.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        foreach (var baseFolder in instanceRelative ? new[] { instanceFolder } : bare ? new[] { filesFolder } : new[] { filesFolder, instanceFolder })
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

    /// <summary>Reads the file for a cell: its text as it is, its bytes as Base64, or (JSON) the whole text or the value at the selector. Throws <see cref="FileNotFoundException"/> when the file is missing.</summary>
    public static async Task<(string Value, int Bytes, string FileName)> LoadAsync(string instanceFolder, Mode mode, string path, CancellationToken cancellationToken, string selector = "")
    {
        var file = Find(instanceFolder, path) ?? throw new FileNotFoundException(NotFound(path));
        var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
        if (mode == Mode.Base64) return (Convert.ToBase64String(bytes), bytes.Length, Path.GetFileName(file));
        var text = new System.Text.UTF8Encoding(false).GetString(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        if (mode == Mode.Json && selector.Length > 0)
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            var picked = JsonPath.Select(document.RootElement, selector);
            text = picked is string str ? str : System.Text.Json.JsonSerializer.Serialize(picked);
        }
        return (text, bytes.Length, Path.GetFileName(file));
    }
}
