using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LoadTestingTool.Editing;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class RecordViewTests
{
    private static DataEditorViewModel Open(TempDir dir)
    {
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3), "Customers");
        editor.OpenRecordTabCommand.Execute(null);
        return editor;
    }

    [Fact]
    public void Record_view_is_a_third_mode_that_shows_the_selected_row_as_request_and_expected_lines()
    {
        using var dir = new TempDir();
        var editor = Open(dir);

        Assert.True(editor.ShowRecord);
        Assert.False(editor.ShowData);
        Assert.False(editor.ShowSteps);
        Assert.Equal(["dontrun", "CreateCustomer.name", "CreateCustomer.email", "CreateCustomer.country"], editor.RequestLines.Select(l => l.Title));
        Assert.Equal(["CreateCustomer.statusCode", "CreateCustomer.id", "CreateCustomer.name"], editor.ExpectedLines.Select(l => l.Title));
        Assert.All(editor.ExpectedLines, l => Assert.True(l.HasCondition));
        Assert.Equal("notempty", editor.ExpectedLines[1].Verb);          // the cell's own {notempty}
        Assert.False(editor.ExpectedLines[1].ShowsValue);                // no value for that condition
        Assert.Equal("eq", editor.ExpectedLines[0].Verb);                // {eq} in the header

        editor.OpenDataTabCommand.Execute(null);
        Assert.True(editor.ShowData);
        Assert.False(editor.ShowRecord);
    }

    [Fact]
    public void Adding_removing_and_moving_parts_writes_the_joined_text_into_the_cell_and_can_be_undone()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var line = editor.RequestLines.Single(l => l.Title == "CreateCustomer.email");
        var row = editor.SelectedRow!;
        var cell = row.Cells[editor.Columns.First(c => c.Title == "CreateCustomer.email").CellIndex];
        Assert.Equal("customer1@example.test", cell);

        line.Add(ValuePart.Random("6"));

        var index = editor.Columns.First(c => c.Title == "CreateCustomer.email").CellIndex;
        Assert.Equal("customer1@example.test<randomnumber:6>", row.Cells[index]);
        Assert.True(editor.IsDirty);
        Assert.Equal([ValuePartKind.Text, ValuePartKind.RandomNumber], line.Parts.Select(p => p.Kind));

        line.Move(1, -1);
        Assert.Equal("<randomnumber:6>customer1@example.test", row.Cells[index]);
        line.Remove(0);
        Assert.Equal("customer1@example.test", row.Cells[index]);

        editor.UndoCommand.Execute(null);   // each change is an undo step, like a cell edit
        Assert.Equal("<randomnumber:6>customer1@example.test", editor.SelectedRow!.Cells[index]);   // undo rebuilds the rows
        Assert.Equal("<randomnumber:6>customer1@example.test", ValueParts.Format(editor.RequestLines.Single(l => l.Title == "CreateCustomer.email").Parts));   // the line follows the cell
    }

    [Fact]
    public void A_condition_in_plain_words_is_stored_as_a_verb_in_the_cell_only_when_it_differs_from_the_header()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var line = editor.ExpectedLines[2];   // header {eq}CreateCustomer.name
        var index = editor.Columns.First(c => c.Title == "CreateCustomer.name" && c.Group == ColumnGroup.Response).CellIndex;
        Assert.Equal("equals", line.ConditionWords);

        line.ConditionWords = "contains";
        Assert.Equal("{contains}Customer 1", editor.SelectedRow!.Cells[index]);

        line.ConditionWords = "equals";
        Assert.Equal("Customer 1", editor.SelectedRow.Cells[index]);

        line.ConditionWords = "is not empty";   // no value needed: the parts go away
        Assert.Equal("{notempty}", editor.SelectedRow.Cells[index]);
        Assert.False(line.ShowsValue);
    }

    [Fact]
    public void Record_view_follows_the_selected_row()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        editor.SelectedRow = editor.VisibleRows[2];
        Assert.Equal("Customer 3", ValueParts.Format(editor.RequestLines.Single(l => l.Title == "CreateCustomer.name").Parts));
        Assert.Contains("CUST003", editor.RecordTitle);
    }
}

public class DrillDownMenuTests
{
    private static RecordMenu.Sources Sources() => new(["email", "name"], ["Login.token", "Login.id"], ["targeturl"], ["Login"]);

    [AvaloniaFact]
    public void The_menu_opens_one_list_at_a_time_with_a_breadcrumb_and_a_filter_on_every_level()
    {
        ValuePart? picked = null;
        var menu = new DrillDownMenu(RecordMenu.Root(Sources()), p => picked = p);
        var window = new Window { Content = menu };
        window.Show();

        Assert.Equal(["Text", "From  ›", "Random number  ›", "GUID  ›", "Date and time  ›", "File  ›"], menu.Shown.Select(n => n.Display));

        var filter = (TextBox)FindTagged(menu, "menu-filter");
        filter.Text = "ran";
        Assert.Equal(["Random number"], menu.Shown.Select(n => n.Title));
        filter.Text = "";

        SelectByTitle(menu, "From"); menu.Choose();
        Assert.Equal(["From"], menu.Path);
        Assert.Equal(["request", "response (an earlier step's result)", "config"], menu.Shown.Select(n => n.Title));

        SelectByTitle(menu, "request"); menu.Choose();
        SelectByTitle(menu, "email"); menu.Choose();
        Assert.Equal(["From", "request", "email"], menu.Path);
        SelectByTitle(menu, "this row"); menu.Choose();

        Assert.Equal("<From_request_email_r>", picked!.Raw);
    }

    [AvaloniaFact]
    public void Final_items_add_the_part_and_prompting_items_ask_for_text_first()
    {
        ValuePart? picked = null;
        var menu = new DrillDownMenu(RecordMenu.Root(Sources()), p => picked = p);
        new Window { Content = menu }.Show();

        SelectByTitle(menu, "GUID"); menu.Choose();
        SelectByTitle(menu, "Without dashes"); menu.Choose();
        Assert.Equal("<guid:N>", picked!.Raw);

        picked = null;
        menu.Reset();
        SelectByTitle(menu, "Text"); menu.Choose();
        var input = (TextBox)FindTagged(menu, "menu-input");
        Assert.True(input.IsEffectivelyVisible);
        input.Text = "Bearer ";
        ((Button)FindTagged(menu, "menu-input-ok")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Bearer ", picked!.Raw);
    }

    [Fact]
    public void Response_fields_come_as_one_flat_list_with_dotted_names()
    {
        var from = RecordMenu.Root(Sources()).Single(n => n.Title == "From").Children!();
        var fields = from.Single(n => n.Title.StartsWith("response")).Children!();
        Assert.Equal(["Login.token", "Login.id"], fields.Select(n => n.Title));
        Assert.Equal("<From_response_Login.token_r>", fields[0].Result!.Raw);
        Assert.Single(RecordMenu.Filter(fields, "tok"));
    }

    private static void SelectByTitle(DrillDownMenu menu, string title)
    {
        var list = (ListBox)FindTagged(menu, "menu-list");
        list.SelectedItem = menu.Shown.First(n => n.Title == title);
    }

    private static Control FindTagged(Control root, string tag)
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<Control>().First(c => Equals(c.Tag, tag));
}

public class RecordViewWindowTests
{
    [AvaloniaFact]
    public void The_record_view_draws_chips_a_plus_button_and_condition_dropdowns()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 860;
        window.Show();
        var vm = window.ViewModel;
        vm.Preview = null;
        vm.OpenRecordTabCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var host = window.FindControl<Border>("RecordHost")!;
        Assert.True(host.IsVisible);
        var view = Assert.IsType<RecordView>(host.Child);
        view.Draw();
        var all = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Control>().ToList();
        Assert.True(all.Count(c => Equals(c.Tag, "chip")) >= 5);
        Assert.True(all.Count(c => Equals(c.Tag, "add-part")) >= 6);
        Assert.Equal(3, all.Count(c => Equals(c.Tag, "condition")));
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-record-view.png"));
    }
}

public class RecordToolsTests
{
    private static DataEditorViewModel Open(TempDir dir, int rows = 3)
    {
        var path = Samples.CreateLarge(dir, rows);
        Directory.CreateDirectory(dir.Combine("Templates"));
        File.WriteAllText(System.IO.Path.Combine(dir.Combine("Templates"), "create.json"), "{\"name\":\"_name_\",\"extra\":\"_newField_\",\"again\":\"_newField_\",\"token\":\"_Login.token_\"}");
        var editor = new DataEditorViewModel(path, "Customers");
        editor.OpenRecordTabCommand.Execute(null);
        return editor;
    }

    [Fact]
    public void Template_placeholders_without_a_column_are_offered_and_added_as_Step_dot_variable()
    {
        using var dir = new TempDir();
        var editor = Open(dir);

        editor.FindTemplateColumns();
        Assert.Equal(["CreateCustomer.newField"], editor.TemplateColumns.Select(c => c.Header));   // name has a column, Login.token is a response

        Assert.Equal(1, editor.AddTemplateColumns());
        Assert.Contains("CreateCustomer.newField", editor.RequestLines.Select(l => l.Title));
        Assert.True(editor.IsDirty);
        editor.FindTemplateColumns();
        Assert.Empty(editor.TemplateColumns);
    }

    [Fact]
    public void Copy_to_rows_writes_the_stored_text_into_the_same_column_of_the_other_rows()
    {
        using var dir = new TempDir();
        var editor = Open(dir, rows: 4);
        var line = editor.RequestLines.Single(l => l.Title == "CreateCustomer.email");
        line.Add(ValuePart.Random("6"));
        var column = editor.Columns.First(c => c.Title == "CreateCustomer.email").CellIndex;

        var copied = editor.CopyLineToRows(line, DataEditorViewModel.CopyScope.TestcaseRows);

        Assert.Equal(3, copied);
        Assert.All(editor.VisibleRows, r => Assert.Equal("customer1@example.test<randomnumber:6>", r.Cells[column]));
        Assert.Equal(editor.VisibleRows[0], editor.SelectedRow);
        editor.UndoCommand.Execute(null);   // the copy can be undone
        Assert.True(editor.VisibleRows.Count(r => r.Cells[column].EndsWith("<randomnumber:6>")) < 4);
    }

    [Fact]
    public void Lines_warn_about_missing_columns_unknown_steps_and_steps_that_run_later()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var line = editor.RequestLines.Single(l => l.Title == "CreateCustomer.country");
        Assert.Empty(line.Warnings);

        line.Add(ValuePart.From("request", "nosuchcolumn"));
        Assert.Contains(line.Warnings, w => w.Contains("no column nosuchcolumn"));
        line.SetRaw("<From_response_Ghost.id_r>");
        Assert.Contains(line.Warnings, w => w.Contains("no step named Ghost"));
        line.SetRaw("<From_response_CreateCustomer.id_r>");
        Assert.Contains(line.Warnings, w => w.Contains("runs after this step"));
        line.SetRaw("<From_request_CreateCustomer.email_r>");
        Assert.Empty(line.Warnings);
    }
}

public class RecordToolsWindowTests
{
    [AvaloniaFact]
    public void The_record_view_shows_warnings_and_has_copy_and_template_buttons()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 860;
        window.Show();
        var vm = window.ViewModel;
        vm.OpenRecordTabCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.RequestLines.Single(l => l.Title == "CreateCustomer.country").SetRaw("<From_request_nosuchcolumn_r>");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var view = Assert.IsType<RecordView>(window.FindControl<Border>("RecordHost")!.Child);
        view.Draw();
        var all = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<Control>().ToList();
        Assert.Contains(all.OfType<TextBlock>(), t => Equals(t.Tag, "line-warning") && t.IsVisible && t.Text!.Contains("nosuchcolumn"));
        Assert.True(all.Count(c => Equals(c.Tag, "copy-to-rows")) >= 6);
        Assert.Single(all, c => Equals(c.Tag, "add-columns-open"));
        Assert.DoesNotContain(all, c => Equals(c.Tag, "template-columns") || Equals(c.Tag, "response-fields"));   // merged into the one button
    }
}

public class ResponseFieldsTests
{
    private const string Body = "{\"id\":\"abc-1\",\"name\":\"Customer 1\",\"address\":{\"city\":\"Malmö\"},\"items\":[{\"sku\":\"S1\"},{\"sku\":\"S2\"}]}";

    private static DataEditorViewModel Open(TempDir dir, bool withHistory = true)
    {
        var path = Samples.CreateLarge(dir, rows: 2);
        if (withHistory)
        {
            Directory.CreateDirectory(dir.Combine("History"));
            using var book = new ClosedXML.Excel.XLWorkbook();
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "r1"; sheet.Cell(2, 2).Value = "CUST001"; sheet.Cell(2, 3).Value = "CreateCustomer"; sheet.Cell(2, 4).Value = Body;
            book.SaveAs(System.IO.Path.Combine(dir.Combine("History"), "run1.xlsx"));
        }
        var editor = new DataEditorViewModel(path, "Customers");
        editor.OpenRecordTabCommand.Execute(null);
        return editor;
    }

    [Fact]
    public void The_From_menu_lists_the_stored_responses_fields_with_the_step_name_in_front()
    {
        using var dir = new TempDir();
        var editor = Open(dir);
        var fields = RecordMenu.Root(editor.MenuSources()).Single(n => n.Title == "From").Children!()
            .Single(n => n.Title.StartsWith("response")).Children!().Select(n => n.Title).ToList();

        Assert.Contains("CreateCustomer.address.city", fields);
        Assert.Contains("CreateCustomer.items[0].sku", fields);
        Assert.Contains("CreateCustomer.statusCode", fields);   // from the workbook's own expected columns
        Assert.Equal(fields.Count, fields.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

public class ResponseFieldsWindowTests
{
    [AvaloniaFact]
    public void The_add_columns_form_has_request_response_and_response_header_tabs()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 2), "Customers");
        window.Width = 1440; window.Height = 860;
        window.Show();
        window.ViewModel.OpenRecordTabCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var form = new AddColumnsWindow(window.ViewModel);
        form.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Request", "Response", "Response header"], form.GetVisualDescendants().OfType<TabItem>().Select(t => (string)t.Header!));   // only the selected tab's content is in the tree
        form.SelectTab("Response");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var response = form.GetVisualDescendants().OfType<Control>().ToList();
        Assert.Equal(["CreateCustomer"], response.OfType<ComboBox>().First(c => Equals(c.Tag, "response-step")).ItemsSource!.Cast<string>());
        Assert.Contains(response.OfType<TextBlock>(), t => Equals(t.Tag, "response-note") && t.Text!.Contains("No stored response"));
        form.SelectTab("Response header");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(form.GetVisualDescendants().OfType<TextBlock>(), t => Equals(t.Tag, "header-note") && t.Text!.Contains("No stored response headers"));
        form.Close();
    }
}

public class PickedFileTests
{
    [Fact]
    public void A_picked_file_gets_a_portable_path_and_one_outside_the_application_is_copied_into_Files()
    {
        using var dir = new TempDir();
        var app = dir.Combine("app");
        var inst = System.IO.Path.Combine(app, "Instances", "Main");
        Directory.CreateDirectory(System.IO.Path.Combine(inst, "Files"));
        Directory.CreateDirectory(System.IO.Path.Combine(app, "Shared"));
        File.WriteAllText(System.IO.Path.Combine(inst, "Files", "a.txt"), "a");
        File.WriteAllText(System.IO.Path.Combine(app, "Shared", "s.txt"), "s");
        var outsideDir = dir.Combine("elsewhere"); Directory.CreateDirectory(outsideDir);
        var outside = System.IO.Path.Combine(outsideDir, "o.txt"); File.WriteAllText(outside, "o");

        Assert.Equal(("a.txt", null), PickedFile.Prepare(inst, System.IO.Path.Combine(inst, "Files", "a.txt")));
        var shared = PickedFile.Prepare(inst, System.IO.Path.Combine(app, "Shared", "s.txt"));
        Assert.Equal("../../Shared/s.txt", shared.Path); Assert.Contains("keeps working when the application is moved", shared.Note);
        var copied = PickedFile.Prepare(inst, outside);
        Assert.Equal("o.txt", copied.Path); Assert.Contains("copied", copied.Note);
        Assert.Equal("o", File.ReadAllText(System.IO.Path.Combine(inst, "Files", "o.txt")));
        Assert.Equal("o2.txt", PickedFile.Prepare(inst, outside).Path);   // the name is taken: a free one
    }

    [Fact]
    public void The_file_menu_offers_pickers_that_report_the_choice_for_text_and_base64()
    {
        var calls = new List<bool>();
        var file = Assert.Single(RecordMenu.Root(new(["a"], [], [], []), null, new RecordMenu.FileHooks((b, _) => calls.Add(b))), n => n.Title == "File");
        var items = file.Children!();
        items.Single(n => n.Title == "Pick a file as text…").Open!(_ => { });
        items.Single(n => n.Title == "Pick a file as Base64…").Open!(_ => { });
        Assert.Equal([false, true], calls);
        Assert.Contains(items, n => n.Title == "File content as text");   // typing a name still works
        Assert.DoesNotContain(RecordMenu.Root(new(["a"], [], [], [])).Single(n => n.Title == "File").Children!(), n => n.Title.StartsWith("Pick"));
    }
}

public class PickedFileStepTests
{
    [Fact]
    public void A_step_setting_gets_a_path_inside_the_instance_and_an_outside_file_is_copied_into_the_kind_of_folder()
    {
        using var dir = new TempDir();
        var inst = dir.Combine("Main");
        Directory.CreateDirectory(System.IO.Path.Combine(inst, "Files", "sub"));
        File.WriteAllText(System.IO.Path.Combine(inst, "Files", "sub", "a.txt"), "a");
        var outsideDir = dir.Combine("elsewhere"); Directory.CreateDirectory(outsideDir);
        var q = System.IO.Path.Combine(outsideDir, "q.graphql"); File.WriteAllText(q, "{ me }");

        Assert.Equal(("Files/sub/a.txt", null), PickedFile.PrepareForStep(inst, System.IO.Path.Combine(inst, "Files", "sub", "a.txt"), "Files"));
        var copied = PickedFile.PrepareForStep(inst, q, "Queries");
        Assert.Equal("Queries/q.graphql", copied.Path); Assert.Contains("only reads files inside the instance", copied.Note);
        Assert.Equal("{ me }", File.ReadAllText(System.IO.Path.Combine(inst, "Queries", "q.graphql")));
        Assert.Equal("Queries/q2.graphql", PickedFile.PrepareForStep(inst, q, "Queries").Path);
        // the runner's guard accepts what the picker writes
        Assert.Equal(System.IO.Path.GetFullPath(System.IO.Path.Combine(inst, "Queries", "q.graphql")), LoadTestingTool.Domain.WorkspaceGuard.Resolve(inst, copied.Path));
    }
}

public class PickerFolderTests
{
    [Fact]
    public void A_picker_remembers_the_folder_it_was_last_used_in_and_falls_back_when_that_is_gone()
    {
        using var dir = new TempDir();
        var original = EditorSettings.FilePath;
        EditorSettings.FilePath = System.IO.Path.Combine(dir.Combine("settings"), "data-editor.json");
        try
        {
            var a = dir.Combine("a"); Directory.CreateDirectory(a);
            var fallback = dir.Combine("fb"); Directory.CreateDirectory(fallback);
            Assert.Equal(fallback, PickerFolders.Last("import", fallback));
            Assert.Null(PickerFolders.Last("import"));
            File.WriteAllText(System.IO.Path.Combine(a, "x.csv"), "x");
            PickerFolders.Remember("import", System.IO.Path.Combine(a, "x.csv"));
            Assert.Equal(a, PickerFolders.Last("import", fallback));
            Assert.Equal(fallback, PickerFolders.Last("export", fallback));   // each picker has its own
            Directory.Delete(a, true);
            Assert.Equal(fallback, PickerFolders.Last("import", fallback));
        }
        finally { EditorSettings.FilePath = original; }
    }
}
