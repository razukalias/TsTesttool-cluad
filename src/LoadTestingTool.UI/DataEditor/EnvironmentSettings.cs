using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The central settings the runner and dashboard share (appsettings.json): the global list of environments,
/// each instance's chosen environments, and the one "run in parallel" flag. Supplied by whoever hosts the editor.
/// </summary>
public interface IEnvironmentSettings
{
    /// <summary>Every environment defined centrally.</summary>
    IReadOnlyList<string> Environments { get; }

    /// <summary>The environments an instance runs in; empty means all of them.</summary>
    IReadOnlyList<string> GetSelection(string instanceName);

    /// <summary>The central "run in parallel" flag.</summary>
    bool RunParallel { get; }

    /// <summary>Saves the chosen environments of one or more instances in one write. Returns null on success, otherwise why not.</summary>
    string? SaveSelections(IReadOnlyDictionary<string, IReadOnlyList<string>> selections);

    /// <summary>Saves the central parallel flag. Returns null on success, otherwise why not.</summary>
    string? SaveRunParallel(bool value);
}

/// <summary>One environment with a tick box, in an instance's or the global environment drop-down.</summary>
public sealed partial class EnvironmentChoice(string name, Action<EnvironmentChoice> changed) : ObservableObject
{
    private bool _silent;

    public string Name { get; } = name;

    [ObservableProperty] private bool _isChosen;

    partial void OnIsChosenChanged(bool value)
    {
        if (!_silent) changed(this);
    }

    internal void SetSilently(bool value)
    {
        _silent = true;
        try { IsChosen = value; }
        finally { _silent = false; }
    }
}
