using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>What happens with a file picked for a <c>&lt;from_filepath&gt;</c> cell: the portable path, and a copy into Files when the file is outside the application.</summary>
public static class PickedFile
{
    public static (string Path, string? Note) Prepare(string instanceFolder, string file)
    {
        var (path, place) = PortablePaths.ToPortable(instanceFolder, PortablePaths.ApplicationRootOf(instanceFolder), file);
        if (place == PathPlace.InApplication) return (path, $"{path} is outside this instance but inside the application folder; the path is relative, so it keeps working when the application is moved.");
        if (place != PathPlace.Outside) return (path, null);
        var files = Path.Combine(instanceFolder, FileTokens.FilesFolder);
        Directory.CreateDirectory(files);
        var name = Path.GetFileName(file);
        var target = Path.Combine(files, name);
        for (var n = 2; File.Exists(target); n++) target = Path.Combine(files, $"{Path.GetFileNameWithoutExtension(name)}{n}{Path.GetExtension(name)}");
        File.Copy(file, target);
        var copied = Path.GetFileName(target);
        return (copied, $"The file is outside the application folder, so it was copied into this instance's Files folder as {copied}.");
    }

    /// <summary>
    /// For a step setting (the File step's file, a query/command/script file, a multipart file part). The runner only reads inside the instance folder, so the path is relative to it
    /// (<c>Files/a.pdf</c>, <c>Queries/q.graphql</c>); a file from elsewhere is copied into <paramref name="folderName"/> first.
    /// </summary>
    public static (string Path, string? Note) PrepareForStep(string instanceFolder, string file, string folderName)
    {
        var instance = Path.GetFullPath(instanceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(file);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.StartsWith(instance + Path.DirectorySeparatorChar, comparison)) return (Path.GetRelativePath(instance, full).Replace('\\', '/'), null);
        var folder = Path.Combine(instance, folderName);
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(full);
        var target = Path.Combine(folder, name);
        for (var n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}{n}{Path.GetExtension(name)}");
        File.Copy(full, target);
        var relative = folderName + "/" + Path.GetFileName(target);
        return (relative, $"The file is outside this instance, so it was copied to {relative}; the runner only reads files inside the instance folder.");
    }
}
