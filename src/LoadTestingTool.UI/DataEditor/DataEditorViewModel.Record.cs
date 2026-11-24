using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Record view: the selected data row shown vertically, each column as a line of value parts.</summary>
public sealed partial class DataEditorViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowData), nameof(ShowDataGrid), nameof(ShowSheetGrid), nameof(SheetSummary), nameof(IsOtherSheet), nameof(ViewModeIndex))]
    private bool _showRecord;

    public ObservableCollection<RecordLineViewModel> RequestLines { get; } = [];
    public ObservableCollection<RecordLineViewModel> ExpectedLines { get; } = [];
    public string RecordTitle => SelectedRow is null ? "Select a row in Table view to see it here." : $"Data ID {SelectedRow.CurrentDataId}" + (SelectedTestcase is { } t ? $" · {t.Name}" : string.Empty);

    [RelayCommand]
    private void OpenRecordTab() { ShowJson = false; ShowSteps = false; ShowRecord = true; }

    partial void OnShowRecordChanged(bool value)
    {
        if (value) { ShowSteps = false; ForgetStoredResponses(); BuildRecord(); }
    }


    /// <summary>Rebuilds the lines for the selected row. Called when the row, the mode or the columns change.</summary>
    private void BuildRecord()
    {
        RequestLines.Clear();
        ExpectedLines.Clear();
        OnPropertyChanged(nameof(RecordTitle));
        if (SelectedRow is not { } row) return;
        foreach (var column in Columns)
        {
            var expected = column.Group == ColumnGroup.Response;
            var dontRun = !expected && column.Title.Equals("dontrun", StringComparison.OrdinalIgnoreCase);
            (expected ? ExpectedLines : RequestLines).Add(new RecordLineViewModel(row, column.CellIndex, column.Title, expected, WarningsFor(expected, dontRun)));
        }
    }

    /// <summary>What the [+] menu offers to refer to: the sheets' headers, and the response fields the workbook already expects.</summary>
    public RecordMenu.Sources MenuSources()
    {
        static List<string> Names(LoadTestingTool.Editing.SheetTable t) => t.Headers.Where(h => h.Length > 0 && !ReservedColumns.Contains(h)).ToList();
        return new(Names(_document.Request), ResponseFieldNames(), Names(_document.Config), ResponseSteps());
    }
}
