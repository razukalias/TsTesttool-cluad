using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Highlighting and wrapping cells. Both are stored in the workbook (cell fill and "wrap text"), so they show in Excel too.
public sealed partial class DataEditorViewModel
{
    public static IReadOnlyList<string> FormatScopes { get; } = ["This cell", "Selected rows", "This column"];

    /// <summary>The colours the Format menu offers, as name and #RRGGBB.</summary>
    public static IReadOnlyList<(string Name, string Hex)> HighlightColors { get; } =
    [
        ("Yellow", "#FFEB9C"), ("Green", "#C6EFCE"), ("Red", "#FFC7CE"), ("Blue", "#BDD7EE"), ("Orange", "#F8CBAD"), ("Purple", "#D9C7F0"), ("Grey", "#D9D9D9")
    ];

    [ObservableProperty] private string _formatScope = FormatScopes[0];

    /// <summary>Applies a change to the look of the given cells as one undo step.</summary>
    public void FormatCells(IReadOnlyList<(IFormattedRow Row, int Cell)> cells, Func<CellFormat, CellFormat> change, string description)
    {
        if (cells.Count == 0) { ShowStatus("Click a cell or select rows first.", isError: false); return; }
        Record(description);
        var changed = 0;
        foreach (var (row, cell) in cells)
        {
            if (row is DataRowViewModel data && cell > 0 && Columns[cell - 1].Group == ColumnGroup.Response) EnsureResponse(data);
            if (row.Locate(cell) is not { } at) continue;
            var before = at.Table.GetFormat(at.Row, at.Column);
            var after = change(before);
            if (after == before) continue;
            at.Table.SetFormat(at.Row, at.Column, after);
            row.Formats[cell].Set(after);
            changed++;
        }
        if (changed == 0)
        {
            DiscardLastRecord();
            ShowStatus("Nothing to change.", isError: false);
            return;
        }
        MarkChanged();
        ShowStatus($"{description}: {changed} {(changed == 1 ? "cell" : "cells")}. Saved with the workbook, so Excel shows it too.", isError: false);
    }

    // ---- Cell editor for long values --------------------------------------------------------

    [ObservableProperty] private bool _cellEditorVisible;
    [ObservableProperty] private string _cellEditorTitle = string.Empty;
    [ObservableProperty] private string _cellEditorText = string.Empty;

    private IFormattedRow? _cellEditorRow;
    private int _cellEditorCell;

    /// <summary>Opens a large text box for one cell, for JSON bodies, scripts and other long values.</summary>
    public void OpenCellEditor(IFormattedRow row, int cell, string title, string text)
    {
        _cellEditorRow = row;
        _cellEditorCell = cell;
        CellEditorTitle = title;
        CellEditorText = text;
        CellEditorVisible = true;
    }

    [RelayCommand]
    private void CloseCellEditor() => CellEditorVisible = false;

    [RelayCommand]
    private void ApplyCellEditor()
    {
        switch (_cellEditorRow)
        {
            case DataRowViewModel data: data.Cells[_cellEditorCell] = CellEditorText; break;
            case StepRowViewModel step: step.Cells[_cellEditorCell] = CellEditorText; break;
        }
        CellEditorVisible = false;
    }
}
