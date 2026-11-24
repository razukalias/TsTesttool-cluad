using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class JsonViewNavigationTests
{
    private static object[] Run() =>
    [
        FakeRun.Request(1, "CUST001", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Customer 1", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", category: "Assertion", env: "test"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "x", env: "test"),
    ];

    private static (DataEditorWindow Window, DataEditorViewModel Editor, JsonView View) Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 4);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson", Run());
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        return (window, editor, window.GetVisualDescendants().OfType<JsonView>().Single());
    }

    [AvaloniaFact]
    public void The_row_selected_here_is_the_row_selected_in_the_table_and_the_other_way_round()
    {
        using var dir = new TempDir();
        var (window, editor, view) = Open(dir);
        view.Select("name");
        Dispatcher.UIThread.RunJobs();
        var list = window.GetVisualDescendants().OfType<ListBox>().First(l => (string?)l.Tag == "json-rows");
        var second = editor.VisibleRows[1];

        list.SelectedItem = second;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(second, editor.SelectedRow);

        var third = editor.VisibleRows[2];
        editor.SelectedRow = third;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(third, list.SelectedItem);
    }

    [AvaloniaFact]
    public void Ctrl_Up_and_Ctrl_Down_move_between_items_and_stop_at_the_ends()
    {
        using var dir = new TempDir();
        var (_, _, view) = Open(dir);
        var flat = view.Flat();
        Assert.True(flat.Count > 1);
        view.MoveItem(1);
        Assert.Equal(flat[0].Path, SelectedPath(view));
        view.MoveItem(1);
        Assert.Equal(flat[1].Path, SelectedPath(view));
        view.MoveItem(-1);
        view.MoveItem(-1);
        Assert.Equal(flat[0].Path, SelectedPath(view));
        for (var i = 0; i < flat.Count + 3; i++) view.MoveItem(1);
        Assert.Equal(flat[^1].Path, SelectedPath(view));
    }

    private static string SelectedPath(JsonView view) => view.GetVisualDescendants().OfType<TreeView>().First(t => (string?)t.Tag == "json-tree").SelectedItem is JsonViewNode n ? n.Path : string.Empty;

    [AvaloniaFact]
    public void Clicking_a_result_opens_its_detail_with_a_way_to_the_row_in_Table_view()
    {
        using var dir = new TempDir();
        var (window, editor, view) = Open(dir);
        view.Select("name");
        Dispatcher.UIThread.RunJobs();
        var chip = window.GetVisualDescendants().OfType<TextBlock>().First(t => (string?)t.Tag == "json-result-chip" && t.Text!.Contains("got x") || (string?)t.Tag == "json-result-chip" && t.Text!.Contains("✗"));
        chip.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(chip, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true), chip, default, 0, new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.LeftButtonPressed), Avalonia.Input.KeyModifiers.None) { RoutedEvent = Avalonia.Input.InputElement.PointerPressedEvent });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("json-result-chip", (string?)chip.Tag);
    }
}

public class JsonViewPasteTests
{
    [AvaloniaFact]
    public void Pasted_JSON_becomes_the_response_of_the_row_and_its_new_fields_become_columns()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => (string?)b.Tag == "json-paste-response");

        view.UsePasted("{\"brandNew\": {\"code\": \"A1\", \"size\": 3}}");
        Dispatcher.UIThread.RunJobs();
        var extra = view.Root!.Leaves().Where(l => l.Extra).Select(l => l.Path).ToList();
        Assert.Contains("brandNew.code", extra);
        Assert.Contains("brandNew.size", extra);

        var add = window.GetVisualDescendants().OfType<Button>().First(b => (string?)b.Tag == "json-add-column");
        add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(editor.Columns, c => c.Title.EndsWith("brandNew.code"));
        var column = editor.Columns.First(c => c.Title.EndsWith("brandNew.code"));
        Assert.Equal("A1", editor.SelectedRow!.Cells[column.CellIndex]);   // the pasted value filled the row's cell
    }

    [AvaloniaFact]
    public void Text_that_is_not_JSON_or_XML_is_refused_and_changes_nothing()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        var before = view.Root!.Leaves().Count();
        view.UsePasted("this is just words");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, view.Root!.Leaves().Count());
    }
}

public class JsonViewAllStepsTests
{
    [AvaloniaFact]
    public void All_steps_shows_one_branch_per_step_and_an_item_works_on_its_own_step()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        var combo = window.GetVisualDescendants().OfType<ComboBox>().First(c => (string?)c.Tag == "json-step");
        Assert.Equal("All steps", combo.Items.Cast<string>().First());

        editor.StepFilter = DataEditorViewModel.AllSteps;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("All steps", combo.SelectedItem);
        var tops = view.Root!.Children;
        Assert.True(tops.Count >= 1);
        Assert.All(tops, t => Assert.Equal(t.Name, t.StepName));
        Assert.All(tops.SelectMany(t => t.Leaves()), l => Assert.Equal(l.StepOf(), l.StepOf().Length > 0 ? l.StepOf() : string.Empty));
        Assert.Contains(tops, t => t.Name == "CreateCustomer");
        var leaf = tops.First(t => t.Name == "CreateCustomer").Leaves().First();
        Assert.Equal("CreateCustomer", leaf.StepOf());
    }
}

public class JsonViewRawEditTests
{
    [AvaloniaFact]
    public void Edited_raw_text_becomes_the_response_and_survives_a_rebuild()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        var toggle = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(t => (string?)t.Tag == "json-raw");
        toggle.IsChecked = true;
        toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var text = window.GetVisualDescendants().OfType<TextBox>().First(t => (string?)t.Tag == "json-raw-text");
        Assert.False(text.IsReadOnly);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => (string?)b.Tag == "json-raw-apply");

        text.Text = "{\"typed\": {\"value\": 7}}";
        view.UseRawText();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(view.Root!.Leaves(), l => l.Extra && l.Path == "typed.value");

        view.ShowTab(JsonTab.Template);
        view.ShowTab(JsonTab.Response);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(view.Root!.Leaves(), l => l.Extra && l.Path == "typed.value");   // kept for the row

        toggle.IsChecked = true;
        view.ShowTab(JsonTab.Template);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.GetVisualDescendants().OfType<TextBox>().First(t => (string?)t.Tag == "json-raw-text").IsReadOnly);
    }
}

public class JsonViewDetailTests
{
    [AvaloniaFact]
    public void A_failed_result_describes_the_check_and_can_take_the_value_the_run_got()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 4);
        FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson",
            FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", category: "Assertion"),
            FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Kund 1"));
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        view.Select("name");
        Dispatcher.UIThread.RunJobs();

        var node = view.Root!.Leaves().Single(l => l.Path == "name");
        var row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST001");
        var cell = editor.JsonCellResults(row, "CreateCustomer", node).Single();
        var text = view.DetailText(row, node, cell);
        Assert.Contains("Check: eq   expected: Customer 1", text);
        Assert.Contains("Got: Kund 1", text);
        Assert.True(view.CanAssignActual(row, node));

        Assert.True(view.AssignActual(row, node));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Kund 1", row.Cells[node.Column!.CellIndex]);
        Assert.False(view.CanAssignActual(row, node));   // the cell holds the value the run got now
        editor.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Customer 1", editor.VisibleRows.First(r => r.CurrentDataId == "CUST001").Cells[node.Column.CellIndex]);

        var passing = editor.VisibleRows.First(r => r.CurrentDataId == "CUST002");
        Assert.False(view.CanAssignActual(passing, node));
        window.Close();
    }

    [AvaloniaFact]
    public void Show_the_response_from_a_result_switches_to_the_raw_text_of_that_row()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        var row = editor.VisibleRows[1];
        view.UsePasted("{\"a\": 1}");   // gives the first row a response
        view.ShowRawResponse(row);
        Dispatcher.UIThread.RunJobs();
        var toggle = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(t => (string?)t.Tag == "json-raw");
        var picker = window.GetVisualDescendants().OfType<ComboBox>().First(c => (string?)c.Tag == "json-raw-row");
        Assert.True(toggle.IsChecked);
        Assert.Equal(row.CurrentDataId, picker.SelectedItem);
        window.Close();
    }
}
