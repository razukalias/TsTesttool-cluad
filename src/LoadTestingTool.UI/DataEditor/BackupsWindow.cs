using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>The backups of the workbook open in the editor: filter by date/time, pick one, restore it or look at a copy of it.</summary>
public sealed class BackupsWindow : Window
{
    private readonly DataEditorViewModel _editor;
    private readonly Func<string?>? _guard;
    private readonly TextBox _from = new() { Watermark = "From  yyyy-MM-dd HH:mm", Width = 190 };
    private readonly TextBox _to = new() { Watermark = "To  yyyy-MM-dd HH:mm", Width = 190 };
    private readonly ListBox _list = new() { MinHeight = 260 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
    private IReadOnlyList<BackupFile> _all = [];

    /// <param name="guard">Why the workbook cannot be replaced right now (it is running…), or null; set by the app.</param>
    public BackupsWindow(DataEditorViewModel editor, Func<string?>? guard = null)
    {
        _editor = editor;
        _guard = guard;
        Title = $"Backups — {editor.FileName}";
        Width = 760; Height = 520; MinWidth = 560; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0B1626");

        var restore = new Button { Content = "Restore selected", Classes = { "primary" }, MinHeight = 34 };
        var open = new Button { Content = "Open a copy", MinHeight = 34 };
        var close = new Button { Content = "Close", MinHeight = 34 };
        restore.Click += async (_, _) => await RestoreAsync();
        open.Click += (_, _) => OpenCopy();
        close.Click += (_, _) => Close();
        _from.TextChanged += (_, _) => Apply();
        _to.TextChanged += (_, _) => Apply();
        _list.DoubleTapped += (_, _) => OpenCopy();

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Taken", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 }, _from, _to } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { restore, open, close } };
        Content = new Grid
        {
            Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            Children =
            {
                new TextBlock { Text = "Backups of this workbook, newest first. Restore keeps the current version as a new backup first.", Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                Row(filters, 1), Row(_list, 2), Row(buttons, 3), Row(_status, 4)
            }
        };
        Refresh();
    }

    private static T Row<T>(T control, int row) where T : Control { Grid.SetRow(control, row); control.Margin = new Thickness(0, 0, 0, 10); return control; }

    private void Refresh()
    {
        _all = WorkbookBackups.List(_editor.WorkbookPath);
        Apply();
    }

    private void Apply()
    {
        var from = DateTime.TryParse(_from.Text, out var f) ? f : (DateTime?)null;
        var to = DateTime.TryParse(_to.Text, out var t) ? (t.TimeOfDay == TimeSpan.Zero && !(_to.Text ?? "").Contains(':') ? t.AddDays(1).AddTicks(-1) : t) : (DateTime?)null;
        var shown = _all.Where(b => (from is null || b.Taken >= from) && (to is null || b.Taken <= to)).ToList();
        _list.ItemsSource = shown.Select(b => new ListBoxItem { Content = b.Label, Tag = b, FontFamily = new FontFamily("Consolas") }).ToList();
        if (shown.Count > 0) _list.SelectedIndex = 0;
        _status.Text = _all.Count == 0 ? "No backups yet. One is made every time you save." : $"{shown.Count} of {_all.Count} backups shown.";
    }

    private BackupFile? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as BackupFile;

    /// <summary>Replaces the confirmation dialog (tests).</summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

    public async Task RestoreAsync()
    {
        if (Selected is not { } backup) { Say("Pick a backup first.", false); return; }
        if (_guard?.Invoke() is { } blocked) { Say($"Cannot restore: {blocked}.", true); return; }
        if (_editor.IsDirty) { Say("Cannot restore: the workbook has unsaved edits — save or reload it first.", true); return; }
        if (!await AskAsync($"Replace {_editor.FileName} with the backup from {backup.Taken:yyyy-MM-dd HH:mm:ss}?")) return;
        try
        {
            var safety = _editor.RestoreFrom(backup.Path);
            Say($"Restored the backup from {backup.Taken:yyyy-MM-dd HH:mm:ss}." + (safety is null ? string.Empty : $" The previous version is kept as {Path.GetFileName(safety)}."), false);
            Refresh();
        }
        catch (Exception ex) { Say($"Restore failed: {ex.Message}", true); }
    }

    private void OpenCopy()
    {
        if (Selected is not { } backup) { Say("Pick a backup first.", false); return; }
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "TestToolBackupView", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(backup.Path));
            File.Copy(backup.Path, copy);
            var viewer = new SpreadsheetPreviewWindow(copy);
            viewer.Title = $"{viewer.Title} (copy — the stored backup is not changed)";
            viewer.Closed += (_, _) => { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } };
            viewer.Show(this);
        }
        catch (Exception ex) { Say($"Could not open the backup: {ex.Message}", true); }
    }

    private void Say(string text, bool error)
    {
        _status.Text = text;
        _status.Foreground = Brush.Parse(error ? "#FF7A7A" : "#39D39B");
    }

    private async Task<bool> AskAsync(string question)
    {
        if (Confirm is not null) return await Confirm(question);
        var yes = new Button { Content = "Restore", Classes = { "primary" } };
        var no = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        var dialog = new Window
        {
            Title = "Restore backup", SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = Brush.Parse("#0B1626"),
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12, MaxWidth = 480,
                Children = { new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } } }
            }
        };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(this);
    }
}
