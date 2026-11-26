using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One instance (workbook) in the editor's tree; its testcases are the children. It may not be loaded yet (lazy loading).</summary>
public sealed partial class InstanceNode : ObservableObject
{
    private static readonly ObservableCollection<TestcaseItem> NoTestcases = [];

    public InstanceNode(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string IconData => TreeIcons.Instance;

    public string Name { get; }

    /// <summary>The name as the tree shows it: without the default "DataEngine_" prefix (the real name, folder and file keep it).</summary>
    public string DisplayName => HidePrefix(Name);

    public const string DefaultPrefix = "DataEngine_";

    internal static string HidePrefix(string name)
        => name.StartsWith(DefaultPrefix, StringComparison.OrdinalIgnoreCase) && name.Length > DefaultPrefix.Length ? name[DefaultPrefix.Length..] : name;

    public string Path { get; }
    public DataEditorViewModel? Editor { get; private set; }
    public string? Error { get; private set; }
    public bool IsLoaded => Editor is not null;
    public bool HasError => Error is not null;
    public bool CanLoad => Editor is null && Error is null && !IsLoading;
    public ObservableCollection<TestcaseItem> Testcases => Editor?.Testcases ?? NoTestcases;
    public string DirtyColor => "#F6C76B";

    /// <summary>Data rows ticked in this instance's grids, shown beside the instance's ▶.</summary>
    public int TickedRows => Editor?.TickedRowCount ?? 0;
    public string TickedRowsText => TickedRows > 0 ? TickedRows.ToString() : string.Empty;
    public bool HasTickedRows => TickedRows > 0;
    internal void RefreshTickedRows()
    {
        OnPropertyChanged(nameof(TickedRows));
        OnPropertyChanged(nameof(TickedRowsText));
        OnPropertyChanged(nameof(HasTickedRows));
    }

    /// <summary>The instance's tick box: ticked when every testcase is, "some" when part of them are.</summary>
    public bool? ChosenState
    {
        get
        {
            var all = Testcases;
            if (all.Count == 0) return false;
            return all.All(t => t.ChosenState == true) ? true : all.Any(t => t.IsChosen) ? null : false;
        }
        set
        {
            using (Editor?.BatchSelection())
                foreach (var t in Testcases) t.IsChosen = value == true;
        }
    }

    public string Subtitle => Error is not null ? "Cannot open: " + Error
        : IsLoading ? "Loading…"
        : Editor is null ? "Not loaded — click to open"
        : $"{Testcases.Count} {(Testcases.Count == 1 ? "testcase" : "testcases")}";

    /// <summary>"Last run …: 12 passed, 3 failed", or "No results yet"; empty until the instance is loaded.</summary>
    public string RunSummary => Editor?.RunSummaryText ?? string.Empty;

    [ObservableProperty] private bool _isDirty;

    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>Expands the instance and every testcase under it, or collapses them all, depending on whether everything is already open.</summary>
    public void ToggleAll() => SetAllExpanded(!(IsExpanded && Testcases.All(t => t.IsExpanded)));

    public void SetAllExpanded(bool expanded)
    {
        IsExpanded = expanded;
        foreach (var testcase in Testcases) testcase.IsExpanded = expanded;
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Subtitle), nameof(CanLoad))]
    private bool _isLoading;

    /// <summary>Reads the workbook now, on the calling thread. Returns false (and keeps the reason in <see cref="Error"/>) when it cannot be opened.</summary>
    public bool LoadNow()
    {
        try
        {
            Attach(new DataEditorViewModel(Path, Name));
            return true;
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
            return false;
        }
    }

    /// <summary>Raised after this instance's workbook was saved, with the workbook path.</summary>
    public event Action<string>? WorkbookSaved;

    internal void Attach(DataEditorViewModel editor)
    {
        Editor = editor;
        editor.PropertyChanged += OnEditorChanged;
        editor.TickedChanged += (_, _) => OnPropertyChanged(nameof(ChosenState));
        editor.WorkbookSaved += path => WorkbookSaved?.Invoke(path);
        IsDirty = editor.IsDirty;
        NotifyLoadedChanged();
    }

    internal void Fail(string message)
    {
        Error = message;
        NotifyLoadedChanged();
    }

    private void NotifyLoadedChanged()
    {
        foreach (var name in new[] { nameof(Editor), nameof(IsLoaded), nameof(HasError), nameof(CanLoad), nameof(Testcases), nameof(Subtitle), nameof(RunSummary) })
            OnPropertyChanged(name);
    }

    /// <summary>Raised when the editor's selected testcase changed, so the tree can follow (after a rename, copy, delete or move).</summary>
    internal event EventHandler? SelectedTestcaseChanged;

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataEditorViewModel.SelectedTestcase)) SelectedTestcaseChanged?.Invoke(this, EventArgs.Empty);
        if (e.PropertyName == nameof(DataEditorViewModel.IsDirty)) IsDirty = Editor!.IsDirty;
        else if (e.PropertyName == nameof(DataEditorViewModel.RunSummaryText)) OnPropertyChanged(nameof(RunSummary));
    }
}

/// <summary>
/// The editor as a whole: every instance as a parent node with its testcases beneath it. Choosing a testcase
/// shows that instance's rows; each instance keeps its own unsaved edits while you move between them.
/// With lazy loading on, an instance's workbook is read only when it is first opened.
/// </summary>
public sealed partial class EditorShellViewModel : ObservableObject
{
    /// <summary>Raised after any instance's workbook was saved in the editor, with the workbook path.</summary>
    public event Action<string>? WorkbookSaved;

    public EditorShellViewModel(IEnumerable<(string Name, string Path)> instances, string? selectedPath = null, bool lazyLoading = false)
    {
        _lazyLoading = lazyLoading;
        ShownRunChoice.Subscribe(this, static (shell, key, _) => shell.OnShownRunChosen(key));
        foreach (var (name, path) in instances)
        {
            var node = new InstanceNode(name, path);
            node.WorkbookSaved += saved => WorkbookSaved?.Invoke(saved);
            Instances.Add(node);
        }
        // No instances yet (empty Instances folder): open an empty editor so the user can create one.
        if (Instances.Count == 0) return;

        var full = selectedPath is null ? null : System.IO.Path.GetFullPath(selectedPath);
        var start = Instances.FirstOrDefault(n => System.IO.Path.GetFullPath(n.Path) == full) ?? Instances[0];
        if (lazyLoading)
        {
            // Only what is shown first is read now; if that workbook cannot be opened, try the next ones until one can.
            foreach (var node in new[] { start }.Concat(Instances.Where(n => n != start)))
                if (Load(node)) { start = node; break; }
        }
        else
        {
            foreach (var node in Instances) Load(node);
            start = start.Editor is not null ? start : Instances.FirstOrDefault(n => n.Editor is not null) ?? start;
        }
        SelectedNode = start.Editor?.SelectedTestcase is { } testcase ? testcase : start;
        if (Current is { } opened) opened.ShowSteps = false; // the editor opens on the data, not the step list
    }

    public ObservableCollection<InstanceNode> Instances { get; } = [];

    // ---- Instance filter ------------------------------------------------------------------------

    private ObservableCollection<InstanceNode>? _visibleInstances;
    private string _instanceFilter = string.Empty;

    /// <summary>The instances the tree shows: all of them, or the ones whose name matches the filter box.</summary>
    public ObservableCollection<InstanceNode> VisibleInstances
    {
        get
        {
            if (_visibleInstances is null)
            {
                _visibleInstances = [];
                Instances.CollectionChanged += (_, _) => ApplyInstanceFilter();
                ApplyInstanceFilter();
            }
            return _visibleInstances;
        }
    }

    /// <summary>Words (joined with &amp;) that an instance's name must all contain; empty shows every instance.</summary>
    public string InstanceFilter
    {
        get => _instanceFilter;
        set { if (_instanceFilter == value) return; _instanceFilter = value; OnPropertyChanged(); ApplyInstanceFilter(); }
    }

    internal static bool NameMatches(InstanceNode instance, string filter)
        => filter.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(term => instance.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || instance.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));

    private void ApplyInstanceFilter()
    {
        var shown = Instances.Where(i => NameMatches(i, _instanceFilter)).ToList();
        var target = VisibleInstances;
        if (!target.SequenceEqual(shown))
        {
            for (var i = target.Count - 1; i >= 0; i--) if (!shown.Contains(target[i])) target.RemoveAt(i);
            for (var i = 0; i < shown.Count; i++)
                if (i >= target.Count || !ReferenceEquals(target[i], shown[i])) target.Insert(i, shown[i]);
        }
        OnPropertyChanged(nameof(InstanceFilterText));
        OnPropertyChanged(nameof(CanRunShown));
        OnPropertyChanged(nameof(RunShownBadge));
    }

    public string InstanceFilterText => string.IsNullOrWhiteSpace(_instanceFilter) ? string.Empty : $"{VisibleInstances.Count} of {Instances.Count} instances shown";
    public string RunShownBadge => $"({VisibleInstances.Count})";

    /// <summary>Run shown is available when the filter leaves something and none of the shown instances is running.</summary>
    public bool CanRunShown => VisibleInstances.Count > 0 && !VisibleInstances.Any(i => i.Run.IsRunning);

    /// <summary>Runs every shown instance completely (all testcases, all rows), whatever is ticked.</summary>
    public async Task RunShownAsync()
    {
        foreach (var instance in VisibleInstances.ToList())
        {
            if (instance.CanLoad) await LoadAsync(instance);
            if (instance.Editor is not { } editor) continue;
            await editor.RunFromTreeAsync(new RunRequest([], [], editor.RowsMode == DataEditorViewModel.RowsModeOptions[0] ? null : editor.RowsMode.ToLowerInvariant(), HasEnvironments ? instance.RunParallel : null), $"all of {instance.Name}");
        }
    }

    /// <summary>The editor for the instance currently shown; the main area binds to this.</summary>
    [ObservableProperty] private DataEditorViewModel? _current;

    /// <summary>Selected tree item: an <see cref="InstanceNode"/> or one of its <see cref="TestcaseItem"/>s.</summary>
    [ObservableProperty] private object? _selectedNode;

    /// <summary>Raised when every instance has been saved or discarded and the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Backup from the most recent save in any instance; the dashboard uses it to know a workbook changed.</summary>
    public string? LastBackupPath => Instances.Select(i => i.Editor?.LastBackupPath).LastOrDefault(p => !string.IsNullOrEmpty(p));

    public IReadOnlyList<string> SavedWorkbookPaths => Instances.Where(i => i.Editor?.LastBackupPath is not null).Select(i => i.Path).ToList();

    /// <summary>True when a testcase (not an instance) is selected in the tree, so the testcase menu applies.</summary>
    public bool TestcaseNodeSelected => SelectedNode is TestcaseItem or StepNode;

    private DataEditorViewModel? EditorOf(object node) => node switch
    {
        TestcaseItem t => Instances.FirstOrDefault(n => n.Testcases.Contains(t))?.Editor,
        StepNode st => Instances.FirstOrDefault(n => n.Testcases.Contains(st.Owner))?.Editor,
        _ => null
    };

    /// <summary>Starts editing a testcase's or step's name in place in the tree.</summary>
    public void BeginRename(object node)
    {
        switch (node)
        {
            case TestcaseItem t: t.EditText = t.Name; t.IsEditing = true; break;
            case StepNode s: s.EditText = s.Name; s.IsEditing = true; break;
        }
    }

    public void CancelRename(object node)
    {
        if (node is TestcaseItem t) t.IsEditing = false;
        else if (node is StepNode s) s.IsEditing = false;
    }

    /// <summary>Applies the typed name. Does nothing if it was already committed or cancelled.</summary>
    public void CommitRename(object node)
    {
        switch (node)
        {
            case TestcaseItem { IsEditing: true } t:
                t.IsEditing = false;
                if (EditorOf(t) is { } te)
                {
                    SelectedNode = t;
                    te.RenameSelectedTestcase(t.EditText);
                }
                break;
            case StepNode { IsEditing: true } s:
                s.IsEditing = false;
                if (EditorOf(s) is { } se)
                {
                    Current = se;
                    if (!ReferenceEquals(se.SelectedTestcase, s.Owner)) se.SelectedTestcase = s.Owner;
                    se.RenameStepNamed(s.Name, s.EditText);
                    var renamed = s.Owner.StepNodes.FirstOrDefault(n => string.Equals(n.Name, s.EditText.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (renamed is not null) SelectedNode = renamed;
                }
                break;
        }
    }

    /// <summary>Opens or closes every instance and testcase in the tree. Collapsing leaves just the instance rows.</summary>
    public void SetAllExpanded(bool expanded)
    {
        foreach (var instance in Instances) instance.SetAllExpanded(expanded);
    }

    /// <summary>Testcases (or steps) ticked in the tree across all loaded instances.</summary>
    public int TickedTestcases => Instances.Sum(i => i.Editor?.TickedTestcaseCount ?? 0);

    /// <summary>Data rows ticked in the grids across all loaded instances.</summary>
    public int TickedRows => Instances.Sum(i => i.Editor?.TickedRowCount ?? 0);

    /// <summary>What Run ticked will run: the ticked data rows when there are any (they take precedence), otherwise the ticked testcases.</summary>
    public int TickedCount => TickedRows > 0 ? TickedRows : TickedTestcases;
    public bool HasTicked => TickedCount > 0;
    public string TickedBadge => TickedCount == 0 ? string.Empty : $"({TickedCount})";
    public string TickedText => TickedRows > 0 ? $"{TickedRows} {(TickedRows == 1 ? "row" : "rows")} ticked"
        : TickedTestcases > 0 ? $"{TickedTestcases} {(TickedTestcases == 1 ? "testcase" : "testcases")} ticked" : "nothing ticked";

    /// <summary>The ▶ beside an instance, testcase or step: runs just that.</summary>
    public async Task RunNodeAsync(object node)
    {
        switch (node)
        {
            case InstanceNode instance:
                if (instance.CanLoad) await LoadAsync(instance);
                if (instance.Editor is not { } ie) return;
                if (ie.BuildTickedRowsRequest(null, null, HasEnvironments ? instance.RunParallel : null) is { } instanceRows)
                    await ie.RunFromTreeAsync(instanceRows.Request, $"{instanceRows.Label} of {instance.Name}");
                else
                    await ie.RunFromTreeAsync(new RunRequest([], [], ie.RowsMode == DataEditorViewModel.RowsModeOptions[0] ? null : ie.RowsMode.ToLowerInvariant(), HasEnvironments ? instance.RunParallel : null), $"all of {instance.Name}");
                break;
            case TestcaseItem testcase when EditorOf(testcase) is { } te:
                if (te.BuildTickedRowsRequest(testcase, null, ParallelFor(testcase)) is { } testcaseRows)
                    await te.RunFromTreeAsync(testcaseRows.Request, $"{testcaseRows.Label} of testcase {testcase.Name}");
                else
                    await te.RunFromTreeAsync(te.BuildRequest([(testcase, null)], ParallelFor(testcase)), $"testcase {testcase.Name}");
                break;
            case StepNode step when EditorOf(step) is { } se:
                if (!se.IsStepEnabled(step.Owner.Index, step.Name)) { se.Report($"Step {step.Name} is disabled, so it cannot be run on its own. Enable it on the Steps tab first.", isError: true); return; }
                if (se.BuildTickedRowsRequest(step.Owner, [step.Name], ParallelFor(step.Owner)) is { } stepRows)
                    await se.RunFromTreeAsync(stepRows.Request, $"{stepRows.Label} of step {step.Name} of {step.Owner.Name}");
                else
                    await se.RunFromTreeAsync(se.BuildRequest([(step.Owner, [step.Name])], ParallelFor(step.Owner)), $"step {step.Name} of {step.Owner.Name}");
                break;
        }
    }

    private bool? ParallelFor(TestcaseItem testcase)
        => HasEnvironments ? Instances.FirstOrDefault(i => i.Testcases.Contains(testcase))?.RunParallel : null;

    /// <summary>Runs everything that is ticked, one run per instance.</summary>
    public async Task RunTickedAsync()
    {
        var started = 0;
        foreach (var instance in Instances.Where(i => i.Editor is not null).ToList())
        {
            var editor = instance.Editor!;
            var (request, label) = editor.BuildTickedRequest(HasEnvironments ? instance.RunParallel : null);
            if (request is null) continue;
            await editor.RunFromTreeAsync(request, label);
            started++;
        }
        if (started == 0) (Current ?? Instances.FirstOrDefault(i => i.Editor is not null)?.Editor)?.Report("Tick at least one testcase, step or data row to run.", isError: true);
    }

    /// <summary>The ⚙ beside a step in the tree: shows the testcase's steps as forms (Config edit mode) with only this step's settings in view.</summary>
    public void OpenStepConfig(StepNode step)
    {
        SelectedNode = step;
        if (Current is not { } editor) return;
        editor.ViewModeIndex = 1;
        editor.SelectStepNamed(step.Name, showSettings: true);
    }

    /// <summary>The ▲ ▼ buttons beside a tree item: runs it earlier (-1) or later (+1).</summary>
    public void MoveNode(object node, int direction)
    {
        switch (node)
        {
            case TestcaseItem t when EditorOf(t) is { } te:
                Current = te;
                if (!ReferenceEquals(te.SelectedTestcase, t)) te.SelectedTestcase = t;
                (direction < 0 ? te.MoveTestcaseUpCommand : te.MoveTestcaseDownCommand).Execute(null);
                break;
            case StepNode s when EditorOf(s) is { } se:
                Current = se;
                if (!ReferenceEquals(se.SelectedTestcase, s.Owner)) se.SelectedTestcase = s.Owner;
                var wasSelected = ReferenceEquals(SelectedNode, s);
                se.MoveStepNamed(s.Name, direction);
                if (wasSelected && s.Owner.StepNodes.FirstOrDefault(n => string.Equals(n.Name, s.Name, StringComparison.OrdinalIgnoreCase)) is { } again) SelectedNode = again;
                break;
        }
    }

    /// <summary>True when a step is selected in the tree, so the step menu applies.</summary>
    public bool StepNodeSelected => SelectedNode is StepNode;

    /// <summary>The path of the selected item for the header above the run views, e.g. <c>DataEngine_Example › Testcase 1 › GetUser</c>.</summary>
    public string SelectionTitle => SelectedNode switch
    {
        StepNode step => $"{InstanceNameOf(step.Owner)} › {step.Owner.Name} › {step.Name}",
        TestcaseItem testcase => $"{InstanceNameOf(testcase)} › {testcase.Name}",
        InstanceNode instance => instance.Name,
        _ => string.Empty,
    };

    /// <summary>The instance the selected tree item belongs to, or the only open one.</summary>
    public string? SelectedInstanceName => SelectedNode switch
    {
        StepNode step => InstanceNameOf(step.Owner),
        TestcaseItem testcase => InstanceNameOf(testcase),
        InstanceNode instance => instance.Name,
        _ => Instances.Count == 1 ? Instances[0].Name : null,
    };

    /// <summary>"Use in instance" of the Mock server window: points the selected instance's step URLs at a server. Returns a sentence for the window's status line.</summary>
    public string PointSelectedInstanceAt(string origin, bool includeRemote)
    {
        if (SelectedInstanceName is not { } name) return "Select an instance in the editor first.";
        var editor = Instances.FirstOrDefault(i => i.Name == name)?.Editor;
        if (editor is null) return $"Open {name} first (click it in the tree), then try again.";
        var made = editor.PointUrlsAt(origin, includeRemote);
        return made == 0 ? $"Nothing changed in {name}: no step has a {(includeRemote ? "" : "local ")}URL that needs changing." : $"{name}: {made} {(made == 1 ? "step" : "steps")} now use {origin}. Save the instance to keep it (Ctrl+Z undoes it).";
    }

    private string InstanceNameOf(TestcaseItem testcase) => Instances.FirstOrDefault(n => n.Testcases.Contains(testcase))?.Name ?? string.Empty;

    partial void OnSelectedNodeChanged(object? value)
    {
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(TestcaseNodeSelected));
        OnPropertyChanged(nameof(StepNodeSelected));
        ShowRunDetails();
        switch (value)
        {
            case StepNode step:
                var stepOwner = Instances.FirstOrDefault(n => n.Testcases.Contains(step.Owner));
                if (stepOwner?.Editor is not { } stepEditor) return;
                Current = stepEditor;
                if (!ReferenceEquals(stepEditor.SelectedTestcase, step.Owner)) stepEditor.SelectedTestcase = step.Owner;
                stepEditor.SelectStepNamed(step.Name, showSettings: stepEditor.ShowSteps);   // the view mode stays as chosen: Config edit mode shows the step form, Table view its columns
                break;
            case InstanceNode { Editor: { } editor }:
                Current = editor;
                editor.ShowWholeInstance();
                break;
            case InstanceNode { CanLoad: true } unloaded:
                WhenIdle = LoadThenShowAsync(unloaded);
                break;
            case TestcaseItem testcase:
                var owner = Instances.FirstOrDefault(n => n.Testcases.Contains(testcase));
                if (owner?.Editor is not { } ownerEditor) return;
                Current = ownerEditor;
                if (!ReferenceEquals(ownerEditor.SelectedTestcase, testcase)) ownerEditor.SelectedTestcase = testcase;
                ownerEditor.ShowTestcaseSteps();
                break;
        }
    }

    /// <summary>Whether instances are read only when first opened. Turning it off reads the ones still waiting.</summary>
    [ObservableProperty] private bool _lazyLoading;

    partial void OnLazyLoadingChanged(bool value)
    {
        if (!value) WhenIdle = LoadAllAsync();
    }

    /// <summary>Completes when the most recent background load (started by selecting or by turning lazy loading off) has finished.</summary>
    public Task WhenIdle { get; private set; } = Task.CompletedTask;

    private bool Load(InstanceNode node)
    {
        if (!node.LoadNow()) return false;
        Wire(node);
        return true;
    }

    /// <summary>Opens the History Explorer filtered to an instance and, optionally, a testcase name; set by the dashboard.</summary>
    public Action<string, string?>? HistoryHandler { get; set; }

    /// <summary>Where an instance's history is and which environments it has (for re-running from history): the instance name.</summary>
    public Func<string, RerunContext?>? RerunContextProvider { get; set; }

    /// <summary>Starts a run of one instance: its name and what to run.</summary>
    public Func<string, RunRequest, Task<string?>>? RunHandler { get; set; }

    /// <summary>Writes a wizard-built testcase into an instance's workbook file: instance name and the draft.</summary>
    public Action<string, TestcaseWizardDraft>? CreateTestcaseHandler { get; set; }

    private void Wire(InstanceNode node)
    {
        AttachSelection(node);
        node.Editor!.TickedChanged += (_, _) => { node.RefreshTickedRows(); OnPropertyChanged(nameof(TickedCount)); OnPropertyChanged(nameof(HasTicked)); OnPropertyChanged(nameof(TickedText)); OnPropertyChanged(nameof(TickedBadge)); OnPropertyChanged(nameof(CanRunTicked)); };
        node.Editor!.CreateTestcaseHandler = draft => CreateTestcaseHandler?.Invoke(node.Name, draft);
        node.Editor!.CloseRequested += (_, _) => OnEditorClosed();
        node.Editor.HistoryHandler = testcase => HistoryHandler?.Invoke(node.Name, testcase);
        node.Editor.RerunContextProvider = () => RerunContextProvider?.Invoke(node.Name);
        node.SelectedTestcaseChanged += (_, _) =>
        {
            if (ReferenceEquals(Current, node.Editor) && node.Editor!.SelectedTestcase is { } chosen2 && !ReferenceEquals(SelectedNode, chosen2) && !(SelectedNode is StepNode sn && ReferenceEquals(sn.Owner, chosen2))) SelectedNode = chosen2;
        };
        node.Editor.RunHandler = request => RunHandler is { } run ? run(node.Name, request) : Task.FromResult<string?>("Running is not available here.");
    }

    /// <summary>Reads one instance's workbook and its latest results off the UI thread. Does nothing if it is already loaded or failed.</summary>
    public async Task LoadAsync(InstanceNode node)
    {
        if (!node.CanLoad) return;
        node.IsLoading = true;
        try
        {
            var editor = await Task.Run(() => new DataEditorViewModel(node.Path, node.Name));
            node.Attach(editor);
            Wire(node);
            _signatures[node.Path] = RunResultsReader.Signature(editor.ResultsFolder)?.Signature;
            RunResults? results = null;
            try { results = await Task.Run(() => RunResultsReader.ReadLatest(editor.ResultsFolder, ShownRunChoice.Get(editor.ResultsFolder))); }
            catch (IOException) { }
            editor.ApplyResults(results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            node.Fail(ex.Message);
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    private async Task LoadThenShowAsync(InstanceNode node)
    {
        await LoadAsync(node);
        if (node.Editor is null || !ReferenceEquals(SelectedNode, node)) return;
        Current = node.Editor;
        node.Editor.ShowWholeInstance();
    }

    private async Task LoadAllAsync()
    {
        foreach (var node in Instances.ToList()) await LoadAsync(node);
    }

    /// <summary>Reads the latest run of every instance from its Results folder, off the UI thread (event files can be large).</summary>
    [RelayCommand]
    public async Task RefreshResultsAsync()
    {
        if (IsLoadingResults) { _refreshAgain = true; return; }
        IsLoadingResults = true;
        try
        {
            foreach (var node in Instances.Where(n => n.Editor is not null))
                _signatures[node.Path] = RunResultsReader.Signature(node.Editor!.ResultsFolder)?.Signature;
            var loaded = await Task.WhenAll(Instances.Where(n => n.Editor is not null).Select(async node =>
            {
                try { return (node, results: await Task.Run(() => RunResultsReader.ReadLatest(node.Editor!.ResultsFolder, ShownRunChoice.Get(node.Editor!.ResultsFolder)))); }
                catch (IOException) { return (node, results: (RunResults?)null); }
            }));
            foreach (var (node, results) in loaded) node.Editor!.ApplyResults(results);
        }
        finally
        {
            IsLoadingResults = false;
        }
        if (_refreshAgain) { _refreshAgain = false; await RefreshResultsAsync(); }   // the run choice changed while reading: read again with the new choice
    }

    private bool _refreshAgain;

    /// <summary>The shared run choice of an instance changed (here or in the dashboard): show that run in the table.</summary>
    private void OnShownRunChosen(string resultsFolderKey)
    {
        if (!Instances.Any(n => n.Editor is { } e && ShownRunChoice.Same(e.ResultsFolder, resultsFolderKey))) return;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) _ = RefreshResultsAsync();
        else Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshResultsAsync());
    }

    [ObservableProperty] private bool _isLoadingResults;

    private readonly Dictionary<string, string?> _signatures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Re-reads the results only if a run has written something since they were last read. Meant to be called on a
    /// timer; returns whether it refreshed. Runs too large to re-read every few seconds are left to the Refresh button.
    /// </summary>
    public async Task<bool> RefreshResultsIfChangedAsync()
    {
        if (IsLoadingResults) return false;
        var changed = await Task.Run(() => Instances.Where(n => n.Editor is not null).Any(node =>
        {
            var now = RunResultsReader.Signature(node.Editor!.ResultsFolder);
            _signatures.TryGetValue(node.Path, out var before);
            return now is { } current && current.Signature != before && current.TotalBytes <= RunResultsReader.AutoRefreshLimitBytes;
        }));
        if (!changed) return false;
        await RefreshResultsAsync();
        return true;
    }

    /// <summary>Called when the user tries to close the window; true when nothing is left unsaved.</summary>
    public bool RequestClose()
    {
        var dirty = Instances.FirstOrDefault(n => n.Editor is { IsDirty: true });
        if (dirty?.Editor is null) return true;
        Show(dirty);
        return dirty.Editor.RequestClose();
    }

    private void OnEditorClosed()
    {
        var next = Instances.FirstOrDefault(n => n.Editor is { IsDirty: true });
        if (next?.Editor is { } editor)
        {
            Show(next);
            editor.RequestClose();
            return;
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Show(InstanceNode node) => SelectedNode = node.Editor?.SelectedTestcase is { } testcase ? testcase : node;
}
