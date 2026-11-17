using Avalonia.Data.Converters;

namespace LoadTestingTool.UI.DataEditor;

public static class SheetTabsConverters
{
    /// <summary>True when there is more than one sheet tab, so a single tab is not shown.</summary>
    public static readonly IValueConverter MoreThanOne = new FuncValueConverter<int, bool>(count => count > 1);
}
