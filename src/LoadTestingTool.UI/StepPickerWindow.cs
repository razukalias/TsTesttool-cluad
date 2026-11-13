using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LoadTestingTool.UI;

internal sealed class StepPickerWindow : Window
{
    public StepPickerWindow(string instanceName, IReadOnlyList<TestcaseOption> catalog, Action<int, string> selected)
    {
        Title = $"Edit test step • {instanceName}";
        Width = 620; Height = 620; MinWidth = 520; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tabs = new TabControl { ItemsSource = BuildItems(catalog).GroupBy(x => new { x.TestcaseIndex, x.TestcaseName }).Select(group => new TabItem
        {
            Header = $"TC {group.Key.TestcaseIndex} · {group.Key.TestcaseName}",
            Content = BuildStepPanel(group, selected)
        }).ToList(), MinHeight = 280 };
        var cancel = DashboardStyle.Action("Cancel", Close);
        var help = DashboardStyle.Action("Help", () => ContextHelpWindow.Show(this, "Edit step picker help", "Choose the testcase and step to edit. The wizard then loads the existing config row, including its StepConfig JSON. Changes are written back to the config sheet and a workbook backup is created."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { help, cancel } };
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Bottom); dock.Children.Add(buttons);
        dock.Children.Add(new StackPanel { Spacing = 8, Children = { DashboardStyle.Text("Choose a step to edit", 18, true), DashboardStyle.Text("All steps are grouped into testcase tabs. Disabled steps remain visible and editable.", 12, false, DashboardStyle.Muted), tabs } });
        Content = new Border { Padding = new Thickness(18), Background = DashboardStyle.Panel, Child = dock };
    }

    private static IReadOnlyList<StepChoice> BuildItems(IReadOnlyList<TestcaseOption> catalog) => catalog
        .SelectMany(c => c.Steps.Select(s => new StepChoice(c.Index, c.Name, s.Name, s.Type, s.Enabled)))
        .ToList();

    private StackPanel BuildStepPanel(IEnumerable<StepChoice> choices, Action<int, string> selected)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var choice in choices)
        {
            var edit = DashboardStyle.Action($"Edit  {choice.StepName}  [{choice.Type}]", () => { selected(choice.TestcaseIndex, choice.StepName); Close(); }, "primary");
            panel.Children.Add(new StackPanel { Spacing = 3, Children = { edit, DashboardStyle.Text(choice.Enabled ? "Enabled step" : "Disabled step — can be enabled in the editor", 11, false, choice.Enabled ? DashboardStyle.Muted : DashboardStyle.Brush("#F6A623")) } });
        }
        return panel;
    }

    private sealed record StepChoice(int TestcaseIndex, string TestcaseName, string StepName, string Type, bool Enabled)
    {
        public string Label => $"TC {TestcaseIndex} • {TestcaseName}  /  {StepName}  [{Type}]" + (Enabled ? "" : "  (disabled)");
        public override string ToString() => Label;
    }
}
