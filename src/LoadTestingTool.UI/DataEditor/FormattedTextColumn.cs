using Avalonia.Controls;
using Avalonia.Data;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>A text column whose cells show the fill colour and wrapping the workbook holds for them. Editing is unchanged.</summary>
public sealed class FormattedTextColumn : DataGridTextColumn
{
    private readonly int _cell;

    public FormattedTextColumn(int cell) => _cell = cell;

    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        var element = base.GenerateElement(cell, dataItem);
        cell.Bind(DataGridCell.BackgroundProperty, new Binding($"Formats[{_cell}].Background"));
        if (element is TextBlock text)
        {
            text.MaxWidth = 300;
            text.Bind(TextBlock.TextWrappingProperty, new Binding($"Formats[{_cell}].Wrapping"));
            text.Bind(TextBlock.ForegroundProperty, new Binding($"Formats[{_cell}].Foreground"));
        }
        return element;
    }
}
