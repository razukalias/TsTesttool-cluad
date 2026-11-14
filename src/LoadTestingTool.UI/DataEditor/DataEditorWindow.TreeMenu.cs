using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LoadTestingTool.UI.DataEditor;

public partial class DataEditorWindow
{
    /// <summary>
    /// Tree behaviour: a right-click selects the item and shows the menu that fits it (testcase or step);
    /// Rename edits the name in place; ▲ ▼ buttons beside an item move it.
    /// </summary>
    private void InitTreeMenu()
    {
        if (this.FindControl<TreeView>("Tree") is not { } tree) return;

        tree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(tree).Properties.IsRightButtonPressed) return;
            var node = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>()?.DataContext;
            if (node is null) return;
            _shell.SelectedNode = node;
            tree.ContextMenu = BuildTreeMenu(tree, node);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        tree.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is not Button { Tag: string way, DataContext: { } node }) return;
            _shell.MoveNode(node, way == "up" ? -1 : 1);
            e.Handled = true;
        });

        tree.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Source is not TextBox { DataContext: { } node }) return;
            if (e.Key == Key.Enter) { _shell.CommitRename(node); e.Handled = true; }
            else if (e.Key == Key.Escape) { _shell.CancelRename(node); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        tree.AddHandler(LostFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox { DataContext: { } node }) _shell.CommitRename(node);
        });
    }

    private ContextMenu? BuildTreeMenu(TreeView tree, object node)
    {
        var menu = new ContextMenu();
        switch (node)
        {
            case TestcaseItem:
                menu.Items.Add(Item("Rename", () => BeginTreeRename(tree, node)));
                menu.Items.Add(Item("Copy testcase…", () => _shell.Current?.OpenCopyTestcaseCommand.Execute(null)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Delete testcase…", () => _shell.Current?.OpenDeleteTestcaseCommand.Execute(null)));
                break;
            case StepNode:
                menu.Items.Add(Item("Rename", () => BeginTreeRename(tree, node)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Remove step", () => _shell.Current?.RemoveStepCommand.Execute(null)));
                break;
            default:
                return null;
        }
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void BeginTreeRename(TreeView tree, object node)
    {
        _shell.BeginRename(node);
        Dispatcher.UIThread.Post(() =>
        {
            var box = tree.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => ReferenceEquals(t.DataContext, node) && t.IsVisible);
            if (box is null) return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Background);
    }
}
