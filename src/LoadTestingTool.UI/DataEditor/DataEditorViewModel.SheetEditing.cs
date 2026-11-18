using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

// Editing the sheets the runner does not read: their cells, rows and columns, and adding, renaming and removing whole sheets.
// Nothing reaches the file until Save; every step can be undone.
public sealed partial class DataEditorViewModel
{
    public const string PlaceColumnAtEnd = "At the end";
    public const string PlaceColumnBefore = "Before the selected column";
    public const string PlaceColumnAfter = "After the selected column";

    public static IReadOnlyList<string> SheetColumnPlacements { get; } = [PlaceColumnAtEnd, PlaceColumnBefore, PlaceColumnAfter];

    private enum SheetAction { None, AddSheet, RenameSheet, RemoveSheet, AddRows, AddColumns, RenameColumn, RemoveColumn }

    private SheetAction _sheetAction;

    [ObservableProperty] private SheetRowViewModel? _selectedSheetRow;

    /// <summary>Position of the column whose cell was last clicked in the sheet grid, or -1; set by the view.</summary>
    [ObservableProperty] private int _sheetCurrentColumn = -1;

    [ObservableProperty] private bool _sheetPanelVisible;
    [ObservableProperty] private string _sheetPanelTitle = string.Empty;
    [ObservableProperty] private string _sheetPanelName = string.Empty;
    [ObservableProperty] private string _sheetPanelNote = string.Empty;
    [ObservableProperty] private string _sheetPanelConfirmText = "OK";
    [ObservableProperty] private bool _sheetPanelAsksName;
    [ObservableProperty] private bool _sheetPanelAsksCount;
    [ObservableProperty] private bool _sheetPanelAsksPlacement;
    [ObservableProperty] private bool _sheetPanelIsDelete;
    [ObservableProperty] private decimal? _sheetPanelCount = 1;
    [ObservableProperty] private string _sheetPanelPlacement = PlaceColumnAtEnd;

    /// <summary>The cells (like <c>request!B5</c>) that refer to this cell of the active other sheet, from the last saved file; empty for a row not saved yet.</summary>
    public IReadOnlyList<string> CellUsedBy(string header, SheetRowViewModel row)
        => row.Row?.SourceRow is int source && IsOtherSheet
            ? _document.FindCellReferences(ActiveSheet, header, source).Select(r => $"{r.Sheet}!{r.Cell}").ToList()
            : [];

    private LoadTestingTool.Editing.SheetTable? ActiveOtherTable => IsOtherSheet ? _document.FindOtherSheet(ActiveSheet) : null;

    // ---- Cells -------------------------------------------------------------------------------

    private void OnOtherCellEdited(SheetRowViewModel row, int cell, string value)
    {
        if (row.Row is null || cell < 0 || cell >= SheetHeaders.Count || row.Row.GetAt(cell) == value) return;
        var header = SheetHeaders[cell];
        Record($"edit {(header.Length == 0 ? $"column {cell + 1}" : header)} in {ActiveSheet}");
        row.Row.SetAt(cell, value);
        MarkChanged();
    }

    // ---- Panel: open ---------------------------------------------------------------------------

    private void OpenSheetPanel(SheetAction action, string title, string confirm, string note, bool asksName = false, bool asksCount = false, bool asksPlacement = false, bool isDelete = false, string name = "")
    {
        _sheetAction = action;
        SheetPanelTitle = title;
        SheetPanelConfirmText = confirm;
        SheetPanelNote = note;
        SheetPanelAsksName = asksName;
        SheetPanelAsksCount = asksCount;
        SheetPanelAsksPlacement = asksPlacement;
        SheetPanelIsDelete = isDelete;
        SheetPanelName = name;
        SheetPanelCount = 1;
        SheetPanelPlacement = SheetCurrentColumn >= 0 ? PlaceColumnAfter : PlaceColumnAtEnd;
        SheetPanelVisible = true;
    }

    [RelayCommand]
    private void OpenAddSheet()
        => OpenSheetPanel(SheetAction.AddSheet, "ADD SHEET", "Add sheet", "Adds an empty sheet at the end of the workbook. Add its columns and rows afterwards; it is written when you save.", asksName: true);

    [RelayCommand]
    private void OpenRenameSheet()
    {
        if (!IsOtherSheet) { ShowStatus("Open one of the other sheets first. The sheets the runner reads (config, request, response) cannot be renamed.", isError: true); return; }
        OpenSheetPanel(SheetAction.RenameSheet, $"RENAME SHEET · {ActiveSheet}", "Rename", "Refused while a <From_…> reference or a formula in another sheet refers to it by name.", asksName: true, name: ActiveSheet);
    }

    [RelayCommand]
    private void OpenRemoveSheet()
    {
        if (!IsOtherSheet) { ShowStatus("Open one of the other sheets first. The sheets the runner reads (config, request, response) cannot be removed.", isError: true); return; }
        OpenSheetPanel(SheetAction.RemoveSheet, $"REMOVE SHEET · {ActiveSheet}", "Remove sheet", $"Removes the sheet and its {SheetRows.Count} {(SheetRows.Count == 1 ? "row" : "rows")} when you save. Refused while another cell refers to it. You can undo until you save.", isDelete: true);
    }

    [RelayCommand]
    private void OpenAddSheetRows()
    {
        if (!IsOtherSheet) return;
        if (SheetHeaders.Count == 0) { ShowStatus("Add a column first; a sheet with no columns has nowhere to put a row.", isError: true); return; }
        OpenSheetPanel(SheetAction.AddRows, $"ADD ROWS · {ActiveSheet}", "Add rows", "Empty rows are added at the end of the sheet.", asksCount: true);
    }

    [RelayCommand]
    private void OpenAddSheetColumns()
    {
        if (!IsOtherSheet) return;
        OpenSheetPanel(SheetAction.AddColumns, $"ADD COLUMNS · {ActiveSheet}", "Add columns",
            "With more than one column the number 1, 2, 3… is added to the name. Placement is relative to the column whose cell you last clicked.",
            asksName: true, asksCount: true, asksPlacement: true);
    }

    [RelayCommand]
    private void OpenRenameSheetColumn()
    {
        if (ActiveOtherTable is not { } table) return;
        if (SheetCurrentColumn < 0 || SheetCurrentColumn >= table.ColumnCount) { ShowStatus("Click a cell in the column you want to rename first.", isError: true); return; }
        OpenSheetPanel(SheetAction.RenameColumn, $"RENAME COLUMN · {DescribeColumn(table, SheetCurrentColumn)}", "Rename", "<From_…> references that use the old column name are renamed when you save.", asksName: true, name: table.Headers[SheetCurrentColumn]);
    }

    [RelayCommand]
    private void OpenRemoveSheetColumn()
    {
        if (ActiveOtherTable is not { } table) return;
        if (SheetCurrentColumn < 0 || SheetCurrentColumn >= table.ColumnCount) { ShowStatus("Click a cell in the column you want to remove first.", isError: true); return; }
        OpenSheetPanel(SheetAction.RemoveColumn, $"REMOVE COLUMN · {DescribeColumn(table, SheetCurrentColumn)}", "Remove column", "The column is emptied when you save; columns to its right keep their place. Refused while a <From_…> reference points at it. You can undo until you save.", isDelete: true);
    }

    private static string DescribeColumn(LoadTestingTool.Editing.SheetTable table, int index)
        => table.Headers[index].Length == 0 ? $"column {index + 1}" : table.Headers[index];

    [RelayCommand]
    private void CloseSheetPanel()
    {
        SheetPanelVisible = false;
        _sheetAction = SheetAction.None;
    }

    // ---- Panel: confirm ------------------------------------------------------------------------

    [RelayCommand]
    private void ConfirmSheetAction()
    {
        var action = _sheetAction;
        if (action == SheetAction.None) return;
        var name = SheetPanelName.Trim();
        var count = (int)Math.Clamp(SheetPanelCount ?? 1, 1, 500);
        var previous = ActiveSheet;
        Record($"{SheetPanelTitle.Split('·')[0].Trim().ToLowerInvariant()}{(name.Length > 0 ? " " + name : string.Empty)}");
        try
        {
            switch (action)
            {
                case SheetAction.AddSheet:
                    _document.AddSheet(name);
                    Finish($"Added sheet {name}. It is written when you save.", select: name);
                    break;
                case SheetAction.RenameSheet:
                    _document.RenameSheet(previous, name);
                    Finish($"Renamed sheet {previous} to {name}.", select: name);
                    break;
                case SheetAction.RemoveSheet:
                    _document.RemoveSheet(previous);
                    Finish($"Removed sheet {previous}. It is deleted from the file when you save; undo brings it back until then.", select: DataSheet);
                    break;
                case SheetAction.AddRows:
                    var table = ActiveOtherTable ?? throw new InvalidOperationException("Open one of the other sheets first.");
                    for (var i = 0; i < count; i++) table.AddRow();
                    Finish($"Added {count} {(count == 1 ? "row" : "rows")} to {previous}.", select: previous);
                    break;
                case SheetAction.AddColumns:
                    Finish(AddColumns(ActiveOtherTable ?? throw new InvalidOperationException("Open one of the other sheets first."), name, count), select: previous);
                    break;
                case SheetAction.RenameColumn:
                    var renamed = ActiveOtherTable ?? throw new InvalidOperationException("Open one of the other sheets first.");
                    var old = renamed.Headers[SheetCurrentColumn];
                    if (name.Length == 0) throw new InvalidOperationException("Enter the new column name.");
                    renamed.RenameColumn(SheetCurrentColumn, name);
                    Finish($"Renamed column {old} to {name}.", select: previous);
                    break;
                case SheetAction.RemoveColumn:
                    var source = ActiveOtherTable ?? throw new InvalidOperationException("Open one of the other sheets first.");
                    var header = source.Headers[SheetCurrentColumn];
                    var refs = header.Length == 0 ? [] : _document.FindColumnReferences(source.SheetName, header);
                    if (refs.Count > 0) throw new InvalidOperationException($"{refs.Count} <From_…> {(refs.Count == 1 ? "reference points" : "references point")} at column {header}, so it cannot be removed. Change {(refs.Count == 1 ? "it" : "them")} first.");
                    source.RemoveColumn(SheetCurrentColumn);
                    SheetCurrentColumn = -1;
                    Finish($"Removed column {(header.Length == 0 ? "(no header)" : header)}. It is emptied when you save.", select: previous);
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            DiscardLastRecord();
            ShowStatus(ex.Message, isError: true);
        }

        void Finish(string message, string select)
        {
            SheetPanelVisible = false;
            _sheetAction = SheetAction.None;
            MarkChanged();
            InitSheets();
            if (OpenSheets.Contains(select, StringComparer.OrdinalIgnoreCase)) ActiveSheet = OpenSheets.First(n => n.Equals(select, StringComparison.OrdinalIgnoreCase));
            else LoadActiveSheet();
            ShowStatus(message, isError: false);
        }
    }

    private string AddColumns(LoadTestingTool.Editing.SheetTable table, string name, int count)
    {
        if (name.Length == 0) throw new InvalidOperationException("Enter a name for the column.");
        var names = count == 1 ? [name] : Enumerable.Range(1, count).Select(i => name + i.ToString(CultureInfo.InvariantCulture)).ToList();
        var taken = names.FirstOrDefault(n => table.FindColumn(n) >= 0);
        if (taken is not null) throw new InvalidOperationException($"{table.SheetName} already has a column '{taken}'.");
        var current = SheetCurrentColumn >= 0 && SheetCurrentColumn < table.ColumnCount ? SheetCurrentColumn : -1;
        var position = SheetPanelPlacement switch
        {
            PlaceColumnBefore when current >= 0 => current,
            PlaceColumnAfter when current >= 0 => current + 1,
            _ => table.ColumnCount,
        };
        foreach (var header in names) table.InsertColumnBefore(position++, header);
        return $"Added {count} {(count == 1 ? "column" : "columns")} to {table.SheetName}: {string.Join(", ", names)}.";
    }

    // ---- Rows ------------------------------------------------------------------------------------

    [RelayCommand]
    private void RemoveSheetRow()
    {
        if (ActiveOtherTable is not { } table || SelectedSheetRow?.Row is not { } row) { ShowStatus("Click a row to remove first.", isError: true); return; }
        Record($"remove row from {ActiveSheet}");
        table.RemoveRow(row);
        MarkChanged();
        LoadActiveSheet();
        ShowStatus("Removed the row. Its cells are cleared when you save; undo brings it back until then.", isError: false);
    }
}
