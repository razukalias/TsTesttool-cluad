using System.Text.Json;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Per-user choices for the data editor, kept in a small JSON file so they survive between sessions.</summary>
public sealed class EditorSettings
{
    /// <summary>When on, an instance's workbook is read only when you first open it; when off, every workbook is read as the editor opens.</summary>
    public bool LazyLoading { get; set; }

    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TSLoadTestingTool", "data-editor.json");

    public static EditorSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath)) ?? new EditorSettings() : new EditorSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new EditorSettings();
        }
    }

    /// <summary>Best effort: a settings file that cannot be written must never get in the way of editing.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
