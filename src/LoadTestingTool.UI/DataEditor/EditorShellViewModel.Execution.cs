using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

// Row-execution boxes: one global set above the tree and an optional override per instance.
public sealed partial class EditorShellViewModel
{
    private ExecutionSettings? _execution;
    private IRunDetails? _runDetails;

    /// <summary>Supplies the dashboard's run views (overview, summary, assertions, artifacts); set by whoever hosts the editor.</summary>
    public IRunDetails? RunDetails
    {
        get => _runDetails;
        set { _runDetails = value; OnPropertyChanged(); }
    }

    /// <summary>Points the run views at the instance of the selected node.</summary>
    public void ShowRunDetails()
    {
        if (_runDetails is not { } details || InstanceFor(SelectedNode) is not { } instance) return;
        RunScope? scope = SelectedNode switch
        {
            TestcaseItem testcase => new RunScope(testcase.Name, testcase.Index, null),
            StepNode step => new RunScope(step.Owner.Name, step.Owner.Index, step.Name),
            _ => null
        };
        details.Select(instance.Name, scope);
    }

    /// <summary>The global row-execution boxes; null while the host supplies no central settings.</summary>
    public ExecutionSettings? Execution
    {
        get => _execution;
        private set
        {
            _execution = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasExecution));
        }
    }

    public bool HasExecution => _execution is not null;

    private void LoadExecution()
    {
        if (_environmentSettings is not { } settings) { Execution = null; return; }
        var global = new ExecutionSettings(settings, null, null);
        global.Problem += (_, message) => Current?.Notify(message, true);
        Execution = global;
        foreach (var instance in Instances) AttachExecution(instance);
    }

    private void AttachExecution(InstanceNode node)
    {
        if (_environmentSettings is not { } settings || _execution is null) { node.Execution = null; return; }
        var own = new ExecutionSettings(settings, node.Name, _execution);
        own.Problem += (_, message) => Current?.Notify(message, true);
        node.Execution = own;
    }
}
