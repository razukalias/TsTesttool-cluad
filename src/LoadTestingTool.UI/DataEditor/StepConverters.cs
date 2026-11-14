using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

public static class StepConverters
{
    public static readonly IValueConverter Chevron = new FuncValueConverter<bool, string>(open => open ? "▾" : "▸");
    public static readonly IValueConverter Brush = new FuncValueConverter<string?, IBrush?>(color => color is null ? null : Avalonia.Media.Brush.Parse(color));
}
