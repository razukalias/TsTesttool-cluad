using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

public enum ColumnGroup { Id, Request, Response }

/// <summary>One environment's result for a row, as a small coloured chip in the table: <paramref name="Text"/> is e.g. "✗ test".</summary>
public sealed record EnvChip(string Environment, string Status, string Text, string Color);

/// <param name="Title">Text shown in the column header.</param>
/// <param name="Group">Where the value lives: the request inputs or the expected response.</param>
/// <param name="CellIndex">Position in <see cref="DataRowViewModel.Cells"/> (0 is the data ID).</param>
/// <param name="SheetColumn">Position of the column in its <see cref="SheetTable"/>.</param>
public sealed record GridColumn(string Title, ColumnGroup Group, int CellIndex, int SheetColumn);

/// <summary>One step under a testcase in the tree.</summary>
public sealed partial class StepNode(TestcaseItem owner, string name, int position) : ObservableObject
{
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = name;
    public bool IsNotEditing => !IsEditing;
    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsNotEditing));

    /// <summary>Ticked in the tree to be run on its own.</summary>
    [ObservableProperty] private bool _isChosen;
    partial void OnIsChosenChanged(bool value) => Owner.StepChosenChanged();

    /// <summary>Ticked to give this step the last run's generated values in the next run (a re-run); clears when that run starts.</summary>
    [ObservableProperty] private bool _reuseValues;

    /// <summary>Steps have no children; present so the tree's expand binding is uniform.</summary>
    [ObservableProperty] private bool _isExpanded = true;

    public TestcaseItem Owner { get; } = owner;

    /// <summary>Result of the last run for this step.</summary>
    public RunBadge Run { get; } = new();
    private string _name = name;
    public string Name { get => _name; internal set { if (SetProperty(ref _name, value)) { OnPropertyChanged(nameof(Title)); } } }
    public int Position { get; } = position;
    /// <summary>The outline number (2, 2.1, 2.2.1); the plain position until the nesting is known.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private string _number = string.Empty;
    public string Title => $"{(Number.Length > 0 ? Number : Position.ToString())} · {Name}{(IsControl ? $"  ({TypeName})" : string.Empty)}{(InElse ? "  · else" : string.Empty)}{(IsAsync ? "  ⇉ async" : string.Empty)}{(IsDisabled ? "  · disabled" : ParentDisabled ? "  · skipped (parent disabled)" : string.Empty)}";

    /// <summary>How deep the step sits inside If, While, For and ForEach steps (0 = top level).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Indent)), NotifyPropertyChangedFor(nameof(Title))] private int _depth;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private bool _isControl;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private bool _inElse;
    /// <summary>The step runs in the background (the async column of the config sheet).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private bool _isAsync;
    /// <summary>The enabled cell is false: the runner skips the step; the tree shows it faded.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title)), NotifyPropertyChangedFor(nameof(Fade))] private bool _isDisabled;
    /// <summary>A control step above this one is disabled, so this step is never reached.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title)), NotifyPropertyChangedFor(nameof(Fade))] private bool _parentDisabled;
    public double Fade => IsDisabled || ParentDisabled ? 0.45 : 1.0;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title)), NotifyPropertyChangedFor(nameof(IconData))] private string _typeName = string.Empty;

    /// <summary>The line icon of the step type, as path data.</summary>
    public string IconData => TreeIcons.ForStep(TypeName);
    public Avalonia.Thickness Indent => new(Depth * 16, 1, 0, 1);
    public string Kind => IsControl ? TypeName : string.Empty;

    internal void ApplyNesting(StepNesting nesting) { Depth = nesting.Depth; IsControl = nesting.IsControl; InElse = nesting.InElse; TypeName = nesting.TypeName; IsAsync = nesting.IsAsync; IsDisabled = nesting.IsDisabled; ParentDisabled = nesting.ParentDisabled; }
}

public sealed partial class TestcaseItem : ObservableObject
{
    public string IconData => TreeIcons.Testcase;

    public TestcaseItem(TestcaseInfo info, int rowCount)
    {
        Index = info.Index;
        Name = info.Name;
        Steps = info.Steps;
        _nesting = info.Nesting;
        _rowCount = rowCount;
        RebuildStepNodes();
    }

    /// <summary>The steps as children of the testcase in the tree, in run order.</summary>
    public ObservableCollection<StepNode> StepNodes { get; } = [];

    /// <summary>How many of this testcase's data rows are ticked in the grid; shown beside its ▶ (and its steps') as the rows that ▶ will run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TickedRowsText))]
    [NotifyPropertyChangedFor(nameof(HasTickedRows))]
    private int _tickedRows;
    public string TickedRowsText => TickedRows > 0 ? TickedRows.ToString() : string.Empty;
    public bool HasTickedRows => TickedRows > 0;

    /// <summary>The owning instance's run state, so the testcase's and its steps' run buttons can disable while it runs.</summary>
    [ObservableProperty] private RunBadge? _instanceRun;

    /// <summary>Result of the last run for this testcase (worst step result, with counts).</summary>
    public RunBadge Run { get; } = new();

    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>True while the name is being edited in the tree.</summary>
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = string.Empty;
    public bool IsNotEditing => !IsEditing;
    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsNotEditing));

    private bool _syncingChosen;

    private void RebuildStepNodes()
    {
        var kept = StepNodes.Where(n => n.IsChosen).Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = StepNodes.Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _syncingChosen = true;
        try
        {
            StepNodes.Clear();
            for (var i = 0; i < Steps.Count; i++) StepNodes.Add(new StepNode(this, Steps[i], i + 1) { IsChosen = kept.Contains(Steps[i]) || (IsChosen && !known.Contains(Steps[i])) });
        }
        finally { _syncingChosen = false; }
        ApplyNesting();
        OnPropertyChanged(nameof(ChosenState));
    }

    /// <summary>The testcase's tick box: ticked when every step is, unticked when none is, "some" in between. Setting it ticks or unticks all steps.</summary>
    public bool? ChosenState
    {
        get => StepNodes.Count == 0 ? IsChosen
            : StepNodes.All(n => n.IsChosen) ? true
            : StepNodes.Any(n => n.IsChosen) ? null : false;
        set => IsChosen = value == true;
    }

    internal void StepChosenChanged()
    {
        if (_syncingChosen) return;
        _syncingChosen = true;
        try { IsChosen = StepNodes.Any(n => n.IsChosen); }
        finally { _syncingChosen = false; }
        OnPropertyChanged(nameof(ChosenState));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised once after the ticked testcase or steps changed, however many boxes that took.</summary>
    internal event EventHandler? SelectionChanged;

    public int Index { get; }
    public string Name { get; }
    public IReadOnlyList<string> Steps { get; private set; }

    private IReadOnlyList<StepNesting>? _nesting;

    /// <summary>One line naming the control steps (If/While/For/ForEach) of this testcase, for the Record view; empty when there are none.</summary>
    public string ControlSummary
    {
        get
        {
            var controls = _nesting?.Where(n => n.IsControl).ToList();
            return controls is not { Count: > 0 } ? string.Empty
                : "Control steps: " + string.Join(", ", controls.Select(c => $"{c.Name} ({c.TypeName})")) + ". Columns of steps inside a loop are filled once and used in every round; edit the settings in the step tree.";
        }
    }

    /// <summary>Indents each step node by how deep it sits inside control steps.</summary>
    private void ApplyNesting()
    {
        if (_nesting is null || _nesting.Count != StepNodes.Count) return;
        for (var i = 0; i < StepNodes.Count; i++) StepNodes[i].ApplyNesting(_nesting[i]);
        var numbers = StepNumbering.Compute(_nesting.Select(n => n.Depth).ToList());
        for (var i = 0; i < StepNodes.Count; i++) StepNodes[i].Number = numbers[i];
    }

    public void UpdateSteps(IReadOnlyList<string> steps, IReadOnlyList<StepNesting>? nesting = null)
    {
        Steps = steps;
        _nesting = nesting;
        if (!StepNodes.Select(n => n.Name).SequenceEqual(steps))
        {
            // A rename keeps the tree's nodes (and its selection); only an add, remove or move rebuilds them.
            if (StepNodes.Count == steps.Count)
            {
                var renamedOnly = StepNodes.Where((n, i) => n.Name != steps[i]).Count() == 1;
                if (renamedOnly) { for (var i = 0; i < steps.Count; i++) { StepNodes[i].Name = steps[i]; StepNodes[i].EditText = steps[i]; } }
                else RebuildStepNodes();
            }
            else RebuildStepNodes();
        }
        ApplyNesting();
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(Summary));
    }
    public string Title => $"{Index} · {Name}";
    public string Summary => $"{RowCount} {(RowCount == 1 ? "row" : "rows")} · {Steps.Count} {(Steps.Count == 1 ? "step" : "steps")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _rowCount;

    /// <summary>Latest-run result for the testcase's rows, e.g. "3 failed · 12 passed"; empty when there are no results.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasResults))]
    private string _resultText = string.Empty;

    [ObservableProperty] private string _resultColor = "#8EA4BC";

    public bool HasResults => ResultText.Length > 0;

    /// <summary>Ticked for "Chosen testcases" in the run options.</summary>
    [ObservableProperty] private bool _isChosen;

    partial void OnIsChosenChanged(bool value)
    {
        if (!_syncingChosen)
        {
            _syncingChosen = true;
            try { foreach (var step in StepNodes) step.IsChosen = value; }
            finally { _syncingChosen = false; }
        }
        OnPropertyChanged(nameof(ChosenState));
        ChosenChanged?.Invoke(this, EventArgs.Empty);
        if (!_syncingChosen) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ticks a testcase and exactly the given steps (all when null) without raising any change.</summary>
    internal void SetSelectionSilently(bool chosen, IReadOnlySet<string>? steps)
    {
        _syncingChosen = true;
        try
        {
            foreach (var step in StepNodes) step.IsChosen = chosen && (steps is null || steps.Contains(step.Name));
            IsChosen = chosen;
        }
        finally { _syncingChosen = false; }
        OnPropertyChanged(nameof(ChosenState));
    }

    /// <summary>The steps ticked for a run; empty when the testcase is chosen as a whole (or none is ticked).</summary>
    public IReadOnlyList<string> ChosenSteps => StepNodes.Where(n => n.IsChosen).Select(n => n.Name).ToList();
    internal event EventHandler? ChosenChanged;

    public void SetResults(int passed, int failed, int errors = 0)
    {
        if (passed + failed == 0) { ResultText = string.Empty; return; }
        ResultText = failed > 0 ? $"{failed} failed{(errors > 0 ? $" ({errors} with errors)" : string.Empty)} · {passed} passed" : $"{passed} passed";
        ResultColor = failed > 0 ? "#FF7588" : "#39D39B";
    }
}

public sealed class ProblemViewModel
{
    public ProblemViewModel(WorkbookProblem problem)
    {
        Problem = problem;
        IsError = problem.Severity == ProblemSeverity.Error;
    }

    public WorkbookProblem Problem { get; }
    public bool IsError { get; }
    public string SeverityText => IsError ? "Error" : "Warning";
    public string BadgeBackground => IsError ? "#432333" : "#443A25";
    public string BadgeForeground => IsError ? "#FF7588" : "#F6C76B";
    public string Message => Problem.Message;
    public string Location => Problem.RowNumber is int row ? $"{Problem.Sheet} · row {row}" : Problem.Row is null ? Problem.Sheet : $"{Problem.Sheet} · new row";
}

/// <summary>
/// One line of the grid: a request row and its response row shown together. Cell 0 is the data ID, followed
/// by the request columns and then the expected-response columns. Editing a cell writes through to the workbook.
/// </summary>
public sealed partial class DataRowViewModel : ObservableObject, IFormattedRow, ICellRow
{
    private readonly SheetTable? _requestTable;
    private readonly SheetTable? _responseTable;
    private readonly IReadOnlyList<GridColumn> _columns;
    private readonly Action<DataRowViewModel, int, string> _edited;
    private bool _suppress;

    /// <summary>Ticked in the grid to be run next; a view-only choice, never written to the workbook.</summary>
    [ObservableProperty] private bool _isTicked;

    public DataRowViewModel(int testcaseIndex, TableRow request, TableRow? response, IReadOnlyList<GridColumn> columns, Action<DataRowViewModel, int, string> edited, SheetTable? requestTable = null, SheetTable? responseTable = null)
    {
        _columns = columns;
        _requestTable = requestTable;
        _responseTable = responseTable;
        TestcaseIndex = testcaseIndex;
        Request = request;
        Response = response;
        _edited = edited;
        var values = new List<string> { request.Get("dataid") };
        foreach (var column in columns)
            values.Add(column.Group == ColumnGroup.Request ? request.GetAt(column.SheetColumn) : response?.GetAt(column.SheetColumn) ?? string.Empty);
        CurrentDataId = values[0];
        Cells = new ObservableCollection<string>(values);
        Cells.CollectionChanged += OnCellsChanged;
        Formats = new ObservableCollection<CellStyleView>(Enumerable.Range(0, values.Count).Select(_ => new CellStyleView()));
        RefreshFormats();
    }

    public ObservableCollection<CellStyleView> Formats { get; }

    public (SheetTable Table, TableRow Row, int Column)? Locate(int cell)
    {
        if (cell == 0) return _requestTable is null || _requestTable.FindColumn("dataid") is not (>= 0 and var id) ? null : (_requestTable, Request, id);
        var column = _columns[cell - 1];
        if (column.Group == ColumnGroup.Request) return _requestTable is null ? null : (_requestTable, Request, column.SheetColumn);
        return _responseTable is null || Response is null ? null : (_responseTable, Response, column.SheetColumn);
    }

    /// <summary>Reads every cell's look from the workbook tables again.</summary>
    public void RefreshFormats()
    {
        for (var cell = 0; cell < Formats.Count; cell++)
            Formats[cell].Set(Locate(cell) is { } at ? at.Table.GetFormat(at.Row, at.Column) : CellFormat.None);
    }

    /// <summary>How this row fared in the latest run, or null when there are no results for it.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ResultText), nameof(ResultColor), nameof(CountsText), nameof(CountsDetail))]
    private RowOutcome? _outcome;

    /// <summary>Whether a failure of this row is a known bug (its expected cell carries a bug tag for the failure's environment); set by the editor.</summary>
    internal Func<DataRowViewModel, FailureDetail, bool>? KnownBugCheck { get; set; }

    /// <summary>The path of a failed assertion as the column header spells it (the event can carry it in lower case); set by the editor.</summary>
    internal Func<FailureDetail, string>? PathText { get; set; }

    /// <summary>PASS, FAIL, ERROR (a step broke: timeout, connection, script …), or BUG when everything that failed is a known bug (so the row is not a failure).</summary>
    public string ResultText => Outcome is null ? "—" : Standing;
    public string ResultColor => Outcome is null ? "#5E7590" : ColorOf(Standing);

    internal static string ColorOf(string status) => status switch { "PASS" => "#39D39B", "BUG" => "#F6A623", "ERROR" => "#C792EA", "FAIL" => "#FF7588", _ => "#5E7590" };
    internal static string SymbolOf(string status) => status switch { "PASS" => "✓", "BUG" => "⚑", "ERROR" => "⚠", "FAIL" => "✗", _ => "–" };

    private string Standing => Outcome is { } outcome ? StandingOf(outcome) : "—";

    /// <summary>The status of a row (or of the row in one environment).</summary>
    internal string StandingOf(RowOutcome outcome)
    {
        if (outcome.Passed) return "PASS";
        if (outcome.Failures.Any(f => f.IsError)) return "ERROR";
        // Failures beyond the ones kept for the row cannot be checked: then it stays a failure
        if (KnownBugCheck is null || outcome.Failures.Count == 0 || outcome.Problems > outcome.Failures.Count) return "FAIL";
        return outcome.Failures.Where(f => !f.IsSecondary).All(f => KnownBugCheck(this, f)) ? "BUG" : "FAIL";
    }

    /// <summary>The row as every environment of the run saw it, whatever environment the table is showing; null without a run.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EnvChips), nameof(EnvDetail), nameof(EnvDiffers), nameof(HasEnvChips))]
    private RowOutcome? _allOutcome;

    /// <summary>The environments of the shown run, in order (set by the editor).</summary>
    internal IReadOnlyList<string> RunEnvironments { get; set; } = [];

    /// <summary>Status of this row in one environment: PASS, FAIL, ERROR, BUG, or — when the row did not run there.</summary>
    public string StatusIn(string environment) => AllOutcome?.In(environment) is { } outcome ? StandingOf(outcome) : "—";

    public bool HasEnvChips => AllOutcome is not null && RunEnvironments.Count > 1;

    /// <summary>One chip per environment; with more than <see cref="MaxEnvChips"/> a single chip counts the passing ones.</summary>
    public IReadOnlyList<EnvChip> EnvChips
    {
        get
        {
            if (!HasEnvChips) return [];
            var all = RunEnvironments.Select(e => (Name: e, Status: StatusIn(e))).ToList();
            if (all.Count <= MaxEnvChips) return all.Select(e => new EnvChip(e.Name, e.Status, $"{SymbolOf(e.Status)} {e.Name}{(EarlierEnvironments.Contains(e.Name) ? " ↶" : string.Empty)}", ColorOf(e.Status))).ToList();
            var ran = all.Where(e => e.Status != "—").ToList();
            var passed = ran.Count(e => e.Status == "PASS");
            var worst = ran.Any(e => e.Status == "ERROR") ? "ERROR" : ran.Any(e => e.Status == "FAIL") ? "FAIL" : ran.Any(e => e.Status == "BUG") ? "BUG" : ran.Count > 0 ? "PASS" : "—";
            return [new EnvChip(string.Empty, worst, $"{passed}/{all.Count} {SymbolOf("PASS")}", ColorOf(worst))];
        }
    }

    public const int MaxEnvChips = 4;

    /// <summary>One line per environment for the tooltip of the chips.</summary>
    public string EnvDetail => !HasEnvChips ? string.Empty
        : string.Join("\n", RunEnvironments.Select(e => $"{e}: {StatusIn(e) switch { "—" => "did not run", var s => s }}{(AllOutcome!.In(e) is { Passed: false } o ? $" ({o.Problems} {(o.Problems == 1 ? "problem" : "problems")})" : string.Empty)}{(AllOutcome.In(e) is { FromRunId: { } from } ? $" — from the run of {RowOutcome.RunTime(from)}" : string.Empty)}"));

    /// <summary>True when the environments that ran this row do not agree on its status.</summary>
    public bool EnvDiffers => HasEnvChips && RunEnvironments.Select(StatusIn).Where(s => s != "—").Distinct().Count() > 1;

    /// <summary>How many things failed in the latest run (failed assertions and failed steps) that are not known bugs.</summary>
    public int FailedCount => Outcome is { Passed: false } outcome ? outcome.Failures.Count(f => !f.IsSecondary && !(KnownBugCheck?.Invoke(this, f) ?? false)) : 0;

    /// <summary>How many failed assertions of the latest run are tagged known bugs.</summary>
    public int BugCount => Outcome is { Passed: false } outcome && KnownBugCheck is { } check ? outcome.Failures.Count(f => !f.IsSecondary && check(this, f)) : 0;

    /// <summary>How many cells of the row carry a &lt;bug: …&gt; tag (shown as ⚑ and a number beside the status, run or not).</summary>
    public int BugTagCount => Cells.Count(c => c is not null && c.Contains("<bug", StringComparison.OrdinalIgnoreCase));

    public string BugTagText => BugTagCount > 0 ? $"⚑ {BugTagCount}" : string.Empty;

    /// <summary>The older run this row's result comes from when the latest run did not run it; null otherwise.</summary>
    public string? EarlierRun => (AllOutcome ?? Outcome)?.FromRunId is { } id ? RowOutcome.RunTime(id) : null;

    /// <summary>The environments whose result comes from an older run while the rest of the row is from the latest.</summary>
    internal IReadOnlyList<string> EarlierEnvironments => AllOutcome is { FromRunId: null } all ? all.EnvironmentPairs.Where(p => p.Value.FromRunId is not null).Select(p => p.Key).ToList() : [];

    /// <summary>"2 failed · 1 bug" beside the PASS / FAIL / BUG status; empty when nothing failed.</summary>
    public string CountsText
    {
        get
        {
            var failed = FailedCount; var bugs = BugCount;
            var parts = new List<string>();
            if (failed > 0) parts.Add($"{failed} failed");
            if (bugs > 0) parts.Add($"{bugs} {(bugs == 1 ? "bug" : "bugs")}");
            if (EarlierRun is not null) parts.Add("earlier run");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>One line per failure for the tooltip of the counts: whether it counts as failed or as a known bug, and which field.</summary>
    public string CountsDetail => (EarlierRun is { } earlier ? $"Result of the run of {earlier}: the latest run did not run this row.\n" : string.Empty) + (Outcome is not { Passed: false } outcome ? string.Empty
        : string.Join("\n", outcome.Failures.Select(f => (f.IsSecondary ? "ALSO   " : KnownBugCheck?.Invoke(this, f) ?? false ? "BUG    " : "FAILED ") + (f.IsAssertion ? PathText?.Invoke(f) ?? $"{f.StepName}.{f.ResponsePath}" : $"{f.StepName} (step)") + (f.Environment.Length > 0 ? $" [{f.Environment}]" : string.Empty))));

    /// <summary>True when the row's latest run failed for something other than a known bug.</summary>
    public bool FailedForReal => Outcome is not null && Standing is "FAIL" or "ERROR";

    /// <summary>True when everything that failed in the latest run is a known bug.</summary>
    public bool OnlyKnownBugs => Outcome is not null && Standing == "BUG";

    internal void RefreshStanding() { if (Outcome is not null) OnPropertyChanged(nameof(ResultText)); OnPropertyChanged(nameof(EnvChips)); OnPropertyChanged(nameof(EnvDiffers)); OnPropertyChanged(nameof(ResultColor)); OnPropertyChanged(nameof(CountsText)); OnPropertyChanged(nameof(CountsDetail)); OnPropertyChanged(nameof(BugTagText)); }

    public int TestcaseIndex { get; }
    public TableRow Request { get; }
    public TableRow? Response { get; internal set; }
    public ObservableCollection<string> Cells { get; }

    /// <summary>The data ID as last accepted by the workbook, so a rejected rename can be undone.</summary>
    public string CurrentDataId { get; internal set; }

    /// <summary>Puts a value in a cell without treating it as a user edit.</summary>
    internal void SetCellSilently(int index, string value)
    {
        _suppress = true;
        try { Cells[index] = value; }
        finally { _suppress = false; }
    }

    private void OnCellsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppress || e.Action != NotifyCollectionChangedAction.Replace) return;
        OnPropertyChanged(nameof(BugTagText));   // a typed or pasted bug tag counts at once
        var index = e.NewStartingIndex;
        _edited(this, index, Cells[index] ?? string.Empty);
    }
}
