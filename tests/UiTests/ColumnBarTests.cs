using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using LoadTestingTool.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// The column letter bar over the headers, picking several columns, in-place rename, the verb button and the question asked when a rename changes references.
public class ColumnBarTests
{
    private static int CellOf(DataEditorViewModel vm, string title) => vm.Columns.First(c => c.Title == title).CellIndex;

    private static string WithReference(TempDir dir)
    {
        var path = Samples.CreateLarge(dir, rows: 3);
        using var workbook = new XLWorkbook(path);
        workbook.Worksheet("config").Cell("F2").Value = "https://api.example.test/<From_request_CreateCustomer.email_3>";
        workbook.Save();
        return path;
    }

    [AvaloniaFact]
    public void Headers_show_their_Excel_letter_and_only_expected_columns_get_the_verb_button()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 800; window.Show();
        Dispatcher.UIThread.RunJobs();
        var grid = window.FindControl<DataGrid>("Grid")!;
        var vm = window.ViewModel;

        string Letter(string title)
        {
            var column = grid.Columns.First(c => (c.Header as Avalonia.Controls.StackPanel)?.Children.OfType<TextBlock>().Any(t => t.Text == title) == true);
            var bar = ((Avalonia.Controls.StackPanel)column.Header!).Children.OfType<Border>().Single(b => b.Tag as string == "column-letter-bar");
            return ((Avalonia.Controls.Grid)bar.Child!).Children.OfType<TextBlock>().Single().Text!;
        }
        bool HasVerbButton(string title)
        {
            var column = grid.Columns.First(c => (c.Header as Avalonia.Controls.StackPanel)?.Children.OfType<TextBlock>().Any(t => t.Text == title) == true);
            var bar = ((Avalonia.Controls.StackPanel)column.Header!).Children.OfType<Border>().Single(b => b.Tag as string == "column-letter-bar");
            return ((Avalonia.Controls.Grid)bar.Child!).Children.OfType<Button>().Any(b => b.Tag as string == "header-wizard");
        }

        Assert.Equal("F", Letter("CreateCustomer.email"));                 // request sheet: testcaseindex, testcase, dataid, dontrun, name, email
        Assert.Equal("E", Letter("{notempty}CreateCustomer.id"));                    // response sheet column E
        Assert.False(HasVerbButton("CreateCustomer.email"));
        Assert.True(HasVerbButton("{notempty}CreateCustomer.id"));
        window.Close();
    }

    [Fact]
    public void Verb_of_an_expected_column_can_be_changed_and_the_header_keeps_its_path()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        var cell = CellOf(vm, "{notempty}CreateCustomer.id");
        Assert.Equal("notempty", vm.VerbOf(cell));

        Assert.True(vm.ChangeColumnVerb(cell, "contains"));
        var changed = vm.Columns.First(c => c.Title == "{contains}CreateCustomer.id").CellIndex;
        Assert.Equal("{contains}CreateCustomer.id", vm.HeaderText(changed));
        Assert.False(vm.ChangeColumnVerb(changed, "contains"));   // already that verb
        Assert.False(vm.ChangeColumnVerb(CellOf(vm, "CreateCustomer.email"), "eq"));      // request columns have no verb
        vm.UndoCommand.Execute(null);
        Assert.Equal("{notempty}CreateCustomer.id", vm.HeaderText(CellOf(vm, "{notempty}CreateCustomer.id")));
    }

    [Fact]
    public void A_header_can_be_renamed_in_place_but_not_to_an_empty_or_taken_name()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        var cell = CellOf(vm, "CreateCustomer.country");

        Assert.False(vm.RenameColumnTo(cell, "   "));
        Assert.False(vm.RenameColumnTo(cell, "CreateCustomer.email"));
        Assert.True(vm.StatusIsError);
        Assert.True(vm.RenameColumnTo(cell, "CreateCustomer.nation"));
        Assert.Contains("CreateCustomer.nation", vm.Columns.Select(c => c.Title));
    }

    [Fact]
    public void Saving_a_rename_asks_and_only_rename_leaves_the_references_alone()
    {
        using var dir = new TempDir();
        var path = WithReference(dir);
        var vm = new DataEditorViewModel(path);
        vm.RenameColumnTo(CellOf(vm, "CreateCustomer.email"), "CreateCustomer.mail");

        vm.SaveCommand.Execute(null);
        Assert.True(vm.RenameFixVisible);
        Assert.Contains("1 cell", vm.RenameFixSummary);
        vm.CancelRenameFixCommand.Execute(null);
        Assert.True(vm.IsDirty);                                   // nothing written

        vm.SaveCommand.Execute(null);
        vm.SaveRenameOnlyCommand.Execute(null);
        Assert.False(vm.IsDirty);
        using var workbook = new XLWorkbook(path);
        Assert.Contains("<From_request_CreateCustomer.email_3>", workbook.Worksheet("config").Cell("F2").GetString());   // still the old name
        Assert.Contains("CreateCustomer.mail", workbook.Worksheet("request").Row(1).CellsUsed().Select(c => c.GetString()));
    }

    [Fact]
    public void A_rename_nobody_refers_to_saves_without_a_question()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        vm.RenameColumnTo(CellOf(vm, "CreateCustomer.country"), "CreateCustomer.nation");
        vm.SaveCommand.Execute(null);
        Assert.False(vm.RenameFixVisible);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Several_columns_can_be_copied_cleared_filled_and_removed_at_once()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(Samples.CreateLarge(dir, rows: 3));
        var name = CellOf(vm, "CreateCustomer.name");
        var country = CellOf(vm, "CreateCustomer.country");
        var rows = vm.VisibleRows.ToList();

        var copied = vm.CopyCellsText([country, name], rows);
        var lines = copied.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.StartsWith("CreateCustomer.name\tCreateCustomer.country", lines[0]);      // left to right
        Assert.StartsWith("Customer 1\t", lines[1]);

        vm.FillCellsDown([name, country], rows);
        Assert.All(rows, r => { Assert.Equal("Customer 1", r.Cells[name]); Assert.Equal(rows[0].Cells[country], r.Cells[country]); });

        vm.ClearCells([name, country], rows.Take(2).ToList());                         // only two of the three rows
        Assert.Equal(string.Empty, rows[0].Cells[name]);
        Assert.Equal(string.Empty, rows[1].Cells[country]);
        Assert.Equal("Customer 1", rows[2].Cells[name]);

        var before = vm.Columns.Count;
        Assert.True(vm.RemoveColumns([name, country]));
        Assert.Equal(before - 2, vm.Columns.Count);
        Assert.DoesNotContain("CreateCustomer.country", vm.Columns.Select(c => c.Title));
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, vm.Columns.Count);
        Assert.Contains("CreateCustomer.country", vm.Columns.Select(c => c.Title));
    }

    [Fact]
    public void Removing_several_columns_is_refused_as_a_whole_when_one_is_referenced()
    {
        using var dir = new TempDir();
        var vm = new DataEditorViewModel(WithReference(dir));
        Assert.False(vm.RemoveColumns([CellOf(vm, "CreateCustomer.country"), CellOf(vm, "CreateCustomer.email")]));
        Assert.Contains("CreateCustomer.country", vm.Columns.Select(c => c.Title));
        Assert.Contains("Nothing was removed", vm.StatusMessage);
    }

    [AvaloniaFact]
    public void The_column_menu_speaks_of_all_picked_columns()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 800; window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.ViewModel;
        var grid = window.FindControl<DataGrid>("Grid")!;
        var picked = (List<FormattedTextColumn>)typeof(DataEditorWindow).GetField("_picked", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        picked.AddRange(grid.Columns.OfType<FormattedTextColumn>().Where(c => c.Tag is int t && (t == CellOf(vm, "CreateCustomer.name") || t == CellOf(vm, "CreateCustomer.email"))));

        var items = window.BuildColumnMenu(vm, CellOf(vm, "CreateCustomer.name")).Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Contains("Delete 2 columns", items);
        Assert.Contains(items, h => h!.StartsWith("Clear 2 columns"));
        Assert.Contains(items, h => h!.StartsWith("Copy 2 columns"));
        Assert.Contains(items, h => h!.StartsWith("Highlight 2 columns"));
        Assert.False(window.BuildColumnMenu(vm, CellOf(vm, "CreateCustomer.name")).Items.OfType<MenuItem>().First(i => (i.Header as string)!.StartsWith("Rename header")).IsEnabled);

        picked.Clear();
        var single = window.BuildColumnMenu(vm, CellOf(vm, "CreateCustomer.name")).Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Contains("Delete column", single);
        Assert.Contains("Clear column", single);
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_the_letters_picks_columns_ctrl_adds_shift_extends_and_double_click_renames()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), "Customers");
        window.Width = 1440; window.Height = 800; window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.ViewModel;
        var grid = window.FindControl<DataGrid>("Grid")!;
        var picked = (List<FormattedTextColumn>)typeof(DataEditorWindow).GetField("_picked", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

        Avalonia.Point CentreOf(string title)
        {
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First(h => (h.Content as Avalonia.Controls.StackPanel)?.Children.OfType<TextBlock>().Any(t => t.Text == title) == true);
            return Avalonia.VisualExtensions.TranslatePoint(header, new Avalonia.Point(header.Bounds.Width / 2, 10), window)!.Value;
        }
        void Click(string title, Avalonia.Input.RawInputModifiers keys = Avalonia.Input.RawInputModifiers.None)
        {
            var at = CentreOf(title);
            window.MouseDown(at, Avalonia.Input.MouseButton.Left, keys);
            window.MouseUp(at, Avalonia.Input.MouseButton.Left, keys);
            Dispatcher.UIThread.RunJobs();
        }

        Click("dontrun");
        Assert.Single(picked);
        Click("CreateCustomer.name", Avalonia.Input.RawInputModifiers.Control);
        Assert.Equal(2, picked.Count);
        Click("dontrun", Avalonia.Input.RawInputModifiers.Control);                  // ctrl again takes it off
        Assert.Single(picked);
        Click("CreateCustomer.name");
        Click("dontrun", Avalonia.Input.RawInputModifiers.Shift);                    // name..dontrun
        Assert.Equal(2, picked.Count);
        Click("CreateCustomer.name");
        Assert.Single(picked);
        Click("dontrun");

        // double click opens the rename box under the header
        var email = CentreOf("CreateCustomer.name");
        for (var i = 0; i < 2; i++) { window.MouseDown(email, Avalonia.Input.MouseButton.Left); window.MouseUp(email, Avalonia.Input.MouseButton.Left); }
        Dispatcher.UIThread.RunJobs();
        var box = window.GetVisualDescendants().Concat(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(TopLevel.GetTopLevel(window)!)).OfType<TextBox>().FirstOrDefault(t => t.Tag as string == "rename-header")
            ?? Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(window)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Tag as string == "rename-header");
        Assert.NotNull(box);
        Assert.Equal("CreateCustomer.name", box!.Text);
        window.Close();
    }
}
