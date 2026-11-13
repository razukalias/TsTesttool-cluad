using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>What to run. Empty <see cref="Testcases"/> means every testcase; empty <see cref="DataIds"/> means every row.</summary>
/// <param name="RowsMode">"parallel", "sequential", or null to use the instance's own setting.</param>
/// <param name="TestcasesParallel">Run the chosen testcases at the same time (true) or one after another (false); null keeps the instance setting.</param>
public sealed record RunRequest(IReadOnlyList<int> Testcases, IReadOnlyList<string> DataIds, string? RowsMode = null, bool? TestcasesParallel = null);

/// <summary>Asks the host to run part of an instance. Returns null when the run was started, otherwise why it was not.</summary>
public delegate Task<string?> RunHandler(RunRequest request);

// Run from the editor: selected rows, one testcase, several testcases or the whole instance.
public sealed partial class DataEditorViewModel
{
    public const string ScopeTestcase = "This testcase";
    public const string ScopeChosen = "Chosen testcases";
    public const string ScopeInstance = "Whole instance";
    public static IReadOnlyList<string> ScopeOptions { get; } = [ScopeTestcase, ScopeChosen, ScopeInstance];
    public static IReadOnlyList<string> RowsModeOptions { get; } = ["Instance setting", "Parallel", "Sequential"];
    public static IReadOnlyList<string> TestcaseOrderOptions { get; } = ["One after another", "At the same time"];

    /// <summary>Set by whoever hosts the editor; without it the run buttons are unavailable.</summary>
    public RunHandler? RunHandler { get; set; }

    public bool CanRun => RunHandler is not null && SelectedTestcase is not null;

    [ObservableProperty] private bool _isStartingRun;
    [ObservableProperty] private bool _runOptionsVisible;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ChoosingTestcases), nameof(CanChooseOrder), nameof(RunSummary))]
    private string _runScope = ScopeTestcase;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RunSummary))] private string _rowsMode = RowsModeOptions[0];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RunSummary))] private string _testcaseOrder = TestcaseOrderOptions[0];

    public bool ChoosingTestcases => RunScope == ScopeChosen;
    public bool CanChooseOrder => RunScope != ScopeTestcase;

    /// <summary>One line saying what the Run button will do with the current choices.</summary>
    public string RunSummary
    {
        get
        {
            var which = RunScope switch
            {
                ScopeInstance => $"all {Testcases.Count} testcases",
                ScopeChosen => $"{Testcases.Count(t => t.IsChosen)} chosen testcases",
                _ => $"testcase {SelectedTestcase?.Name}",
            };
            var order = CanChooseOrder ? (TestcaseOrder == TestcaseOrderOptions[1] ? ", at the same time" : ", one after another") : string.Empty;
            return $"Runs {which}{order}; rows {(RowsMode == RowsModeOptions[0] ? "as the instance is set up" : RowsMode.ToLowerInvariant())}.";
        }
    }

    private TestcaseItem Track(TestcaseItem item)
    {
        item.ChosenChanged += (_, _) => OnPropertyChanged(nameof(RunSummary));
        return item;
    }

    [RelayCommand]
    private void ToggleRunOptions() => RunOptionsVisible = !RunOptionsVisible;

    /// <summary>Runs what the run options describe.</summary>
    [RelayCommand]
    private Task RunAsync()
    {
        if (SelectedTestcase is not { } current) return Task.CompletedTask;
        IReadOnlyList<int> testcases = RunScope switch
        {
            ScopeInstance => [],
            ScopeChosen => Testcases.Where(t => t.IsChosen).Select(t => t.Index).ToList(),
            _ => [current.Index],
        };
        if (RunScope == ScopeChosen && testcases.Count == 0) { ShowStatus("Tick at least one testcase to run.", isError: true); return Task.CompletedTask; }
        var request = new RunRequest(testcases, [], RowsMode == RowsModeOptions[0] ? null : RowsMode.ToLowerInvariant(),
            CanChooseOrder ? TestcaseOrder == TestcaseOrderOptions[1] : null);
        var label = RunScope == ScopeInstance ? "the whole instance" : testcases.Count == 1 ? $"testcase {Testcases.First(t => t.Index == testcases[0]).Name}" : $"{testcases.Count} testcases";
        return StartRunAsync(request, label);
    }

    [RelayCommand]
    private Task RunTestcaseAsync() => StartRunAsync(new RunRequest([SelectedTestcase?.Index ?? 0], []), $"testcase {SelectedTestcase?.Name}");

    [RelayCommand]
    private Task RunRowAsync()
    {
        if (SelectedTestcase is not { } testcase || SelectedRow is not { } row) { ShowStatus("Select a data row to run.", isError: true); return Task.CompletedTask; }
        return StartRunAsync(new RunRequest([testcase.Index], [row.Cells[0]]), $"{row.Cells[0]} in {testcase.Name}");
    }

    private async Task StartRunAsync(RunRequest request, string label)
    {
        if (RunHandler is not { } handler) { ShowStatus("Running is only available when the editor is opened from the dashboard.", isError: true); return; }
        if (IsDirty) { ShowStatus("Save your changes first — a run reads the saved workbook.", isError: true); return; }
        IsStartingRun = true;
        try
        {
            var problem = await handler(request);
            RunOptionsVisible = problem is not null && RunOptionsVisible;
            ShowStatus(problem ?? $"Started {label}. Results appear in the Result column as it runs.", isError: problem is not null);
        }
        finally { IsStartingRun = false; }
    }

    // ---- Compare runs ----------------------------------------------------------------------

    /// <summary>Opens the History Explorer for this instance, filtered to the given testcase name; set by whoever hosts the editor.</summary>
    public Action<string?>? HistoryHandler { get; set; }

    public bool CanOpenHistory => HistoryHandler is not null;

    [RelayCommand]
    private void OpenHistory()
    {
        if (HistoryHandler is null) { ShowStatus("Comparing runs is only available when the editor is opened from the dashboard.", isError: true); return; }
        HistoryHandler(SelectedTestcase?.Name);
        ShowStatus("Opened the History Explorer. Tick two or more runs and use Compare there.", isError: false);
    }

    // ---- New testcase ----------------------------------------------------------------------

    /// <summary>Writes a testcase from the wizard into the workbook file; set by whoever hosts the editor.</summary>
    public Action<TestcaseWizardDraft>? CreateTestcaseHandler { get; set; }

    public bool CanCreateTestcase => CreateTestcaseHandler is not null;

    /// <summary>Raised with the next free testcase index when the view should show the testcase wizard.</summary>
    public event EventHandler<int>? NewTestcaseRequested;

    [RelayCommand]
    private void NewTestcase()
    {
        if (CreateTestcaseHandler is null) { ShowStatus("Creating a testcase is only available when the editor is opened from the dashboard.", isError: true); return; }
        if (IsDirty) { ShowStatus("Save your changes first — the wizard adds the testcase to the saved workbook.", isError: true); return; }
        NewTestcaseRequested?.Invoke(this, Testcases.Select(t => t.Index).DefaultIfEmpty(0).Max() + 1);
    }

    public void CreateTestcase(TestcaseWizardDraft draft) => CreateTestcaseHandler?.Invoke(draft);

    /// <summary>After the wizard wrote the file: re-reads it and selects the new testcase.</summary>
    public void FinishNewTestcase(int index)
    {
        _document = LoadTestingTool.Editing.WorkbookDocument.Load(WorkbookPath);
        _referenceScan = null;
        ClearUndoHistory();
        ConflictVisible = false;
        IsDirty = false;
        ClearRowFilters();
        LoadTestcases(index);
        var created = Testcases.Any(t => t.Index == index);
        ShowStatus(created ? $"Created testcase {index}. Add its data rows below." : $"The testcase was not created — see the dashboard for the reason.", isError: !created);
    }
}
