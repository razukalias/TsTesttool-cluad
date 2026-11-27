using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class DifferingColumnsTests
{
    private static (DataEditorWindow Window, DataEditorViewModel Editor, DataGrid Grid) Open(TempDir dir)
    {
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 4));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, window.Shell.Current!, window.FindControl<DataGrid>("Grid")!);
    }

    private static void Select(DataGrid grid, params DataRowViewModel[] rows)
    {
        grid.SelectedItems.Clear();
        foreach (var row in rows) grid.SelectedItems.Add(row);
        Dispatcher.UIThread.RunJobs();
    }

    private static List<int> Shown(DataGrid grid) => grid.Columns.Where(c => c.IsVisible && c.Tag is int cell && cell > 0).Select(c => (int)c.Tag!).ToList();

    private static void SetCell(DataRowViewModel row, int cell, string value) { row.Cells[cell] = value; Dispatcher.UIThread.RunJobs(); }

    [AvaloniaFact]
    public void Only_the_columns_where_the_selected_rows_differ_stay()
    {
        using var dir = new TempDir();
        var (window, editor, grid) = Open(dir);
        var rows = editor.VisibleRows.Take(3).ToList();
        var all = editor.Columns.Select(c => c.CellIndex).ToList();
        Assert.True(all.Count >= 4);
        int same = all[0], alone = all[1], different = all[2];
        foreach (var row in rows) { SetCell(row, same, "same everywhere"); SetCell(row, alone, "x"); }
        SetCell(rows[0], alone, "x"); SetCell(rows[1], alone, "x"); SetCell(rows[2], alone, "y");     // differs only in the third row
        for (var i = 0; i < rows.Count; i++) SetCell(rows[i], different, "v" + i);

        // off: nothing is hidden
        Select(grid, rows.ToArray());
        Assert.Equal(all.Count, Shown(grid).Count);
        Assert.Equal(string.Empty, editor.DifferingColumnsText);

        // on, fewer than two rows selected: nothing is hidden, and it says what to do
        editor.ShowDifferingColumns = true; Dispatcher.UIThread.RunJobs();
        Select(grid, rows[0]);
        Assert.Equal(all.Count, Shown(grid).Count);
        Assert.Contains("select two or more rows", editor.DifferingColumnsText);

        // on, three rows: the column with the same value everywhere goes, the ones that differ stay
        Select(grid, rows.ToArray());
        var shown = Shown(grid);
        Assert.DoesNotContain(same, shown);
        Assert.Contains(alone, shown); Assert.Contains(different, shown);
        var expected = all.Where(c => rows.Select(r => r.Cells[c].Trim()).Distinct().Count() > 1).ToList();
        Assert.Equal(expected.Order().ToList(), shown.Order().ToList());                          // exactly what the values say, nothing more or less
        Assert.Equal($"Differing columns: {expected.Count} of {all.Count} columns differ in the 3 selected rows", editor.DifferingColumnsText);
        Assert.Contains("Differing columns", editor.ShowOptionsTitle);
        Assert.True(grid.Columns.Where(c => c.Tag is int n && n == 0).All(c => c.IsVisible));   // the Data ID column is never hidden

        // only the first two rows: the column that differed only in the third row is gone
        Select(grid, rows[0], rows[1]);
        Assert.DoesNotContain(alone, Shown(grid));
        Assert.Contains(different, Shown(grid));

        // editing a cell so the rows agree takes its column out; making them differ brings it back
        SetCell(rows[1], different, "v0"); Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(different, Shown(grid));
        SetCell(rows[1], different, "changed"); Dispatcher.UIThread.RunJobs();
        Assert.Contains(different, Shown(grid));

        // rows that are alike in every column: nothing but the Data ID column is left, and it says so
        foreach (var c in all) SetCell(rows[1], c, rows[0].Cells[c]);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Shown(grid));
        Assert.Contains("have the same value in every column", editor.DifferingColumnsText);

        // off again: everything is back
        editor.ShowDifferingColumns = false; Dispatcher.UIThread.RunJobs();
        Assert.Equal(all.Count, Shown(grid).Count);
        window.Close();
    }

    [AvaloniaFact]
    public void The_option_is_in_the_Show_menu_with_its_own_text()
    {
        using var dir = new TempDir();
        var (window, editor, _) = Open(dir);
        var box = window.GetVisualDescendantsOf<CheckBox>().FirstOrDefault(c => (string?)c.Tag == "differing-columns")
                  ?? FindInFlyout(window);
        Assert.NotNull(box);
        Assert.Equal("Differing columns (selected rows)", box!.Content);
        box.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Assert.True(editor.ShowDifferingColumns);
        Assert.Contains("Differing columns", editor.ShowOptionsTitle);
        window.Close();
    }

    private static CheckBox? FindInFlyout(DataEditorWindow window)
    {
        var button = window.GetVisualDescendantsOf<Button>().First(b => (string?)b.Tag == "show-options");
        var flyout = (Flyout)button.Flyout!;
        flyout.ShowAt(button); Dispatcher.UIThread.RunJobs();
        return ((Control)flyout.Content!).GetVisualDescendantsOf<CheckBox>().FirstOrDefault(c => (string?)c.Tag == "differing-columns");
    }
}

internal static class DifferingVisualExtensions
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Control root) where T : Control
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
