using Avalonia.Data.Converters;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Value converters for the tree rows.</summary>
public static class TreeConverters
{
    /// <summary>True for 0 (a tree item without children).</summary>
    public static readonly IValueConverter IsZero = new FuncValueConverter<int, bool>(count => count == 0);
}

/// <summary>Small line icons (24 x 24 outlines, drawn with a 1.6 stroke) for the tree: workbook, testcase, and one per step type.</summary>
public static class TreeIcons
{
    public const string Instance = "M7 3h7l4 4v14H7z M14 3v4h4 M10 12h5 M10 16h5 M10 12v4 M15 12v4";
    public const string Testcase = "M10 3h4 M11 3v6l-5 9a1.6 1.6 0 0 0 1.4 2.3h9.2a1.6 1.6 0 0 0 1.4-2.3l-5-9V3 M8.5 14h7";

    private const string Http = "M7 7h11 M15 4l3 3-3 3 M17 17H6 M9 14l-3 3 3 3";
    private const string GraphQl = "M12 3l7.5 4.5v9L12 21l-7.5-4.5v-9z M12 3v18 M4.5 7.5l15 9 M19.5 7.5l-15 9";
    private const string Service = "M5 4h14v6H5z M5 14h14v6H5z M8 7h.01 M8 17h.01 M12 10v4";
    private const string List = "M5 6h14 M5 12h14 M5 18h9";
    private const string Database = "M12 5c4 0 7 1 7 2.5S16 10 12 10 5 9 5 7.5 8 5 12 5z M5 7.5v9C5 18 8 19 12 19s7-1 7-2.5v-9 M5 12c0 1.5 3 2.5 7 2.5s7-1 7-2.5";
    private const string File = "M7 3h7l4 4v14H7z M14 3v4h4";
    private const string Script = "M9 8l-4 4 4 4 M15 8l4 4-4 4 M13 6l-2 12";
    private const string Hand = "M12 8m-3 0a3 3 0 1 0 6 0a3 3 0 1 0 -6 0 M6 20v-1a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v1";
    private const string Branch = "M7 4v10 M7 17m-2.5 0a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0 -5 0 M17 7m-2.5 0a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0 -5 0 M17 9.5a6 6 0 0 1-6 6H9.5";
    private const string Repeat = "M4 12v-3a3 3 0 0 1 3-3h12 M16 3l3 3-3 3 M20 12v3a3 3 0 0 1-3 3H5 M8 21l-3-3 3-3";

    /// <summary>The icon of a step by its type name (HTTP, GraphQL, CaseQL, SQL, File, Script, ActionManual, If, While, For, ForEach); HTTP for anything else.</summary>
    public static string ForStep(string typeName) => typeName switch
    {
        "WCF" => Service,
        "GraphQL" => GraphQl,
        "CaseQL" => List,
        "SQL" => Database,
        "File" => File,
        "Script" => Script,
        "ActionManual" => Hand,
        "If" => Branch,
        "While" or "For" or "ForEach" => Repeat,
        _ => Http,
    };
}
