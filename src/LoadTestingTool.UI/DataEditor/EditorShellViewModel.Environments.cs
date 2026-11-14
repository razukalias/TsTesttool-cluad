using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

// The global environment drop-down and parallel tick box, which set every instance at once.
public sealed partial class EditorShellViewModel
{
    private IEnvironmentSettings? _environmentSettings;
    private bool _syncingGlobal;

    /// <summary>The central settings; set by whoever hosts the editor. Without it the environment controls stay hidden.</summary>
    public IEnvironmentSettings? EnvironmentSettings
    {
        get => _environmentSettings;
        set
        {
            _environmentSettings = value;
            foreach (var instance in Instances)
            {
                instance.LoadEnvironments(value);
                instance.EnvironmentsChanged -= OnInstanceEnvironmentsChanged;
                instance.EnvironmentProblem -= OnEnvironmentProblem;
                instance.EnvironmentsChanged += OnInstanceEnvironmentsChanged;
                instance.EnvironmentProblem += OnEnvironmentProblem;
            }
            foreach (var instance in Instances.Where(i => i.Editor is not null)) AttachSelection(instance);
            RebuildGlobalEnvironments();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEnvironments));
        }
    }

    /// <summary>Loads an instance's saved testcase/step selection into its tick boxes and saves later changes back.</summary>
    private void AttachSelection(InstanceNode node)
    {
        if (node.Editor is not { } editor || _environmentSettings is not { } settings) return;
        editor.SelectionSaver = (testcases, steps) => settings.SaveRunSelection(node.Name, testcases, steps);
        var (savedTestcases, savedSteps) = settings.GetRunSelection(node.Name);
        editor.ApplySavedSelection(savedTestcases, savedSteps);
    }

    public ObservableCollection<EnvironmentChoice> GlobalEnvironmentChoices { get; } = [];

    public bool HasEnvironments => GlobalEnvironmentChoices.Count > 0;

    /// <summary>The global drop-down's caption.</summary>
    public string GlobalEnvironmentsText
    {
        get
        {
            if (GlobalEnvironmentChoices.Count == 0) return string.Empty;
            var sets = Instances.Select(i => string.Join('|', i.ChosenEnvironments.Order(StringComparer.OrdinalIgnoreCase))).Distinct().Take(2).ToList();
            if (sets.Count > 1) return "Mixed";
            var chosen = Instances.FirstOrDefault()?.ChosenEnvironments ?? [];
            return chosen.Count == GlobalEnvironmentChoices.Count ? "All environments" : string.Join(", ", chosen);
        }
    }

    /// <summary>Ticked when every instance runs its environments in parallel.</summary>
    public bool GlobalRunParallel
    {
        get => Instances.Count > 0 && Instances.All(i => i.RunParallel);
        set
        {
            if (_syncingGlobal || _environmentSettings is null) return;
            var problem = _environmentSettings.SaveRunParallel(value);
            foreach (var instance in Instances) instance.RunParallel = value;
            OnPropertyChanged();
            if (problem is not null) ReportEnvironmentProblem(problem);
        }
    }

    private void RebuildGlobalEnvironments()
    {
        GlobalEnvironmentChoices.Clear();
        if (_environmentSettings is not null)
            foreach (var environment in _environmentSettings.Environments)
                GlobalEnvironmentChoices.Add(new EnvironmentChoice(environment, OnGlobalChoiceChanged));
        SyncGlobalEnvironments();
    }

    /// <summary>Ticks a global environment when every instance has it.</summary>
    private void SyncGlobalEnvironments()
    {
        _syncingGlobal = true;
        try
        {
            foreach (var choice in GlobalEnvironmentChoices)
                choice.SetSilently(Instances.Count > 0 && Instances.All(i => i.EnvironmentChoices.Any(c => c.IsChosen && string.Equals(c.Name, choice.Name, StringComparison.OrdinalIgnoreCase))));
        }
        finally { _syncingGlobal = false; }
        OnPropertyChanged(nameof(GlobalEnvironmentsText));
        OnPropertyChanged(nameof(GlobalRunParallel));
    }

    private void OnInstanceEnvironmentsChanged(object? sender, EventArgs e)
    {
        if (!_syncingGlobal) SyncGlobalEnvironments();
    }

    private void OnGlobalChoiceChanged(EnvironmentChoice choice)
    {
        if (_syncingGlobal || _environmentSettings is null) return;
        if (GlobalEnvironmentChoices.All(c => !c.IsChosen))
        {
            choice.SetSilently(true);
            ReportEnvironmentProblem("At least one environment must stay ticked.");
            return;
        }
        _syncingGlobal = true;
        try { foreach (var instance in Instances) instance.SetEnvironmentSilently(choice.Name, choice.IsChosen); }
        finally { _syncingGlobal = false; }
        // Every instance now follows the global choice, so save them all in one write.
        var chosen = GlobalEnvironmentChoices.Where(c => c.IsChosen).Select(c => c.Name).ToList();
        var problem = _environmentSettings.SaveSelections(Instances.ToDictionary(i => i.Name, i => (IReadOnlyList<string>)i.ChosenEnvironments.ToList()));
        OnPropertyChanged(nameof(GlobalEnvironmentsText));
        if (problem is not null) ReportEnvironmentProblem(problem);
        else (Current ?? Instances.FirstOrDefault(i => i.Editor is not null)?.Editor)?.Report($"All instances now run in: {string.Join(", ", chosen)}.", isError: false);
    }

    private void OnEnvironmentProblem(object? sender, string message) => ReportEnvironmentProblem(message);

    private void ReportEnvironmentProblem(string message)
        => (Current ?? Instances.FirstOrDefault(i => i.Editor is not null)?.Editor)?.Report(message, isError: true);
}
