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

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowData))]
    private bool _showSteps;

    public bool ShowData => !ShowSteps;
    public string StepCountText => $"{StepRows.Count} {(StepRows.Count == 1 ? "step" : "steps")}";

    [RelayCommand]
    private void OpenDataTab() => ShowSteps = false;

    [RelayCommand]
    private void OpenStepsTab() => ShowSteps = true;

    partial void OnShowStepsChanged(bool value)
    {
        if (value || !_dataStale || _loading) return;
        _dataStale = false;
        BuildRows(); // step names decide which data columns matter, so rebuild after step edits
    }

    private void BuildSteps()
    {
        StepRows.Clear();
        SelectedStep = null;
        var columns = new List<GridColumn>();
        for (var i = 0; i < _document.Config.ColumnCount; i++)
        {
            var header = _document.Config.Headers[i];
            if (!StepReservedColumns.Contains(header)) columns.Add(new GridColumn(header, ColumnGroup.Request, columns.Count + 1, i));
        }
        StepColumns = columns;
        if (SelectedTestcase is { } testcase)
            foreach (var row in _document.Config.Rows.Where(r => !r.IsBlank && WorkbookDocument.EffectiveIndex(r) == testcase.Index))
                StepRows.Add(new StepRowViewModel(row, StepColumns, OnStepEdited));
        StepColumnsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(StepCountText));
        SelectedStep = StepRows.FirstOrDefault();
    }

    private void OnStepEdited(StepRowViewModel step, int cellIndex, string value)
    {
        if (cellIndex == 0)
        {
            var name = value.Trim();
            if (!step.IsNew)
            {
                RejectStepName(step, $"Step '{step.CurrentName}' cannot be renamed: its data columns ({step.CurrentName}.…) and expected values refer to that name. Add a new step instead.");
                return;
            }
            if (name.Length == 0) { RejectStepName(step, "A step name cannot be empty."); return; }
            if (StepNameTaken(name, except: step.Row)) { RejectStepName(step, $"A step named '{name}' already exists."); return; }
            step.Row.Set("stepname", name);
            step.CurrentName = name;
            if (name != value) step.SetCellSilently(0, name);
        }
        else
        {
            step.Row.SetAt(StepColumns[cellIndex - 1].SheetColumn, value);
        }
        StepsChanged();
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

        var row = _document.Config.AddRow();
        row.Set("testcaseindex", testcase.Index.ToString(CultureInfo.InvariantCulture));
        row.Set("testcase", testcase.Name);
        row.Set("stepname", name);
        row.Set("steptype", "HTTP");
        row.Set("stage", "Test");
        row.Set("enabled", "True");
        var step = new StepRowViewModel(row, StepColumns, OnStepEdited);
        StepRows.Add(step);
        SelectedStep = step;
        OnPropertyChanged(nameof(StepCountText));
        StepsChanged();
        ShowStatus($"Added step {name}. Steps run in sheet order, so it runs after the others; rename it, fill in its URL or template, then add its data columns on the Data tab.", isError: false);
    }

    [RelayCommand]
    private void RemoveStep()
    {
        if (SelectedStep is not { } step) return;
        var name = step.CurrentName;
        var index = StepRows.IndexOf(step);
        _document.Config.RemoveRow(step.Row);
        StepRows.Remove(step);
        SelectedStep = StepRows.Count == 0 ? null : StepRows[Math.Min(index, StepRows.Count - 1)];
        OnPropertyChanged(nameof(StepCountText));
        StepsChanged();
        var leftovers = _document.Request.Headers.Concat(_document.Response.Headers)
            .Any(h => h.Contains(name + ".", StringComparison.OrdinalIgnoreCase));
        ShowStatus(leftovers
            ? $"Removed step {name}. Its data columns remain in the request and response sheets."
            : $"Removed step {name}.", isError: false);
    }

    /// <summary>Keeps the testcase's step list current and flags the data columns for a rebuild.</summary>
    private void StepsChanged()
    {
        if (SelectedTestcase is { } testcase)
        {
            var info = _document.GetTestcases().FirstOrDefault(t => t.Index == testcase.Index);
            if (info is not null) testcase.UpdateSteps(info.Steps);
        }
        _dataStale = true;
        MarkChanged();
    }
}
