using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Rename, copy, reorder and delete for the selected testcase, and reorder for the selected step.
// Every action is an in-memory edit (one undo step) and is saved with the rest.
public sealed partial class DataEditorViewModel
{
    private enum TestcaseAction { None, Rename, Copy, Delete }

    private TestcaseAction _testcaseAction;

    [ObservableProperty] private bool _testcasePanelVisible;
    [ObservableProperty] private string _testcasePanelTitle = string.Empty;
    [ObservableProperty] private string _testcasePanelName = string.Empty;
    [ObservableProperty] private string _testcasePanelNote = string.Empty;
    [ObservableProperty] private string _testcasePanelConfirmText = "OK";
    [ObservableProperty] private bool _testcasePanelAsksName;
    [ObservableProperty] private bool _testcasePanelIsDelete;

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

    [RelayCommand]
    private void OpenCopyTestcase()
    {
        if (SelectedTestcase is not { } testcase) return;
        _testcaseAction = TestcaseAction.Copy;
        TestcasePanelTitle = $"COPY TESTCASE · {testcase.Title}";
        TestcasePanelName = testcase.Name + " copy";
        TestcasePanelConfirmText = "Copy";
        TestcasePanelNote = $"Copies its {testcase.Steps.Count} {(testcase.Steps.Count == 1 ? "step" : "steps")} (named Name + Copy, with their data columns) and its {testcase.RowCount} data {(testcase.RowCount == 1 ? "row" : "rows")} under the next free testcase number. The copy uses the same template files.";
        OpenTestcasePanel(asksName: true, isDelete: false);
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

    private void OpenTestcasePanel(bool asksName, bool isDelete)
    {
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
                    var notes = new List<string>
                    {
                        $"Copied testcase {index} as {copy.NewIndex} ({copy.Steps} {(copy.Steps == 1 ? "step" : "steps")}, {copy.DataRows} data {(copy.DataRows == 1 ? "row" : "rows")}, {copy.ColumnsAdded} new data {(copy.ColumnsAdded == 1 ? "column" : "columns")})"
                    };
                    if (copy.AbsoluteReferences > 0) notes.Add($"{copy.AbsoluteReferences} <From_…_N> row {(copy.AbsoluteReferences == 1 ? "reference" : "references")} in the copy still point at the original rows — check {(copy.AbsoluteReferences == 1 ? "it" : "them")}");
                    FinishTestcaseAction(copy.NewIndex, string.Join("; ", notes) + ".");
                    break;
                case TestcaseAction.Delete:
                    var next = Testcases.Where(t => t.Index != index).OrderBy(t => Math.Abs(t.Index - index)).FirstOrDefault()?.Index;
                    var removed = _document.DeleteTestcase(index);
                    FinishTestcaseAction(next, $"Deleted testcase {index}: {removed.Steps} {(removed.Steps == 1 ? "step" : "steps")} and {removed.DataRows} data {(removed.DataRows == 1 ? "row" : "rows")}." + (removed.LeftoverColumns > 0 ? $" {removed.LeftoverColumns} data {(removed.LeftoverColumns == 1 ? "column" : "columns")} of its steps remain in the sheets." : string.Empty));
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

    private void MoveStep(int direction)
    {
        if (SelectedStep is not { } step) return;
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
