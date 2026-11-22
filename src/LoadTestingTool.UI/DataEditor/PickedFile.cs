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
}
