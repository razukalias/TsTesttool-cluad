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

    public string Name { get; }
    public string Path { get; }
    public DataEditorViewModel? Editor { get; private set; }
    public string? Error { get; private set; }
    public bool IsLoaded => Editor is not null;
    public bool HasError => Error is not null;
    public bool CanLoad => Editor is null && Error is null && !IsLoading;
    public ObservableCollection<TestcaseItem> Testcases => Editor?.Testcases ?? NoTestcases;
    public string DirtyColor => "#F6C76B";

    public string Subtitle => Error is not null ? "Cannot open: " + Error
        : IsLoading ? "Loading…"
        : Editor is null ? "Not loaded — click to open"
        : $"{Testcases.Count} {(Testcases.Count == 1 ? "testcase" : "testcases")}";

    /// <summary>"Last run …: 12 passed, 3 failed", or "No results yet"; empty until the instance is loaded.</summary>
    public string RunSummary => Editor?.RunSummaryText ?? string.Empty;

    [ObservableProperty] private bool _isDirty;

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

    internal void Attach(DataEditorViewModel editor)
    {
        Editor = editor;
        editor.PropertyChanged += OnEditorChanged;
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
    public EditorShellViewModel(IEnumerable<(string Name, string Path)> instances, string? selectedPath = null, bool lazyLoading = false)
    {
        _lazyLoading = lazyLoading;
        foreach (var (name, path) in instances) Instances.Add(new InstanceNode(name, path));
        if (Instances.Count == 0) throw new ArgumentException("There are no instances to edit.", nameof(instances));

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
    }

    public ObservableCollection<InstanceNode> Instances { get; } = [];

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
    public bool TestcaseNodeSelected => SelectedNode is TestcaseItem;

    partial void OnSelectedNodeChanged(object? value)
    {
        OnPropertyChanged(nameof(TestcaseNodeSelected));
        switch (value)
        {
            case InstanceNode { Editor: { } editor }:
                Current = editor;
                break;
            case InstanceNode { CanLoad: true } unloaded:
                WhenIdle = LoadThenShowAsync(unloaded);
                break;
            case TestcaseItem testcase:
                var owner = Instances.FirstOrDefault(n => n.Testcases.Contains(testcase));
                if (owner?.Editor is not { } ownerEditor) return;
                Current = ownerEditor;
                if (!ReferenceEquals(ownerEditor.SelectedTestcase, testcase)) ownerEditor.SelectedTestcase = testcase;
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

    /// <summary>Starts a run of one instance: its name and what to run.</summary>
    public Func<string, RunRequest, Task<string?>>? RunHandler { get; set; }

    /// <summary>Writes a wizard-built testcase into an instance's workbook file: instance name and the draft.</summary>
    public Action<string, TestcaseWizardDraft>? CreateTestcaseHandler { get; set; }

    private void Wire(InstanceNode node)
    {
        node.Editor!.CreateTestcaseHandler = draft => CreateTestcaseHandler?.Invoke(node.Name, draft);
        node.Editor!.CloseRequested += (_, _) => OnEditorClosed();
        node.SelectedTestcaseChanged += (_, _) =>
        {
            if (ReferenceEquals(Current, node.Editor) && node.Editor!.SelectedTestcase is { } chosen && !ReferenceEquals(SelectedNode, chosen)) SelectedNode = chosen;
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
            try { results = await Task.Run(() => RunResultsReader.ReadLatest(editor.ResultsFolder)); }
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
        if (node.Editor.SelectedTestcase is { } testcase) SelectedNode = testcase;
        else Current = node.Editor;
    }

    private async Task LoadAllAsync()
    {
        foreach (var node in Instances.ToList()) await LoadAsync(node);
    }

    /// <summary>Reads the latest run of every instance from its Results folder, off the UI thread (event files can be large).</summary>
    [RelayCommand]
    public async Task RefreshResultsAsync()
    {
        IsLoadingResults = true;
        try
        {
            foreach (var node in Instances.Where(n => n.Editor is not null))
                _signatures[node.Path] = RunResultsReader.Signature(node.Editor!.ResultsFolder)?.Signature;
            var loaded = await Task.WhenAll(Instances.Where(n => n.Editor is not null).Select(async node =>
            {
                try { return (node, results: await Task.Run(() => RunResultsReader.ReadLatest(node.Editor!.ResultsFolder))); }
                catch (IOException) { return (node, results: (RunResults?)null); }
            }));
            foreach (var (node, results) in loaded) node.Editor!.ApplyResults(results);
        }
        finally
        {
            IsLoadingResults = false;
        }
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
