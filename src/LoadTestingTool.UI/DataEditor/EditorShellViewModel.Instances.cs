namespace LoadTestingTool.UI.DataEditor;

// Adding and duplicating whole instances from the tree.
public sealed partial class EditorShellViewModel
{
    /// <summary>The instance a tree node belongs to.</summary>
    public InstanceNode? InstanceFor(object? node) => node switch
    {
        InstanceNode instance => instance,
        TestcaseItem testcase => Instances.FirstOrDefault(i => i.Testcases.Contains(testcase)),
        StepNode step => Instances.FirstOrDefault(i => i.Testcases.Contains(step.Owner)),
        _ => null
    };

    /// <summary>Shows a freshly created or copied instance folder in the tree and selects it.</summary>
    public InstanceNode? AddInstance(string name, string folder)
    {
        var workbook = InstanceOperations.FindWorkbook(folder, _environmentSettings?.WorkbookName ?? "DataEngine.xlsx");
        if (workbook is null) { Current?.Notify($"{name} has no workbook (.xlsx) to open.", true); return null; }
        var node = new InstanceNode(name, workbook);
        Instances.Add(node);
        if (_environmentSettings is { } settings)
        {
            node.LoadEnvironments(settings);
            node.EnvironmentsChanged += OnInstanceEnvironmentsChanged;
            node.EnvironmentProblem += OnEnvironmentProblem;
        }
        Load(node);
        RebuildGlobalEnvironments();
        SelectedNode = node.Editor?.SelectedTestcase is { } first ? first : node;
        return node;
    }

    /// <summary>Copies an instance's folder under a new name, registers it, and shows it. Unsaved edits are not in the copy, so they must be saved first.</summary>
    public InstanceNode? DuplicateInstance(InstanceNode source, string newName)
    {
        if (_environmentSettings is not { } settings) return null;
        if (source.Editor?.IsDirty == true) { source.Editor.Notify($"{source.Name} has unsaved changes. Save it first so the copy includes them.", true); return null; }
        var root = settings.InstancesRoot;
        newName = newName.Trim();
        if (InstanceOperations.CheckName(root, newName) is { } problem) { Current?.Notify(problem, true); return null; }
        var target = System.IO.Path.Combine(root, newName);
        try { InstanceOperations.CopyFolder(System.IO.Path.GetDirectoryName(source.Path)!, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Current?.Notify($"Could not copy {source.Name}: {ex.Message}", true); return null; }
        if (settings.RegisterInstance(newName, source.Name) is { } failed) { Current?.Notify(failed, true); return null; }
        var node = AddInstance(newName, target);
        node?.Editor?.Notify($"Copied {source.Name} as {newName} (workbook, templates and files; no results, logs or history).", false);
        return node;
    }

    /// <summary>Registers a folder created by the instance builder and shows it.</summary>
    public InstanceNode? AddCreatedInstance(string folder)
    {
        if (_environmentSettings is not { } settings) return null;
        var name = System.IO.Path.GetFileName(folder);
        if (settings.RegisterInstance(name, null) is { } failed) { Current?.Notify(failed, true); return null; }
        return AddInstance(name, folder);
    }
}
