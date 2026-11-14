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
            if (way == "toggle") { if (node is InstanceNode instance) instance.ToggleAll(); }
            else if (way == "run") _ = _shell.RunNodeAsync(node);
            else if (way == "add") _ = AddChildAsync(tree, node);
            else _shell.MoveNode(node, way == "up" ? -1 : 1);
            e.Handled = true;
        });

        if (this.FindControl<Button>("RunTickedButton") is { } runTicked)
            runTicked.Click += (_, _) => _ = _shell.RunTickedAsync();
        if (this.FindControl<Button>("ExpandAllButton") is { } expandAll) expandAll.Click += (_, _) => _shell.SetAllExpanded(true);
        if (this.FindControl<Button>("CollapseAllButton") is { } collapseAll) collapseAll.Click += (_, _) => _shell.SetAllExpanded(false);
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
                menu.Items.Add(Item("Duplicate testcase…", () => _shell.Current?.OpenCopyTestcaseCommand.Execute(null)));
                menu.Items.Add(CopyTestcaseMenu((TestcaseItem)node));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Add step", () => _ = AddChildAsync(tree, node)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Delete testcase…", () => _shell.Current?.OpenDeleteTestcaseCommand.Execute(null)));
                break;
            case StepNode step:
                menu.Items.Add(Item("Rename", () => BeginTreeRename(tree, node)));
                menu.Items.Add(Item("Duplicate step", () => _shell.DuplicateStep(step)));
                menu.Items.Add(CopyStepMenu(step));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Remove step", () => _shell.Current?.RemoveStepCommand.Execute(null)));
                break;
            default:
                return null;
        }
        return menu;
    }

    private async Task AddChildAsync(TreeView tree, object parent)
    {
        object? created = parent switch
        {
            InstanceNode instance => await _shell.AddTestcaseAsync(instance),
            TestcaseItem testcase => _shell.AddStep(testcase),
            _ => null
        };
        if (created is not null) BeginTreeRename(tree, created);
    }

    /// <summary>Copy testcase to ▸ another instance.</summary>
    private MenuItem CopyTestcaseMenu(TestcaseItem testcase)
    {
        var menu = new MenuItem { Header = "Copy to instance" };
        var owner = _shell.Instances.FirstOrDefault(i => i.Testcases.Contains(testcase));
        foreach (var instance in _shell.Instances.Where(i => !ReferenceEquals(i, owner)))
            menu.Items.Add(Item(instance.Name, () => _ = _shell.CopyTestcaseToAsync(testcase, instance)));
        if (menu.Items.Count == 0) { menu.Items.Add(new MenuItem { Header = "No other instance", IsEnabled = false }); }
        return menu;
    }

    /// <summary>Copy step to ▸ instance ▸ testcase; an instance's testcases are read when its submenu opens.</summary>
    private MenuItem CopyStepMenu(StepNode step)
    {
        var menu = new MenuItem { Header = "Copy to instance" };
        foreach (var instance in _shell.Instances)
        {
            var entry = new MenuItem { Header = instance.Name };
            entry.Items.Add(new MenuItem { Header = "…", IsEnabled = false });
            entry.SubmenuOpened += (_, _) =>
            {
                _shell.EnsureLoaded(instance);
                entry.Items.Clear();
                foreach (var testcase in instance.Testcases.ToList())
                    entry.Items.Add(Item(testcase.Title, () => _ = _shell.CopyStepToAsync(step, instance, testcase)));
                if (entry.Items.Count == 0) entry.Items.Add(new MenuItem { Header = instance.HasError ? "Could not be opened" : "No testcases", IsEnabled = false });
            };
            menu.Items.Add(entry);
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
