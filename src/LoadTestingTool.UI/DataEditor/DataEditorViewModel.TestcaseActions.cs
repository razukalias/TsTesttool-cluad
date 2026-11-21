using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Rename, copy, reorder and delete for the selected testcase, and reorder for the selected step.
// Every action is an in-memory edit (one undo step) and is saved with the rest.
public sealed partial class DataEditorViewModel
{
    private enum TestcaseAction { None, Rename, Copy, Delete, RenameStep, DuplicateStep }

    private string _duplicateStepSource = string.Empty;

    private TestcaseAction _testcaseAction;

    [ObservableProperty] private bool _testcasePanelVisible;
    [ObservableProperty] private string _testcasePanelTitle = string.Empty;
    [ObservableProperty] private string _testcasePanelName = string.Empty;
    [ObservableProperty] private string _testcasePanelNote = string.Empty;
    [ObservableProperty] private string _testcasePanelConfirmText = "OK";
    [ObservableProperty] private bool _testcasePanelAsksName;
    [ObservableProperty] private bool _testcasePanelIsDelete;
    [ObservableProperty] private bool _testcasePanelAsksTemplate;
    [ObservableProperty] private bool _testcasePanelCopyTemplate = true;

    [RelayCommand]
    private void OpenRenameTestcase()
    {
        if (SelectedTestcase is not { } testcase) return;
        _testcaseAction = TestcaseAction.Rename;
        TestcasePanelTitle = $"RENAME TESTCASE · {testcase.Title}";
        TestcasePanelName = testcase.Name;
        TestcasePanelConfirmText = "Rename";
        TestcasePanelNote = "Changes the name on every row of this testcase in the config, request and response sheets.";
        OpenTestcasePanel(asksName: true, isDelete: false);
    }

    /// <summary>Selects a step by name (from the tree); shows its data columns, or its settings on the Steps tab.</summary>
    public void SelectStepNamed(string name, bool showSettings)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase));
        if (step is null) return;
        SelectedStep = step;
        if (showSettings) { FocusedStep = step; ShowStepTable = false; ShowSteps = true; }
        else ShowStepData();
    }

    /// <summary>Renames the selected testcase to the text typed in the tree.</summary>
    public void RenameSelectedTestcase(string name)
    {
        if (SelectedTestcase is not { } testcase || name.Trim() == testcase.Name) return;
        _testcaseAction = TestcaseAction.Rename;
        TestcasePanelName = name;
        ConfirmTestcaseAction();
    }

    /// <summary>Renames the step called <paramref name="current"/> in the selected testcase to the text typed in the tree.</summary>
    public void RenameStepNamed(string current, string name)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, current, StringComparison.OrdinalIgnoreCase));
        if (step is null || name.Trim() == step.CurrentName) return;
        SelectedStep = step;
        _testcaseAction = TestcaseAction.RenameStep;
        TestcasePanelName = name;
        ConfirmTestcaseAction();
    }

    /// <summary>Moves the step called <paramref name="name"/> one place earlier or later.</summary>
    public void MoveStepNamed(string name, int direction)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase));
        if (step is null) return;
        SelectedStep = step;
        MoveStep(direction);
    }

    /// <summary>Puts the step (with what is inside it) into a control step, or back at the top level when <paramref name="parent"/> is null.</summary>
    public void SetStepParentNamed(string name, string? parent, bool elseBranch = false)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase));
        if (step is null) return;
        Record(parent is null ? $"take step {name} out of its control step" : $"put step {name} inside {parent}{(elseBranch ? " (else)" : string.Empty)}");
        try { _document.SetStepParent(step.Row, parent, elseBranch); }
        catch (InvalidOperationException ex) { DiscardLastRecord(); ShowStatus(ex.Message, isError: true); return; }
        AfterControlMove(name, parent is null ? $"Moved {name} to the top level." : $"Put {name} inside {parent}{(elseBranch ? " (else branch)" : string.Empty)}.");
    }

    /// <summary>Moves the step one level out: next to the control step it is inside.</summary>
    public void MoveStepOutNamed(string name)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase));
        if (step is null) return;
        Record($"take step {name} out of its control step");
        try { _document.MoveStepOut(step.Row); }
        catch (InvalidOperationException ex) { DiscardLastRecord(); ShowStatus(ex.Message, isError: true); return; }
        AfterControlMove(name, $"Moved {name} one level out.");
    }

    private void AfterControlMove(string name, string message)
    {
        StepsChanged();
        BuildSteps();   // the Parent column may be new, and the rows changed places
        SelectedStep = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase)) ?? SelectedStep;
        ShowStatus(message, isError: false);
    }

    [RelayCommand]
    private void OpenRenameStep()
    {
        if (SelectedStep is not { } step) return;
        _testcaseAction = TestcaseAction.RenameStep;
        TestcasePanelTitle = $"RENAME STEP · {step.CurrentName}";
        TestcasePanelName = step.CurrentName;
        TestcasePanelConfirmText = "Rename";
        TestcasePanelNote = "Renames the step and its request and response data columns, and updates references to it (dontrun, extractvariable, runtime references).";
        OpenTestcasePanel(asksName: true, isDelete: false);
    }

    [RelayCommand]
    private void OpenCopyTestcase()
    {
        if (SelectedTestcase is not { } testcase) return;
        _testcaseAction = TestcaseAction.Copy;
        TestcasePanelTitle = $"DUPLICATE TESTCASE · {testcase.Title}";
        TestcasePanelName = testcase.Name + " copy";
        TestcasePanelConfirmText = "Duplicate";
        TestcasePanelNote = $"Copies its {testcase.Steps.Count} {(testcase.Steps.Count == 1 ? "step" : "steps")} (named Name + Copy, with their data columns, extract mappings and dontrun entries) and its {testcase.RowCount} data {(testcase.RowCount == 1 ? "row" : "rows")} under the next free testcase number. Tick the box to give the copy its own template and query (stepconfig source) files; untick it to share the originals.";
        OpenTestcasePanel(asksName: true, isDelete: false, asksTemplate: true);
    }

    /// <summary>Opens the duplicate panel for one step: its new name (offered, editable) and whether it gets its own template file.</summary>
    public void OpenDuplicateStep(string stepName)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, stepName, StringComparison.OrdinalIgnoreCase));
        if (step is null) return;
        _testcaseAction = TestcaseAction.DuplicateStep;
        _duplicateStepSource = step.CurrentName;
        TestcasePanelTitle = $"DUPLICATE STEP · {step.CurrentName}";
        TestcasePanelName = _document.SuggestCopyName(step.CurrentName);
        TestcasePanelConfirmText = "Duplicate";
        TestcasePanelNote = "Copies the step right after the original with its data columns and values, extract mappings (Step.path:variable, under the new name) and settings. Tick the box to give it its own template and query (stepconfig source) file; untick it to share the original's.";
        OpenTestcasePanel(asksName: true, isDelete: false, asksTemplate: true);
    }

    [RelayCommand]
    private void OpenDeleteTestcase()
    {
        if (SelectedTestcase is not { } testcase) return;
        _testcaseAction = TestcaseAction.Delete;
        TestcasePanelTitle = $"DELETE TESTCASE · {testcase.Title}";
        TestcasePanelConfirmText = "Delete testcase";
        TestcasePanelNote = $"Removes its {testcase.Steps.Count} {(testcase.Steps.Count == 1 ? "step" : "steps")} and {testcase.RowCount} data {(testcase.RowCount == 1 ? "row" : "rows")}. Their sheet rows are cleared when you save; the step data columns stay. Refused if another cell refers to them. You can undo until you save.";
        OpenTestcasePanel(asksName: false, isDelete: true);
    }

    private void OpenTestcasePanel(bool asksName, bool isDelete, bool asksTemplate = false)
    {
        TestcasePanelAsksTemplate = asksTemplate;
        TestcasePanelAsksName = asksName;
        TestcasePanelIsDelete = isDelete;
        TestcasePanelVisible = true;
    }

    [RelayCommand]
    private void CloseTestcasePanel() => TestcasePanelVisible = false;

    [RelayCommand]
    private void ConfirmTestcaseAction()
    {
        if (SelectedTestcase is not { } testcase || _testcaseAction == TestcaseAction.None) return;
        var index = testcase.Index;
        var action = _testcaseAction;
        var name = TestcasePanelName.Trim();
        if (action == TestcaseAction.RenameStep)
        {
            if (SelectedStep is not { } step) return;
            if (name.Length == 0) { _testcaseAction = TestcaseAction.None; ShowStatus("A step name cannot be empty.", isError: true); return; }
            TestcasePanelVisible = false;
            _testcaseAction = TestcaseAction.None;
            OnStepEdited(step, 0, name);
            if (!ReferenceEquals(step.CurrentName, name) && step.CurrentName == name) step.SetCellSilently(0, name);
            return;
        }
        if (action == TestcaseAction.DuplicateStep)
        {
            var source = _duplicateStepSource;
            var copyTemplate = TestcasePanelCopyTemplate;
            if (name.Length == 0) { ShowStatus("A step name cannot be empty.", isError: true); return; }
            if (DuplicateStepNamed(source, name, copyTemplate) is null) return;
            TestcasePanelVisible = false;
            _testcaseAction = TestcaseAction.None;
            return;
        }
        Record($"{action.ToString().ToLowerInvariant()} testcase {testcase.Title}");
        try
        {
            switch (action)
            {
                case TestcaseAction.Rename:
                    _document.RenameTestcase(index, name);
                    FinishTestcaseAction(index, $"Renamed testcase {index} to {name}.");
                    break;
                case TestcaseAction.Copy:
                    var copy = _document.CopyTestcase(index, name);
                    var newRows = _document.Config.Rows.Where(r => !r.IsBlank && WorkbookDocument.EffectiveIndex(r) == copy.NewIndex).ToList();
                    var templateNote = TestcasePanelCopyTemplate
                        ? GiveOwnTemplates(newRows) + GiveOwnQueryFiles(newRows)
                        : "sharing the original template and query files";
                    var notes = new List<string>
                    {
                        $"Copied testcase {index} as {copy.NewIndex} ({copy.Steps} {(copy.Steps == 1 ? "step" : "steps")}, {copy.DataRows} data {(copy.DataRows == 1 ? "row" : "rows")}, {copy.ColumnsAdded} new data {(copy.ColumnsAdded == 1 ? "column" : "columns")})"
                    };
                    notes.Add(templateNote);
                    if (copy.AbsoluteReferences > 0) notes.Add($"{copy.AbsoluteReferences} <From_…_N> row {(copy.AbsoluteReferences == 1 ? "reference" : "references")} in the copy still point at the original rows — check {(copy.AbsoluteReferences == 1 ? "it" : "them")}");
                    FinishTestcaseAction(copy.NewIndex, string.Join("; ", notes) + ".");
                    break;
                case TestcaseAction.Delete:
                    var next = Testcases.Where(t => t.Index != index).OrderBy(t => Math.Abs(t.Index - index)).FirstOrDefault()?.Index;
                    var removed = _document.DeleteTestcase(index);
                    FinishTestcaseAction(next, $"Deleted testcase {index}: {removed.Steps} {(removed.Steps == 1 ? "step" : "steps")} and {removed.DataRows} data {(removed.DataRows == 1 ? "row" : "rows")}." + (removed.ColumnsRemoved > 0 ? $" Removed {removed.ColumnsRemoved} data {(removed.ColumnsRemoved == 1 ? "column" : "columns")} of its steps from the request and response sheets." : string.Empty) + (removed.LeftoverColumns > 0 ? $" {removed.LeftoverColumns} data {(removed.LeftoverColumns == 1 ? "column" : "columns")} stay because another testcase has a step with the same name." : string.Empty));
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            DiscardLastRecord();
            ShowStatus(ex.Message, isError: true);
        }
    }

    private void FinishTestcaseAction(int? selectIndex, string message)
    {
        TestcasePanelVisible = false;
        _testcaseAction = TestcaseAction.None;
        MarkChanged();
        LoadTestcases(selectIndex);
        ShowStatus(message, isError: false);
    }

    [RelayCommand]
    private void MoveTestcaseUp() => MoveTestcase(-1);

    [RelayCommand]
    private void MoveTestcaseDown() => MoveTestcase(1);

    private void MoveTestcase(int direction)
    {
        if (SelectedTestcase is not { } testcase) return;
        Record($"move testcase {testcase.Title} {(direction < 0 ? "up" : "down")}");
        try
        {
            var swappedWith = _document.MoveTestcase(testcase.Index, direction);
            // The moved testcase now carries the other's number, so follow it there.
            FinishTestcaseAction(swappedWith, $"Moved testcase {(direction < 0 ? "earlier" : "later")}: it is now number {swappedWith.ToString(CultureInfo.InvariantCulture)} and runs {(direction < 0 ? "before" : "after")} the one it swapped with. Run results are listed by testcase number.");
        }
        catch (InvalidOperationException ex)
        {
            DiscardLastRecord();
            ShowStatus(ex.Message, isError: true);
        }
    }

    // ---- Step order ------------------------------------------------------------------------

    [RelayCommand]
    private void MoveStepUp() => MoveStep(-1);

    [RelayCommand]
    private void MoveStepDown() => MoveStep(1);

    private void MoveStep(int direction) => MoveStepRow(SelectedStep, direction);

    /// <summary>Moves one step a place earlier (-1) or later (1) among the testcase's steps.</summary>
    public void MoveStepRow(StepRowViewModel? step, int direction)
    {
        if (step is null) return;
        var position = StepRows.IndexOf(step);
        Record($"move step {step.CurrentName} {(direction < 0 ? "up" : "down")}");
        if (!_document.MoveStep(step.Row, direction))
        {
            DiscardLastRecord();
            ShowStatus(direction < 0 ? "That step already runs first." : "That step already runs last.", isError: false);
            return;
        }
        StepsChanged();
        BuildSteps();
        SelectedStep = StepRows[Math.Clamp(position + direction, 0, StepRows.Count - 1)];
        ShowStatus("Moved the step. Steps run top to bottom.", isError: false);
    }
}
