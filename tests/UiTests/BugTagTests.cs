using Avalonia.Controls;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// The known-bug tag <bug: dev,test; description> in an expected-value cell.
public class BugTagTests
{
    private static AssertionDefinition Def(string path, string expected, int column) => new()
    {
        TestcaseIndex = 1, Testcase = "t", DataId = "d", StepName = "s", ResponsePath = path, ExpectedValue = expected, ExcelRowNumber = 2, ExpectedValueColumn = column,
    };

    private static IReadOnlyList<AssertionResult> Run(string environment, params AssertionDefinition[] definitions)
    {
        var (plain, bugs) = BugTag.Strip(definitions, environment);
        var results = new AssertionEngine().Evaluate("""{"name":"Ann","age":"30"}""", "json", plain, "run", "req", 200, true, false);
        BugTag.Apply(results, bugs);
        return results;
    }

    [Fact]
    public void The_tag_is_read_and_taken_out_of_the_value()
    {
        var (value, tag) = BugTag.Parse("Bo <bug: dev, TEST; wrong name, JIRA-5>");
        Assert.Equal("Bo", value);
        Assert.Equal(["dev", "test"], tag!.Environments);
        Assert.Equal("wrong name, JIRA-5", tag.Description);
        Assert.True(tag.AppliesTo("Test"));
        Assert.False(tag.AppliesTo("prod"));
        Assert.True(BugTag.Parse("<bug: all; x>").Tag!.AppliesTo("anything"));
        Assert.Null(BugTag.Parse("plain").Tag);
    }

    [Fact]
    public void A_failing_tagged_assertion_in_a_listed_environment_is_a_known_bug_and_others_still_fail()
    {
        var results = Run("dev", Def("name", "Bo <bug: dev; name is wrong>", 5), Def("age", "99", 6));
        var name = results.Single(r => r.ResponsePath == "name");
        Assert.Equal("KNOWN_BUG", name.Result);
        Assert.True(name.KnownBug);
        Assert.Equal("name is wrong", name.BugDescription);
        Assert.Equal("Bo", name.ExpectedValue);
        Assert.Equal("FAIL", results.Single(r => r.ResponsePath == "age").Result);
    }

    [Fact]
    public void In_an_environment_that_is_not_listed_the_failure_is_a_failure_as_usual()
    {
        var name = Run("prod", Def("name", "Bo <bug: dev; name is wrong>", 5)).Single(r => r.ResponsePath == "name");
        Assert.Equal("FAIL", name.Result);
        Assert.False(name.KnownBug);
        Assert.Equal("Bo", name.ExpectedValue);
    }

    [Fact]
    public void A_tagged_assertion_that_passes_is_flagged_as_maybe_fixed()
    {
        var name = Run("dev", Def("name", "Ann <bug: dev; was wrong>", 5)).Single(r => r.ResponsePath == "name");
        Assert.Equal("PASS", name.Result);
        Assert.True(name.KnownBug);
    }
}

public class BugTagEditorTests
{
    [Fact]
    public void Removing_the_tag_or_one_environment_from_it_rewrites_the_cell()
    {
        Assert.Equal("Bo", BugTag.Without("Bo <bug: dev,test; wrong>"));
        Assert.Equal("Bo <bug: test; wrong>", BugTag.Without("Bo <bug: dev,test; wrong>", "dev"));
        Assert.Equal("Bo", BugTag.Without("Bo <bug: dev; wrong>", "dev"));
        Assert.Equal("Bo", BugTag.Without("Bo <bug: all; wrong>", "dev"));       // all cannot lose one environment, the tag goes
        Assert.Equal("<bug: test>", BugTag.Without("<bug: dev,test>", "dev"));
        Assert.Equal("plain", BugTag.Without("plain", "dev"));
    }

    [Fact]
    public void A_badly_written_or_misplaced_tag_is_warned_about()
    {
        Assert.Empty(DataEditorViewModel.BugTagWarnings("200", expected: true));
        Assert.Empty(DataEditorViewModel.BugTagWarnings("Bo <bug: dev; x>", expected: true));
        Assert.Contains("not written as one", Assert.Single(DataEditorViewModel.BugTagWarnings("Bo <bug dev", expected: true)));
        Assert.Contains("no environment", Assert.Single(DataEditorViewModel.BugTagWarnings("Bo <bug: ; x>", expected: true)));
        Assert.Contains("only works on an expected-value cell", Assert.Single(DataEditorViewModel.BugTagWarnings("Bo <bug: dev; x>", expected: false)));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_tagged_cell_shows_the_marker_in_the_table_view()
    {
        using var dir = new TempDir();
        var window = LoadTestingTool.UI.DataEditor.DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 700; window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var column = window.ViewModel.Columns.First(c => c.Group == LoadTestingTool.UI.DataEditor.ColumnGroup.Response);
        window.ViewModel.VisibleRows[0].Cells[column.CellIndex] = "x <bug: dev; known>";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<Avalonia.Controls.DataGrid>("Grid")!;
        var marks = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(grid).OfType<Avalonia.Controls.TextBlock>().Where(t => t.Tag as string == "bug-mark" && t.IsVisible).ToList();
        Assert.Single(marks);
        window.Close();
    }
}
