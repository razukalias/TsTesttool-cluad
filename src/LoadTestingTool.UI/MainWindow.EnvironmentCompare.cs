using Avalonia.Controls;
using Avalonia.Layout;

namespace LoadTestingTool.UI;

public sealed partial class MainWindow
{
    private readonly StackPanel _environmentCompareItems = new() { Orientation = Orientation.Vertical, Margin = new Avalonia.Thickness(12) };

    /// <summary>The Environments tab: how the steps of the run ended in each environment, which differ, and the failures grouped by error.</summary>
    private void RefreshEnvironmentCompare(IReadOnlyList<DashboardRequest> requests, IReadOnlyList<AssertionInfo> assertions)
    {
        _environmentCompareItems.Children.Clear();
        string? AssertionText(DashboardRequest request)
        {
            var failed = assertions.FirstOrDefault(a => a.Result == "FAIL" && a.StepName.Equals(request.StepName, StringComparison.OrdinalIgnoreCase) && a.DataId.Equals(request.DataId, StringComparison.OrdinalIgnoreCase)
                && a.Testcase.Equals(request.Testcase, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(a.Environment) ? "default" : a.Environment).Equals(string.IsNullOrWhiteSpace(request.Environment) ? "default" : request.Environment, StringComparison.OrdinalIgnoreCase));
            return failed is null ? null : $"{failed.ResponsePath} {failed.AssertionVerb}: expected {failed.ExpectedValue}, got {failed.ActualValue}";
        }
        var report = EnvironmentReport.Build(requests, AssertionText);
        void Add(Control control) => _environmentCompareItems.Children.Add(control);
        if (requests.Count == 0) { Add(DashboardStyle.Text("No steps have finished yet.", 12, false, DashboardStyle.Muted)); return; }
        if (report.Environments.Count < 2)
        {
            Add(DashboardStyle.Text($"This run used one environment ({string.Join(", ", report.Environments)}), so there is nothing to compare. Run the rows in two or more environments to see matches and differences.", 12, false, DashboardStyle.Muted));
            AddErrorGroups(report);
            return;
        }
        var summary = DashboardStyle.Text($"{report.Compared.Count} steps compared across {string.Join(", ", report.Environments)}: {report.Matching} match ({report.PassedEverywhere} passed everywhere, {report.FailedEverywhere} failed everywhere with the same error), {report.Differing.Count} differ.", 12);
        summary.TextWrapping = Avalonia.Media.TextWrapping.Wrap; summary.Margin = new Avalonia.Thickness(0, 0, 0, 8); summary.Tag = "environment-summary";
        Add(summary);
        if (report.Differing.Count > 0)
        {
            Add(DashboardStyle.Text("Differ between environments", 13, true));
            foreach (var step in report.Differing) Add(CreateStepComparisonCard(step));
        }
        AddErrorGroups(report);
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
