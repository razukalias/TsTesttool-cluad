using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;
using static LoadTestingTool.UI.DataEditor.EditorShellViewModel;

namespace UiTests;

// Right-click on the tree: add a step of any type (after a step, inside a control step, in an else branch), remove a step with or without what is inside it,
// outline numbers and indentation.
public class StepTreeMenuTests
{
    private static (EditorShellViewModel Shell, TestcaseItem Testcase) Open(TempDir dir)
    {
        var shell = new EditorShellViewModel([("Sample", Samples.CreateLarge(dir, rows: 3))]);
        var testcase = shell.Instances[0].Testcases[0];
        shell.SelectedNode = testcase;
        shell.Current!.ShowSteps = true;
        return (shell, testcase);
    }

    private static string[] Names(TestcaseItem t) => t.StepNodes.Select(n => n.Name).ToArray();

    [Fact]
    public void Add_step_offers_every_type_and_the_step_gets_that_type()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        foreach (var type in new[] { "GraphQL", "Script", "If", "For" })
        {
            var node = shell.AddStepOfType(testcase, type, StepPlacement.End)!;
            Assert.NotNull(node);
            Assert.StartsWith(type, node.Name);
            Assert.Equal(type, shell.Current!.StepRows.Single(r => r.CurrentName == node.Name).TypeText);
        }
        Assert.Equal("If", testcase.StepNodes.Single(n => n.Name == "If1").TypeName);
    }

    [Fact]
    public void A_step_added_after_another_lands_right_behind_it_and_its_contents_at_the_same_level()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        var first = testcase.StepNodes[0];
        var control = shell.AddStepOfType(testcase, "For", StepPlacement.End)!;
        var inner = shell.AddStepOfType(control, "Script", StepPlacement.Inside)!;
        var after = shell.AddStepOfType(first, "SQL", StepPlacement.After)!;
        Assert.Equal(first.Name, Names(testcase)[0]);
        Assert.Equal(after.Name, Names(testcase)[1]);
        Assert.Equal(0, testcase.StepNodes.Single(n => n.Name == after.Name).Depth);
        // "after" the control step goes behind everything inside it
        var behind = shell.AddStepOfType(testcase.StepNodes.Single(n => n.Name == control.Name), "File", StepPlacement.After)!;
        var order = Names(testcase).ToList();
        Assert.True(order.IndexOf(behind.Name) > order.IndexOf(inner.Name));
        Assert.Equal(0, testcase.StepNodes.Single(n => n.Name == behind.Name).Depth);
        Assert.Equal(1, testcase.StepNodes.Single(n => n.Name == inner.Name).Depth);
    }

    [Fact]
    public void Child_steps_go_inside_a_control_step_and_an_if_has_a_then_and_an_else_branch()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        var branch = shell.AddStepOfType(testcase, "If", StepPlacement.End)!;
        var then = shell.AddStepOfType(branch, "HTTP", StepPlacement.Inside)!;
        var other = shell.AddStepOfType(branch, "HTTP", StepPlacement.InsideElse)!;
        var nodes = testcase.StepNodes;
        Assert.Equal(1, nodes.Single(n => n.Name == then.Name).Depth);
        Assert.False(nodes.Single(n => n.Name == then.Name).InElse);
        Assert.True(nodes.Single(n => n.Name == other.Name).InElse);
        // a step that is not a control step cannot take children, and nothing changes
        var plain = testcase.StepNodes[0];
        var before = Names(testcase);
        Assert.Null(shell.AddStepOfType(plain, "HTTP", StepPlacement.Inside));
        Assert.Equal(before, Names(testcase));
        // a While has no else branch
        var loop = shell.AddStepOfType(testcase, "While", StepPlacement.End)!;
        Assert.Null(shell.AddStepOfType(loop, "HTTP", StepPlacement.InsideElse));
    }

    [Fact]
    public void Nesting_stops_at_five_levels_with_a_message_and_no_change()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        var node = shell.AddStepOfType(testcase, "For", StepPlacement.End)!;
        for (var i = 0; i < 4; i++) node = shell.AddStepOfType(node, "For", StepPlacement.Inside)!;
        Assert.Equal(4, node.Depth);
        var count = testcase.StepNodes.Count;
        Assert.Null(shell.AddStepOfType(node, "HTTP", StepPlacement.Inside));
        Assert.Equal(count, testcase.StepNodes.Count);
        Assert.Contains("5 levels", shell.Current!.StatusMessage);
    }

    [Fact]
    public void Removing_a_control_step_moves_its_children_out_one_level_and_re_indents_them()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        var outer = shell.AddStepOfType(testcase, "For", StepPlacement.End)!;
        var inner = shell.AddStepOfType(outer, "If", StepPlacement.Inside)!;
        var leaf = shell.AddStepOfType(inner, "HTTP", StepPlacement.Inside)!;
        Assert.Equal(2, testcase.StepNodes.Single(n => n.Name == leaf.Name).Depth);
        shell.RemoveStep(testcase.StepNodes.Single(n => n.Name == inner.Name));
        Assert.DoesNotContain(inner.Name, Names(testcase));
        Assert.Equal(1, testcase.StepNodes.Single(n => n.Name == leaf.Name).Depth);   // was 2 inside the If, now directly inside the For
        shell.RemoveStep(testcase.StepNodes.Single(n => n.Name == outer.Name));
        Assert.Equal(0, testcase.StepNodes.Single(n => n.Name == leaf.Name).Depth);
    }

    [Fact]
    public void Remove_with_children_takes_the_whole_block_in_one_undo_step()
    {
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        var editor = shell.Current!;
        var before = Names(testcase);
        var outer = shell.AddStepOfType(testcase, "For", StepPlacement.End)!;
        var inner = shell.AddStepOfType(outer, "If", StepPlacement.Inside)!;
        shell.AddStepOfType(inner, "HTTP", StepPlacement.Inside);
        shell.AddStepOfType(testcase, "Script", StepPlacement.End);
        Assert.Equal(2, editor.StepsInsideCount(inner.Name) + 1);
        shell.RemoveStepWithChildren(testcase.StepNodes.Single(n => n.Name == outer.Name));
        Assert.Equal(before.Length + 1, testcase.StepNodes.Count);   // only the Script step is left of the four added
        Assert.DoesNotContain(outer.Name, Names(testcase));
        Assert.DoesNotContain(inner.Name, Names(testcase));
        editor.UndoCommand.Execute(null);
        Assert.Contains(outer.Name, Names(testcase));
        Assert.Contains(inner.Name, Names(testcase));
        Assert.Equal(1, testcase.StepNodes.Single(n => n.Name == inner.Name).Depth);
    }

    [Fact]
    public void Steps_are_numbered_like_an_outline_in_the_tree_and_on_the_cards()
    {
        Assert.Equal(["1", "2", "2.1", "2.1.1", "2.2", "3"], StepNumbering.Compute([0, 0, 1, 2, 1, 0]));
        using var dir = new TempDir();
        var (shell, testcase) = Open(dir);
        var loop = shell.AddStepOfType(testcase, "For", StepPlacement.End)!;
        shell.AddStepOfType(loop, "HTTP", StepPlacement.Inside);
        shell.AddStepOfType(loop, "HTTP", StepPlacement.Inside);
        shell.AddStepOfType(testcase, "HTTP", StepPlacement.End);
        var numbers = testcase.StepNodes.Select(n => n.Number).ToArray();
        var loopNumber = numbers[testcase.StepNodes.IndexOf(testcase.StepNodes.Single(n => n.Name == loop.Name))];
        Assert.Contains(loopNumber + ".1", numbers);
        Assert.Contains(loopNumber + ".2", numbers);
        Assert.StartsWith(loopNumber + " · ", testcase.StepNodes.Single(n => n.Name == loop.Name).Title);
        var cards = shell.Current!.StepRows;
        Assert.Equal(numbers, cards.Select(c => c.Number).ToArray());
        Assert.Equal(testcase.StepNodes.Select(n => n.Depth).ToArray(), cards.Select(c => c.Depth).ToArray());
        Assert.True(cards.First(c => c.Depth == 1).CardMargin.Left > 0);
    }

    [AvaloniaFact]
    public void The_right_click_menus_have_the_type_submenus_and_the_remove_entries()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1400; window.Height = 900; window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var shell = window.Shell;
        var tree = window.GetVisualDescendants().OfType<TreeView>().First();
        var build = typeof(DataEditorWindow).GetMethod("BuildTreeMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var testcase = shell.Instances[0].Testcases[0];
        shell.SelectedNode = testcase;
        shell.Current!.ShowSteps = true;
        var loop = shell.AddStepOfType(testcase, "If", StepPlacement.End)!;
        var child = shell.AddStepOfType(loop, "HTTP", StepPlacement.Inside)!;

        IEnumerable<string?> Tags(object node) => ((ContextMenu)build.Invoke(window, [tree, node])!).Items.OfType<MenuItem>().Select(i => i.Tag as string);
        var types = ((ContextMenu)build.Invoke(window, [tree, testcase])!).Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "tree-add-step")).Items.OfType<MenuItem>().Select(i => i.Header as string).ToArray();
        Assert.Equal(["HTTP", "GraphQL", "CaseQL", "SQL", "File", "Script", "ActionManual", "If", "While", "For", "ForEach"], types);

        var plain = testcase.StepNodes[0];
        Assert.Contains("tree-add-after", Tags(plain));
        Assert.DoesNotContain("tree-add-child", Tags(plain));                        // only control steps take children
        var branch = testcase.StepNodes.Single(n => n.Name == loop.Name);
        Assert.Contains("tree-add-child", Tags(branch));
        Assert.Contains("tree-add-else", Tags(branch));                              // an If has a then and an else
        Assert.Contains("tree-remove-with-children", Tags(branch));
        Assert.DoesNotContain("tree-remove-from-parent", Tags(branch));              // top level: nothing to leave
        var nested = testcase.StepNodes.Single(n => n.Name == child.Name);
        Assert.Contains("tree-remove-from-parent", Tags(nested));
        Assert.DoesNotContain("tree-remove-with-children", Tags(nested));
        window.Close();
    }
}
