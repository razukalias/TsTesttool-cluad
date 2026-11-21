using System.Text.Json;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Per-user choices for the data editor, kept in a small JSON file so they survive between sessions.</summary>
public sealed class EditorSettings
{
    /// <summary>When on, an instance's workbook is read only when you first open it; when off, every workbook is read as the editor opens.</summary>
    public bool LazyLoading { get; set; }

    /// <summary>Per workbook, the sheets whose tab is hidden in Table view (empty or missing = all sheets).</summary>
    public Dictionary<string, List<string>> HiddenSheets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last choices made importing a file of this name into a testcase or sheet, so importing it again starts where the last import ended.</summary>
    public Dictionary<string, ImportChoice> ImportChoices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

/// <summary>What the Import window remembers per file name: the part of the file, the mode and the hand-made column mapping (by header name, so it survives column changes).</summary>
public sealed class ImportChoice
{
    public string Sheet { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public int HeaderRow { get; set; } = 1;
    public string Mode { get; set; } = nameof(ImportMode.AppendRows);
    public string Match { get; set; } = nameof(ImportMatch.ByHeader);
    public string RowMatch { get; set; } = nameof(ImportRowMatch.DataId);
    public string Placement { get; set; } = nameof(ImportPlacement.End);
    public bool CreateUnmatched { get; set; }
    public bool ClearOnEmpty { get; set; }
    public string? Step { get; set; }
    public bool Expected { get; set; }
    public string Verb { get; set; } = "eq";
    public Dictionary<string, string> Manual { get; set; } = [];
}
