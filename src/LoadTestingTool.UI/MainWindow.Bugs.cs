using Avalonia.Controls;
using Avalonia.Layout;
using ClosedXML.Excel;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI;

public sealed partial class MainWindow
{
    private readonly StackPanel _bugItems = new() { Orientation = Orientation.Vertical, Margin = new Avalonia.Thickness(12) };
    private readonly StackPanel _bugStats = new() { Orientation = Orientation.Vertical };
    private readonly StackPanel _bugList = new() { Orientation = Orientation.Vertical };
    private readonly HashSet<string> _bugOpen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _bugSeen = new(StringComparer.OrdinalIgnoreCase);
    private (string Instance, string Testcase, string? Step)? _bugFilter;
    private TabItem _bugsTab = null!;
    private TextBlock _bugStatsNote = null!;
    private List<BugStatNode>? _bugStatRoots;
    private bool _bugScanning;

    /// <summary>Reads every instance's workbook (off the UI thread) and shows the bug tags per instance, testcase and step. Runs at start and when Refresh is pressed.</summary>
    private async void RefreshBugStatistics()
    {
        if (_bugScanning) return;
        _bugScanning = true;
        _bugStatsNote?.SetCurrentValue(TextBlock.TextProperty, "Reading the workbooks…");
        try
        {
            var sources = _instances.Select(i => (i.Name, i.WorkbookPath)).ToList();
            var roots = await Task.Run(() => sources.Select(x => BugStatistics.Build(x.Name, BugStatistics.Scan(x.WorkbookPath))).ToList());
            _bugStatRoots = roots;
            foreach (var root in roots) if (_bugSeen.Add(root.Name) && root.Count > 0) _bugOpen.Add(root.Name);   // an instance with bugs starts open, once
        }
        finally { _bugScanning = false; }
        RefreshBugList();
    }

    /// <summary>The Bugs tab. Top: how many bug tags each instance, testcase and step holds (all instances, read from the workbooks). Below: the bugs of the selected instance's run, optionally narrowed by a count button.</summary>
    private void RefreshBugList()
    {
        if (_bugItems.Children.Count == 0)
        {
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(DashboardStyle.Text("STATISTICS · BUG TAGS IN ALL INSTANCES (no run needed)", 11, true, DashboardStyle.Muted));
            var refresh = DashboardStyle.Action("↻ Refresh", RefreshBugStatistics);
            refresh.Margin = new Avalonia.Thickness(0); refresh.Padding = new Avalonia.Thickness(10, 4); refresh.Tag = "bug-refresh";
            ToolTip.SetTip(refresh, "Read the workbooks again, so bug tags you added or removed show here");
            Grid.SetColumn(refresh, 1); header.Children.Add(refresh);
            _bugStatsNote = DashboardStyle.Text("", 12, false, DashboardStyle.Muted);
            _bugItems.Children.Add(header);
            _bugItems.Children.Add(new Border { Margin = new Avalonia.Thickness(0, 6, 0, 14), Child = new StackPanel { Children = { _bugStatsNote, _bugStats } } });
            _bugItems.Children.Add(DashboardStyle.Text("KNOWN BUGS OF THIS RUN · SELECTED INSTANCE", 11, true, DashboardStyle.Muted));
            _bugItems.Children.Add(new Border { Margin = new Avalonia.Thickness(0, 6, 0, 0), Child = _bugList });
        }
        if (!_bugScanning && (_bugStatRoots is null || _bugStatRoots.Count != _instances.Count)) RefreshBugStatistics();   // first time, and when instances were added or removed
        _bugStats.Children.Clear();
        _bugList.Children.Clear();
        var all = _selectedInstance is null ? [] : ScopedAssertions(_selectedInstance).ToList();
        var bugs = all.Where(a => a.KnownBug).ToList();

        var total = _bugStatRoots?.Sum(r => r.Count) ?? 0;
        if (_bugsTab is not null) _bugsTab.Header = _bugStatRoots is null ? "Bugs" : $"Bugs ({total})";
        if (_bugStatRoots is not null) _bugStatsNote.Text = _bugStatRoots.Count == 0 ? "No instances." : total == 0 ? "No bug tags in any instance." : $"{total} bug tag(s) in {_bugStatRoots.Count(r => r.Count > 0)} of {_bugStatRoots.Count} instance(s).";
        foreach (var root in (_bugStatRoots ?? []).Where(r => r.Count > 0))   // only instances that hold bug tags
        {
            var open = _bugOpen.Contains(root.Name);
            _bugStats.Children.Add(StatRow(root, 0, open));
            if (!open) continue;
            foreach (var testcase in root.Children.Where(c => c.Count > 0))
            {
                var key = root.Name + "/" + testcase.Name;
                var testcaseOpen = _bugOpen.Contains(key);
                _bugStats.Children.Add(StatRow(testcase, 1, testcaseOpen));
                if (testcaseOpen) foreach (var step in testcase.Children.Where(c => c.Count > 0)) _bugStats.Children.Add(StatRow(step, 2, false));
            }
        }

        if (_bugFilter is { } filter)
        {
            bugs = bugs.Where(b => b.Testcase == filter.Testcase && (filter.Step is null || b.StepName == filter.Step)).ToList();
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            bar.Children.Add(DashboardStyle.Text($"Showing {filter.Testcase}" + (filter.Step is null ? "" : $" / {filter.Step}"), 12, false, DashboardStyle.Muted));
            bar.Children.Add(DashboardStyle.Action("Show all", () => { _bugFilter = null; RefreshBugList(); }));
            _bugList.Children.Add(bar);
        }
        _bugList.Children.Add(DashboardStyle.Text(bugs.Count == 0
            ? "No known bugs in this run. Tag an expected-value cell with <bug: dev,test; description> to list its failure here instead of failing the test."
            : $"{bugs.Count(b => b.Result == "KNOWN_BUG")} known bug(s) and {bugs.Count(b => b.Result != "KNOWN_BUG")} that may be fixed. Known bugs do not fail the test.", 12, false, DashboardStyle.Muted));
        foreach (var bug in bugs.OrderBy(b => b.Result == "KNOWN_BUG" ? 0 : 1).ThenBy(b => b.Testcase).ThenBy(b => b.ExcelRowNumber)) _bugList.Children.Add(CreateBugRow(bug));
    }

    /// <summary>One statistics line: kind, name (instances and testcases open and close), and a "⚑ n" button that selects the instance and narrows the list below. No button when the item has no bug tags.</summary>
    private Control StatRow(BugStatNode node, int level, bool open)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,Auto"), Margin = new Avalonia.Thickness(level * 18, 2, 0, 2), Tag = "bug-stat-" + node.Kind };
        grid.Children.Add(DashboardStyle.Text(node.Kind, 11, false, DashboardStyle.Muted));
        Control name;
        if (node.Kind != "Step")
        {
            var key = node.Kind == "Instance" ? node.Name : node.Instance + "/" + node.Name;
            var toggle = new Button { Content = (open ? "▾ " : "▸ ") + node.Name, Padding = new Avalonia.Thickness(0), Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0), FontSize = 12, Tag = "bug-stat-toggle" };
            toggle.Click += (_, _) => { if (!_bugOpen.Remove(key)) _bugOpen.Add(key); RefreshBugList(); };
            name = toggle;
        }
        else name = DashboardStyle.Text(node.Name, 12, false, node.Count == 0 ? DashboardStyle.Muted : null);
        Grid.SetColumn(name, 1); grid.Children.Add(name);
        if (node.Count > 0)
        {
            var button = new Button { Content = $"⚑ {node.Count}", Padding = new Avalonia.Thickness(9, 3), FontSize = 12, Tag = "bug-count", MinHeight = 0 };
            ToolTip.SetTip(button, "Select this instance and show only its known bugs of this " + node.Kind.ToLowerInvariant());
            button.Click += (_, _) =>
            {
                if (_instances.FirstOrDefault(i => i.Name == node.Instance) is { } instance && !ReferenceEquals(instance, _selectedInstance)) { _selectedInstance = instance; _instanceList.SelectedItem = instance; ShowDetails(instance); }
                _bugFilter = node.Testcase is null ? null : (node.Instance, node.Testcase, node.Step);
                RefreshBugList();
            };
            Grid.SetColumn(button, 2); grid.Children.Add(button);
        }
        return grid;
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
