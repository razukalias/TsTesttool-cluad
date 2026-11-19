using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace LoadTestingTool.UI.DataEditor;

// Fits every data column to its longest value (and its header), so long values are no longer cut off.
public sealed partial class DataEditorWindow
{
    private const double MaxFittedWidth = 640;
    private bool _autofitPosted;

    private void OnRowsResetAutofit(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset || _autofitPosted) return;
        _autofitPosted = true;
        Dispatcher.UIThread.Post(() => { _autofitPosted = false; AutofitColumns(); }, DispatcherPriority.Background);
    }

    /// <summary>Sets each data column as wide as its longest value, up to a limit. Only the longest text of a column is measured, so thousands of rows cost one pass over the text.</summary>
    public void AutofitColumns()
    {
        if (this.FindControl<DataGrid>("Grid") is not { } grid || _shell?.Current is not { } editor) return;
        if (_stretched is not null) { _stretched.Width = _stretchedWidth; _stretched = null; }
        double Measure(string text, double size, FontWeight weight = FontWeight.Normal)
            => string.IsNullOrEmpty(text) ? 0 : new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(grid.FontFamily, FontStyle.Normal, weight), size, Brushes.White).Width;
        var size = grid.FontSize > 0 ? grid.FontSize : 14;
        foreach (var column in grid.Columns.OfType<FormattedTextColumn>())
        {
            if (column.Tag is not int cell || cell < 0) continue;
            var longest = string.Empty;
            foreach (var row in editor.VisibleRows)
            {
                var text = row.Cells.Count > cell ? row.Cells[cell] ?? string.Empty : string.Empty;
                var newline = text.IndexOf('\n');
                if (newline >= 0) text = text[..newline];
                if (text.Length > longest.Length) longest = text;
            }
            var title = editor.Columns.FirstOrDefault(c => c.CellIndex == cell)?.Title ?? (cell == 0 ? "Data ID" : string.Empty);
            var wanted = Math.Max(Measure(longest, size) + (column.ExpectedColumn ? 64 : 40), Measure(title, 12.5, FontWeight.SemiBold) + 44);
            wanted = Math.Clamp(Math.Ceiling(wanted), 110, MaxFittedWidth);
            column.MaxWidth = MaxFittedWidth;   // before MinWidth: the grid refuses a minimum above the maximum
            column.MinWidth = wanted;
            column.Width = new DataGridLength(wanted);
            column.ContentMaxWidth = wanted - 24;
        }
        StretchLastColumn(grid);
    }
}
