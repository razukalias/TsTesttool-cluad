using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.UI;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class PagerTests
{
    [Fact]
    public void Slices_of_100_with_the_right_page_count_edges_and_summary()
    {
        var pager = new Pager();
        var all = Enumerable.Range(1, 1234).ToList();
        Assert.Equal(100, pager.PageSize);
        var first = pager.Slice(all);
        Assert.Equal(100, first.Count); Assert.Equal(1, first[0]); Assert.Equal(13, pager.PageCount); Assert.Equal("1–100 of 1,234", pager.Summary);
        pager.GoTo(13);
        var last = pager.Slice(all);
        Assert.Equal(34, last.Count); Assert.Equal(1201, last[0]); Assert.Equal("1,201–1,234 of 1,234", pager.Summary);
        pager.GoTo(99); Assert.Equal(13, pager.Page);
        pager.GoTo(0); Assert.Equal(1, pager.Page);
        Assert.Empty(pager.Slice(new List<int>())); Assert.Equal(1, pager.PageCount); Assert.Equal("No items", pager.Summary);
    }

    [Fact]
    public void A_filter_shrinking_the_list_keeps_the_page_inside_and_reset_returns_to_page_one()
    {
        var pager = new Pager();
        pager.Slice(Enumerable.Range(1, 1000).ToList()); pager.GoTo(10);
        var slice = pager.Slice(Enumerable.Range(1, 150).ToList());
        Assert.Equal(2, pager.Page); Assert.Equal(50, slice.Count);
        pager.Reset(); Assert.Equal(1, pager.Page);
    }

    [Fact]
    public void Changing_the_page_size_returns_to_page_one_and_only_accepts_the_offered_sizes()
    {
        var pager = new Pager(); var changed = 0; pager.Changed += () => changed++;
        pager.Slice(Enumerable.Range(1, 1000).ToList()); pager.GoTo(4); changed = 0;
        pager.PageSize = 250;
        Assert.Equal(1, pager.Page); Assert.Equal(1, changed); Assert.Equal(4, pager.PageCount);
        pager.PageSize = 77;   // not offered: back to the default
        Assert.Equal(100, pager.PageSize);
    }

    private static T Find<T>(Control root, string tag) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => Equals(c.Tag, tag));

    [AvaloniaFact]
    public void The_bar_pages_jumps_and_remembers_the_page_size()
    {
        using var dir = new TempDir();
        var original = EditorSettings.FilePath;
        EditorSettings.FilePath = System.IO.Path.Combine(dir.Combine("s"), "data-editor.json");
        try
        {
            var pager = new Pager(); var all = Enumerable.Range(1, 1050).ToList(); pager.Slice(all);
            var bar = new PagerBar(pager);
            var window = new Window { Content = bar }; window.Show();
            Assert.False(Find<Button>(bar, "page-previous").IsEnabled);
            Assert.Contains("of 11", Find<TextBlock>(bar, "page-label").Text);
            Find<Button>(bar, "page-next").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, pager.Page); Assert.Equal("2", Find<TextBox>(bar, "page-jump").Text);
            Find<Button>(bar, "page-last").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(11, pager.Page); Assert.False(Find<Button>(bar, "page-next").IsEnabled);
            var jump = Find<TextBox>(bar, "page-jump"); jump.Text = "5";
            jump.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            Assert.Equal(5, pager.Page);
            Find<ComboBox>(bar, "page-size").SelectedItem = 250;
            Assert.Equal(250, pager.PageSize); Assert.Equal(1, pager.Page);
            Assert.Equal(250, EditorSettings.Load().HistoryPageSize);
            Assert.Equal(250, new Pager { PageSize = EditorSettings.Load().HistoryPageSize }.PageSize);   // a new bar starts with it
            window.Close();
        }
        finally { EditorSettings.FilePath = original; }
    }
}

public class HistoryWorkspacePagingTests
{
    [AvaloniaFact]
    public void The_history_window_shows_100_files_a_page_and_a_filter_returns_to_page_one()
    {
        using var dir = new TempDir();
        var original = EditorSettings.FilePath;
        EditorSettings.FilePath = System.IO.Path.Combine(dir.Combine("s"), "data-editor.json");
        try
        {
            var history = System.IO.Path.Combine(dir.Combine("Instances"), "Main", "History", "passed", "dev");
            Directory.CreateDirectory(history);
            for (var i = 1; i <= 250; i++) File.WriteAllBytes(System.IO.Path.Combine(history, $"Case{i % 2}_Row{i}_run{i:D4}.xlsx"), []);
            var window = new HistoryWorkspaceWindow(dir.Combine("Instances"));
            window.Show();
            var label = () => window.GetVisualDescendants().OfType<TextBlock>().Single(t => Equals(t.Tag, "page-label")).Text;
            for (var i = 0; i < 100 && !(label()?.Contains("250") ?? false); i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(50); }
            Assert.Equal("of 3  ·  1–100 of 250", label());
            window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "page-next")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("of 3  ·  101–200 of 250", label());
            window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "page-last")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("of 3  ·  201–250 of 250", label());
            window.Close();
        }
        finally { EditorSettings.FilePath = original; }
    }
}
