namespace LoadTestingTool.UI.DataEditor;

// Tree actions that create or copy testcases and steps.
public sealed partial class EditorShellViewModel
{
    /// <summary>The "+" on an instance: adds a testcase and selects it with its name ready to type.</summary>
    public async Task<TestcaseItem?> AddTestcaseAsync(InstanceNode instance)
    {
        if (instance.CanLoad) await LoadAsync(instance);
        if (instance.Editor is not { } editor) return null;
        instance.IsExpanded = true;
        if (editor.AddTestcaseQuick() is not { } index) return null;
        var node = instance.Testcases.FirstOrDefault(t => t.Index == index);
        if (node is not null) SelectedNode = node;
        return node;
    }

    /// <summary>Reads an instance's workbook now (for the copy menus, which need its testcases).</summary>
    public void EnsureLoaded(InstanceNode instance) { if (instance.CanLoad) Load(instance); }

    /// <summary>The "+" on a testcase: adds a step under it and selects it with its name ready to type.</summary>
    public StepNode? AddStep(TestcaseItem testcase)
    {
        if (EditorOf(testcase) is not { } editor) return null;
        Current = editor;
        if (!ReferenceEquals(editor.SelectedTestcase, testcase)) editor.SelectedTestcase = testcase;
        var before = editor.StepRows.Count;
        editor.AddStepCommand.Execute(null);
        if (editor.StepRows.Count == before || editor.SelectedTestcase is not { } owner) return null;
        owner.IsExpanded = true;
        var node = owner.StepNodes.FirstOrDefault(n => string.Equals(n.Name, editor.StepRows[^1].CurrentName, StringComparison.OrdinalIgnoreCase));
        if (node is not null) SelectedNode = node;
        return node;
    }

    public void DuplicateStep(StepNode step)
    {
        if (EditorOf(step) is not { } editor) return;
        Current = editor;
        if (!ReferenceEquals(editor.SelectedTestcase, step.Owner)) editor.SelectedTestcase = step.Owner;
        if (editor.DuplicateStepNamed(step.Name) is not { } name) return;
        var node = editor.SelectedTestcase?.StepNodes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
        if (node is not null) SelectedNode = node;
    }

    /// <summary>Removes the step that was right-clicked (not the selected one) with its data columns, then selects a neighbour.</summary>
    public void RemoveStep(StepNode step)
    {
        if (EditorOf(step) is not { } editor) return;
        Current = editor;
        var owner = step.Owner;
        if (!ReferenceEquals(editor.SelectedTestcase, owner)) editor.SelectedTestcase = owner;
        var position = owner.StepNodes.IndexOf(step);
        editor.RemoveStepNamed(step.Name);
        if (owner.StepNodes.Count == 0) SelectedNode = owner;
        else SelectedNode = owner.StepNodes[Math.Clamp(position, 0, owner.StepNodes.Count - 1)];
    }

    /// <summary>Copies a testcase into another instance; the target keeps it unsaved until saved.</summary>
    public async Task CopyTestcaseToAsync(TestcaseItem testcase, InstanceNode target)
    {
        if (EditorOf(testcase) is not { } source) return;
        if (target.CanLoad) await LoadAsync(target);
        if (target.Editor is not { } editor) return;
        if (ReferenceEquals(editor, source)) { Current = source; source.OpenCopyTestcaseCommand.Execute(null); return; }
        if (editor.ImportTestcase(source, testcase.Index) is not { } index) { Current = editor; return; }
        target.IsExpanded = true;
        if (target.Testcases.FirstOrDefault(t => t.Index == index) is { } node) SelectedNode = node;
    }

    /// <summary>Copies a step into a testcase of another instance (or another testcase of the same one).</summary>
    public async Task CopyStepToAsync(StepNode step, InstanceNode target, TestcaseItem targetTestcase)
    {
        if (EditorOf(step) is not { } source) return;
        if (target.CanLoad) await LoadAsync(target);
        if (target.Editor is not { } editor) return;
        var row = source.StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, step.Name, StringComparison.OrdinalIgnoreCase))
            ?? (ReferenceEquals(source.SelectedTestcase, step.Owner) ? null : SelectStepOwner(source, step));
        if (row is null) return;
        var targetIndex = targetTestcase.Index;
        if (editor.ImportStep(source, row, targetIndex) is not { } name) { Current = editor; return; }
        target.IsExpanded = true;
        var owner = target.Testcases.FirstOrDefault(t => t.Index == targetIndex);
        if (owner is null) return;
        owner.IsExpanded = true;
        SelectedNode = (object?)owner.StepNodes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) ?? owner;
    }

    private static StepRowViewModel? SelectStepOwner(DataEditorViewModel editor, StepNode step)
    {
        editor.SelectedTestcase = step.Owner;
        return editor.StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, step.Name, StringComparison.OrdinalIgnoreCase));
    }
}
