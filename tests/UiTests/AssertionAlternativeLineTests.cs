using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class AssertionAlternativeLineTests
{
    private sealed class Row : ICellRow { public ObservableCollection<string> Cells { get; } = ["", ""]; }

    [Fact]
    public void Or_and_and_checks_are_written_after_the_value_and_read_back()
    {
        var row = new Row();
        var line = new RecordLineViewModel(row, 1, "{eq}GetOrder.status", expected: true);
        line.Add(LoadTestingTool.Editing.ValuePart.Text("200"));
        Assert.True(line.CanCombine);
        line.AddAlternative(isOr: true);
        line.ChangeAlternative(0, line.Alternatives[0] with { Value = "201" });
        line.AddAlternative(isOr: false);
        line.ChangeAlternative(1, line.Alternatives[1] with { Verb = "contains", Value = "1" });
        Assert.Equal("200 <or: 201> <and: {contains}1>", row.Cells[1]);

        var again = new RecordLineViewModel(row, 1, "{eq}GetOrder.status", expected: true);
        Assert.Equal("200", LoadTestingTool.Editing.ValueParts.Format(again.Parts));
        Assert.Equal(2, again.Alternatives.Count);
        Assert.False(again.Alternatives[1].IsOr);

        again.RemoveAlternative(0);
        Assert.Equal("200 <and: {contains}1>", row.Cells[1]);
    }

    [Fact]
    public void The_size_verb_cannot_be_combined_and_changing_to_one_drops_the_checks()
    {
        var row = new Row();
        var line = new RecordLineViewModel(row, 1, "{eq}GetOrder.status", expected: true);
        line.Add(LoadTestingTool.Editing.ValuePart.Text("200"));
        line.AddAlternative(true);
        line.SetCondition("size");
        Assert.False(line.CanCombine);
        Assert.Empty(line.Alternatives);
        Assert.DoesNotContain("<or", row.Cells[1]);
        line.AddAlternative(true);                       // not allowed for this verb
        Assert.Empty(line.Alternatives);
        var plain = new RecordLineViewModel(new Row(), 1, "GetOrder", expected: false);
        Assert.False(plain.CanCombine);                   // only expected values on a response path
    }

    [AvaloniaFact]
    public void The_wizard_line_shows_plus_OR_and_plus_AND_and_the_check_rows()
    {
        var row = new Row();
        row.Cells[1] = "200 <or: 201>";
        var line = new RecordLineViewModel(row, 1, "{eq}GetOrder.status", expected: true);
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2));
        var view = new RecordLineView(line, editor);
        var window = new Window { Content = view, Width = 900, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var tags = view.GetVisualDescendants().OfType<Control>().Select(c => c.Tag as string).Where(s => s is not null).ToList();
        Assert.Contains("alt-add-or", tags);
        Assert.Contains("alt-add-and", tags);
        Assert.Single(tags, "alt-row");
        window.Close();
    }
}

public class HeaderVerbTests
{
    [Fact]
    public void Plain_name_drops_the_verb_prefix()
    {
        Assert.Equal("GetOrder.status", LoadTestingTool.UI.DataEditor.DataEditorViewModel.PlainName("{contains}GetOrder.status"));
        Assert.Equal("GetOrder.status", LoadTestingTool.UI.DataEditor.DataEditorViewModel.PlainName("GetOrder.status"));
    }
}
