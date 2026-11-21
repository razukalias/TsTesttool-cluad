using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

public enum ColumnGroup { Id, Request, Response }

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
    public string Title => $"{(Number.Length > 0 ? Number : Position.ToString())} · {Name}{(IsControl ? $"  ({TypeName})" : string.Empty)}{(InElse ? "  · else" : string.Empty)}";

    /// <summary>How deep the step sits inside If, While, For and ForEach steps (0 = top level).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Indent)), NotifyPropertyChangedFor(nameof(Title))] private int _depth;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private bool _isControl;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title))] private bool _inElse;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title)), NotifyPropertyChangedFor(nameof(IconData))] private string _typeName = string.Empty;

    /// <summary>The line icon of the step type, as path data.</summary>
    public string IconData => TreeIcons.ForStep(TypeName);
    public Avalonia.Thickness Indent => new(Depth * 16, 1, 0, 1);
    public string Kind => IsControl ? TypeName : string.Empty;

    internal void ApplyNesting(StepNesting nesting) { Depth = nesting.Depth; IsControl = nesting.IsControl; InElse = nesting.InElse; TypeName = nesting.TypeName; }
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

    public void SetResults(int passed, int failed)
    {
        if (passed + failed == 0) { ResultText = string.Empty; return; }
        ResultText = failed > 0 ? $"{failed} failed · {passed} passed" : $"{passed} passed";
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

    /// <summary>PASS, FAIL, or BUG when everything that failed is a known bug (so the row is not a failure).</summary>
    public string ResultText => Outcome is null ? "—" : Standing;
    public string ResultColor => Outcome is null ? "#5E7590" : Standing switch { "PASS" => "#39D39B", "BUG" => "#F6A623", _ => "#FF7588" };

    private string Standing
    {
        get
        {
            if (Outcome is not { } outcome) return "—";
            if (outcome.Passed) return "PASS";
            // Failures beyond the ones kept for the row cannot be checked: then it stays a failure
            if (KnownBugCheck is null || outcome.Failures.Count == 0 || outcome.Problems > outcome.Failures.Count) return "FAIL";
            return outcome.Failures.Where(f => !f.IsSecondary).All(f => KnownBugCheck(this, f)) ? "BUG" : "FAIL";
        }
    }

    /// <summary>How many things failed in the latest run (failed assertions and failed steps) that are not known bugs.</summary>
    public int FailedCount => Outcome is { Passed: false } outcome ? outcome.Failures.Count(f => !f.IsSecondary && !(KnownBugCheck?.Invoke(this, f) ?? false)) : 0;

    /// <summary>How many failed assertions of the latest run are tagged known bugs.</summary>
    public int BugCount => Outcome is { Passed: false } outcome && KnownBugCheck is { } check ? outcome.Failures.Count(f => !f.IsSecondary && check(this, f)) : 0;

    /// <summary>How many cells of the row carry a &lt;bug: …&gt; tag (shown as ⚑ and a number beside the status, run or not).</summary>
    public int BugTagCount => Cells.Count(c => c is not null && c.Contains("<bug", StringComparison.OrdinalIgnoreCase));

    public string BugTagText => BugTagCount > 0 ? $"⚑ {BugTagCount}" : string.Empty;

    /// <summary>"2 failed · 1 bug" beside the PASS / FAIL / BUG status; empty when nothing failed.</summary>
    public string CountsText
    {
        get
        {
            var failed = FailedCount; var bugs = BugCount;
            var parts = new List<string>();
            if (failed > 0) parts.Add($"{failed} failed");
            if (bugs > 0) parts.Add($"{bugs} {(bugs == 1 ? "bug" : "bugs")}");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>One line per failure for the tooltip of the counts: whether it counts as failed or as a known bug, and which field.</summary>
    public string CountsDetail => Outcome is not { Passed: false } outcome ? string.Empty
        : string.Join("\n", outcome.Failures.Select(f => (f.IsSecondary ? "ALSO   " : KnownBugCheck?.Invoke(this, f) ?? false ? "BUG    " : "FAILED ") + (f.IsAssertion ? PathText?.Invoke(f) ?? $"{f.StepName}.{f.ResponsePath}" : $"{f.StepName} (step)") + (f.Environment.Length > 0 ? $" [{f.Environment}]" : string.Empty)));

    /// <summary>True when the row's latest run failed for something other than a known bug.</summary>
    public bool FailedForReal => Outcome is not null && Standing == "FAIL";

    /// <summary>True when everything that failed in the latest run is a known bug.</summary>
    public bool OnlyKnownBugs => Outcome is not null && Standing == "BUG";

    internal void RefreshStanding() { if (Outcome is not null) OnPropertyChanged(nameof(ResultText)); OnPropertyChanged(nameof(ResultColor)); OnPropertyChanged(nameof(CountsText)); OnPropertyChanged(nameof(CountsDetail)); OnPropertyChanged(nameof(BugTagText)); }

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
