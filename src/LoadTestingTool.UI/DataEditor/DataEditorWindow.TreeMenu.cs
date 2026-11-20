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
            else if (way == "stop") { if (node is InstanceNode running) _shell.StopInstance(running); }
            else if (way == "status") ShowStatusDetails(node);
            else if (way == "add") _ = AddChildAsync(tree, node);
            else _shell.MoveNode(node, way == "up" ? -1 : 1);
            e.Handled = true;
        });

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => _shell.RefreshRunStatus();
        _statusTimer.Start();
        Closed += (_, _) => _statusTimer?.Stop();
        if (this.FindControl<Button>("RunTickedButton") is { } runTicked)
            runTicked.Click += (_, _) => _ = _shell.RunTickedAsync();
        if (this.FindControl<Button>("RunShownButton") is { } runShown)
            runShown.Click += (_, _) => _ = _shell.RunShownAsync();
        if (this.FindControl<TextBox>("InstanceFilterBox") is { } filterBox)
            filterBox.KeyDown += (_, e) => { if (e.Key == Key.Escape) { filterBox.Text = string.Empty; e.Handled = true; } };
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

    private DispatcherTimer? _statusTimer;

    /// <summary>A status icon was clicked: points the run views at the node and brings the matching tab to the front.</summary>
    private void ShowStatusDetails(object node)
    {
        var tab = _shell.OpenStatus(node);
        if (tab is null || this.FindControl<Border>("RightHost")?.Child is not TabControl tabs) return;
        var match = tabs.Items.OfType<TabItem>().FirstOrDefault(t => string.Equals(t.Header?.ToString(), tab, StringComparison.OrdinalIgnoreCase));
        if (match is not null) tabs.SelectedItem = match;
    }

    private ContextMenu? BuildTreeMenu(TreeView tree, object node)
    {
        var menu = new ContextMenu();
        switch (node)
        {
            case InstanceNode instance:
                menu.Items.Add(Item("Add testcase", () => _ = AddChildAsync(tree, node)));
                menu.Items.Add(Item("Duplicate instance…", () => _ = DuplicateInstanceAsync(instance)));
                menu.Items.Add(Item("New instance…", () => _ = NewInstanceAsync()));
                break;
            case TestcaseItem:
                menu.Items.Add(Item("Rename", () => BeginTreeRename(tree, node)));
                menu.Items.Add(Item("Duplicate testcase…", () => _shell.OpenTestcasePanel((TestcaseItem)node, delete: false)));
                menu.Items.Add(CopyTestcaseMenu((TestcaseItem)node));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Add step", () => _ = AddChildAsync(tree, node)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Delete testcase…", () => _shell.OpenTestcasePanel((TestcaseItem)node, delete: true)));
                break;
            case StepNode step:
                menu.Items.Add(Item("Rename", () => BeginTreeRename(tree, node)));
                menu.Items.Add(Item("Duplicate step…", () => _shell.OpenDuplicateStepPanel(step)));
                menu.Items.Add(CopyStepMenu(step));
                menu.Items.Add(Item("Add columns from template / response…", () => _ = AddColumnsAsync(step)));
                menu.Items.Add(NestMenu(step));
                if (step.Depth > 0) menu.Items.Add(Item("Move out (one level)", () => _shell.PutStepInside(step, null, oneLevelOut: true)));
                menu.Items.Add(new Separator());
                menu.Items.Add(Item("Remove step", () => _shell.RemoveStep(step)));
                break;
            default:
                return null;
        }
        return menu;
    }

    /// <summary>Opens the Add columns form for one step of the clicked step's instance.</summary>
    /// <summary>"Put inside ▸": the If, While, For and ForEach steps of the testcase (an If also offers its else branch).</summary>
    private MenuItem NestMenu(StepNode step)
    {
        var menu = new MenuItem { Header = "Put inside" };
        foreach (var control in step.Owner.StepNodes.Where(n => n.IsControl && !ReferenceEquals(n, step)))
        {
            menu.Items.Add(Item($"{control.Name}  ({control.TypeName})", () => _shell.PutStepInside(step, control.Name)));
            if (control.TypeName == "If") menu.Items.Add(Item($"{control.Name}  (else)", () => _shell.PutStepInside(step, control.Name, elseBranch: true)));
        }
        if (step.Depth > 0) menu.Items.Add(Item("Top level", () => _shell.PutStepInside(step, null)));
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No If, While, For or ForEach step here", IsEnabled = false });
        return menu;
    }

    private async Task AddColumnsAsync(StepNode step)
    {
        if (_shell.EditorFor(step) is not { } editor) return;
        await new AddColumnsWindow(editor, step.Name).ShowDialog(this);
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

    private void OnAddEnvironmentClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<TextBox>("NewEnvironmentBox") is not { } box) return;
        if (_shell.AddEnvironment(box.Text) is null) box.Text = string.Empty;
    }

    private void OnRemoveEnvironmentClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name }) _shell.RemoveEnvironment(name);
    }
}
