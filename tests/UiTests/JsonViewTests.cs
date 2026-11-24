using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class JsonViewTests
{
    private static object[] Run() =>
    [
        FakeRun.Request(1, "CUST001", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "Customer 1", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST001", "CreateCustomer", result: "FAIL", category: "Assertion", env: "test"), FakeRun.Assertion(1, "CUST001", "CreateCustomer", "name", "eq", "Customer 1", "x", env: "test"),
        FakeRun.Request(1, "CUST002", "CreateCustomer", env: "dev"), FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Customer 2", result: "PASS", env: "dev"),
        FakeRun.Request(1, "CUST002", "CreateCustomer", env: "test"), FakeRun.Assertion(1, "CUST002", "CreateCustomer", "name", "eq", "Customer 2", "Customer 2", result: "PASS", env: "test"),
    ];

    private static (DataEditorWindow Window, DataEditorViewModel Editor, JsonView View) Open(TempDir dir, bool withRun = true)
    {
        var path = Samples.CreateLarge(dir, rows: 4);
        if (withRun) FakeRun.Write(FakeRun.Folder(path), $"events_{FakeRun.RunId}.ndjson", Run());
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        if (withRun) editor.ApplyResults(RunResultsReader.ReadLatest(editor.ResultsFolder));
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        return (window, editor, view);
    }

    private static T Tagged<T>(Avalonia.Visual root, string tag) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => (string?)c.Tag == tag);
    private static List<TextBox> Cells(Avalonia.Visual root) => root.GetVisualDescendants().OfType<TextBox>().Where(t => (string?)t.Tag == "json-cell").ToList();
    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void The_view_mode_list_has_a_JSON_view_that_shows_the_tree_of_the_step()
    {
        using var dir = new TempDir();
        var (window, editor, view) = Open(dir);
        Assert.Equal("{ }  JSON view", editor.ViewModes[3]);
        Assert.True(editor.ShowJsonView);
        Assert.Equal("CreateCustomer", editor.StepFilter);       // the view works on one step: the first was chosen
        Assert.Contains(view.Root!.Leaves(), l => l.Path == "name");
        Assert.False(window.FindControl<DataGrid>("Grid")!.IsVisible); // the table grid is hidden meanwhile
        editor.ViewModeIndex = 0;
        Assert.False(editor.ShowJson);
    }

    [AvaloniaFact]
    public void Picking_an_item_lists_its_value_for_every_row_and_environments_split_only_where_they_differ()
    {
        using var dir = new TempDir();
        var (window, editor, view) = Open(dir);
        view.Select("name");
        Dispatcher.UIThread.RunJobs();
        var cells = Cells(window);
        Assert.Equal(editor.VisibleRows.Count, cells.Count);
        Assert.Equal(editor.VisibleRows.Select(r => r.Cells[view.Root!.Leaves().Single(l => l.Path == "name").Column!.CellIndex]).ToArray(), cells.Select(c => c.Text).ToArray());

        var results = window.GetVisualDescendants().OfType<StackPanel>().Where(p => (string?)p.Tag == "json-result").ToList();
        var texts = results.Select(p => string.Join(" ", p.Children.OfType<TextBlock>().Select(t => t.Text))).ToList();
        Assert.Contains(texts, t => t.Contains("dev ✓") && t.Contains("test ✗ got x") && t.Contains("▲"));   // CUST001: the environments differ
        Assert.Contains(texts, t => t == "✓");                                                           // CUST002: they agree, one result
    }

    [AvaloniaFact]
    public void The_tree_shows_counts_and_the_environment_triangle_on_the_item()
    {
        using var dir = new TempDir();
        var (window, _, view) = Open(dir);
        var badges = window.GetVisualDescendants().OfType<TextBlock>().Where(t => (string?)t.Tag == "json-badge").Select(t => t.Text).ToList();
        Assert.Contains("✓ 1", badges); Assert.Contains("✗ 1", badges); Assert.Contains("▲", badges);
    }

    [AvaloniaFact]
    public void Editing_a_value_writes_the_cell_and_undo_takes_it_back()
    {
        using var dir = new TempDir();
        var (window, editor, view) = Open(dir);
        view.Select("name");
        Dispatcher.UIThread.RunJobs();
        var column = view.Root!.Leaves().Single(l => l.Path == "name").Column!;
        var box = Cells(window)[0];
        var before = editor.VisibleRows[0].Cells[column.CellIndex];
        box.Text = "Changed";
        box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
        Assert.Equal("Changed", editor.VisibleRows[0].Cells[column.CellIndex]);
        editor.UndoCommand.Execute(null);
        Assert.Equal(before, editor.VisibleRows[0].Cells[column.CellIndex]);
    }

    [AvaloniaFact]
    public void Several_rows_can_be_selected_filled_down_pasted_into_cleared_and_deleted()
    {
        using var dir = new TempDir();
        var (window, editor, view) = Open(dir);
        view.Select("name");
        Dispatcher.UIThread.RunJobs();
        var column = view.Root!.Leaves().Single(l => l.Path == "name").Column!;
        var cell = column.CellIndex;
        var list = Tagged<ListBox>(window, "json-rows");
        list.SelectAll();
        Assert.Equal(editor.VisibleRows.Count, list.SelectedItems!.Count);

        editor.VisibleRows[0].Cells[cell] = "same";
        Click(Tagged<Button>(window, "json-fill"));
        Assert.All(editor.VisibleRows, r => Assert.Equal("same", r.Cells[cell]));

        Assert.Equal(editor.VisibleRows.Count, editor.PasteColumnValues(editor.VisibleRows.ToList(), cell, string.Join("\n", editor.VisibleRows.Select((_, i) => "v" + i))));
        Assert.Equal(["v0", "v1", "v2", "v3"], editor.VisibleRows.Select(r => r.Cells[cell]).ToArray());
        Assert.Equal("v0\r\nv1\r\nv2\r\nv3".Replace("\r\n", Environment.NewLine), DataEditorViewModel.CopyColumnValues(editor.VisibleRows, cell));

        Assert.Equal(2, editor.ClearColumnValues([editor.VisibleRows[0], editor.VisibleRows[1]], cell));
        Assert.Equal(string.Empty, editor.VisibleRows[1].Cells[cell]);

        var rows = editor.VisibleRows.Count;
        Click(Tagged<Button>(window, "json-add-row"));
        Assert.Equal(rows + 1, editor.VisibleRows.Count);
        list = Tagged<ListBox>(window, "json-rows");
        list.SelectedItems!.Clear();
        list.SelectedItems.Add(editor.VisibleRows[^1]);
        Click(Tagged<Button>(window, "json-delete"));
        Assert.Equal(rows, editor.VisibleRows.Count);
    }

    [AvaloniaFact]
    public void Filters_of_the_table_decide_which_rows_the_item_shows()
    {
        using var dir = new TempDir();
        var (window, editor, view) = Open(dir);
        view.Select("name");
        editor.ShowFailedOnly = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["CUST001"], editor.VisibleRows.Select(r => r.CurrentDataId).ToArray());
        Assert.Single(Cells(window));
    }

    [AvaloniaFact]
    public void The_request_tabs_show_variables_without_results_and_the_wizard_opens_for_a_value()
    {
        using var dir = new TempDir();
        var (window, editor, view) = Open(dir, withRun: false);
        view.ShowTab(JsonTab.Template);
        Dispatcher.UIThread.RunJobs();
        var leaf = view.Root!.Leaves().FirstOrDefault(l => l.Column is not null);
        if (leaf is null) { view.ShowTab(JsonTab.Config); leaf = view.Root!.Leaves().First(l => l.Column is not null); }
        view.Select(leaf.Path);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(editor.VisibleRows.Count, Cells(window).Count);
        Assert.Empty(window.GetVisualDescendants().OfType<StackPanel>().Where(p => (string?)p.Tag == "json-result"));
        var opened = 0;
        view.OpenWizard = (_, _, _, _) => opened++;
        Click(Tagged<Button>(window, "json-wizard"));
        Assert.Equal(1, opened);
    }

    [AvaloniaFact]
    public void A_template_variable_without_a_column_is_marked_extra_and_can_be_added()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var templates = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "Templates");
        Directory.CreateDirectory(templates);
        File.WriteAllText(System.IO.Path.Combine(templates, "create.json"), "{\"name\":\"_name_\",\"email\":\"_email_\",\"nickname\":\"_nickname_\"}");
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        view.ShowTab(JsonTab.Template);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["email", "name", "nickname"], view.Root!.Leaves().Select(l => l.Name).OrderBy(n => n).ToArray());
        var extra = view.Root.Leaves().Single(l => l.Name == "nickname");
        Assert.True(extra.Extra);
        Assert.Null(extra.Column);
        view.ShowTab(JsonTab.Config);
        Assert.Contains(view.Root!.Leaves(), l => l.Name == "country");   // not in the template
        Assert.Contains(view.Root.Leaves(), l => l.Name == "dontrun" && l.Shared);
        view.ShowTab(JsonTab.Template);
        view.Select("nickname");
        Dispatcher.UIThread.RunJobs();
        Click(Tagged<Button>(window, "json-add-column"));
        Assert.Contains(editor.Columns, c => c.Title == "CreateCustomer.nickname");
        Assert.NotNull(view.Root!.Leaves().Single(l => l.Name == "nickname").Column);
    }

    [AvaloniaFact]
    public void The_wizard_button_opens_the_tables_own_cell_wizard()
    {
        using var dir = new TempDir();
        var (window, _, view) = Open(dir);
        view.Select("name");
        Dispatcher.UIThread.RunJobs();
        Click(Tagged<Button>(window, "json-wizard"));
        var layer = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window)!;
        Assert.Contains(layer.GetVisualDescendants().OfType<Button>(), b => (string?)b.Tag == "cell-wizard-ok");
        Assert.Contains(layer.GetVisualDescendants().OfType<Control>(), c => (string?)c.Tag == "bug-bar");   // an expected cell: the known-bug bar is there too
    }

    [AvaloniaFact]
    public void All_extra_fields_of_the_response_can_be_added_at_once_and_filled_from_the_responses()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        Directory.CreateDirectory(dir.Combine("History"));
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer";
            sheet.Cell(2, 4).Value = "{\"name\":\"Customer 1\",\"phone\":\"123\",\"address\":{\"city\":\"Oslo\"}}";
            sheet.Cell(3, 1).Value = "r1"; sheet.Cell(3, 2).Value = "CUST002"; sheet.Cell(3, 3).Value = "CreateCustomer";
            sheet.Cell(3, 4).Value = "{\"name\":\"Customer 2\",\"phone\":\"456\",\"address\":{\"city\":\"Bergen\"}}";
            book.SaveAs(System.IO.Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        var window = DataEditorWindow.Open(path);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.Shell.Current!;
        editor.ViewModeIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var view = window.GetVisualDescendants().OfType<JsonView>().Single();
        Assert.Equal(["address.city", "phone"], DataEditorViewModel.JsonExtras(view.Root!).Select(n => n.Path).OrderBy(p => p).ToArray());

        Tagged<ComboBox>(window, "json-fill-choice").SelectedIndex = 2;   // every row from its own response
        var add = Tagged<Button>(window, "json-add-column");
        Assert.Equal("＋ Add 2 columns", add.Content);
        Click(add);

        var phone = editor.Columns.Single(c => c.Title == "CreateCustomer.phone");
        var city = editor.Columns.Single(c => c.Title == "CreateCustomer.address.city");
        Assert.Equal(["123", "456", ""], editor.VisibleRows.Select(r => r.Cells[phone.CellIndex]).ToArray());   // CUST003 has no stored response
        Assert.Equal("Bergen", editor.VisibleRows[1].Cells[city.CellIndex]);
        Assert.Empty(DataEditorViewModel.JsonExtras(view.Root!));
        editor.UndoCommand.Execute(null);                               // one undo step takes the columns back
        Assert.DoesNotContain(editor.Columns, c => c.Title == "CreateCustomer.phone");
    }
}
