namespace LoadTestingTool.Execution;

/// <summary>Where a picked file lives, and the path to write into a <c>&lt;from_filepath&gt;</c> cell so it keeps working when the whole application folder is moved.</summary>
public enum PathPlace { InFiles, InInstance, InApplication, Outside }

public static class PortablePaths
{
    /// <summary>
    /// The application root is the folder that holds the Instances folder. Paths use <c>/</c> and are relative:
    /// in the instance's Files folder <c>invoices/a.pdf</c>; elsewhere in the instance <c>Templates/a.json</c>; elsewhere in the application <c>../../Shared/a.pdf</c> (from the instance folder).
    /// A file outside the application gets its full path and <see cref="PathPlace.Outside"/>; the editor copies such a file into Files instead.
    /// </summary>
    public static (string Path, PathPlace Place) ToPortable(string instanceFolder, string applicationRoot, string file)
    {
        var instance = Full(instanceFolder);
        var app = Full(applicationRoot);
        var target = Full(file);
        var files = Path.Combine(instance, FileTokens.FilesFolder);
        if (IsInside(files, target)) return (Slash(Path.GetRelativePath(files, target)), PathPlace.InFiles);
        if (IsInside(instance, target)) return (Slash(Path.GetRelativePath(instance, target)), PathPlace.InInstance);
        if (IsInside(app, target)) return (Slash(Path.GetRelativePath(instance, target)), PathPlace.InApplication);
        return (target, PathPlace.Outside);
    }

    /// <summary>The application root for an instance folder under the default layout (<c>&lt;app&gt;/Instances/&lt;name&gt;</c>).</summary>
    public static string ApplicationRootOf(string instanceFolder) => Path.GetDirectoryName(Path.GetDirectoryName(Full(instanceFolder)) ?? Full(instanceFolder)) ?? Full(instanceFolder);

    private static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static string Slash(string path) => path.Replace('\\', '/');

    private static bool IsInside(string folder, string file)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return file.StartsWith(folder + Path.DirectorySeparatorChar, comparison);
    }
}
