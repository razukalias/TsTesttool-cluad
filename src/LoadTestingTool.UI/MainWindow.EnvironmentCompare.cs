using Avalonia.Controls;
using Avalonia.Layout;

namespace LoadTestingTool.UI;

public sealed partial class MainWindow
{
    private readonly StackPanel _environmentCompareItems = new() { Orientation = Orientation.Vertical, Margin = new Avalonia.Thickness(12) };
    private readonly ComboBox _compareRunBox = new() { MinWidth = 280, Tag = "compare-run" };
    private bool _compareRunRefreshing;
    private string _compareRunId = string.Empty;
    private readonly Dictionary<string, (List<DashboardRequest> Requests, List<AssertionInfo> Assertions)> _compareRunCache = new(StringComparer.OrdinalIgnoreCase);

    private static Func<DashboardRequest, string?> AssertionTextFor(IReadOnlyList<AssertionInfo> assertions) => request =>
    {
        var failed = assertions.FirstOrDefault(a => a.Result == "FAIL" && a.StepName.Equals(request.StepName, StringComparison.OrdinalIgnoreCase) && a.DataId.Equals(request.DataId, StringComparison.OrdinalIgnoreCase)
            && a.Testcase.Equals(request.Testcase, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(a.Environment) ? "default" : a.Environment).Equals(string.IsNullOrWhiteSpace(request.Environment) ? "default" : request.Environment, StringComparison.OrdinalIgnoreCase));
        return failed is null ? null : $"{failed.ResponsePath} {failed.AssertionVerb}: expected {failed.ExpectedValue}, got {failed.ActualValue}";
    };

    /// <summary>The steps and assertions of another saved run, read from its event files without touching the instance that is shown (kept until the instance changes its runs).</summary>
    private (List<DashboardRequest> Requests, List<AssertionInfo> Assertions)? LoadOtherRun(InstanceInfo item, SavedRun run)
    {
        var key = $"{item.Name}|{run.RunId}";
        if (_compareRunCache.TryGetValue(key, out var cached)) return cached;
        var copy = new InstanceInfo { Name = item.Name, FolderPath = item.FolderPath, WorkbookPath = item.WorkbookPath, CurrentRunId = run.RunId, CurrentRunFolder = run.Folder };
        ReadEvents(copy);
        var loaded = (copy.Requests.ToList(), copy.Assertions.ToList());
        _compareRunCache[key] = loaded;
        return loaded;
    }

    private bool MatchesCompareFilter(DashboardRequest request) => MatchesOverviewStep(request, applyExclude: true, forComparison: true);

    /// <summary>The Environments tab: how the steps of the run ended in each environment, which differ, the failures grouped by error, and how the run differs from an earlier one.</summary>
    private void RefreshEnvironmentCompare(IReadOnlyList<DashboardRequest> allRequests, IReadOnlyList<AssertionInfo> allAssertions)
    {
        if (_compareRunBox.Parent is Panel old) old.Children.Remove(_compareRunBox);
        _environmentCompareItems.Children.Clear();
        void Add(Control control) => _environmentCompareItems.Children.Add(control);
        if (allRequests.Count == 0) { Add(DashboardStyle.Text("No steps have finished yet.", 12, false, DashboardStyle.Muted)); return; }

        // the Overview's step filters (include box, exclude box, testcase) also narrow this tab; status and environment do not, as the tab compares environments
        var requests = allRequests.Where(MatchesCompareFilter).ToList();
        var shown = requests.Select(r => (r.Testcase, r.DataId, r.StepName)).ToHashSet();
        var assertions = allAssertions.Where(a => shown.Contains((a.Testcase, a.DataId, a.StepName))).ToList();
        if (requests.Count < allRequests.Count) Add(DashboardStyle.Text($"The step filters of the Overview narrow this tab: {requests.Count} of {allRequests.Count} finished steps are considered.", 11, false, DashboardStyle.Muted));
        AddRunPicker();

        var report = EnvironmentReport.Build(requests, AssertionTextFor(allAssertions), assertions);
        if (report.Environments.Count < 2)
        {
            Add(DashboardStyle.Text($"This run used one environment ({string.Join(", ", report.Environments)}), so there is nothing to compare between environments. Run the rows in two or more environments to see matches and differences.", 12, false, DashboardStyle.Muted));
        }
        else
        {
            var tiles = new Avalonia.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Avalonia.Thickness(0, 4, 0, 0) };
            tiles.Children.Add(DashboardStyle.Tile("Compared", report.Compared.Count.ToString("N0"), DashboardStyle.Blue, width: double.NaN));
            tiles.Children.Add(DashboardStyle.Tile("Match", report.Matching.ToString("N0"), DashboardStyle.Green, width: double.NaN));
            tiles.Children.Add(DashboardStyle.Tile("All passed", report.PassedEverywhere.ToString("N0"), width: double.NaN));
            tiles.Children.Add(DashboardStyle.Tile("All failed", report.FailedEverywhere.ToString("N0"), report.FailedEverywhere > 0 ? DashboardStyle.Red : null, width: double.NaN));
            tiles.Children.Add(DashboardStyle.Tile("Differ", report.Differing.Count.ToString("N0"), report.Differing.Count > 0 ? DashboardStyle.Brush("#FFB36B") : null, width: double.NaN));
            Add(tiles);
            var summary = DashboardStyle.Text($"{report.Compared.Count} steps compared across {string.Join(", ", report.Environments)}: {report.Matching} match ({report.PassedEverywhere} passed everywhere, {report.FailedEverywhere} failed everywhere with the same error), {report.Differing.Count} differ.", 12);
            summary.TextWrapping = Avalonia.Media.TextWrapping.Wrap; summary.Margin = new Avalonia.Thickness(0, 0, 0, 8); summary.Tag = "environment-summary";
            Add(summary);
            if (report.Differing.Count > 0)
            {
                Add(DashboardStyle.Section("Differ between environments"));
                foreach (var step in report.Differing) Add(CreateStepComparisonCard(step));
            }
            if (report.ValueDifferences.Count > 0)
            {
                _environmentCompareItems.Children.Add(DashboardStyle.Section($"Response values that differ ({report.ValueDifferences.Count})", "between environments"));
                foreach (var difference in report.ValueDifferences.Take(100)) Add(CreateValueDifferenceCard(difference));
                if (report.ValueDifferences.Count > 100) Add(DashboardStyle.Text($"+ {report.ValueDifferences.Count - 100} more", 11, false, DashboardStyle.Muted));
            }
        }
        AddErrorGroups(report);
        AddRunChanges(requests, allAssertions);
    }

    private void AddRunPicker()
    {
        if (_selectedInstance is not { } item) return;
        var runs = SavedRuns(item).Where(r => !r.RunId.Equals(item.CurrentRunId, StringComparison.OrdinalIgnoreCase)).ToList();
        _compareRunRefreshing = true;
        var labels = new List<string> { "(none)" }.Concat(runs.Select(r => r.Label)).ToList();
        if (_compareRunBox.ItemsSource is not List<string> current || !current.SequenceEqual(labels)) _compareRunBox.ItemsSource = labels;
        var at = runs.FindIndex(r => r.RunId.Equals(_compareRunId, StringComparison.OrdinalIgnoreCase));
        _compareRunBox.SelectedIndex = at < 0 ? 0 : at + 1;
        if (at < 0) _compareRunId = string.Empty;
        _compareRunBox.IsEnabled = runs.Count > 0;
        _compareRunRefreshing = false;
        _compareRunBox.SelectionChanged -= OnCompareRunChosen;
        _compareRunBox.SelectionChanged += OnCompareRunChosen;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        line.Children.Add(DashboardStyle.Text("Compare with an earlier run", color: DashboardStyle.Muted));
        line.Children.Add(_compareRunBox);
        if (runs.Count == 0) line.Children.Add(DashboardStyle.Text("There is no other saved run.", 11, false, DashboardStyle.Muted));
        _environmentCompareItems.Children.Add(line);
    }

    private void OnCompareRunChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_compareRunRefreshing || _selectedInstance is not { } item) return;
        var runs = SavedRuns(item).Where(r => !r.RunId.Equals(item.CurrentRunId, StringComparison.OrdinalIgnoreCase)).ToList();
        var index = _compareRunBox.SelectedIndex - 1;
        _compareRunId = index >= 0 && index < runs.Count ? runs[index].RunId : string.Empty;
        RefreshDashboardDetails(true);
    }

    /// <summary>"Compared with the run from …": steps that got worse, got fixed, fail differently, or appeared or disappeared since the earlier run.</summary>
    private void AddRunChanges(IReadOnlyList<DashboardRequest> requests, IReadOnlyList<AssertionInfo> assertions)
    {
        if (_compareRunId.Length == 0 || _selectedInstance is not { } item) return;
        var run = SavedRuns(item).FirstOrDefault(r => r.RunId.Equals(_compareRunId, StringComparison.OrdinalIgnoreCase));
        if (run is null || LoadOtherRun(item, run) is not { } other) return;
        var earlier = other.Requests.Where(MatchesCompareFilter).ToList();
        var changes = RunComparer.Compare(earlier, requests, AssertionTextFor(other.Assertions), AssertionTextFor(assertions));
        _environmentCompareItems.Children.Add(new TextBlock { Text = $"Compared with the run from {run.When:yyyy-MM-dd HH:mm:ss}", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 12, 0, 4), Tag = "run-changes-title" });
        string Count(RunChange change) => changes.Count(c => c.Change == change).ToString();
        var summary = DashboardStyle.Text(changes.Count == 0 ? "Every step ended the same way in both runs." : $"{Count(RunChange.Regressed)} got worse, {Count(RunChange.Fixed)} got fixed, {Count(RunChange.ErrorChanged)} fail with a different error, {Count(RunChange.New)} new, {Count(RunChange.Gone)} not run this time.", 12);
        summary.TextWrapping = Avalonia.Media.TextWrapping.Wrap; summary.Margin = new Avalonia.Thickness(0, 0, 0, 6); summary.Tag = "run-changes-summary";
        _environmentCompareItems.Children.Add(summary);
        foreach (var kind in new[] { RunChange.Regressed, RunChange.Fixed, RunChange.ErrorChanged, RunChange.New, RunChange.Gone })
        {
            var of = changes.Where(c => c.Change == kind).ToList();
            if (of.Count == 0) continue;
            var label = kind switch { RunChange.Regressed => "Got worse", RunChange.Fixed => "Got fixed", RunChange.ErrorChanged => "Fail with a different error", RunChange.New => "New in this run", _ => "Not run this time" };
            var lines = new StackPanel { Spacing = 3 };
            foreach (var change in of.Take(50))
            {
                var text = DashboardStyle.Text($"{change.Testcase} / Data {change.DataId} / {change.StepName}  [{change.Environment}]\n   was: {change.Before}\n   now: {change.After}", 11);
                text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
                lines.Children.Add(text);
            }
            if (of.Count > 50) lines.Children.Add(DashboardStyle.Text($"+ {of.Count - 50} more", 11, false, DashboardStyle.Muted));
            var card = DashboardStyle.Card(new StackPanel { Spacing = 6, Children = { DashboardStyle.Badge($"{label} ({of.Count})"), lines } }, new Avalonia.Thickness(10)); card.Margin = new Avalonia.Thickness(0, 0, 0, 8); card.Tag = "run-changes";
            _environmentCompareItems.Children.Add(card);
        }
    }

    private Control CreateValueDifferenceCard(ValueDifference difference)
    {
        var lines = $"{difference.Testcase} / Data {difference.DataId} / {difference.StepName}.{difference.Path}\n" + string.Join("\n", difference.Values.Select(v => $"{v.Environment,-10} {(v.Actual.Length == 0 ? "(empty)" : v.Actual)}"));
        var text = DashboardStyle.Text(lines, 12); text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var card = DashboardStyle.Card(text, new Avalonia.Thickness(10)); card.Margin = new Avalonia.Thickness(0, 0, 0, 8); card.Tag = "value-differ";
        return card;
    }

    private Control CreateStepComparisonCard(StepComparison step)
    {
        var lines = $"{step.Testcase} / Data {step.DataId} / {step.StepName}\n" + string.Join("\n", step.Outcomes.Select(o => $"{o.Environment,-10} {o.Result}" + (o.Error.Length > 0 ? $" — {o.Error}" : string.Empty)));
        var text = DashboardStyle.Text(lines, 12); text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        var badge = DashboardStyle.Badge(step.Kind == StepMatch.ResultDiffers ? "Result differs" : "Different errors");
        var card = DashboardStyle.Card(new StackPanel { Spacing = 6, Children = { badge, text } }, new Avalonia.Thickness(10)); card.Margin = new Avalonia.Thickness(0, 0, 0, 8); card.Tag = "environment-differ";
        return card;
    }

    private void AddErrorGroups(EnvironmentReport report)
    {
        if (report.Groups.Count == 0) return;
        _environmentCompareItems.Children.Add(new TextBlock { Text = $"Failures grouped by error ({report.Groups.Count})", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 10, 0, 6) });
        foreach (var group in report.Groups)
        {
            var title = DashboardStyle.Text(group.Sample.Length > 300 ? group.Sample[..300] + "…" : group.Sample, 12, true); title.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            var where = DashboardStyle.Text($"{group.Steps} {(group.Steps == 1 ? "step" : "steps")} · {group.Occurrences.Count} {(group.Occurrences.Count == 1 ? "failure" : "failures")} · {string.Join(", ", group.Environments)}", 11, false, DashboardStyle.Muted);
            var list = new StackPanel { Spacing = 2 };
            foreach (var o in group.Occurrences.Take(15)) list.Children.Add(DashboardStyle.Text($"{o.Testcase} / Data {o.DataId} / {o.StepName}  [{o.Environment}]", 11));
            if (group.Occurrences.Count > 15) list.Children.Add(DashboardStyle.Text($"+ {group.Occurrences.Count - 15} more", 11, false, DashboardStyle.Muted));
            var card = DashboardStyle.Card(new StackPanel { Spacing = 6, Children = { title, where, list } }, new Avalonia.Thickness(10)); card.Margin = new Avalonia.Thickness(0, 0, 0, 8); card.Tag = "error-group";
            _environmentCompareItems.Children.Add(card);
        }
    }
}
