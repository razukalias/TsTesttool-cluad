using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

// The instance's environment drop-down and its "parallel" tick box.
public sealed partial class InstanceNode
{
    private IEnvironmentSettings? _settings;
    private bool _loadingEnvironments;

    public ObservableCollection<EnvironmentChoice> EnvironmentChoices { get; } = [];

    public bool HasEnvironments => EnvironmentChoices.Count > 0;

    /// <summary>Environments to run in when started from the tree. Unlike the choice of environments, the runner has only one central parallel flag, so a per-instance change applies to runs started from the editor.</summary>
    [ObservableProperty] private bool _runParallel;

    public IReadOnlyList<string> ChosenEnvironments => EnvironmentChoices.Where(c => c.IsChosen).Select(c => c.Name).ToList();

    /// <summary>The drop-down's caption: "All environments" or the chosen names.</summary>
    public string EnvironmentsText => EnvironmentChoices.Count == 0 ? string.Empty
        : ChosenEnvironments.Count == EnvironmentChoices.Count ? "All environments"
        : string.Join(", ", ChosenEnvironments);

    internal event EventHandler? EnvironmentsChanged;

    /// <summary>Raised with a message when a choice was refused.</summary>
    internal event EventHandler<string>? EnvironmentProblem;

    internal void LoadEnvironments(IEnvironmentSettings? settings)
    {
        _settings = settings;
        _loadingEnvironments = true;
        try
        {
            EnvironmentChoices.Clear();
            if (settings is null) return;
            var saved = settings.GetSelection(Name);
            foreach (var environment in settings.Environments)
                EnvironmentChoices.Add(new EnvironmentChoice(environment, OnChoiceChanged)
                {
                    IsChosen = saved.Count == 0 || saved.Contains(environment, StringComparer.OrdinalIgnoreCase)
                });
            if (EnvironmentChoices.Count > 0 && EnvironmentChoices.All(c => !c.IsChosen)) foreach (var c in EnvironmentChoices) c.SetSilently(true);
            RunParallel = settings.RunParallel;
        }
        finally { _loadingEnvironments = false; }
        OnPropertyChanged(nameof(HasEnvironments));
        OnPropertyChanged(nameof(EnvironmentsText));
        EnvironmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnChoiceChanged(EnvironmentChoice choice)
    {
        if (_loadingEnvironments || _settings is null) return;
        if (ChosenEnvironments.Count == 0)
        {
            choice.SetSilently(true);
            EnvironmentProblem?.Invoke(this, $"{Name} needs at least one environment.");
            return;
        }
        var problem = _settings.SaveSelections(new Dictionary<string, IReadOnlyList<string>> { [Name] = ChosenEnvironments });
        OnPropertyChanged(nameof(EnvironmentsText));
        if (problem is not null) EnvironmentProblem?.Invoke(this, problem);
        EnvironmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets the choices from outside (the global drop-down) without saving; the caller saves once for all instances.</summary>
    internal void SetEnvironmentSilently(string environment, bool chosen)
    {
        EnvironmentChoices.FirstOrDefault(c => string.Equals(c.Name, environment, StringComparison.OrdinalIgnoreCase))?.SetSilently(chosen);
        OnPropertyChanged(nameof(EnvironmentsText));
    }

    partial void OnRunParallelChanged(bool value)
    {
        if (!_loadingEnvironments) EnvironmentsChanged?.Invoke(this, EventArgs.Empty);
    }
}
