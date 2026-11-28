using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The settings of a For, ForEach or While step as named fields (From, To, Count, Step, Items, …). Every field takes a fixed value, a <c>_column_</c> placeholder
/// or a <c>&lt;From_response_Step.path_r&gt;</c> reference; "Column" creates the request column <c>Step.key</c> and puts its placeholder in the field.
/// </summary>
public sealed class LoopSettingsView : StackPanel
{
    private static readonly IBrush Muted = Brush.Parse("#8EA4BC");
    private readonly StepRowViewModel _step;
    private readonly Dictionary<string, TextBox> _boxes = new(StringComparer.OrdinalIgnoreCase);

    public LoopSettingsView(StepRowViewModel step, string type, Action<string, Action<string>> follow)
    {
        _step = step; Spacing = 6;
        Children.Add(new TextBlock { Text = "LOOP SETTINGS", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Muted });
        Children.Add(new TextBlock { Text = Examples(type), FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Muted });
        foreach (var setting in ControlStepSettings.For(type))
        {
            Children.Add(Row(setting));
            if (type == "ForEach" && setting.Key == "file") Children.Add(BrowseRow());
        }
        follow("stepconfig", _ => Refresh());
        Refresh();
    }

    /// <summary>Examples shown above the fields (also in the help of the step type).</summary>
    public static string Examples(string type) => type switch
    {
        "For" => "Examples:  From 1, To 5  →  rounds 1 2 3 4 5.   From 10, To 1, Step -3  →  10 7 4 1.   Count 3  →  3 rounds.   To = _rounds_  →  the number comes from the request column Step.rounds of each data row.   Use the counter as _i_ (or the name in Variable name) in the steps inside; the loop also gives <From_response_Step.output_r> (the value) and <From_response_Step.index_r> (round, from 0).",
        "ForEach" => "Examples:  Data file  users.csv  →  one round per row; use _item.email_ for a column.   Excel: Data file  people.xlsx, Sheet  People, Only this column  id  →  _item_ is the id.   JSON: Data file  d.json, Array path  $.data.users  →  _item.id_, _item.name_.   Items  <From_response_List.people_r>  →  a list of objects from a step response gives _item.<field>_ too.   Items  a,b,c  →  three rounds.   [\"x\",\"y\"]  →  a JSON list.   <From_response_List.ids_r>  →  the list a step returned.   _ids_  →  the request column Step.ids of the data row (a comma list).   Use the item as _item_ and its position as _index_ (from 0) in the steps inside.",
        "While" => "Examples:  Most rounds 10  →  never more than 10 tries.   Wait between rounds 2000  →  a pause of 2 seconds (polling).   When the limit is reached: continue  →  go on instead of failing.   Most rounds = _tries_  →  taken from the request column Step.tries.   The round is _round_ (from 1); the condition above decides when to stop.",
        _ => string.Empty
    };

    private Control Row(ControlStepSettings.Setting setting)
    {
        var box = new TextBox { Watermark = setting.Placeholder, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "loop-" + setting.Key };
        _boxes[setting.Key] = box;
        void Commit()
        {
            var text = box.Text ?? string.Empty;
            if (text.Trim() == (ControlStepSettings.Read(_step.Value("stepconfig")).GetValueOrDefault(setting.Key, string.Empty))) return;
            _step.Write("stepconfig", ControlStepSettings.WithKey(_step.Value("stepconfig"), setting.Key, text));
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Commit(); e.Handled = true; } };
        var column = new Button { Content = "Column", Margin = new Thickness(6, 0, 0, 0), Tag = "loop-column-" + setting.Key };
        ToolTip.SetTip(column, $"Take this from the data row: adds the request column {_step.CurrentName}.{setting.Key} and puts _{setting.Key}_ here");
        column.Click += (_, _) =>
        {
            _step.Editor?.AddControlColumn(_step.CurrentName, setting.Key);
            box.Text = "_" + setting.Key + "_";
            Commit();
        };
        var label = new TextBlock { Text = setting.Label, FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
        if (setting.Help.Length > 0) ToolTip.SetTip(label, setting.Help);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,Auto") };
        Grid.SetColumn(box, 1); Grid.SetColumn(column, 2);
        grid.Children.Add(label); grid.Children.Add(box); grid.Children.Add(column);
        var help = new TextBlock { Text = setting.Help, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, IsVisible = setting.Help.Length > 0 };
        return new StackPanel { Spacing = 1, Children = { grid, help } };
    }

    private readonly TextBlock _preview = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "loop-preview", IsVisible = false };

    /// <summary>Browse… picks the file; Preview shows its sheets, columns, first rows and (JSON) the arrays you can name in Array path.</summary>
    private Control BrowseRow()
    {
        var browse = new Button { Content = "Browse…", Tag = "loop-browse" };
        var preview = new Button { Content = "Preview", Tag = "loop-preview-button", Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (provider is null) return;
            var files = await provider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { Title = "Choose a CSV, Excel or JSON file", AllowMultiple = false });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            var instance = InstanceFolder();
            var filesFolder = instance is null ? null : System.IO.Path.Combine(instance, "Files");
            var inFiles = filesFolder is not null && System.IO.Path.GetDirectoryName(path)?.TrimEnd('/', '\\').Equals(filesFolder.TrimEnd('/', '\\'), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == true;
            _step.Write("stepconfig", ControlStepSettings.WithKey(_step.Value("stepconfig"), "file", inFiles ? System.IO.Path.GetFileName(path) : path.Replace('\\', '/')));
            ShowPreview();
        };
        preview.Click += (_, _) => ShowPreview();
        return new StackPanel { Spacing = 3, Margin = new Thickness(170, 0, 0, 0), Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { browse, preview } }, _preview } };
    }

    private string? InstanceFolder() => _step.Editor is { } editor ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(editor.WorkbookPath)) : null;

    /// <summary>Fills the preview text from the current Data file, Sheet and Array path settings.</summary>
    public string ShowPreview()
    {
        var values = ControlStepSettings.Read(_step.Value("stepconfig"));
        string text;
        try
        {
            var file = values.GetValueOrDefault("file", string.Empty);
            if (file.Length == 0) text = "Choose a file first (Browse…).";
            else
            {
                var info = RowSources.Inspect(InstanceFolder() ?? ".", file, new RowSourceOptions { Sheet = values.GetValueOrDefault("sheet", string.Empty), Header = values.GetValueOrDefault("header", "yes") != "no", Delimiter = values.GetValueOrDefault("delimiter", string.Empty), JsonPath = values.GetValueOrDefault("path", "$") });
                var lines = new List<string> { $"{info.TotalRows} rows." };
                if (info.Sheets.Count > 0) lines.Add("Sheets: " + string.Join(", ", info.Sheets) + "  (type one into Sheet)");
                if (info.Arrays.Count > 0) lines.Add("Lists in the file: " + string.Join(", ", info.Arrays) + "  (type one into Array path)");
                if (info.Columns.Count > 0) lines.Add("Columns: " + string.Join(", ", info.Columns) + "  → use _item.<column>_");
                foreach (var row in info.Rows.Take(5)) lines.Add("  " + string.Join(" | ", info.Columns.Select(c => row.GetValueOrDefault(c) ?? string.Empty)));
                text = string.Join("\n", lines);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { text = ex.Message; }
        _preview.Text = text; _preview.IsVisible = true;
        return text;
    }

    private void Refresh()
    {
        var values = ControlStepSettings.Read(_step.Value("stepconfig"));
        foreach (var (key, box) in _boxes)
        {
            var text = values.GetValueOrDefault(key, string.Empty);
            if (box.Text != text && !box.IsFocused) box.Text = text;
        }
    }
}
