namespace LoadTestingTool.UI.DataEditor;

// Status icons in the tree: the latest run of each instance, rolled up to testcases and steps.
public sealed partial class EditorShellViewModel
{
    private readonly HashSet<InstanceNode> _statusWired = [];

    /// <summary>True while any instance is running.</summary>
    public bool AnyRunning => Instances.Any(i => i.Run.IsRunning);

    /// <summary>Reads the hosts's latest run state into the tree. Cheap enough to call every second.</summary>
    public void RefreshRunStatus()
    {
        if (_runDetails is not { } details) return;
        foreach (var instance in Instances)
        {
            WireStale(instance);
            var snapshot = details.GetStatus(instance.Name);
            instance.Run.Apply(snapshot.Outcomes, snapshot.Running);
            if (instance.Editor is { } editor && editor.IsRunning != snapshot.Running) editor.IsRunning = snapshot.Running;
            var byTestcase = snapshot.Outcomes.ToLookup(o => o.Testcase, StringComparer.OrdinalIgnoreCase);
            foreach (var testcase in instance.Testcases)
            {
                testcase.InstanceRun = instance.Run;
                var own = byTestcase[testcase.Name].ToList();
                testcase.Run.Apply(own, false);
                var bySteps = own.ToLookup(o => o.Step, StringComparer.OrdinalIgnoreCase);
                foreach (var step in testcase.StepNodes) step.Run.Apply(bySteps[step.Name].ToList(), false);
            }
        }
        OnPropertyChanged(nameof(AnyRunning));
        OnPropertyChanged(nameof(CanRunTicked));
    }

    /// <summary>Run ticked is available when something is ticked and none of the ticked instances is already running.</summary>
    public bool CanRunTicked => HasTicked && !Instances.Any(i => i.Run.IsRunning && i.Editor is { } e && e.TickedTestcaseCount > 0);

    private void WireStale(InstanceNode instance)
    {
        if (instance.Editor is not { } editor || !_statusWired.Add(instance)) return;
        editor.StepEdited += (testcase, step) =>
        {
            instance.Testcases.FirstOrDefault(t => t.Name.Equals(testcase, StringComparison.OrdinalIgnoreCase))
                ?.StepNodes.FirstOrDefault(s => s.Name.Equals(step, StringComparison.OrdinalIgnoreCase))?.Run.MarkStale();
        };
    }

    /// <summary>A status icon was clicked: shows the matching detail and returns the tab that should come to the front.</summary>
    public string? OpenStatus(object node)
    {
        if (_runDetails is not { } details) return null;
        switch (node)
        {
            case InstanceNode instance:
                details.Focus(instance.Name, new RunFocus(string.Empty, 0, null, instance.Run.State));
                return TabFor(instance.Run.State);
            case TestcaseItem testcase when InstanceFor(testcase) is { } owner:
                details.Focus(owner.Name, new RunFocus(testcase.Name, testcase.Index, null, testcase.Run.State));
                return TabFor(testcase.Run.State);
            case StepNode step when InstanceFor(step) is { } owner:
                details.Focus(owner.Name, new RunFocus(step.Owner.Name, step.Owner.Index, step.Name, step.Run.State));
                return TabFor(step.Run.State);
        }
        return null;
    }

    private static string TabFor(RunState state) => state switch
    {
        RunState.AssertionFailed => "Assertions",
        RunState.ValidationError => "Workbook",
        _ => "Overview"
    };

    /// <summary>Another part of the app rewrote an instance's workbook; reloads it in the editor when that is safe.</summary>
    public void ReloadAfterExternalChange(string instanceName, string reason = "by an assertion fix")
        => Instances.FirstOrDefault(i => i.Name == instanceName)?.Editor?.ReloadAfterExternalChange(reason);

    /// <summary>Why an instance's workbook cannot be replaced from outside right now (it is running, or open with unsaved edits), or null when it can.</summary>
    public string? ReplaceBlockedReason(string instanceName)
    {
        var node = Instances.FirstOrDefault(i => i.Name == instanceName);
        if (node?.Editor is { IsRunning: true }) return "it is running";
        if (node?.Editor is { IsDirty: true }) return "it is open in the editor with unsaved edits — save or reload it first";
        return null;
    }

    /// <summary>The stop button beside a running instance.</summary>
    public void StopInstance(InstanceNode instance) => _runDetails?.Stop(instance.Name);
}
