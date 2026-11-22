using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ToolbarLayoutTests
{
    private static (DataEditorWindow Window, EditorShellViewModel Shell) Open(TempDir dir)
    {
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1400; window.Height = 900;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, window.Shell);
    }

    private static IEnumerable<string> ButtonTexts(Control root) => root.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).Select(b => b.Content as string ?? "");

    [AvaloniaFact]
    public void The_view_mode_droplist_switches_the_mode_and_follows_it()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var editor = shell.Current!;
        var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(c => Equals(c.Tag, "view-mode"));
        Assert.Equal(0, combo.SelectedIndex);
        combo.SelectedIndex = 1;
        Assert.True(editor.ShowSteps);
        combo.SelectedIndex = 2;
        Assert.True(editor.ShowRecord);
        Assert.False(editor.ShowSteps);
        editor.OpenDataTabCommand.Execute(null);
        Assert.Equal(0, combo.SelectedIndex);
        editor.OpenStepsTabCommand.Execute(null);
        Assert.Equal(1, combo.SelectedIndex);
        window.Close();
    }

    [AvaloniaFact]
    public void The_old_view_tabs_and_column_buttons_are_gone_and_csv_is_a_droplist_with_a_button()
    {
        using var dir = new TempDir();
        var (window, _) = Open(dir);
        var texts = ButtonTexts(window).ToList();
        foreach (var gone in new[] { "▦  Table view", "⚙  Config edit mode", "☰  Record view", "Add column", "Rename / remove column", "Import CSV…", "Export CSV…", "Re-run…" })
            Assert.DoesNotContain(gone, texts);
        Assert.Contains("Compare runs…", texts);   // a visible button next to History & logs…
        foreach (var gone in Array.Empty<string>())
            Assert.DoesNotContain(gone, texts);
        Assert.DoesNotContain(texts, t => t.StartsWith("Column filters"));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<ToggleButton>(), b => b.Content as string == "Preview request");
        var csv = window.GetVisualDescendants().OfType<ComboBox>().Single(c => Equals(c.Tag, "csv-action"));
        Assert.Equal(new[] { "Import…", "Export CSV…", "Export Excel…" }, csv.ItemsSource!.Cast<string>().ToArray());
        Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Tag, "csv-run"));
        window.Close();
    }

    [AvaloniaFact]
    public void The_csv_button_runs_the_selected_action()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var editor = shell.Current!;
        var asked = new List<string>();
        editor.ImportRequested += (_, _) => asked.Add("import");
        editor.ExportRequested += (_, request) => asked.Add(request.Format + "-export");
        editor.RunCsvActionCommand.Execute(null);
        editor.CsvActionIndex = 1;
        editor.RunCsvActionCommand.Execute(null);
        editor.CsvActionIndex = 2;
        editor.RunCsvActionCommand.Execute(null);
        Assert.Equal(new[] { "import", "csv-export", "xlsx-export" }, asked.ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void The_filter_boxes_sit_in_the_title_bar_row_not_above_the_grid()
    {
        using var dir = new TempDir();
        var (window, _) = Open(dir);
        var filter = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark?.ToString()?.StartsWith("Headers:") == true);
        var titleBar = window.GetVisualDescendants().OfType<Border>().First(b => b.Padding == new Avalonia.Thickness(20, 14));
        Assert.Contains(filter, titleBar.GetVisualDescendants());
        window.Close();
    }

    [AvaloniaFact]
    public void Every_tree_item_has_view_mode_rerun_compare_and_preview_in_its_menu()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var tree = window.GetVisualDescendants().OfType<TreeView>().First();
        var build = typeof(DataEditorWindow).GetMethod("BuildTreeMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var testcase = shell.Instances[0].Testcases[0];
        foreach (var node in new object[] { shell.Instances[0], testcase, testcase.StepNodes[0] })
        {
            shell.SelectedNode = node;
            var menu = (ContextMenu)build.Invoke(window, [tree, node])!;
            var tags = menu.Items.OfType<MenuItem>().Select(i => i.Tag as string).ToList();
            Assert.Contains("view-mode-menu", tags);
            Assert.Contains("tree-rerun", tags);
            Assert.Contains("tree-compare", tags);
            Assert.Contains("tree-preview", tags);
        }
        shell.SelectedNode = testcase;
        var modes = ((ContextMenu)build.Invoke(window, [tree, testcase])!).Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "view-mode-menu"));
        modes.Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "view-mode-2")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(shell.Current!.ShowRecord);
        var preview = ((ContextMenu)build.Invoke(window, [tree, testcase])!).Items.OfType<MenuItem>().Single(i => Equals(i.Tag, "tree-preview"));
        preview.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(shell.Current.PreviewVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void A_column_header_menu_offers_filtering_by_that_column()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var editor = shell.Current!;
        shell.SelectedNode = shell.Instances[0].Testcases[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var menu = window.BuildColumnMenu(editor, 1);
        var filter = menu.Items.OfType<MenuItem>().Single(i => (i.Header as string)?.StartsWith("Filter rows by this column") == true);
        filter.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(editor.ColumnFiltersVisible);
        Assert.Equal(editor.FilterColumnOptions[1], editor.NewFilterColumn);
        window.Close();
    }

    [AvaloniaFact]
    public void The_editor_area_starts_at_the_same_place_and_has_the_same_height_in_every_mode()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var tops = new List<double>();
        var heights = new List<double>();
        foreach (var mode in new[] { 0, 1, 2, 0 })
        {
            shell.Current!.ViewModeIndex = mode;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(c => Equals(c.Tag, "view-mode"));
            tops.Add(((Avalonia.Visual)combo).TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.Y);
            heights.Add(window.GetVisualDescendants().OfType<ScrollViewer>().Single(v => Equals(v.Tag, "toolbar-area")).Bounds.Height);
        }
        Assert.Single(tops.Select(t => Math.Round(t)).Distinct());
        Assert.Single(heights.Distinct());
        window.Close();
    }

    [AvaloniaFact]
    public void The_show_droplist_titles_the_options_that_are_on_and_holds_the_five_checkboxes()
    {
        using var dir = new TempDir();
        var (window, shell) = Open(dir);
        var editor = shell.Current!;
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "show-options"));
        Assert.Equal("Show: default ▾", button.Content);
        editor.ShowBugColumns = true;
        editor.ShowAllColumns = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("Show: Every column, Bug columns ▾", button.Content);
        editor.ShowAllColumns = false;
        editor.ShowBugColumns = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("Show: default ▾", button.Content);
        var panel = Assert.IsType<StackPanel>(Assert.IsType<Flyout>(button.Flyout).Content);
        Assert.Equal(new[] { "Show every column", "Failed rows only", "Failed columns only", "Bug columns only", "Failed cells only" }, panel.Children.OfType<CheckBox>().Select(c => c.Content as string).ToArray());
        window.Close();
    }
}
