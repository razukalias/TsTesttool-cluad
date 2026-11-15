using Avalonia.Controls;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The run views of the dashboard (Overview, Run Summary, Assertions, Artifacts, Action Manual), shown in the editor's right-hand panel.
/// They follow the live run of the selected instance and, when nothing ran in this session, its last saved run.
/// </summary>
public interface IRunDetails
{
    /// <summary>The dashboard's tab control, taken out of the dashboard so the editor can host it. Called once.</summary>
    TabControl TakeTabs();

    /// <summary>Makes the views follow this instance (everything it ran).</summary>
    void Show(string instanceName);

    /// <summary>Makes the views follow an instance and, when given, narrow them to one testcase or one step of it.</summary>
    void Select(string instanceName, RunScope? scope) => Show(instanceName);

    /// <summary>What the latest run of an instance did, step by step; empty when it never ran.</summary>
    RunStatusSnapshot GetStatus(string instanceName) => RunStatusSnapshot.Empty;

    /// <summary>Brings the matching detail (assertions, overview entry) of an instance to the front after a status icon was clicked.</summary>
    void Focus(string instanceName, RunFocus focus) { }

    /// <summary>Asks the running instance to stop gracefully.</summary>
    void Stop(string instanceName) { }
}
