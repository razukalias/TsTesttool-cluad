using Avalonia.Controls;

namespace LoadTestingTool.UI.DataEditor;

public sealed partial class DataEditorWindow
{
    private bool _editOnly;

    /// <summary>
    /// The editor for one workbook in its own process (<c>--table-editor path</c>): the same table view with all its functions and filters, but
    /// only for editing. There is no running, no history and no results, and no Record or Config mode; it starts fresh from the file.
    /// </summary>
    public static DataEditorWindow OpenEditOnly(string workbookPath)
    {
        var window = Open(workbookPath);
        window.ApplyEditOnly(workbookPath);
        return window;
    }

    private void ApplyEditOnly(string workbookPath)
    {
        _editOnly = true;
        foreach (var name in new[] { "RunTickedButton", "RunShownButton", "HistoryLogsButton", "CompareRunsButton", "MockServerButton", "StorageCleanupButton", "GlobalRunSettingsButton", "OpenSeparateToolButton", "ViewModeBox", "InstanceTreePanel", "TreeSplitter", "BodySplitter", "RightHost" })
            if (this.FindControl<Control>(name) is { } control) control.IsVisible = false;
        // only the middle panel: no instance tree on the left, no preview / run panel on the right
        if (this.FindControl<Grid>("RootGrid") is { } root) { root.ColumnDefinitions[0].MinWidth = 0; root.ColumnDefinitions[0].Width = new GridLength(0); }
        if (this.FindControl<Grid>("BodyGrid") is { } body) { body.ColumnDefinitions[2].MinWidth = 0; body.ColumnDefinitions[2].Width = new GridLength(0); body.ColumnDefinitions[0].Width = GridLength.Star; body.Children.OfType<Control>().First(c => Avalonia.Controls.Grid.GetColumn(c) == 0).Margin = default; }
        if (_shell.Current is { } editor)
        {
            editor.ViewModeIndex = 0;   // Table view only
            if (TableEditorLock.OtherProcessHas(workbookPath))
                editor.Notify("This workbook is also open in another table editor window. Whoever saves last overwrites the other's changes.", true);
        }
        TableEditorLock.Take(workbookPath);
        Closed += (_, _) => TableEditorLock.Release(workbookPath);
        UpdateEditOnlyTitle();
    }

    private void UpdateEditOnlyTitle()
    {
        if (_editOnly && _subscribed is { } editor) Title = $"Edit only · {editor.InstanceName} · {System.IO.Path.GetFileName(editor.WorkbookPath)}";
    }

    /// <summary>The ↗ button: opens the selected instance's saved workbook in its own edit-only process.</summary>
    internal void OpenSelectedInSeparateWindow()
    {
        if (_shell.Current is not { } editor) return;
        if (editor.IsDirty) { editor.Notify("Save your changes first: the separate window starts from the saved workbook.", true); return; }
        try
        {
            var other = TableEditorLock.OtherProcessHas(editor.WorkbookPath);
            TableEditorLauncher.Open(editor.WorkbookPath);
            editor.Notify(other ? "Opened another separate window. This workbook was already open in one: whoever saves last wins." : "Opened in a separate window (edit only).", other);
        }
        catch (Exception ex) { editor.Notify($"Could not open the separate window: {ex.Message}", true); }
    }
}
