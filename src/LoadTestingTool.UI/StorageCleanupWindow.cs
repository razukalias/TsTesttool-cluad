using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI;

public sealed class StorageCleanupWindow : Window
{
    private sealed record Candidate(string Path, string Kind, string Instance, string Environment, string Testcase, string Status, DateTime Timestamp, long Size, string Key);

    private readonly string _backupRoot;
    private readonly IReadOnlyList<string> _instanceRoots;
    private readonly TextBox _search = new() { Watermark = "Filename / instance / testcase / environment: term1 & term2…", MinWidth = 360 };
    private readonly ComboBox _type = new() { ItemsSource = new[] { "All storage", "DataEngine backups", "Logs", "History", "Results" }, SelectedIndex = 0, Width = 170 };
    private readonly ComboBox _instance = new() { Width = 180 };
    private readonly ComboBox _status = new() { ItemsSource = new[] { "All statuses", "Passed", "Failed", "Cancelled" }, SelectedIndex = 0, Width = 130 };
    private readonly TextBox _from = new() { Watermark = "From date YYYY-MM-DD", Width = 150 };
    private readonly TextBox _to = new() { Watermark = "To date YYYY-MM-DD", Width = 150 };
    private readonly CheckBox _duplicatesOnly = new() { Content = "Duplicates only (keep newest)", Margin = new Thickness(8, 0) };
    private readonly TextBlock _summary = DashboardStyle.Text("Click Preview to scan files.", 12, false, DashboardStyle.Muted);
    private readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new Avalonia.Media.FontFamily("Consolas"), MinHeight = 360 };
    private readonly TextBlock _statusText = DashboardStyle.Text("Permanent deletion is disabled until a preview is generated.", 12, false, DashboardStyle.Muted);
    private List<Candidate> _matches = [];

    public StorageCleanupWindow(string backupRoot, IEnumerable<string> instanceRoots)
    {
        _backupRoot = Path.GetFullPath(backupRoot);
        _instanceRoots = instanceRoots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Title = "Storage & Cleanup";
        Width = 1100; Height = 760; MinWidth = 820; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _instance.ItemsSource = new[] { "All instances" }.Concat(_instanceRoots.Select(x => Path.GetFileName(x.TrimEnd(Path.DirectorySeparatorChar))).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)).ToArray();
        _instance.SelectedIndex = 0;
        _preview.DoubleTapped += (_, _) => OpenSelected();
        foreach (var control in new Control[] { _search, _type, _instance, _status, _from, _to, _duplicatesOnly })
        {
            if (control is TextBox text) text.TextChanged += (_, _) => ClearPreview();
            if (control is ComboBox combo) combo.SelectionChanged += (_, _) => ClearPreview();
            if (control is CheckBox check) check.IsCheckedChanged += (_, _) => ClearPreview();
        }
        var filters = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            DashboardStyle.Text("Type", 11, false, DashboardStyle.Muted), _type,
            DashboardStyle.Text("Instance", 11, false, DashboardStyle.Muted), _instance,
            DashboardStyle.Text("Status", 11, false, DashboardStyle.Muted), _status,
            _from, _to, _duplicatesOnly, _search
        }};
        foreach (var child in filters.Children) if (child is Control c) c.Margin = new Thickness(0, 0, 8, 6);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
        {
            DashboardStyle.Action("Preview", Preview, "primary"),
            DashboardStyle.Action("Delete permanently", ConfirmDelete, "danger"),
            DashboardStyle.Action("Refresh", () => { ClearPreview(); Preview(); }),
            DashboardStyle.Action("Open selected", OpenSelected),
            DashboardStyle.Action("Restore selected", RestoreSelected, "danger"),
            DashboardStyle.Action("Close", Close)
        }};
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Margin = new Thickness(18),
            Children =
            {
                new StackPanel { Spacing = 6, Children = { DashboardStyle.Text("Storage & Cleanup", 23, true), DashboardStyle.Text("Preview matching files before permanent deletion. Active-run files are skipped.", 12, false, DashboardStyle.Muted) } },
                filters,
                actions,
                new Border { BorderBrush = DashboardStyle.Line, BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = new ScrollViewer { Content = _preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } },
                new StackPanel { Spacing = 4, Children = { _summary, _statusText } }
            }
        };
        Grid.SetRow(filters, 1); Grid.SetRow(actions, 2); Grid.SetRow(Content is Grid grid ? grid.Children[3] : null!, 3); Grid.SetRow(Content is Grid grid2 ? grid2.Children[4] : null!, 4);
    }

    /// <summary>The previewed file on the line the caret is on, or null.</summary>
    internal string? FileOnCurrentLine()
    {
        var text = _preview.Text ?? string.Empty;
        if (text.Length == 0) return null;
        var caret = Math.Clamp(_preview.CaretIndex, 0, text.Length);
        var start = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var end = text.IndexOf('\n', start);
        var line = (end < 0 ? text[start..] : text[start..end]).TrimEnd('\r');
        return _matches.Select(x => x.Path).FirstOrDefault(path => line.EndsWith(path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Opens the file on the caret's line. A workbook opens in the spreadsheet editor on a temporary copy, so saving there can never change the stored backup;
    /// anything else (logs, history, results) opens in the system's default app.
    /// </summary>
    private void OpenSelected()
    {
        var path = FileOnCurrentLine();
        if (path is null) { _statusText.Text = "Click a file line first (or double-click it), then Open selected."; _statusText.Foreground = DashboardStyle.Muted; return; }
        try
        {
            if (!File.Exists(path)) { _statusText.Text = "That file no longer exists. Click Refresh."; return; }
            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                var folder = Path.Combine(Path.GetTempPath(), "TestToolBackupView", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                var copy = Path.Combine(folder, Path.GetFileName(path));
                File.Copy(path, copy);
                var viewer = new SpreadsheetPreviewWindow(copy);
                viewer.Title = $"{viewer.Title} (copy — the stored file is not changed)";
                viewer.Closed += (_, _) => { try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } };
                viewer.Show(this);
            }
            else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            _statusText.Text = $"Opened {Path.GetFileName(path)}.";
        }
        catch (Exception ex) { _statusText.Text = $"Could not open the file: {ex.Message}"; _statusText.Foreground = DashboardStyle.Brush("#FF7A7A"); }
    }

    /// <summary>Asked before a restore replaces a workbook: returns why it must not (null = fine). Set by the app.</summary>
    internal Func<string, string?>? RestoreGuard { get; set; }

    /// <summary>Called after a workbook was restored, with its path, so the app can reload it.</summary>
    internal Action<string>? Restored { get; set; }

    /// <summary>The workbook a backup was taken from: "Name_20260101_120000123.backup.xlsx" (central) or "Name.xlsx.backup.20260101….xlsx" (beside it).</summary>
    internal string? WorkbookFor(string backupPath)
    {
        var file = Path.GetFileName(backupPath);
        string name;
        var legacy = System.Text.RegularExpressions.Regex.Match(file, @"^(?<n>.+\.xlsx)\.backup\..+\.xlsx$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (legacy.Success) name = legacy.Groups["n"].Value;
        else
        {
            var central = System.Text.RegularExpressions.Regex.Match(file, @"^(?<n>.+?)_\d{8}_\d{9}(_\d+)?\.backup\.xlsx$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!central.Success) return null;
            name = central.Groups["n"].Value + ".xlsx";
        }
        var instance = Path.GetFileName(Path.GetDirectoryName(backupPath) ?? string.Empty);
        var root = _instanceRoots.FirstOrDefault(r => string.Equals(Path.GetFileName(r.TrimEnd(Path.DirectorySeparatorChar)), instance, StringComparison.OrdinalIgnoreCase))
                   ?? _instanceRoots.FirstOrDefault(r => string.Equals(Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Path.GetDirectoryName(backupPath) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
        return root is null ? null : Path.Combine(root, name);
    }

    internal void RestoreSelectedForTest() => RestoreSelected();

    private async void RestoreSelected()
    {
        var backup = FileOnCurrentLine();
        if (backup is null || !backup.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || !backup.Contains(".backup.", StringComparison.OrdinalIgnoreCase))
        { Say("Click a workbook backup line first, then Restore selected.", false); return; }
        var target = WorkbookFor(backup);
        if (target is null) { Say("Cannot tell which instance workbook this backup belongs to.", true); return; }
        if (RestoreGuard?.Invoke(target) is { } blocked) { Say($"Cannot restore {Path.GetFileName(target)}: {blocked}.", true); return; }
        var when = File.GetLastWriteTime(backup).ToString("yyyy-MM-dd HH:mm:ss");
        if (!await ConfirmAsync($"Replace {Path.GetFileName(target)} with the backup from {when}?", "The current workbook is backed up first, so this can be undone by restoring that new backup.")) return;
        try
        {
            string? safety = null;
            if (File.Exists(target)) safety = DataEngineBackup.CreateBackup(target);
            var temp = target + ".restore-" + Guid.NewGuid().ToString("N");
            File.Copy(backup, temp);
            File.Move(temp, target, overwrite: true);
            Restored?.Invoke(target);
            Say($"Restored {Path.GetFileName(target)} from {when}." + (safety is null ? string.Empty : $" Previous version saved as {Path.GetFileName(safety)}."), false);
            Preview();
        }
        catch (Exception ex) { Say($"Restore failed: {ex.Message}", true); }
    }

    private void Say(string text, bool error)
    {
        _statusText.Text = text;
        _statusText.Foreground = error ? DashboardStyle.Brush("#FF7A7A") : DashboardStyle.Brush("#39D39B");
    }

    /// <summary>Replaces the confirmation dialog (tests).</summary>
    internal Func<string, string, Task<bool>>? Confirm { get; set; }

    private async Task<bool> ConfirmAsync(string title, string detail)
    {
        if (Confirm is not null) return await Confirm(title, detail);
        var yes = new Button { Content = "Restore", Classes = { "accent" } };
        var no = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0) };
        var dialog = new Window
        {
            Title = "Restore backup", SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, Background = Brush.Parse("#0B1626"),
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 10, MaxWidth = 520,
                Children =
                {
                    new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } }
                }
            }
        };
        yes.Click += (_, _) => dialog.Close(true);
        no.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(this);
    }

    private void ClearPreview()
    {
        _matches = [];
        _preview.Text = string.Empty;
        _summary.Text = "Filters changed. Click Preview to scan files.";
        _statusText.Text = "Permanent deletion is disabled until a preview is generated.";
        _statusText.Foreground = DashboardStyle.Muted;
    }

    private void Preview()
    {
        try
        {
            _matches = Scan().Where(Matches).ToList();
            if (_duplicatesOnly.IsChecked == true) _matches = KeepOnlyOlderDuplicates(_matches);
            var total = _matches.Sum(x => x.Size);
            _summary.Text = $"Matching files: {_matches.Count:N0}  •  Total size: {FormatSize(total)}  •  Preview only";
            var lines = new StringBuilder();
            lines.AppendLine("ACTION   TYPE                 INSTANCE                 STATUS     TIMESTAMP                SIZE       FILE");
            lines.AppendLine(new string('-', 140));
            foreach (var file in _matches.OrderBy(x => x.Timestamp))
                lines.AppendLine($"DELETE   {file.Kind,-20} {Trim(file.Instance, 24),-24} {file.Status,-10} {file.Timestamp:yyyy-MM-dd HH:mm:ss} {FormatSize(file.Size),-10} {file.Path}");
            _preview.Text = lines.ToString();
            _statusText.Text = _matches.Count == 0 ? "No files match the current filters." : "Preview generated. Click Delete permanently to remove these files.";
            _statusText.Foreground = _matches.Count == 0 ? DashboardStyle.Muted : DashboardStyle.Brush("#F0B44D");
        }
        catch (Exception ex) { _statusText.Text = $"Could not scan storage: {ex.Message}"; _statusText.Foreground = DashboardStyle.Red; }
    }

    private void ConfirmDelete()
    {
        if (_matches.Count == 0) { _statusText.Text = "Generate a preview first."; _statusText.Foreground = DashboardStyle.Red; return; }
        var input = new TextBox { Watermark = "Type DELETE to confirm", Width = 260 };
        var dialog = new Window { Title = "Confirm permanent deletion", Width = 520, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var message = DashboardStyle.Text($"This will permanently delete {_matches.Count:N0} file(s) totaling {FormatSize(_matches.Sum(x => x.Size))}. This cannot be undone.", 13);
        var error = DashboardStyle.Text("", 11, false, DashboardStyle.Red);
        var delete = DashboardStyle.Action("Delete permanently", () =>
        {
            if (!string.Equals(input.Text?.Trim(), "DELETE", StringComparison.Ordinal)) { error.Text = "Type DELETE exactly to confirm."; return; }
            var deleted = 0; var skipped = 0;
            foreach (var file in _matches.ToArray())
            {
                try
                {
                    if (IsActive(file.Path)) { skipped++; continue; }
                    File.Delete(file.Path); deleted++;
                }
                catch { skipped++; }
            }
            foreach (var historyRoot in _matches.Select(m => m.Path).Where(p => p.Contains(Path.DirectorySeparatorChar + "History" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                         .Select(p => p[..(p.LastIndexOf(Path.DirectorySeparatorChar + "History" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) + "History".Length + 1)]).Distinct())
                try { LoadTestingTool.Reporting.HistoryValueFiles.Prune(historyRoot); } catch (Exception) { /* leftover value files are harmless */ }
            dialog.Close();
            _matches = [];
            _preview.Text = string.Empty;
            _summary.Text = $"Deleted: {deleted:N0}  •  Skipped: {skipped:N0}";
            _statusText.Text = "Permanent deletion completed.";
            _statusText.Foreground = DashboardStyle.Green;
        }, "danger");
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { message, input, error, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { DashboardStyle.Action("Cancel", dialog.Close), delete } } } };
        dialog.Show(this); input.Focus();
    }

    private IEnumerable<Candidate> Scan()
    {
        foreach (var file in EnumerateFiles(_backupRoot, "DataEngine backups", "*.xlsx")) yield return Describe(file, "DataEngine backups");
        foreach (var root in _instanceRoots)
        {
            // Older backups were written beside the workbook ("<name>.xlsx.backup.<stamp>.xlsx").
            if (Directory.Exists(root))
                foreach (var file in Directory.EnumerateFiles(root, "*.backup.*xlsx", SearchOption.TopDirectoryOnly)) yield return Describe(file, "DataEngine backups");
            foreach (var file in EnumerateFiles(Path.Combine(root, "Logs"), "Logs", "*.log")) yield return Describe(file, "Logs");
            foreach (var file in EnumerateFiles(Path.Combine(root, "History"), "History", "*.xlsx")) yield return Describe(file, "History");
            foreach (var file in EnumerateFiles(Path.Combine(root, "Results"), "Results", "*.*")) yield return Describe(file, "Results");
        }
    }

    private Candidate Describe(string path, string kind)
    {
        var name = Path.GetFileName(path);
        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var status = parts.FirstOrDefault(x => x.Equals("passed", StringComparison.OrdinalIgnoreCase) || x.Equals("failed", StringComparison.OrdinalIgnoreCase) || x.Equals("cancelled", StringComparison.OrdinalIgnoreCase) || x.Equals("pass", StringComparison.OrdinalIgnoreCase)) ?? "";
        var pathParts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var pathStatus = pathParts.LastOrDefault(x => x.Equals("passed", StringComparison.OrdinalIgnoreCase) || x.Equals("failed", StringComparison.OrdinalIgnoreCase) || x.Equals("cancelled", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(pathStatus)) status = pathStatus;
        var environment = Directory.GetParent(path)?.Name ?? "";
        if (kind.Equals("History", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pathStatus))
            environment = Directory.GetParent(path)?.Parent?.Name ?? environment;
        var instance = _instanceRoots.FirstOrDefault(root => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        instance = string.IsNullOrWhiteSpace(instance)
            ? kind.Equals("DataEngine backups", StringComparison.OrdinalIgnoreCase) ? Directory.GetParent(path)?.Name ?? "" : Path.GetFileName(Directory.GetParent(path)?.Parent?.FullName ?? "")
            : Path.GetFileName(instance);
        var testcase = Regex.Replace(Path.GetFileNameWithoutExtension(name), @"_Row\d+.*$", "", RegexOptions.IgnoreCase);
        var row = Regex.Match(name, @"_Row([^_]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var thread = Regex.Match(name, @"_T(\d+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var iteration = Regex.Match(name, @"_I(\d+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var stamp = File.GetLastWriteTime(path);
        var size = new FileInfo(path).Length;
        var key = kind.Equals("History", StringComparison.OrdinalIgnoreCase) ? $"{instance}|{environment}|{testcase}|row:{row}|{status}" : $"{instance}|{environment}|{testcase}|{status}|T:{thread}|I:{iteration}|{kind}";
        return new Candidate(path, kind, instance, environment, testcase, status, stamp, size, key);
    }

    private bool Matches(Candidate file)
    {
        var type = _type.SelectedItem?.ToString() ?? "All storage";
        if (!type.Equals("All storage", StringComparison.OrdinalIgnoreCase) && !file.Kind.Equals(type, StringComparison.OrdinalIgnoreCase)) return false;
        var instance = _instance.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(instance) && instance != "All instances" && !file.Instance.Equals(instance, StringComparison.OrdinalIgnoreCase)) return false;
        var status = _status.SelectedItem?.ToString();
        if (status is "Passed" or "Failed" or "Cancelled" && !file.Status.Equals(status, StringComparison.OrdinalIgnoreCase) && !(status == "Passed" && file.Status.Equals("pass", StringComparison.OrdinalIgnoreCase))) return false;
        if (!DateFilter(_from.Text, file.Timestamp, true) || !DateFilter(_to.Text, file.Timestamp, false)) return false;
        return TermsMatch($"{Path.GetFileName(file.Path)} {file.Instance} {file.Environment} {file.Testcase} {file.Status}", _search.Text);
    }

    private static bool DateFilter(string? text, DateTime value, bool from)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return !DateTime.TryParse(text.Trim(), out var date) || (from ? value.Date >= date.Date : value.Date <= date.Date);
    }

    private static List<Candidate> KeepOnlyOlderDuplicates(List<Candidate> files)
    {
        var groups = files.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
        return groups.SelectMany(group => group.OrderByDescending(x => x.Timestamp).Skip(1)).ToList();
    }

    private static IEnumerable<string> EnumerateFiles(string root, string kind, string pattern)
        => Directory.Exists(root) ? Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Where(x => !Path.GetFileName(x).StartsWith("~$", StringComparison.OrdinalIgnoreCase)) : [];

    private static bool IsActive(string path) => path.Contains(Path.DirectorySeparatorChar + "running" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || path.Contains(Path.DirectorySeparatorChar + ".runtime" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool TermsMatch(string value, string? filter) => (filter ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string FormatSize(long bytes) => bytes switch { >= 1024 * 1024 * 1024 => $"{bytes / 1024d / 1024d / 1024d:F2} GB", >= 1024 * 1024 => $"{bytes / 1024d / 1024d:F2} MB", >= 1024 => $"{bytes / 1024d:F1} KB", _ => $"{bytes} B" };
    private static string Trim(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}

public static class DataEngineBackup
{
    /// <summary>The central backup folder from the nearest appsettings.json above the workbook, or null when there is none (backups then stay beside the workbook).</summary>
    public static string? ConfiguredFolder(string workbookPath)
    {
        var settings = FindSettings(workbookPath);
        if (settings is null) return null;
        try
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settings)) as System.Text.Json.Nodes.JsonObject;
            var root = json?["DataEngineBackupFolder"]?.ToString();
            if (string.IsNullOrWhiteSpace(root)) root = "backup";
            return Path.GetFullPath(root, Path.GetDirectoryName(settings)!);
        }
        catch { return null; }
    }

    public static string CreateBackup(string workbookPath)
    {
        var settings = FindSettings(workbookPath);
        var root = "backup";
        try
        {
            if (settings is not null)
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settings)) as System.Text.Json.Nodes.JsonObject;
                root = json?["DataEngineBackupFolder"]?.ToString() ?? root;
                root = Path.IsPathRooted(root) ? root : Path.Combine(Path.GetDirectoryName(settings)!, root);
            }
        }
        catch { }
        var instance = Path.GetFileName(Path.GetDirectoryName(workbookPath) ?? "default");
        var folder = Path.Combine(Path.GetFullPath(root), instance);
        Directory.CreateDirectory(folder);
        var name = $"{Path.GetFileNameWithoutExtension(workbookPath)}_{DateTime.Now:yyyyMMdd_HHmmssfff}.backup.xlsx";
        var destination = Path.Combine(folder, name);
        File.Copy(workbookPath, destination, overwrite: false);
        return destination;
    }

    private static string? FindSettings(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "appsettings.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
