using Avalonia.Controls;
using Avalonia.Layout;
using ClosedXML.Excel;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI;

public sealed partial class MainWindow
{
    private readonly StackPanel _bugItems = new() { Orientation = Orientation.Vertical, Margin = new Avalonia.Thickness(12) };

    /// <summary>The Bugs tab: cells tagged &lt;bug: …&gt; for the environment of the run. A failing one is a known bug; a passing one may be fixed, and offers to drop the tag.</summary>
    private void RefreshBugList()
    {
        _bugItems.Children.Clear();
        var bugs = (_selectedInstance is null ? [] : ScopedAssertions(_selectedInstance)).Where(a => a.KnownBug).ToList();
        var known = bugs.Count(b => b.Result == "KNOWN_BUG");
        _bugItems.Children.Add(DashboardStyle.Text(bugs.Count == 0
            ? "No known bugs in this run. Tag an expected-value cell with <bug: dev,test; description> to list its failure here instead of failing the test."
            : $"{known} known bug(s) and {bugs.Count - known} that may be fixed. Known bugs do not fail the test.", 12, false, DashboardStyle.Muted));
        foreach (var bug in bugs.OrderBy(b => b.Result == "KNOWN_BUG" ? 0 : 1).ThenBy(b => b.Testcase).ThenBy(b => b.ExcelRowNumber)) _bugItems.Children.Add(CreateBugRow(bug));
    }

    private Control CreateBugRow(AssertionInfo bug)
    {
        var fixedMaybe = bug.Result == "PASS";
        var lines = $"[{bug.Environment}] {bug.Testcase} / Data {bug.DataId} / row {bug.ExcelRowNumber}\n{bug.StepName} / {bug.ResponsePath}\nBug: {(bug.BugDescription.Length == 0 ? "(no description)" : bug.BugDescription)}\n"
            + (fixedMaybe ? $"The assertion passes now (value {bug.ActualValue}); the bug may be fixed." : $"Expected {bug.ExpectedValue}, actual {bug.ActualValue}" + (bug.FailureMessage.Length > 0 ? $"\n{bug.FailureMessage}" : string.Empty));
        var text = DashboardStyle.Text(lines, 12); text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var content = new StackPanel { Spacing = 6, Children = { DashboardStyle.Badge(fixedMaybe ? "May be fixed" : "Known bug"), text } };
        if (fixedMaybe)
        {
            var actions = new WrapPanel();
            actions.Children.Add(DashboardStyle.Action("Remove the tag", () => RemoveBugTag(bug, null)));
            var named = bug.BugEnvironments.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(e => e is not ("all" or "*"));
            if (named) actions.Children.Add(DashboardStyle.Action($"Remove {bug.Environment} from the tag", () => RemoveBugTag(bug, bug.Environment)));
            content.Children.Add(actions);
        }
        var card = DashboardStyle.Card(content, new Avalonia.Thickness(10)); card.Margin = new Avalonia.Thickness(0, 0, 0, 8); return card;
    }

    /// <summary>Writes the tag change into the response cell (after a backup, like Apply actual values) and drops the entry from the list.</summary>
    private void RemoveBugTag(AssertionInfo bug, string? environment)
    {
        if (_selectedInstance is null || bug.ExcelRowNumber <= 0 || bug.ExpectedValueColumn <= 0) return;
        try
        {
            DataEngineBackup.CreateBackup(_selectedInstance.WorkbookPath);
            using var workbook = new XLWorkbook(_selectedInstance.WorkbookPath);
            var cell = workbook.Worksheet("response").Cell(bug.ExcelRowNumber, bug.ExpectedValueColumn);
            var before = cell.GetString();
            var after = BugTag.Without(before, environment);
            if (after == before) { _details.Text = "The cell no longer has that bug tag."; return; }
            cell.Value = after;
            workbook.Save();
            NotifyEditorWorkbookChanged(_selectedInstance);
            bug.KnownBug = false;
            _details.Text = environment is null ? $"Removed the bug tag from {bug.StepName}.{bug.ResponsePath} (row {bug.ExcelRowNumber})." : $"Removed {environment} from the bug tag of {bug.StepName}.{bug.ResponsePath} (row {bug.ExcelRowNumber}).";
            RefreshBugList();
        }
        catch (Exception ex) { _details.Text = $"Workbook update failed: {ex.Message}"; }
    }
}
