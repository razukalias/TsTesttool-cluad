namespace LoadTestingTool.UI.DataEditor;

/// <summary>Creating and copying instance folders on disk.</summary>
public static class InstanceOperations
{
    // Output of earlier runs; a copy starts clean.
    private static readonly HashSet<string> NotCopied = new(["Results", "Logs", "History", ".runtime"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The folders every instance needs; <c>Files</c> is where <c>&lt;from_filepath&gt;</c> reads from.</summary>
    public static readonly string[] RequiredFolders = ["Templates", "Files", "Queries", "Scripts", "Inputs", "Artifacts", "History", "Logs", "Results"];

    /// <summary>Creates the folders of an instance that are missing (and a starter <c>empty.json</c> template when Templates is new). Returns the names it created.</summary>
    public static IReadOnlyList<string> EnsureFolders(string folder)
    {
        var created = new List<string>();
        Directory.CreateDirectory(folder);
        foreach (var name in RequiredFolders)
        {
            var path = Path.Combine(folder, name);
            if (Directory.Exists(path)) continue;
            Directory.CreateDirectory(path);
            created.Add(name);
        }
        var starter = Path.Combine(folder, "Templates", "empty.json");
        if (created.Contains("Templates") && !File.Exists(starter)) File.WriteAllText(starter, "{}\n");
        return created;
    }

    /// <summary>The workbook of an instance folder: the preferred name if present, otherwise the only/first .xlsx.</summary>
    public static string? FindWorkbook(string folder, string preferredName)
    {
        if (!Directory.Exists(folder)) return null;
        var preferred = Path.Combine(folder, preferredName);
        if (File.Exists(preferred)) return preferred;
        return Directory.EnumerateFiles(folder, "*.xlsx").Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>Why <paramref name="name"/> cannot be an instance name under <paramref name="root"/>, or null.</summary>
    public static string? CheckName(string root, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return "Enter a name.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..") return "That name has characters a folder cannot have.";
        return Directory.Exists(Path.Combine(root, name)) ? $"An instance called {name} already exists." : null;
    }

    /// <summary>A free name like <c>Name_copy</c>, <c>Name_copy2</c>.</summary>
    public static string SuggestCopyName(string root, string name)
    {
        var candidate = name + "_copy";
        for (var n = 2; Directory.Exists(Path.Combine(root, candidate)); n++) candidate = name + "_copy" + n;
        return candidate;
    }

    /// <summary>Copies an instance folder (workbook, templates, files) without its Results, Logs and History. Returns the number of files copied.</summary>
    public static int CopyFolder(string source, string target)
    {
        var copied = 0;
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)) continue;
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            copied++;
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (NotCopied.Contains(name)) { Directory.CreateDirectory(Path.Combine(target, name)); continue; }
            copied += CopyFolder(directory, Path.Combine(target, name));
        }
        return copied;
    }
}
