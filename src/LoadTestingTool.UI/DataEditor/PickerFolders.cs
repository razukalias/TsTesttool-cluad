using Avalonia.Platform.Storage;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Each file picker opens in the folder it was last used in (kept in the editor's own settings, never in a workbook), else in <c>fallback</c>.</summary>
public static class PickerFolders
{
    public static string? Last(string key, string? fallback = null)
    {
        var settings = EditorSettings.Load();
        return settings.LastFolders.TryGetValue(key, out var folder) && Directory.Exists(folder) ? folder : (fallback is not null && Directory.Exists(fallback) ? fallback : null);
    }

    public static void Remember(string key, string pickedPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(pickedPath));
        if (string.IsNullOrEmpty(folder)) return;
        var settings = EditorSettings.Load();
        settings.LastFolders[key] = folder;
        settings.Save();
    }

    public static async Task<IStorageFolder?> StartAsync(IStorageProvider provider, string key, string? fallback = null)
        => Last(key, fallback) is { } folder ? await provider.TryGetFolderFromPathAsync(folder) : null;
}
