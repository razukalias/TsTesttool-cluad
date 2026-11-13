using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>What the grid shows for one cell besides its text: a fill colour and whether long text wraps.</summary>
public sealed partial class CellStyleView : ObservableObject
{
    private static readonly IBrush PlainText = new SolidColorBrush(Color.Parse("#E8F0FA"));
    private static readonly IBrush FilledText = new SolidColorBrush(Color.Parse("#14202F"));

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Background), nameof(Foreground))] private string? _fill;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Wrapping))] private bool _wrap;

    public IBrush Background => Fill is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(Fill));
    public IBrush Foreground => Fill is null ? PlainText : FilledText;
    public TextWrapping Wrapping => Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public CellFormat Format => new(Fill, Wrap);

    public void Set(CellFormat format)
    {
        Fill = format.Fill;
        Wrap = format.Wrap;
    }
}

/// <summary>A grid row whose cells can be coloured and wrapped, and knows which workbook cell each grid cell is.</summary>
public interface IFormattedRow
{
    ObservableCollection<CellStyleView> Formats { get; }

    /// <summary>The sheet, row and column that grid cell <paramref name="cell"/> shows, or null when it has no workbook cell yet.</summary>
    (SheetTable Table, TableRow Row, int Column)? Locate(int cell);
}
