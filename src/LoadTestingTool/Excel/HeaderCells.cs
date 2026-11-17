using ClosedXML.Excel;

namespace LoadTestingTool.Excel;

/// <summary>
/// Header rows can hold cells that are "used" but empty (a column whose contents were cleared, or a stray formatted cell).
/// Readers that build a name-to-column map must skip them, or several empty names collide and the whole workbook fails to load.
/// </summary>
public static class HeaderCells
{
    public static bool HasText(IXLCell cell) => !string.IsNullOrWhiteSpace(cell.GetString());
}
