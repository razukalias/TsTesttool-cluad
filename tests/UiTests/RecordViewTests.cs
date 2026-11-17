using Avalonia.Controls;
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
