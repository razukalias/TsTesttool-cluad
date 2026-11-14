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

    /// <summary>Makes the views follow this instance.</summary>
    void Show(string instanceName);
}
