using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// The steps of the selected testcase: its rows in the config sheet, edited in a grid of their own.
public sealed partial class DataEditorViewModel
{
    private static readonly HashSet<string> StepReservedColumns = new(["testcaseindex", "testcase", "stepname"], StringComparer.OrdinalIgnoreCase);
    private bool _dataStale;

    public ObservableCollection<StepRowViewModel> StepRows { get; } = [];
    public IReadOnlyList<GridColumn> StepColumns { get; private set; } = [];

    /// <summary>Raised after the step grid's columns changed, so the view can rebuild them.</summary>
    public event EventHandler? StepColumnsChanged;

    [ObservableProperty] private StepRowViewModel? _selectedStep;

    /// <summary>The one step shown alone as a form (chosen in the tree); null shows the testcase's whole step list.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFocusedStep), nameof(NoFocusedStep))]
    private StepRowViewModel? _focusedStep;

    public bool HasFocusedStep => FocusedStep is not null;
    public bool NoFocusedStep => FocusedStep is null;

    /// <summary>The Steps tab as the original side-by-side table instead of step cards.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowStepCards))]
    private bool _showStepTable;

    public bool ShowStepCards => !ShowStepTable;

    [RelayCommand]
    private void ToggleStepTable() => ShowStepTable = !ShowStepTable;

    [RelayCommand]
    private void ShowAllSteps() => FocusedStep = null;

    [RelayCommand]
    private void ExpandAllSteps() { foreach (var step in StepRows) step.IsExpanded = true; }

    [RelayCommand]
    private void CollapseAllSteps() { foreach (var step in StepRows) step.IsExpanded = false; }

    /// <summary>From the tree: clicking a testcase shows all of its steps as a list.</summary>
    public void ShowTestcaseSteps()
    {
        FocusedStep = null;   // the view mode (Table view or Config edit mode) stays as it was
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowData), nameof(ShowDataGrid), nameof(ShowSheetGrid), nameof(SheetSummary), nameof(IsOtherSheet))]
    private bool _showSteps;

    public bool ShowData => !ShowSteps && !ShowRecord;
    public string StepCountText => $"{StepRows.Count} {(StepRows.Count == 1 ? "step" : "steps")}";

    [RelayCommand]
    private void OpenDataTab() { ShowRecord = false; ShowSteps = false; }

    [RelayCommand]
    private void OpenStepsTab() { ShowRecord = false; ShowSteps = true; }

    partial void OnShowStepsChanged(bool value)
    {
        if (value) { ColumnFiltersVisible = false; ShowRecord = false; }
        if (value && _stepsStale && !_loading) { _stepsStale = false; BuildSteps(); }
        OnPropertyChanged(nameof(CurrentColumnTitle));
        CurrentCellIndex = -1;
        UpdateMatches();
        if (value || !_dataStale || _loading) return;
        _dataStale = false;
        BuildRows(); // step names decide which data columns matter, so rebuild after step edits
    }

    private void BuildSteps()
    {
        var open = StepRows.Where(r => r.IsExpanded).Select(r => r.CurrentName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var focused = FocusedStep?.CurrentName;
        StepRows.Clear();
        SelectedStep = null;
        var columns = new List<GridColumn>();
        for (var i = 0; i < _document.Config.ColumnCount; i++)
        {
            var header = _document.Config.Headers[i];
            if (!StepReservedColumns.Contains(header) && !LegacyColumns.IsLegacy(header)) columns.Add(new GridColumn(header, ColumnGroup.Request, columns.Count + 1, i));
        }
        StepColumns = columns;
        if (SelectedTestcase is { } testcase)
            foreach (var row in _document.Config.Rows.Where(r => !r.IsBlank && WorkbookDocument.EffectiveIndex(r) == testcase.Index))
                StepRows.Add(new StepRowViewModel(row, StepColumns, OnStepEdited, _document.Config) { Editor = this });
        else if (ShowInstance)
            foreach (var row in _document.Config.Rows.Where(r => !r.IsBlank))
                StepRows.Add(new StepRowViewModel(row, StepColumns, OnStepEdited, _document.Config) { Editor = this });
        foreach (var step in StepRows) step.IsExpanded = open.Contains(step.CurrentName);
        FocusedStep = focused is null ? null : StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, focused, StringComparison.OrdinalIgnoreCase));
        StepColumnsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(StepCountText));
        SelectedStep = StepRows.FirstOrDefault();
        OnPropertyChanged(nameof(PreviewStepNames));
        RefreshPreview();
        ReloadSheetIfShown();
    }

    /// <summary>Adds a config column the sheet lacks and sets this step's value in it; the step list is rebuilt around the new column.</summary>
    internal void SetStepCellAddingColumn(StepRowViewModel step, string header, string value)
    {
        Record($"add column {header}");
        _document.Config.EnsureColumn(header);
        step.Row.Set(header, value);
        RaiseStepEdited(step.CurrentName);
        StepsChanged();
        BuildSteps();
    }

    /// <summary>Raised with the testcase name and step name after a step was changed, so run statuses can show they are out of date.</summary>
    public event Action<string, string>? StepEdited;

    private void RaiseStepEdited(string step) { if (SelectedTestcase is { } testcase) StepEdited?.Invoke(testcase.Name, step); }

    private void OnStepEdited(StepRowViewModel step, int cellIndex, string value)
    {
        if (cellIndex == 0 ? value == step.CurrentName : step.Row.GetAt(StepColumns[cellIndex - 1].SheetColumn) == value) return;
        if (cellIndex == 0)
        {
            var name = value.Trim();
            if (name.Length == 0) { RejectStepName(step, "A step name cannot be empty."); return; }
            var old = step.CurrentName;
            RaiseStepEdited(old);
            Record($"rename step {old} to {name}");
            StepRenameResult result;
            try { result = _document.RenameStep(old, name); }
            catch (InvalidOperationException ex)
            {
                DiscardLastRecord();
                RejectStepName(step, ex.Message);
                return;
            }
            step.Row.Set("stepname", name);
            step.CurrentName = name;
            if (name != value) step.SetCellSilently(0, name);
            var parentColumn = StepColumns.ToList().FindIndex(c => c.Title.Equals("parent", StringComparison.OrdinalIgnoreCase));
            if (parentColumn >= 0)   // steps inside a renamed control step now name it differently
                foreach (var other in StepRows) other.SetCellSilently(parentColumn + 1, other.Row.GetAt(StepColumns[parentColumn].SheetColumn));
            StepsChanged();
            ShowStatus(RenameSummary(old, name, result), isError: false);
            return;
        }
        else
        {
            Record($"edit {StepColumns[cellIndex - 1].Title} of step {step.CurrentName}");
            step.Row.SetAt(StepColumns[cellIndex - 1].SheetColumn, value);
            RaiseStepEdited(step.CurrentName);
        }
        StepsChanged();
    }

    private static string RenameSummary(string old, string name, StepRenameResult r)
    {
        var parts = new List<string> { $"Renamed step {old} to {name}" };
        if (r.ColumnsRenamed > 0) parts.Add($"{r.ColumnsRenamed} data {(r.ColumnsRenamed == 1 ? "column" : "columns")} renamed");
        if (r.CellsRewritten > 0) parts.Add($"{r.CellsRewritten} {(r.CellsRewritten == 1 ? "cell" : "cells")} updated (references, dontrun, extractvariable)");
        var text = string.Join("; ", parts) + ".";
        if (r.ElsewhereMentions > 0) text += $" {r.ElsewhereMentions} {(r.ElsewhereMentions == 1 ? "cell" : "cells")} on other sheets still use the old name; update {(r.ElsewhereMentions == 1 ? "it" : "them")} in Excel. Template files are not scanned.";
        return text;
    }

    private void RejectStepName(StepRowViewModel step, string message)
    {
        step.SetCellSilently(0, step.CurrentName);
        ShowStatus(message, isError: true);
    }

    private bool StepNameTaken(string name, TableRow? except = null)
        => _document.Config.Rows.Any(r => !ReferenceEquals(r, except) && !r.IsBlank && string.Equals(r.Get("stepname").Trim(), name, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void AddStep()
    {
        if (SelectedTestcase is not { } testcase) return;
        var number = StepRows.Count;
        string name;
        do name = "Step" + (++number).ToString(CultureInfo.InvariantCulture);
        while (StepNameTaken(name));

        Record("add step");
        var row = _document.Config.AddRow();
        row.Set("testcaseindex", testcase.Index.ToString(CultureInfo.InvariantCulture));
        row.Set("testcase", testcase.Name);
        row.Set("stepname", name);
        row.Set("steptype", "HTTP");
        row.Set("stage", "Test");
        row.Set("enabled", "True");
        var step = new StepRowViewModel(row, StepColumns, OnStepEdited, _document.Config) { Editor = this, IsExpanded = true };
        StepRows.Add(step);
        SelectedStep = step;
        OnPropertyChanged(nameof(StepCountText));
        StepsChanged();
        ShowStatus($"Added step {name}. Steps run in sheet order, so it runs after the others; rename it, fill in its URL or template, then add its data columns on the Data tab.", isError: false);
    }

    [RelayCommand]
    private void RemoveStep() => RemoveStepRow(SelectedStep);

    /// <summary>Removes the step called <paramref name="name"/> of the selected testcase (the one right-clicked in the tree), not whichever is selected.</summary>
    public void RemoveStepNamed(string name)
        => RemoveStepRow(StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase)));

    private void RemoveStepRow(StepRowViewModel? step)
    {
        if (step is null) return;
        var name = step.CurrentName;
        var index = StepRows.IndexOf(step);
        Record($"remove step {name}");
        var result = _document.RemoveStep(step.Row);
        StepRows.Remove(step);
        if (ReferenceEquals(FocusedStep, step)) FocusedStep = null;
        SelectedStep = StepRows.Count == 0 ? null : StepRows[Math.Min(index, StepRows.Count - 1)];
        OnPropertyChanged(nameof(StepCountText));
        StepsChanged();
        var text = $"Removed step {name}";
        if (result.ColumnsRemoved > 0) text += $" and its {result.ColumnsRemoved} data {(result.ColumnsRemoved == 1 ? "column" : "columns")} from the request and response sheets (Undo brings them back)";
        text += ".";
        if (result.ReferencesLeft > 0) text += $" {result.ReferencesLeft} {(result.ReferencesLeft == 1 ? "cell" : "cells")} still read its response at run time; change {(result.ReferencesLeft == 1 ? "it" : "them")}.";
        ShowStatus(text, isError: result.ReferencesLeft > 0);
    }

    /// <summary>Keeps the testcase's step list current and flags the data columns for a rebuild.</summary>
    private void StepsChanged()
    {
        if (SelectedTestcase is { } testcase)
        {
            var info = _document.GetTestcases().FirstOrDefault(t => t.Index == testcase.Index);
            if (info is not null) testcase.UpdateSteps(info.Steps, info.Nesting);
        }
        _dataStale = true;
        MarkChanged();
    }
}
