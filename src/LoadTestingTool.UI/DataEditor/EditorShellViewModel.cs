using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One instance (workbook) in the editor's tree; its testcases are the children.</summary>
public sealed partial class InstanceNode : ObservableObject
{
    private static readonly ObservableCollection<TestcaseItem> NoTestcases = [];

    public InstanceNode(string name, string path)
    {
        Name = name;
        Path = path;
        try
        {
            Editor = new DataEditorViewModel(path, name);
            Editor.PropertyChanged += OnEditorChanged;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    public string Name { get; }
    public string Path { get; }
    public DataEditorViewModel? Editor { get; }
    public string? Error { get; }
    public ObservableCollection<TestcaseItem> Testcases => Editor?.Testcases ?? NoTestcases;
    public bool HasError => Error is not null;
    public string Subtitle => Error is not null ? "Cannot open: " + Error : $"{Testcases.Count} {(Testcases.Count == 1 ? "testcase" : "testcases")}";
    public string DirtyColor => "#F6C76B";

    [ObservableProperty] private bool _isDirty;

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DataEditorViewModel.IsDirty)) IsDirty = Editor!.IsDirty;
    }
}

/// <summary>
/// The editor as a whole: every instance as a parent node with its testcases beneath it. Choosing a testcase
/// shows that instance's rows; each instance keeps its own unsaved edits while you move between them.
/// </summary>
public sealed partial class EditorShellViewModel : ObservableObject
{
    public EditorShellViewModel(IEnumerable<(string Name, string Path)> instances, string? selectedPath = null)
    {
        foreach (var (name, path) in instances)
        {
            var node = new InstanceNode(name, path);
            Instances.Add(node);
            if (node.Editor is { } editor) editor.CloseRequested += (_, _) => OnEditorClosed();
        }
        if (Instances.Count == 0) throw new ArgumentException("There are no instances to edit.", nameof(instances));

        var full = selectedPath is null ? null : System.IO.Path.GetFullPath(selectedPath);
        var start = Instances.FirstOrDefault(n => n.Editor is not null && n.Editor.WorkbookPath == full)
                    ?? Instances.FirstOrDefault(n => n.Editor is not null)
                    ?? Instances[0];
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

    partial void OnSelectedNodeChanged(object? value)
    {
        switch (value)
        {
            case InstanceNode { Editor: { } editor }:
                Current = editor;
                break;
            case TestcaseItem testcase:
                var owner = Instances.FirstOrDefault(n => n.Testcases.Contains(testcase));
                if (owner?.Editor is not { } ownerEditor) return;
                Current = ownerEditor;
                if (!ReferenceEquals(ownerEditor.SelectedTestcase, testcase)) ownerEditor.SelectedTestcase = testcase;
                break;
        }
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
