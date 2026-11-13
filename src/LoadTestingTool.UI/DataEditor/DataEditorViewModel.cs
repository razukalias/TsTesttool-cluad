using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// Edit a workbook's data rows testcase by testcase: each grid line pairs a request row with its response
/// row, and only the columns that matter to the selected testcase are shown.
/// </summary>
public sealed partial class DataEditorViewModel : ObservableObject
{
    private static readonly HashSet<string> ReservedColumns = new(["testcaseindex", "testcase", "dataid"], StringComparer.OrdinalIgnoreCase);

    private WorkbookDocument _document;
    private readonly List<DataRowViewModel> _allRows = [];
    private bool _loading;

    public DataEditorViewModel(string workbookPath, string? instanceName = null)
    {
        WorkbookPath = Path.GetFullPath(workbookPath);
        InstanceName = string.IsNullOrWhiteSpace(instanceName) ? Path.GetFileNameWithoutExtension(workbookPath) : instanceName;
        _document = WorkbookDocument.Load(WorkbookPath);
        LoadTestcases(null);
    }

    public string WorkbookPath { get; }
    public string InstanceName { get; }
    public string FileName => Path.GetFileName(WorkbookPath);

    /// <summary>Backup created by the most recent save, if any; the dashboard uses it to know the file changed.</summary>
    public string? LastBackupPath { get; private set; }

    public ObservableCollection<TestcaseItem> Testcases { get; } = [];
    public ObservableCollection<DataRowViewModel> VisibleRows { get; } = [];
    public ObservableCollection<ProblemViewModel> Problems { get; } = [];
    public IReadOnlyList<GridColumn> Columns { get; private set; } = [];

    /// <summary>Raised after the set of grid columns changed, so the view can rebuild them.</summary>
    public event EventHandler? ColumnsChanged;

    /// <summary>Raised when the window should close (after saving or discarding).</summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty] private TestcaseItem? _selectedTestcase;
    [ObservableProperty] private DataRowViewModel? _selectedRow;
    [ObservableProperty] private ProblemViewModel? _selectedProblem;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _showAllColumns;
    [ObservableProperty] private bool _conflictVisible;
    [ObservableProperty] private bool _closePromptVisible;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(SaveStatusText), nameof(SaveStatusColor))]
    private bool _isDirty;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _statusIsError;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoProblems), nameof(ProblemSummary))]
    private int _errorCount;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoProblems), nameof(ProblemSummary))]
    private int _warningCount;

    public string SaveStatusText => IsDirty ? "Unsaved changes" : "All changes saved";
    public string SaveStatusColor => IsDirty ? "#F6C76B" : "#39D39B";
    public bool HasNoProblems => ErrorCount == 0 && WarningCount == 0;
    public string ProblemSummary => HasNoProblems ? "No problems" : $"{ErrorCount} {(ErrorCount == 1 ? "error" : "errors")}, {WarningCount} {(WarningCount == 1 ? "warning" : "warnings")}";
    public string RowCountText => FilterText.Length == 0 ? $"{VisibleRows.Count} rows" : $"{VisibleRows.Count} of {_allRows.Count} rows";

    // ---- Selection and filtering ---------------------------------------------------------

    partial void OnSelectedTestcaseChanged(TestcaseItem? value)
    {
        if (_loading) return;
        BuildRows();
    }

    partial void OnShowAllColumnsChanged(bool value)
    {
        if (!_loading) BuildRows();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedProblemChanged(ProblemViewModel? value)
    {
        if (value is null) return;
        GoToProblem(value);
        SelectedProblem = null;
    }

    private void LoadTestcases(int? keepIndex)
    {
        _loading = true;
        try
        {
            Testcases.Clear();
            foreach (var info in _document.GetTestcases())
                Testcases.Add(new TestcaseItem(info, _document.GetDataIds(info.Index).Count));
            SelectedTestcase = Testcases.FirstOrDefault(t => t.Index == keepIndex) ?? Testcases.FirstOrDefault();
        }
        finally { _loading = false; }
        UpdateTestcaseResults();
        BuildRows();
        Revalidate();
    }

    private void BuildRows()
    {
        _allRows.Clear();
        SelectedRow = null;
        var testcase = SelectedTestcase;
        if (testcase is null)
        {
            Columns = [];
            VisibleRows.Clear();
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(RowCountText));
            BuildSteps();
            return;
        }

        var pairs = _document.Request.Rows
            .Where(r => !r.IsBlank && WorkbookDocument.EffectiveIndex(r) == testcase.Index)
            .Select(r => (Request: r, Response: FindResponse(r)))
            .ToList();
        Columns = BuildColumns(testcase, pairs);
        foreach (var pair in pairs) _allRows.Add(new DataRowViewModel(testcase.Index, pair.Request, pair.Response, Columns, OnCellEdited));
        AssignOutcomes();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
        ApplyFilter();
        SelectedRow = VisibleRows.FirstOrDefault();
        BuildSteps();
    }

    private void ApplyFilter()
    {
        var text = FilterText.Trim();
        var keep = SelectedRow;
        VisibleRows.Clear();
        foreach (var row in _allRows)
        {
            if (ShowFailedOnly && row.Outcome is not { Passed: false }) continue;
            if (text.Length == 0 || row.Cells.Any(c => c.Contains(text, StringComparison.OrdinalIgnoreCase)))
                VisibleRows.Add(row);
        }
        if (keep is not null && VisibleRows.Contains(keep)) SelectedRow = keep;
        OnPropertyChanged(nameof(RowCountText));
    }

    /// <summary>
    /// Request and response columns relevant to the testcase: those for its own steps, columns not tied to
    /// any step, and any column that already holds a value in one of its rows. Everything else is hidden
    /// unless "show every column" is on.
    /// </summary>
    private IReadOnlyList<GridColumn> BuildColumns(TestcaseItem testcase, List<(TableRow Request, TableRow? Response)> pairs)
    {
        var steps = new HashSet<string>(testcase.Steps, StringComparer.OrdinalIgnoreCase);
        var columns = new List<GridColumn>();
        var cell = 1;

        var requestHeaders = _document.Request.Headers;
        for (var i = 0; i < requestHeaders.Count; i++)
        {
            var header = requestHeaders[i];
            if (ReservedColumns.Contains(header)) continue;
            var prefix = RequestStepPrefix(header);
            var relevant = ShowAllColumns || prefix is null || steps.Contains(prefix) || pairs.Any(p => !string.IsNullOrWhiteSpace(p.Request.GetAt(i)));
            if (relevant) columns.Add(new GridColumn(header, ColumnGroup.Request, cell++, i));
        }

        var responseHeaders = _document.Response.Headers;
        for (var i = 0; i < responseHeaders.Count; i++)
        {
            var header = responseHeaders[i];
            if (ReservedColumns.Contains(header)) continue;
            var prefix = ResponseStepPrefix(header);
            var relevant = ShowAllColumns || prefix is null || steps.Contains(prefix) || pairs.Any(p => p.Response is { } r && !string.IsNullOrWhiteSpace(r.GetAt(i)));
            if (relevant) columns.Add(new GridColumn(DisplayResponseHeader(header), ColumnGroup.Response, cell++, i));
        }
        return columns;
    }

    private static string? RequestStepPrefix(string header)
    {
        var dot = header.IndexOf('.');
        return dot > 0 ? header[..dot] : null;
    }

    private static string? ResponseStepPrefix(string header)
    {
        var text = header.Trim();
        if (text.StartsWith('{') && text.IndexOf('}') is var closing and > 0) text = text[(closing + 1)..];
        var dot = text.IndexOf('.');
        return dot > 0 ? text[..dot].Trim() : null;
    }

    private static string DisplayResponseHeader(string header)
        => header.StartsWith("{eq}", StringComparison.OrdinalIgnoreCase) ? header[4..] : header;

    private TableRow? FindResponse(TableRow request)
    {
        var index = WorkbookDocument.EffectiveIndex(request);
        var dataId = request.Get("dataid").Trim();
        return _document.Response.Rows.FirstOrDefault(r => WorkbookDocument.EffectiveIndex(r) == index
            && string.Equals(r.Get("dataid").Trim(), dataId, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Editing -------------------------------------------------------------------------

    private void OnCellEdited(DataRowViewModel row, int cellIndex, string value)
    {
        if (cellIndex == 0 && value == row.CurrentDataId) return;
        if (cellIndex > 0)
        {
            var shown = Columns[cellIndex - 1];
            var existing = shown.Group == ColumnGroup.Request ? row.Request.GetAt(shown.SheetColumn) : row.Response?.GetAt(shown.SheetColumn) ?? string.Empty;
            if (existing == value) return;
        }
        if (cellIndex == 0)
        {
            Record($"rename data ID {row.CurrentDataId}");
            try
            {
                if (!_document.RenameDataRow(row.TestcaseIndex, row.CurrentDataId, value))
                {
                    // The row was never paired by ID (for example its ID was empty): set it on the rows we hold.
                    row.Request.Set("dataid", value);
                    row.Response?.Set("dataid", value);
                }
                row.CurrentDataId = value.Trim();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                DiscardLastRecord();
                row.SetCellSilently(0, row.CurrentDataId);
                ShowStatus(ex is ArgumentException ? "A data ID cannot be empty." : ex.Message, isError: true);
                return;
            }
        }
        else
        {
            var column = Columns[cellIndex - 1];
            Record($"edit {column.Title} of {row.CurrentDataId}");
            if (column.Group == ColumnGroup.Request) row.Request.SetAt(column.SheetColumn, value);
            else EnsureResponse(row).SetAt(column.SheetColumn, value);
        }
        MarkChanged();
    }

    private TableRow EnsureResponse(DataRowViewModel row)
    {
        if (row.Response is { } existing) return existing;
        var created = _document.Response.AddRow();
        created["testcaseindex"] = row.TestcaseIndex.ToString(CultureInfo.InvariantCulture);
        created["testcase"] = row.Request.Get("testcase");
        created["dataid"] = row.CurrentDataId;
        row.Response = created;
        return created;
    }

    [RelayCommand]
    private void AddRow()
    {
        if (SelectedTestcase is not { } testcase) return;
        AppendRow(testcase, NextDataId(testcase), copyFrom: null);
    }

    [RelayCommand]
    private void DuplicateRow()
    {
        if (SelectedTestcase is not { } testcase || SelectedRow is not { } source) return;
        AppendRow(testcase, NextDataId(testcase), source);
    }

    private void AppendRow(TestcaseItem testcase, string dataId, DataRowViewModel? copyFrom)
    {
        Record(copyFrom is null ? $"add row {dataId}" : $"duplicate row as {dataId}");
        var (request, response) = _document.AddDataRow(testcase.Index, dataId);
        if (copyFrom is not null)
        {
            CopyValues(copyFrom.Request, request, _document.Request);
            if (copyFrom.Response is { } sourceResponse) CopyValues(sourceResponse, response, _document.Response);
        }
        FilterText = string.Empty;
        var row = new DataRowViewModel(testcase.Index, request, response, Columns, OnCellEdited);
        _allRows.Add(row);
        ApplyFilter();
        testcase.RowCount++;
        SelectedRow = row;
        MarkChanged();
        ShowStatus($"Added row {dataId}. It is saved with the workbook.", isError: false);
    }

    private static void CopyValues(TableRow from, TableRow to, SheetTable table)
    {
        for (var i = 0; i < table.ColumnCount; i++)
            if (!ReservedColumns.Contains(table.Headers[i])) to.SetAt(i, from.GetAt(i));
    }

    [RelayCommand]
    private void RemoveRow()
    {
        if (SelectedTestcase is not { } testcase || SelectedRow is not { } row) return;
        Record($"remove row {row.CurrentDataId}");
        _document.Request.RemoveRow(row.Request);
        if (row.Response is not null) _document.Response.RemoveRow(row.Response);
        _allRows.Remove(row);
        VisibleRows.Remove(row);
        testcase.RowCount = Math.Max(0, testcase.RowCount - 1);
        MarkChanged();
        ShowStatus($"Removed row {row.CurrentDataId}. Reload to bring it back until you save.", isError: false);
    }

    private string NextDataId(TestcaseItem testcase)
    {
        var ids = _document.GetDataIds(testcase.Index);
        var taken = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var match = Regex.Match(ids.LastOrDefault() ?? string.Empty, @"^(.*?)(\d{1,9})$");
        var prefix = match.Success ? match.Groups[1].Value : "ROW";
        var width = match.Success ? match.Groups[2].Length : 3;
        var number = match.Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : ids.Count;
        string candidate;
        do
        {
            number++;
            candidate = prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        }
        while (taken.Contains(candidate));
        return candidate;
    }

    private void MarkChanged()
    {
        IsDirty = _document.IsDirty;
        Revalidate();
    }

    // ---- Problems ------------------------------------------------------------------------

    private void Revalidate()
    {
        var problems = _document.Validate();
        Problems.Clear();
        foreach (var problem in problems.OrderBy(p => p.Severity)) Problems.Add(new ProblemViewModel(problem));
        ErrorCount = problems.Count(p => p.Severity == ProblemSeverity.Error);
        WarningCount = problems.Count - ErrorCount;
    }

    private void GoToProblem(ProblemViewModel item)
    {
        var row = item.Problem.Row;
        var onDataSheet = item.Problem.Sheet == _document.Request.SheetName || item.Problem.Sheet == _document.Response.SheetName;
        var onConfig = item.Problem.Sheet == _document.Config.SheetName;
        if (row is null || (!onDataSheet && !onConfig))
        {
            ShowStatus("That problem is about a column, not a row.", isError: false);
            return;
        }
        var index = WorkbookDocument.EffectiveIndex(row);
        if (SelectedTestcase?.Index != index)
        {
            var testcase = Testcases.FirstOrDefault(t => t.Index == index);
            if (testcase is null) { ShowStatus($"Testcase {index} has no steps, so its rows are not shown here.", isError: false); return; }
            SelectedTestcase = testcase;
        }
        if (onConfig)
        {
            ShowSteps = true;
            SelectedStep = StepRows.FirstOrDefault(r => r.Row == row);
            return;
        }
        ShowSteps = false;
        FilterText = string.Empty;
        var target = _allRows.FirstOrDefault(r => r.Request == row || r.Response == row);
        if (target is not null) SelectedRow = target;
    }

    // ---- Saving, reloading, closing ------------------------------------------------------

    [RelayCommand]
    private void Save() => SaveCore(new WorkbookSaveOptions());

    [RelayCommand]
    private void OverwriteAndSave() => SaveCore(new WorkbookSaveOptions { OverwriteExternalChanges = true });

    private bool SaveCore(WorkbookSaveOptions options)
    {
        try
        {
            var result = _document.Save(options);
            if (result.Saved) ClearUndoHistory();
            ConflictVisible = false;
            IsDirty = _document.IsDirty;
            if (result.BackupPath is not null) LastBackupPath = result.BackupPath;
            Revalidate();
            ShowStatus(result.Saved
                ? $"Saved {FileName}.{(result.BackupPath is null ? string.Empty : $" Backup: {Path.GetFileName(result.BackupPath)}")}"
                : "Nothing to save.", isError: false);
            return true;
        }
        catch (WorkbookChangedException)
        {
            ConflictVisible = true;
            ShowStatus($"{FileName} changed on disk (for example in Excel) after you opened it.", isError: true);
        }
        catch (IOException ex)
        {
            ShowStatus($"Could not save: {ex.Message} If the workbook is open in Excel, close it and try again.", isError: true);
        }
        return false;
    }

    [RelayCommand]
    private void Reload()
    {
        var keep = SelectedTestcase?.Index;
        _document = WorkbookDocument.Load(WorkbookPath);
        ClearUndoHistory();
        ConflictVisible = false;
        ClosePromptVisible = false;
        IsDirty = false;
        FilterText = string.Empty;
        LoadTestcases(keep);
        ShowStatus($"Reloaded {FileName} from disk.", isError: false);
    }

    /// <summary>Called by the window when the user tries to close it; returns true when it may close now.</summary>
    public bool RequestClose()
    {
        if (!IsDirty) return true;
        ClosePromptVisible = true;
        return false;
    }

    [RelayCommand]
    private void SaveAndClose()
    {
        if (!SaveCore(new WorkbookSaveOptions())) { ClosePromptVisible = false; return; }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DiscardAndClose()
    {
        IsDirty = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void KeepEditing() => ClosePromptVisible = false;

    private void ShowStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }
}
