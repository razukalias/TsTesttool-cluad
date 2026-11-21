using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClosedXML.Excel;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class ImportWindowTests
{
    private static string Csv(TempDir dir, string text, string name = "in.csv")
    {
        var path = dir.Combine(name);
        File.WriteAllText(path, text);
        return path;
    }

    private static DataEditorViewModel Editor(TempDir dir, int rows = 3) => new(Samples.CreateLarge(dir, rows: rows), "Customers");

    private static T Find<T>(Control root, string tag) where T : Control => root.GetVisualDescendants().OfType<T>().First(c => Equals(c.Tag, tag));

    [AvaloniaFact]
    public async Task The_window_reads_a_file_shows_the_preview_and_imports_with_the_chosen_ticks()
    {
        using var dir = new TempDir();
        var editor = Editor(dir);
        var window = new ImportWindow(editor, new EditorSettings());
        window.Show();
        window.LoadFile(Csv(dir, "Data ID;CreateCustomer.name;CreateCustomer.country\nA;n1;SE\nB;n2;NO\nC;n3;DK"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, window.GetVisualDescendants().OfType<CheckBox>().Count(c => Equals(c.Tag, "import-row-tick")));
        Assert.Equal(3, window.GetVisualDescendants().OfType<CheckBox>().Count(c => Equals(c.Tag, "import-column-tick")));
        Assert.Contains("→ request · CreateCustomer.name", window.GetVisualDescendants().OfType<TextBlock>().Where(b => Equals(b.Tag, "import-column-label")).Select(b => b.Text));
        Assert.Contains("3 of 3 rows", Find<TextBlock>(window, "import-impact").Text);

        Find<Button>(window, "import-no-rows").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(Find<Button>(window, "import-run").IsEnabled);
        window.GetVisualDescendants().OfType<CheckBox>().First(c => Equals(c.Tag, "import-row-tick")).IsChecked = true;
        window.GetVisualDescendants().OfType<CheckBox>().Last(c => Equals(c.Tag, "import-row-tick")).IsChecked = true;
        Assert.True(Find<Button>(window, "import-run").IsEnabled);
        Assert.True(await window.RunAsync());
        Assert.Equal(["CUST001", "CUST002", "CUST003", "A", "C"], editor.VisibleRows.Select(r => r.Cells[0]).ToArray());
        Assert.Equal(2, window.Result!.Added);
    }

    [AvaloniaFact]
    public async Task An_Excel_file_can_be_read_from_a_range_below_a_header_row_on_another_sheet()
    {
        using var dir = new TempDir();
        var path = dir.Combine("src.xlsx");
        using (var wb = new XLWorkbook())
        {
            wb.AddWorksheet("Skip").Cell(1, 1).Value = "nothing";
            var data = wb.AddWorksheet("Data");
            data.Cell(2, 2).Value = "Data ID"; data.Cell(2, 3).Value = "CreateCustomer.name";
            data.Cell(3, 2).Value = "X1"; data.Cell(3, 3).Value = "Xavier";
            data.Cell(4, 2).Value = "X2"; data.Cell(4, 3).Value = "Xena";
            data.Cell(9, 2).Value = "X9"; data.Cell(9, 3).Value = "far away";
            wb.SaveAs(path);
        }
        var editor = Editor(dir);
        var window = new ImportWindow(editor, new EditorSettings());
        window.Show();
        window.LoadFile(path);
        window.Session.SheetIndex = 1;
        window.Session.RangeText = "B2:C4";
        window.Session.HeaderRow = 2;
        window.Session.Refresh();
        Assert.Equal(["Data ID", "CreateCustomer.name"], window.Session.Table!.Headers);
        Assert.Equal(2, window.Session.Table.Rows.Count);
        Assert.True(await window.RunAsync());
        Assert.Contains("Xena", editor.VisibleRows.Select(r => r.Cells[editor.Columns.First(c => c.Title == "CreateCustomer.name").CellIndex]));
        Assert.DoesNotContain("far away", editor.VisibleRows.SelectMany(r => r.Cells));
    }

    [AvaloniaFact]
    public async Task Replace_asks_first_and_cancel_changes_nothing()
    {
        using var dir = new TempDir();
        var editor = Editor(dir);
        var window = new ImportWindow(editor, new EditorSettings());
        window.Show();
        window.LoadFile(Csv(dir, "Data ID,CreateCustomer.name\nZ,only"));
        window.Session.Mode = ImportMode.ReplaceRows;
        string? asked = null;
        window.Confirm = (title, text) => { asked = title + "|" + text; return Task.FromResult(false); };
        Assert.False(await window.RunAsync());
        Assert.Contains("REPLACES all 3 existing rows", asked);
        Assert.Equal(3, editor.VisibleRows.Count);
        Assert.False(editor.IsDirty);
        window.Confirm = (_, _) => Task.FromResult(true);
        Assert.True(await window.RunAsync());
        Assert.Equal(["Z"], editor.VisibleRows.Select(r => r.Cells[0]).ToArray());
    }

    [AvaloniaFact]
    public void A_bad_range_and_a_bad_file_say_what_is_wrong()
    {
        using var dir = new TempDir();
        var window = new ImportWindow(Editor(dir), new EditorSettings());
        window.Show();
        window.LoadFile(Csv(dir, "a,b\n1,2"));
        window.Session.RangeText = "not a range";
        window.Session.Refresh();
        Assert.Contains("B3:F40", window.Session.RangeError);
        var bad = dir.Combine("broken.xlsx");
        File.WriteAllText(bad, "this is not a zip");
        Assert.StartsWith("Could not read broken.xlsx", window.Session.Open(bad));
        Assert.Contains("no cells", window.Session.Open(Csv(dir, "", "empty.csv")));
    }

    [Fact]
    public void The_choices_for_a_file_name_are_remembered_including_the_hand_made_mapping()
    {
        using var dir = new TempDir();
        var settings = new EditorSettings();
        EditorSettings.FilePath = Path.Combine(dir.Path, "settings.json");
        var csv = Csv(dir, "x,y\nCUST001,hello", "same.csv");
        var first = new ImportSession(Editor(dir), settings);
        Assert.Null(first.Open(csv));
        var email = first.Targets.First(t => t.Display.EndsWith("CreateCustomer.email")).Key;
        first.Mode = ImportMode.FillExistingRows;
        first.Match = ImportMatch.Manual;
        first.Manual[0] = 0;
        first.Manual[1] = email;
        Assert.Null(first.Run()!.Error);

        var again = new ImportSession(Editor(dir), settings);
        Assert.Null(again.Open(csv));
        Assert.Equal(ImportMode.FillExistingRows, again.Mode);
        Assert.Equal(ImportMatch.Manual, again.Match);
        Assert.Equal([0, email], again.Manual);
    }

    [AvaloniaFact]
    public void The_toolbar_Import_entry_and_the_row_menu_open_the_window_for_a_testcase_and_for_a_sheet()
    {
        using var dir = new TempDir();
        var editor = Editor(dir);
        Assert.True(editor.CanImport);
        var opened = 0;
        editor.ImportRequested += (_, _) => opened++;
        editor.OpenImportCommand.Execute(null);
        Assert.Equal(1, opened);
        Assert.Equal("testcase Customers", editor.ImportDestination);
    }
}
