using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

// Record view: a bar of rows on top; click a row or press Left / Right to move between rows.
public class RecordRowSwitcherTests
{
    private static (DataEditorViewModel Editor, RecordView View, Window Window) Open(TempDir dir, int rows = 4)
    {
        var editor = new DataEditorViewModel(Samples.CreateLarge(dir, rows), "Customers");
        editor.OpenRecordTabCommand.Execute(null);
        var view = new RecordView(editor);
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (editor, view, window);
    }

    private static List<Button> Buttons(RecordView view) => view.GetVisualDescendants().OfType<Button>().Where(b => b.Tag as string == "row-switch").ToList();

    [AvaloniaFact]
    public void The_bar_has_one_button_per_row_with_position_and_data_id_and_marks_the_selected_one()
    {
        using var dir = new TempDir();
        var (editor, view, window) = Open(dir);
        var buttons = Buttons(view);
        Assert.Equal(4, buttons.Count);
        Assert.Equal("1 · CUST001", ((TextBlock)buttons[0].Content!).Text);
        Assert.Equal("3 · CUST003", ((TextBlock)buttons[2].Content!).Text);
        Assert.Equal(editor.VisibleRows.IndexOf(editor.SelectedRow!), buttons.FindIndex(b => b.Classes.Contains("primary")));
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(Path.Combine(shots, "record-row-bar.png"));
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_row_selects_it_and_the_record_shows_it()
    {
        using var dir = new TempDir();
        var (editor, view, window) = Open(dir);
        Buttons(view)[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("CUST003", editor.SelectedRow!.CurrentDataId);
        Assert.Contains("CUST003", editor.RecordTitle);
        Assert.True(Buttons(view)[2].Classes.Contains("primary"));
        Assert.False(Buttons(view)[0].Classes.Contains("primary"));
        window.Close();
    }

    [AvaloniaFact]
    public void Left_and_right_move_between_rows_and_stop_at_the_ends()
    {
        using var dir = new TempDir();
        var (editor, view, window) = Open(dir);
        editor.SelectedRow = editor.VisibleRows[0];
        Dispatcher.UIThread.RunJobs();
        view.Focus();

        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Assert.Equal("CUST002", editor.SelectedRow!.CurrentDataId);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        Assert.Equal("CUST004", editor.SelectedRow!.CurrentDataId);
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Assert.Equal("CUST003", editor.SelectedRow!.CurrentDataId);
        window.Close();
    }
}
