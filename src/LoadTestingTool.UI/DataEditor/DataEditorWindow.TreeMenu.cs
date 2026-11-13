using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace LoadTestingTool.UI.DataEditor;

public partial class DataEditorWindow
{
    /// <summary>A right-click in the tree selects the item under the pointer first, so the testcase menu acts on what was clicked.</summary>
    private void InitTreeMenu()
    {
        if (this.FindControl<TreeView>("Tree") is not { } tree) return;
        tree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(tree).Properties.IsRightButtonPressed) return;
            if ((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>()?.DataContext is { } node && _shell is not null) _shell.SelectedNode = node;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
