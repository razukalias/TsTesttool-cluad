using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoadTestingTool.UI;

internal sealed class MultipartPartsEditor : StackPanel
{
    private sealed class PartRow
    {
        public ComboBox Type { get; } = new() { ItemsSource = new[] { "Text", "File" }, SelectedIndex = 0, Width = 100 };
        public TextBox Name { get; } = new() { Watermark = "Form field name", MinWidth = 150 };
        public ComboBox Source { get; } = new() { ItemsSource = new[] { "Fixed value", "Request column", "Runtime variable" }, SelectedIndex = 0, Width = 145 };
        public TextBox Value { get; } = new() { Watermark = "Value or column name", MinWidth = 180 };
        public TextBox FileName { get; } = new() { Watermark = "Optional filename", MinWidth = 140 };
        public TextBox ContentType { get; } = new() { Text = "application/octet-stream", MinWidth = 180 };
        public Border Card { get; set; } = null!;
    }

    private readonly StackPanel _items = new() { Spacing = 7 };
    private readonly TextBlock _status = DashboardStyle.Text("Add at least one text or file part.", 11, false, DashboardStyle.Muted);
    private readonly List<PartRow> _rows = [];
    public event EventHandler? Changed;

    public MultipartPartsEditor()
    {
        Spacing = 7;
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { DashboardStyle.Text("Multipart form parts", 13, true), DashboardStyle.Action("＋ Add part", () => AddPart(), "primary") } });
        Children.Add(DashboardStyle.Text("Choose a fixed value or map the part to a request-sheet column. File bytes are never logged or written to History.", 11, false, DashboardStyle.Muted));
        Children.Add(_items); Children.Add(_status);
        AddPart(); AddPart("File");
    }

    public JsonArray BuildParts()
    {
        var parts = new JsonArray();
        foreach (var row in _rows)
        {
            var source = row.Source.SelectedItem?.ToString() ?? "Fixed value";
            var token = source == "Fixed value" ? row.Value.Text ?? "" : $"_*{(row.Value.Text ?? "").Trim()}*_";
            var item = new JsonObject { ["Name"] = row.Name.Text?.Trim() ?? "", ["Type"] = (row.Type.SelectedItem?.ToString() ?? "Text").ToLowerInvariant() };
            if (item["Type"]!.ToString() == "file")
            {
                item["Path"] = token; item["FileName"] = string.IsNullOrWhiteSpace(row.FileName.Text) ? null : (source == "Fixed value" ? row.FileName.Text : $"_*{row.FileName.Text.Trim()}*_"); item["ContentType"] = row.ContentType.Text?.Trim() ?? "application/octet-stream";
            }
            else item["Value"] = token;
            parts.Add(item);
        }
        return parts;
    }

    public bool Validate(out string error)
    {
        error = "";
        if (_rows.Count == 0) { error = "Add at least one multipart part."; return false; }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            var name = row.Name.Text?.Trim() ?? ""; if (string.IsNullOrWhiteSpace(name)) { error = "Every multipart part needs a form field name."; return false; }
            if (!names.Add(name)) { error = $"Duplicate multipart form field name: {name}"; return false; }
            if (string.IsNullOrWhiteSpace(row.Value.Text)) { error = $"Part '{name}' needs a value or request-column name."; return false; }
            if ((row.Type.SelectedItem?.ToString() ?? "Text") == "File" && string.IsNullOrWhiteSpace(row.ContentType.Text)) { error = $"File part '{name}' needs a content type."; return false; }
        }
        return true;
    }

    public void Load(JsonElement root)
    {
        if (root.TryGetProperty("Parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            _rows.Clear(); _items.Children.Clear();
            foreach (var part in parts.EnumerateArray())
            {
                var row = AddPart(part.TryGetProperty("Type", out var type) && type.GetString()?.Equals("file", StringComparison.OrdinalIgnoreCase) == true ? "File" : "Text", false);
                row.Name.Text = part.TryGetProperty("Name", out var name) ? name.GetString() : "";
                var key = row.Type.SelectedItem?.ToString() == "File" ? "Path" : "Value";
                var value = part.TryGetProperty(key, out var raw) ? raw.GetString() ?? "" : "";
                if (value.StartsWith("_*") && value.EndsWith("*_")) { row.Source.SelectedItem = "Request column"; row.Value.Text = value[2..^2]; } else row.Value.Text = value;
                if (row.Type.SelectedItem?.ToString() == "File") { row.FileName.Text = part.TryGetProperty("FileName", out var fileName) ? fileName.GetString() ?? "" : ""; row.ContentType.Text = part.TryGetProperty("ContentType", out var contentType) ? contentType.GetString() ?? "application/octet-stream" : "application/octet-stream"; }
            }
            if (_rows.Count == 0) { AddPart("Text"); AddPart("File"); }
            RefreshStatus();
        }
    }

    private PartRow AddPart(string type = "Text", bool notify = true)
    {
        var row = new PartRow(); row.Type.SelectedItem = type; _rows.Add(row);
        var fields = new StackPanel { Spacing = 4, Children = { Field("Type", row.Type), Field("Form field name", row.Name), Field("Source", row.Source), Field("Value / request column", row.Value) } };
        var fileFields = new StackPanel { Spacing = 4, IsVisible = type == "File", Children = { Field("Filename (optional or request column)", row.FileName), Field("File content type", row.ContentType) } };
        var remove = DashboardStyle.Action("− Remove", () => { _rows.Remove(row); _items.Children.Remove(row.Card); RefreshStatus(); Changed?.Invoke(this, EventArgs.Empty); }); remove.Classes.Add("danger");
        row.Card = new Border { BorderBrush = DashboardStyle.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10), Child = new StackPanel { Spacing = 5, Children = { new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { DashboardStyle.Text("Multipart part", 12, true), remove } }, fields, fileFields } } };
        Grid.SetColumn(remove, 1); _items.Children.Add(row.Card);
        row.Type.SelectionChanged += (_, _) => { fileFields.IsVisible = row.Type.SelectedItem?.ToString() == "File"; RefreshStatus(); Changed?.Invoke(this, EventArgs.Empty); };
        row.Source.SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        row.Name.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty); row.Value.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty); row.FileName.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty); row.ContentType.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        if (notify) { RefreshStatus(); Changed?.Invoke(this, EventArgs.Empty); }
        return row;
    }

    private void RefreshStatus() => _status.Text = _rows.Count == 0 ? "Add at least one text or file part." : $"{_rows.Count} multipart part(s) configured. File content is excluded from logs and History.";
    private static Control Field(string label, Control control) => new StackPanel { Spacing = 2, Children = { DashboardStyle.Text(label, 10, false, DashboardStyle.Muted), control } };
}
