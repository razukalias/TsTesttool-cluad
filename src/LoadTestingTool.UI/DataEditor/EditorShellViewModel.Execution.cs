using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

// Row-execution boxes: one global set above the tree and an optional override per instance.
public sealed partial class EditorShellViewModel
{
    private ExecutionSettings? _execution;

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
