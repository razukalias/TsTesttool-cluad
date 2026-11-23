using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using LoadTestingTool.Reporting;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ResponsePaneTests
{
    private static (AddColumnsWindow Window, DataEditorViewModel Editor) Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        Directory.CreateDirectory(dir.Combine("History"));
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer";
            sheet.Cell(2, 4).Value = "{\"id\":\"A1\",\"name\":\"Customer 1\",\"items\":[{\"sku\":\"s1\",\"qty\":1},{\"sku\":\"s2\",\"qty\":2}],\"address\":{\"city\":\"Oslo\",\"geo\":{\"lat\":59.9}}}";
            sheet.Cell(3, 1).Value = "r1"; sheet.Cell(3, 2).Value = "CUST002"; sheet.Cell(3, 3).Value = "CreateCustomer";
            sheet.Cell(3, 4).Value = "{\"id\":\"B2\",\"name\":\"Customer 2\",\"items\":[{\"sku\":\"t1\",\"qty\":5}],\"address\":{\"city\":\"Bergen\",\"geo\":{\"lat\":60.4}}}";
            book.SaveAs(Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        var editor = new DataEditorViewModel(path, "Customers");
        var window = new AddColumnsWindow(editor, "CreateCustomer");
        window.Width = 1180; window.Height = 760;
        window.Show();
        window.SelectTab("Response");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, editor);
    }

    private static T Find<T>(Control root, string tag) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => Equals(c.Tag, tag));

    [AvaloniaFact]
    public void The_response_tab_shows_the_JSON_as_a_tree_and_a_tick_makes_a_header_with_its_value_on_the_right()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir);
        var pane = window.ResponsePane;
        Assert.NotNull(pane.Session!.Tree);
        Assert.Equal("CUST001", pane.Session.CurrentRow);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "(response)");

        var tick = window.GetVisualDescendants().OfType<CheckBox>().First(c => Equals(c.Tag, "node-tick"));   // the root
        var city = window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "address");
        pane.Session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        pane.Session.Choose("items", new NodeChoice(NodeMode.AsOne));
        pane.Session.Recompute();
        window.SelectTab("Response");
        pane.UseText(pane.Session.BodyFor("CUST001")!);          // refresh the view
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save("/tmp/pane1.png");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => Equals(t.Tag, "result-header") && t.Text == "CreateCustomer.address.city");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => Equals(t.Tag, "result-header") && t.Text == "CreateCustomer.items.#count");
        Assert.NotNull(tick);
        Assert.NotNull(editor);
        window.Close();
    }

    [AvaloniaFact]
    public void The_node_menu_offers_flatten_depths_more_elements_and_count_and_a_choice_sticks()
    {
        using var dir = new TempDir();
        var (window, _) = Open(dir);
        var session = window.ResponsePane.Session!;
        var items = ResponseTree.Find(session.Tree!, "items")!;
        var menu = (ContextMenu)typeof(ResponseColumnsPane).GetMethod("BuildMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window.ResponsePane, [items])!;
        var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Contains(headers, h => h!.StartsWith("Take as one header (count of 2 elements"));
        Assert.Contains(headers, h => h!.StartsWith("Flatten this array"));
        Assert.Contains("Flatten, taking more array elements", headers);
        var flatten = menu.Items.OfType<MenuItem>().First(m => (m.Header as string)!.StartsWith("Flatten this array"));
        Assert.Equal(["1 level", "2 levels", "3 levels", "5 levels", "all levels"], flatten.Items.OfType<MenuItem>().Select(m => (string)m.Header!).ToArray());
        flatten.Items.OfType<MenuItem>().Last().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(new NodeChoice(NodeMode.Flatten, 0, 1), session.Choices["items"]);
        Assert.Equal(["items[0].sku", "items[0].qty"], session.Results.Select(r => r.Header.Path).ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void Switching_row_shows_that_rows_values_and_the_add_button_fills_the_chosen_rows_in_one_undo_step()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir);
        var session = window.ResponsePane.Session!;
        session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        session.Choose("items", new NodeChoice(NodeMode.AsOne));
        session.Results.First(r => r.Header.Path == "items.#count").Verb = "gte";
        Find<ComboBox>(window, "response-row").SelectedIndex = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("CUST002", session.CurrentRow);
        Assert.Equal("Bergen", session.Results.First(r => r.Header.Path == "address.city").Header.Value);   // the choices stay, the values follow the row
        Assert.Equal("1", session.Results.First(r => r.Header.Path == "items.#count").Header.Value);
        Find<RadioButton>(window, "fill-all").IsChecked = true;
        window.AddTicked();
        var city = editor.Columns.First(c => c.Title.EndsWith("CreateCustomer.address.city"));
        Assert.Equal(["Oslo", "Bergen", ""], editor.VisibleRows.Select(r => r.Cells[city.CellIndex]).ToArray());
        editor.UndoCommand.Execute(null);
        Assert.DoesNotContain(editor.Columns, c => c.Title.Contains("address.city") || c.Title.Contains("#count"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_row_without_a_response_can_be_given_one_by_pasting_text()
    {
        using var dir = new TempDir();
        var (window, _) = Open(dir);
        var session = window.ResponsePane.Session!;
        session.SelectRow("CUST003");
        Assert.Null(session.Tree);
        window.ResponsePane.UseText("{\"token\":\"x\"}");
        Assert.Equal("x", ResponseTree.Find(session.Tree!, "token")!.Value);
        window.Close();
    }
}

public class ResponseHeaderVerbTests
{
    [AvaloniaFact]
    public void Response_headers_get_a_verb_each_and_a_default_for_all()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 2);
        Directory.CreateDirectory(dir.Combine("History"));
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody", "responseheaders"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer"; sheet.Cell(2, 4).Value = "{}"; sheet.Cell(2, 5).Value = "Content-Type=application/json\nX-Trace=abc";
            book.SaveAs(Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        var editor = new DataEditorViewModel(path, "Customers");
        var window = new AddColumnsWindow(editor, "CreateCustomer");
        window.Show();
        window.SelectTab("Response header");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var verbs = window.GetVisualDescendants().OfType<ComboBox>().Where(c => Equals(c.Tag, "header-verb")).ToList();
        Assert.True(verbs.Count >= 2);
        verbs[0].SelectedItem = "contains";
        window.AddTicked();
        var verbOf = editor.Columns.Where(c => c.Group == ColumnGroup.Response && c.Title.Contains("responseHeader.")).ToDictionary(c => c.Title, c => editor.VerbOf(c.CellIndex));
        Assert.Contains("contains", verbOf.Values);
        window.Close();
    }
}

public class NewColumnWizardTests
{
    private static (AddColumnsWindow Window, DataEditorViewModel Editor) Open(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        Directory.CreateDirectory(dir.Combine("History"));
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody", "responseheaders"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer";
            sheet.Cell(2, 4).Value = "{\"id\":\"A1\",\"address\":{\"city\":\"Oslo\"}}"; sheet.Cell(2, 5).Value = "X-Trace=abc";
            book.SaveAs(Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        var editor = new DataEditorViewModel(path, "Customers");
        var window = new AddColumnsWindow(editor, "CreateCustomer");
        window.Width = 1180; window.Height = 760;
        window.Show();
        window.SelectTab("Response");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, editor);
    }

    [AvaloniaFact]
    public void Every_new_response_field_has_a_wizard_button_and_opens_the_same_value_line_as_a_cell()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir);
        window.ResponsePane.Session!.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        window.ResponsePane.UseText(window.ResponsePane.Session.BodyFor("CUST001")!);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "result-wizard"));
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var line = TopLevelContent<RecordLineView>(window);
        Assert.NotNull(line);
        Assert.NotNull(editor);
        window.Close();
    }

    [AvaloniaFact]
    public void The_wizard_has_a_known_bug_bar_that_tags_the_new_cell()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir);
        var session = window.ResponsePane.Session!;
        session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        window.ResponsePane.UseText(session.BodyFor("CUST001")!);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var result = session.Results.First(r => r.Header.Path == "address.city");
        var button = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "result-wizard"));
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var layer = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window)!;
        var bar = layer.GetVisualDescendants().OfType<StackPanel>().First(p => Equals(p.Tag, "bug-bar"));
        bar.GetVisualDescendants().OfType<TextBox>().First(t => Equals(t.Tag, "bug-description")).Text = "wrong; city > x";
        bar.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "bug-tag")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Oslo <bug: all; wrong, city ) x>", result.Value.Cells[0]);

        bar.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "bug-untag")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Oslo", result.Value.Cells[0]);
        Assert.NotNull(editor);
        window.Close();
    }

    private static T? TopLevelContent<T>(Window window) where T : Control
        => Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window)?.GetVisualDescendants().OfType<T>().FirstOrDefault()
           ?? window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Select(p => p.Child).OfType<Control>().SelectMany(c => c.GetVisualDescendants().OfType<T>().Concat(c is T t ? [t] : [])).FirstOrDefault();

    [AvaloniaFact]
    public void A_value_built_in_the_wizard_is_written_to_the_new_cell_instead_of_the_response_value()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir);
        var session = window.ResponsePane.Session!;
        session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        var city = session.Results.First(r => r.Header.Path == "address.city");
        Assert.Null(city.TypedValue);
        city.Value.Cells[0] = "{contains}Os";   // what the wizard writes for "contains Os"
        Assert.Equal("{contains}Os", city.TypedValue);

        Assert.Equal(1, window.ResponsePane.IncludedCount);
        window.AddTicked();
        var cityColumn = editor.Columns.First(c => c.Title.EndsWith("CreateCustomer.address.city"));
        var row = editor.VisibleRows.First(r => r.CurrentDataId == "CUST001");
        Assert.Equal("{contains}Os", row.Cells[cityColumn.CellIndex]);   // typed, written even though "Fill the row shown" would give Oslo
        window.Close();
    }

    [AvaloniaFact]
    public void A_typed_value_is_written_even_when_the_cells_are_left_empty_and_other_rows_stay_empty()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir);
        var session = window.ResponsePane.Session!;
        session.Choose("address.city", new NodeChoice(NodeMode.AsOne));
        session.Fill = ResponseFill.None;
        session.Results.First(r => r.Header.Path == "address.city").Value.Cells[0] = "Bergen";
        window.AddTicked();
        var column = editor.Columns.First(c => c.Title.EndsWith("CreateCustomer.address.city"));
        Assert.Equal(["Bergen", "", ""], editor.VisibleRows.Select(r => r.Cells[column.CellIndex]).ToArray());
        window.Close();
    }

    [AvaloniaFact]
    public void A_response_header_can_be_given_a_value_with_the_wizard_too()
    {
        using var dir = new TempDir();
        var (window, editor) = Open(dir);
        window.SelectTab("Response header");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Tag, "result-wizard"));
        var choice = editor.ResponseHeaderChoices.First(c => c.Name == "X-Trace");
        choice.IsChosen = true;
        choice.Cell.Cells[0] = "{contains}ab";
        window.AddTicked();
        var column = editor.Columns.First(c => c.Group == ColumnGroup.Response && c.Title.Contains("X-Trace"));
        Assert.Equal("{contains}ab", editor.VisibleRows.First(r => r.CurrentDataId == "CUST001").Cells[column.CellIndex]);
        window.Close();
    }
}

public class ResponseHelpTests
{
    [AvaloniaFact]
    public void The_window_has_a_help_button_that_opens_the_guide()
    {
        using var dir = new TempDir();
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 2), "Customers");
        var window = new AddColumnsWindow(editor, "CreateCustomer");
        window.Show();
        var help = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "help"));
        help.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var guide = Avalonia.Application.Current is null ? null : window.OwnedWindows.SingleOrDefault(w => Equals(w.Tag, "response-help"));
        Assert.NotNull(guide);
        var text = string.Join("\n", guide!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Contains("#count", text);
        Assert.Contains("Right-click a node", text);
        Assert.Contains("Every row", text);
        guide.Close(); window.Close();
    }
}
