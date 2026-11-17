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

    /// <summary>The instance's saved testcase selection ("0" or empty = all; otherwise "1,3-5") and step selection (empty = every enabled step, else {"1":["Step"]}).</summary>
    (string Testcases, string Steps) GetRunSelection(string instanceName);

    /// <summary>Saves which testcases and steps an instance runs. Returns null on success, otherwise why not.</summary>
    string? SaveRunSelection(string instanceName, string testcases, string stepsJson);

    /// <summary>The folder holding every instance folder, and the default workbook file name for a new instance.</summary>
    string InstancesRoot { get; }
    string WorkbookName { get; }

    /// <summary>Lists a new instance folder in appsettings.json, copying the settings of <paramref name="copyFrom"/> when given. Returns null on success, otherwise why not.</summary>
    string? RegisterInstance(string name, string? copyFrom);

    /// <summary>Replaces the central list of environment names (after an add or a remove) and drops removed names from every instance's selection. Returns null on success, otherwise why not.</summary>
    string? SaveEnvironmentList(IReadOnlyList<string> environments) => "Changing the environment list is not supported here.";

    /// <summary>Saves the central parallel flag. Returns null on success, otherwise why not.</summary>
    string? SaveRunParallel(bool value);

    /// <summary>The row-execution settings. For <c>null</c> (global) the effective root values with defaults; for an instance only what it overrides (null = inherits the global value).</summary>
    ExecutionValues GetExecution(string? instance) => ExecutionValues.Defaults;

    /// <summary>
    /// Saves one execution setting (key: RowsExecutionMode, MaxParallelRows, Iterations, ParallelRowStartIntervalMs, IterationIntervalMs).
    /// For an instance a null value removes the override. For the global scope the value goes to the root of appsettings.json and every instance override of that key is removed.
    /// Returns null on success, otherwise why not.
    /// </summary>
    string? SaveExecution(string? instance, string key, string? value) => "Execution settings are not available here.";
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
