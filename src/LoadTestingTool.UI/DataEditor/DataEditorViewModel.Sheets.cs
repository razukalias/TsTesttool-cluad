using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One line of a sheet shown as a plain table. Editable only when it belongs to a sheet the editor holds (config).</summary>
public sealed class SheetRowViewModel : ICellRow
{
    private readonly Action<SheetRowViewModel, int, string>? _edited;
    private bool _suppress;

    public SheetRowViewModel(IEnumerable<string> cells, TableRow? row = null, Action<SheetRowViewModel, int, string>? edited = null)
    {
        Cells = new ObservableCollection<string>(cells);
        Row = row;
        _edited = edited;
        Cells.CollectionChanged += (_, e) =>
        {
            if (_suppress || e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Replace) return;
            _edited?.Invoke(this, e.NewStartingIndex, Cells[e.NewStartingIndex] ?? string.Empty);
        };
    }

    public ObservableCollection<string> Cells { get; }
    public TableRow? Row { get; }
    public bool Editable => Row is not null;

    internal void SetSilently(int cell, string value)
    {
        _suppress = true;
        try { Cells[cell] = value; }
        finally { _suppress = false; }
    }
}

/// <summary>
/// The sheets of the workbook as tabs in Table view: the request and response rows together (the data grid), the config
/// sheet (editable) and every other sheet (read only here). The sheet filter chooses which tabs are shown.
/// </summary>
public sealed partial class DataEditorViewModel
{
    public const string DataSheet = "Data (request + response)";
    public const string ConfigSheet = "config";

    private readonly List<string> _hiddenSheets = [];
    private bool _syncingSheets;

    /// <summary>Every sheet that can be shown, in workbook order, with a tick for the ones that are.</summary>
    public ObservableCollection<SheetChoice> SheetChoices { get; } = [];

    /// <summary>The tabs shown.</summary>
    public ObservableCollection<string> OpenSheets { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowDataGrid), nameof(ShowSheetGrid), nameof(SheetIsEditable), nameof(SheetHasRowToggle), nameof(IsConfigSheet), nameof(IsOtherSheet))]
    private string _activeSheet = DataSheet;

    /// <summary>For the config sheet: show every row instead of only the selected testcase's (or step's) rows.</summary>
    [ObservableProperty] private bool _showAllRows;

    public IReadOnlyList<string> SheetHeaders { get; private set; } = [];
    public ObservableCollection<SheetRowViewModel> SheetRows { get; } = [];
    public event EventHandler? SheetColumnsChanged;

    public bool ShowDataGrid => ShowData && ActiveSheet == DataSheet;
    public bool ShowSheetGrid => ShowData && ActiveSheet != DataSheet;
    public bool IsConfigSheet => ActiveSheet.Equals(ConfigSheet, StringComparison.OrdinalIgnoreCase);

    /// <summary>A sheet the runner does not read: its cells, rows and columns can be edited, and the sheet itself renamed or removed.</summary>
    public bool IsOtherSheet => ShowSheetGrid && !IsConfigSheet;
    public bool SheetIsEditable => ShowSheetGrid;
    public bool SheetHasRowToggle => IsConfigSheet && (SelectedTestcase is not null || FilteringByStep);
    public string SheetSummary => ShowSheetGrid
        ? $"{SheetRows.Count} {(SheetRows.Count == 1 ? "row" : "rows")}"
        : string.Empty;

    /// <summary>Called when the workbook is (re)loaded: lists the sheets and applies the choice remembered for this instance.</summary>
    private void InitSheets()
    {
        _syncingSheets = true;
        try
        {
            _hiddenSheets.Clear();
            _hiddenSheets.AddRange(EditorSettings.Load().HiddenSheets.GetValueOrDefault(WorkbookPath) ?? []);
            SheetChoices.Clear();
            foreach (var name in new[] { DataSheet, ConfigSheet }.Concat(_document.OtherSheets.Where(s => !s.Equals("config", StringComparison.OrdinalIgnoreCase) && !s.Equals("request", StringComparison.OrdinalIgnoreCase) && !s.Equals("response", StringComparison.OrdinalIgnoreCase))))
            {
                var choice = new SheetChoice(name, !_hiddenSheets.Contains(name, StringComparer.OrdinalIgnoreCase));
                choice.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SheetChoice.IsShown)) OnSheetChoiceChanged(); };
                SheetChoices.Add(choice);
            }
        }
        finally { _syncingSheets = false; }
        RefreshOpenSheets();
    }

    private void OnSheetChoiceChanged()
    {
        if (_syncingSheets) return;
        _hiddenSheets.Clear();
        _hiddenSheets.AddRange(SheetChoices.Where(c => !c.IsShown).Select(c => c.Name));
        var settings = EditorSettings.Load();
        if (_hiddenSheets.Count == 0) settings.HiddenSheets.Remove(WorkbookPath); else settings.HiddenSheets[WorkbookPath] = [.. _hiddenSheets];
        settings.Save();
        RefreshOpenSheets();
        OnPropertyChanged(nameof(SheetFilterText));
    }

    /// <summary>Text on the toolbar button: "All sheets" or how many are shown.</summary>
    public string SheetFilterText => SheetChoices.All(c => c.IsShown) ? "All sheets" : $"{SheetChoices.Count(c => c.IsShown)} of {SheetChoices.Count} sheets";

    private void RefreshOpenSheets()
    {
        var shown = SheetChoices.Where(c => c.IsShown).Select(c => c.Name).ToList();
        if (shown.Count == 0) shown.Add(DataSheet);   // there is always something to look at
        OpenSheets.Clear();
        foreach (var name in shown) OpenSheets.Add(name);
        if (!OpenSheets.Contains(ActiveSheet)) ActiveSheet = OpenSheets[0]; else LoadActiveSheet();
        OnPropertyChanged(nameof(SheetFilterText));
    }

    partial void OnActiveSheetChanged(string value) => LoadActiveSheet();
    partial void OnShowAllRowsChanged(bool value) => LoadActiveSheet();

    private void ReloadSheetIfShown()
    {
        if (ActiveSheet != DataSheet) LoadActiveSheet();
    }

    /// <summary>Fills the sheet grid for the active tab.</summary>
    private void LoadActiveSheet()
    {
        OnPropertyChanged(nameof(ShowDataGrid));
        OnPropertyChanged(nameof(ShowSheetGrid));
        OnPropertyChanged(nameof(SheetHasRowToggle));
        OnPropertyChanged(nameof(IsConfigSheet));
        OnPropertyChanged(nameof(IsOtherSheet));
        SheetRows.Clear();
        SelectedSheetRow = null;
        if (ActiveSheet == DataSheet) { SheetHeaders = []; SheetColumnsChanged?.Invoke(this, EventArgs.Empty); OnPropertyChanged(nameof(SheetSummary)); return; }

        if (IsConfigSheet)
        {
            var config = _document.Config;
            SheetHeaders = config.Headers.ToList();
            var onlyStep = FilteringByStep ? StepFilter : null;
            foreach (var row in config.Rows.Where(r => !r.IsBlank))
            {
                if (!ShowAllRows && SelectedTestcase is { } testcase && WorkbookDocument.EffectiveIndex(row) != testcase.Index) continue;
                if (!ShowAllRows && onlyStep is not null && !string.Equals(row.Get("stepname").Trim(), onlyStep, StringComparison.OrdinalIgnoreCase)) continue;
                SheetRows.Add(new SheetRowViewModel(Enumerable.Range(0, config.ColumnCount).Select(row.GetAt), row, OnConfigCellEdited));
            }
        }
        else
        {
            var table = _document.FindOtherSheet(ActiveSheet);
            SheetHeaders = table?.Headers.ToList() ?? [];
            if (table is not null)
                foreach (var row in table.Rows)
                    SheetRows.Add(new SheetRowViewModel(Enumerable.Range(0, table.ColumnCount).Select(row.GetAt), row, OnOtherCellEdited));
        }
        SheetColumnsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(SheetSummary));
    }

    private void OnConfigCellEdited(SheetRowViewModel row, int cell, string value)
    {
        if (row.Row is null || row.Row.GetAt(cell) == value) return;
        Record($"edit {SheetHeaders[cell]} in config");
        row.Row.SetAt(cell, value);
        StepsChanged();
        _stepsStale = true;   // the step list is rebuilt when Config edit mode opens
    }

    private bool _stepsStale;
}

/// <summary>One entry of the sheet filter: a sheet and whether its tab is shown.</summary>
public sealed partial class SheetChoice(string name, bool shown) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private bool _isShown = shown;
}
