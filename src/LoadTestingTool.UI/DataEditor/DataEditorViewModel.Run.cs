using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>What to run. Empty <see cref="Testcases"/> means every testcase; empty <see cref="DataIds"/> means every row.</summary>
/// <param name="RowsMode">"parallel", "sequential", or null to use the instance's own setting.</param>
/// <param name="TestcasesParallel">Run the chosen testcases at the same time (true) or one after another (false); null keeps the instance setting.</param>
/// <param name="RowsByTestcase">Data IDs to run per testcase index (for ticked rows across testcases); when given it takes the place of <see cref="DataIds"/>.</param>
/// <param name="Steps">Step names to run per testcase index; null runs every enabled step. When given it must list every testcase in <see cref="Testcases"/>.</param>
public sealed record RunRequest(IReadOnlyList<int> Testcases, IReadOnlyList<string> DataIds, string? RowsMode = null, bool? TestcasesParallel = null, IReadOnlyDictionary<int, IReadOnlyList<string>>? Steps = null, bool Rerun = false, IReadOnlyDictionary<int, IReadOnlyList<string>>? RerunSteps = null, string? RerunOf = null, IReadOnlyList<string>? Environments = null, IReadOnlyDictionary<int, IReadOnlyList<string>>? RowsByTestcase = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? RowsByEnvironment = null);

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

    public bool CanRun => RunHandler is not null && SelectedTestcase is not null && !IsRunning;

    /// <summary>True while a run of this instance is in progress; the run buttons stay disabled until it finishes.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanRun))] private bool _isRunning;

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
        item.ChosenChanged += (_, _) => { OnPropertyChanged(nameof(RunSummary)); TickedChanged?.Invoke(this, EventArgs.Empty); };
        item.SelectionChanged += (_, _) => OnSelectionChanged();
        return item;
    }

    // ---- Selection saved to the central settings ---------------------------------------------

    /// <summary>Saves the ticked testcases and steps (testcases, steps JSON); returns why it could not. Set by the shell.</summary>
    internal Func<string, string, string?>? SelectionSaver { get; set; }

    private int _selectionBatch;
    private bool _selectionPending, _applyingSelection;

    /// <summary>Groups several tick changes into one save.</summary>
    internal IDisposable BatchSelection()
    {
        _selectionBatch++;
        return new SelectionBatch(this);
    }

    private sealed class SelectionBatch(DataEditorViewModel owner) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner._selectionBatch--;
            if (owner._selectionBatch == 0 && owner._selectionPending) owner.SaveSelection();
        }
    }

    private void OnSelectionChanged()
    {
        TickedChanged?.Invoke(this, EventArgs.Empty);
        if (_applyingSelection || SelectionSaver is null) return;
        _selectionPending = true;
        if (_selectionBatch == 0) SaveSelection();
    }

    private void SaveSelection()
    {
        _selectionPending = false;
        if (SelectionSaver is null) return;
        var (testcases, steps) = BuildSelectionSetting();
        if (testcases is null) { ShowStatus("Nothing is ticked, so the saved selection stays as it was. Tick at least one testcase or step.", isError: false); return; }
        var problem = SelectionSaver(testcases, steps);
        if (problem is not null) ShowStatus(problem, isError: true);
    }

    /// <summary>
    /// What the ticks mean in the central settings: the testcase expression ("0" = all) and the step JSON
    /// (empty = every enabled step). Testcases without an enabled ticked step are left out. Null testcases = nothing to run.
    /// </summary>
    private (string? Testcases, string Steps) BuildSelectionSetting()
    {
        var chosen = new List<(TestcaseItem Testcase, List<string> Steps, bool All)>();
        foreach (var testcase in Testcases)
        {
            var enabled = EnabledSteps(testcase.Index);
            var ticked = testcase.StepNodes.Count == 0
                ? (testcase.IsChosen ? enabled.ToList() : [])
                : testcase.StepNodes.Where(n => n.IsChosen && enabled.Contains(n.Name, StringComparer.OrdinalIgnoreCase)).Select(n => n.Name).ToList();
            if (ticked.Count > 0) chosen.Add((testcase, ticked, ticked.Count == enabled.Count));
        }
        if (chosen.Count == 0) return (null, string.Empty);
        var everyTestcase = chosen.Count == Testcases.Count(t => EnabledSteps(t.Index).Count > 0);
        var testcases = everyTestcase ? "0" : string.Join(',', chosen.Select(c => c.Testcase.Index.ToString(CultureInfo.InvariantCulture)));
        var steps = chosen.All(c => c.All)
            ? string.Empty
            : System.Text.Json.JsonSerializer.Serialize(chosen.ToDictionary(c => c.Testcase.Index, c => c.Steps.ToArray()));
        return (testcases, steps);
    }

    /// <summary>Ticks what the central settings select, without saving it back.</summary>
    internal void ApplySavedSelection(string testcases, string stepsJson)
    {
        _applyingSelection = true;
        try
        {
            IReadOnlyList<int> indexes;
            try { indexes = new LoadTestingTool.Validation.TestcaseSelectionParser().Parse(testcases, Testcases.Select(t => t.Index)); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException) { indexes = Testcases.Select(t => t.Index).ToList(); }
            Dictionary<int, string[]>? steps = null;
            if (!string.IsNullOrWhiteSpace(stepsJson))
            {
                try { steps = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string[]>>(stepsJson); }
                catch (System.Text.Json.JsonException) { }
            }
            foreach (var testcase in Testcases)
            {
                var picked = indexes.Contains(testcase.Index);
                IReadOnlySet<string>? names = steps is not null && steps.TryGetValue(testcase.Index, out var listed)
                    ? new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase)
                    : null;
                testcase.SetSelectionSilently(picked, names);
            }
        }
        finally { _applyingSelection = false; }
        TickedChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(RunSummary));
    }

    /// <summary>Raised when a tick box in the tree changed.</summary>
    public event EventHandler? TickedChanged;

    /// <summary>Testcases (or single steps) ticked in the tree.</summary>
    public int TickedTestcaseCount => Testcases.Count(t => t.IsChosen);

    private IReadOnlyList<string> EnabledSteps(int testcaseIndex) => _document.Config.Rows
        .Where(r => !r.IsBlank && LoadTestingTool.Editing.WorkbookDocument.EffectiveIndex(r) == testcaseIndex && !IsOff(r.Get("enabled")))
        .Select(r => r.Get("stepname").Trim()).Where(n => n.Length > 0).ToList();

    private static bool IsOff(string value) => value.Trim().ToLowerInvariant() is "false" or "0" or "no" or "off";

    /// <summary>A request for the given testcases; steps given per testcase (null = all of its enabled steps). Reading the tree's ticks or one clicked item both go through here.</summary>
    internal RunRequest BuildRequest(IReadOnlyList<(TestcaseItem Testcase, IReadOnlyList<string>? Steps)> chosen, bool? parallel = null)
    {
        var indexes = chosen.Select(c => c.Testcase.Index).ToList();
        IReadOnlyDictionary<int, IReadOnlyList<string>>? steps = null;
        if (chosen.Any(c => c.Steps is not null))
            steps = chosen.ToDictionary(c => c.Testcase.Index, c => c.Steps ?? EnabledSteps(c.Testcase.Index));
        return new RunRequest(indexes, [], RowsMode == RowsModeOptions[0] ? null : RowsMode.ToLowerInvariant(),
            parallel ?? (indexes.Count > 1 ? TestcaseOrder == TestcaseOrderOptions[1] : null), steps);
    }

    /// <summary>Runs from the tree: <paramref name="request"/> for the given label, with the same save-first check as the Run button.</summary>
    internal Task RunFromTreeAsync(RunRequest request, string label) => StartRunAsync(request, label);

    /// <summary>What "Run ticked" runs. Data rows ticked in the grid take precedence: only those rows run (the tree ticks every testcase by default, so
    /// they would otherwise drown the choice). With no rows ticked it is whole testcases, or only the ticked steps of a partly ticked testcase.</summary>
    public (RunRequest? Request, string Label) BuildTickedRequest(bool? parallel = null)
    {
        var chosen = Testcases.Where(t => t.IsChosen)
            .Select(t => (t, (IReadOnlyList<string>?)(t.StepNodes.Count > 0 && t.StepNodes.Any(n => !n.IsChosen) ? t.ChosenSteps.Where(n => IsStepEnabled(t.Index, n)).ToList() : null)))
            .Where(c => c.Item2 is null || c.Item2.Count > 0)
            .ToList();
        var rows = TickedRowsByTestcase().Where(p => Testcases.Any(t => t.Index == p.Key)).ToDictionary(p => p.Key, p => p.Value);
        if (rows.Count > 0)
        {
            var forRows = rows.Keys.Select(index => chosen.FirstOrDefault(c => c.t.Index == index) is { t: not null } own ? own : (Testcases.First(t => t.Index == index), null)).ToList();
            var rowRequest = BuildRequest(forRows.OrderBy(c => c.Item1.Index).ToList(), parallel);
            var count = rows.Values.Sum(v => v.Count);
            return (rowRequest with { DataIds = rows.Values.SelectMany(v => v).Distinct().ToList(), RowsByTestcase = rows },
                $"{count} ticked {(count == 1 ? "row" : "rows")}");
        }
        if (chosen.Count == 0) return (null, string.Empty);
        var stepCount = chosen.Sum(c => c.Item2?.Count ?? c.t.StepNodes.Count);
        return (BuildRequest(chosen, parallel), $"{stepCount} ticked {(stepCount == 1 ? "step" : "steps")} in {chosen.Count} {(chosen.Count == 1 ? "testcase" : "testcases")}");
    }

    internal void Report(string message, bool isError) => ShowStatus(message, isError);

    /// <summary>The names of a testcase's steps that run (the enabled ones).</summary>
    internal bool IsStepEnabled(int testcaseIndex, string stepName) => EnabledSteps(testcaseIndex).Contains(stepName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Tick to make the next run reuse the last run's generated values (random numbers, GUIDs, dates, script results, response values) — a re-run of a failed step.</summary>
    [ObservableProperty] private bool _reuseLastValues;

    /// <summary>Clears the toolbar box and every step's ↻ box (they are for the next run only).</summary>
    internal void ClearReuseChoices()
    {
        ReuseLastValues = false;
        foreach (var node in Testcases.SelectMany(t => t.StepNodes)) node.ReuseValues = false;
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

    /// <summary>What the "Re-run from history" dialog needs: where the instance's history is and which environments it has; set by whoever hosts the editor.</summary>
    public Func<RerunContext?>? RerunContextProvider { get; set; }

    /// <summary>Shows a problem in the editor's status line.</summary>
    public void ShowProblem(string message) => ShowStatus(message, isError: true);

    public bool CanRerunFromHistory => RerunContextProvider is not null;

    /// <summary>Starts a run built by the "Re-run from history" dialog; its own old/fresh choices are used as they are.</summary>
    public Task StartRerunAsync(RunRequest request, string label) => StartRunAsync(request, label, applyReuseChoices: false);

    [RelayCommand]
    private Task RunTestcaseAsync() => StartRunAsync(new RunRequest([SelectedTestcase?.Index ?? 0], []), $"testcase {SelectedTestcase?.Name}");

    [RelayCommand]
    private Task RunRowAsync()
    {
        if (SelectedTestcase is not { } testcase || SelectedRow is not { } row) { ShowStatus("Select a data row to run.", isError: true); return Task.CompletedTask; }
        return StartRunAsync(new RunRequest([testcase.Index], [row.Cells[0]]), $"{row.Cells[0]} in {testcase.Name}");
    }

    /// <summary>Raised when a run has been started, so the window can show the Overview.</summary>
    public event EventHandler? RunStarted;

    /// <summary>Raised whenever pressing Run did not start a run, with the reason. The window shows it prominently: the status line alone was easy to miss.</summary>
    public event EventHandler<string>? RunRefused;

    private void Refuse(string reason) { ShowStatus(reason, isError: true); RunRefused?.Invoke(this, reason); }

    private async Task StartRunAsync(RunRequest request, string label, bool applyReuseChoices = true)
    {
        if (RunHandler is not { } handler) { Refuse("Running is only available when the editor is opened from the dashboard."); return; }
        if (IsRunning) { Refuse("This instance is already running. Wait for it to finish or stop it first."); return; }
        if (IsDirty) { Refuse("Save your changes first — a run reads the saved workbook."); return; }
        IsStartingRun = true;
        try
        {
            // The box is for the next run only: it clears as soon as that run is started (or refused).
            if (applyReuseChoices)
            {
                var reuseSteps = Testcases.Where(t => t.StepNodes.Any(n => n.ReuseValues))
                    .ToDictionary(t => t.Index, t => (IReadOnlyList<string>)t.StepNodes.Where(n => n.ReuseValues).Select(n => n.Name).ToList());
                request = request with { Rerun = ReuseLastValues, RerunSteps = reuseSteps.Count > 0 ? reuseSteps : null };
                ClearReuseChoices();
            }
            var problem = await handler(request);
            RunOptionsVisible = problem is not null && RunOptionsVisible;
            if (problem is not null) Refuse(problem);
            else ShowStatus($"Started {label}. Results appear in the Result column as it runs.", isError: false);
            if (problem is null) RunStarted?.Invoke(this, EventArgs.Empty);
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
        RememberFileTime();
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

/// <summary>Where an instance's history is and which environments it can run in.</summary>
public sealed record RerunContext(string HistoryFolder, IReadOnlyList<string> Environments, LoadTestingTool.Reporting.HistoryStorage Storage = LoadTestingTool.Reporting.HistoryStorage.Files);
