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
        InstanceOperations.EnsureFolders(folder);
        var node = new InstanceNode(name, workbook);
        Instances.Add(node);
        if (_environmentSettings is { } settings)
        {
            node.LoadEnvironments(settings);
            node.EnvironmentsChanged += OnInstanceEnvironmentsChanged;
            node.EnvironmentProblem += OnEnvironmentProblem;
            AttachExecution(node);
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

    /// <summary>
    /// Re-reads the instances folder: instance folders with a workbook that the tree does not show yet are added (and registered), and instances whose workbook is gone are removed
    /// unless they have unsaved edits. Missing standard folders are created. Returns a one-line summary.
    /// </summary>
    public string RefreshInstances()
    {
        if (_environmentSettings is not { } settings) return "Refreshing instances is not available here.";
        var root = settings.InstancesRoot;
        if (!Directory.Exists(root)) return $"The instances folder does not exist: {root}";
        var known = Instances.Select(i => System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(i.Path)!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();
        var problems = new List<string>();
        foreach (var folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var full = System.IO.Path.GetFullPath(folder);
            if (known.Contains(full) || InstanceOperations.FindWorkbook(full, settings.WorkbookName) is null) continue;
            var name = System.IO.Path.GetFileName(full);
            if (settings.RegisterInstance(name, null) is { } failed) { problems.Add($"{name}: {failed}"); continue; }
            if (AddInstance(name, full) is not null) added.Add(name);
        }
        var removed = new List<string>();
        var kept = new List<string>();
        foreach (var node in Instances.ToList())
        {
            if (File.Exists(node.Path)) { InstanceOperations.EnsureFolders(System.IO.Path.GetDirectoryName(node.Path)!); continue; }
            if (node.Editor?.IsDirty == true) { kept.Add(node.Name); continue; }
            Instances.Remove(node); removed.Add(node.Name);
        }
        if (SelectedNode is null || (SelectedNode is InstanceNode sel && !Instances.Contains(sel))) SelectedNode = Instances.FirstOrDefault();
        var parts = new List<string>();
        parts.Add(added.Count == 0 && removed.Count == 0 ? "Instances are up to date." : $"Instances refreshed: {added.Count} added{(added.Count > 0 ? " (" + string.Join(", ", added) + ")" : "")}, {removed.Count} removed{(removed.Count > 0 ? " (" + string.Join(", ", removed) + ")" : "")}.");
        if (kept.Count > 0) parts.Add($"{string.Join(", ", kept)}: the workbook is gone but there are unsaved edits, so it stays until you save or close it.");
        if (problems.Count > 0) parts.Add("Could not add: " + string.Join("; ", problems));
        return string.Join(" ", parts);
    }
}
