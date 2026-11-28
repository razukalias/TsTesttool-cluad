using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The settings that only one step type has, kept in the step's StepConfig JSON: GraphQL query and variables, SQL command, file
/// action, script source, or the parts of a multipart HTTP body. Other keys already in the JSON are left alone.
/// </summary>
public sealed class StepTypeSection : UserControl
{
    private enum Kind { Text, Multi, Combo, Json, Number, Path }
    private sealed record Spec(string Key, string Label, Kind Kind, string[]? Options = null, string? Hint = null, Func<JsonObject, bool>? Visible = null, string Folder = "Files", bool CSharp = false);

    private static readonly IBrush Muted = Brush.Parse("#8EA4BC");
    private static readonly IBrush Line = Brush.Parse("#22364F");
    private static readonly string[] ContentTypes = ["text/plain", "application/octet-stream", "application/pdf", "image/png", "image/jpeg", "application/json", "application/xml", "text/csv"];

    private readonly StepRowViewModel _step;
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly List<Action> _cleanup = [];
    private JsonObject _config = new();
    private string _lastWritten = string.Empty;
    private bool _raw;

    public StepTypeSection(StepRowViewModel step)
    {
        _step = step;
        Content = _body;
        Rebuild();
        NotifyCollectionChangedEventHandler follow = (_, e) =>
        {
            if (e.Action != NotifyCollectionChangedAction.Replace) return;
            var typeCell = _step.CellOf("steptype"); var contentCell = _step.CellOf("contenttype"); var configCell = _step.CellOf("stepconfig");
            if (e.NewStartingIndex == typeCell || e.NewStartingIndex == contentCell || (e.NewStartingIndex == configCell && _step.Value("stepconfig") != _lastWritten)) Dispatcher.UIThread.Post(Rebuild);
        };
        _step.Cells.CollectionChanged += follow;
        DetachedFromVisualTree += (_, _) => _step.Cells.CollectionChanged -= follow;
    }

    public static bool HasSettings(string type) => type.Trim().ToLowerInvariant() is "http" or "wcf" or "graphql" or "caseql" or "sql" or "file" or "script";

    public static string Describe(string type) => type.Trim().ToLowerInvariant() switch
    {
        "graphql" or "caseql" => "The query comes from the Template, a query file, or the inline text below. Variables are a JSON object and may use _*column*_ to read a request column.",
        "sql" => "The command comes from the Template, a file, or the text below. The connection profile is a name defined in appsettings.json.",
        "file" => "Reads or writes a file inside the workspace and stores the result in an output variable for later steps.",
        "script" => "Runs a trusted C# script. Trusted scripts must be enabled in the instance settings.",
        "wcf" => "A SOAP call to a WCF (or any SOAP) service over HTTP. The Template is the request body (one element such as <GetOrder xmlns=\"…\">…</GetOrder>) or a whole <Envelope>; it is put in an envelope for you. Faults fail the step.",
        "http" => "Sends the Template as the request body. For multipart/form-data, define the form parts here.",
        "actionmanual" => "Pauses the run until someone confirms the manual action; the wait limit is in the step settings.",
        _ => string.Empty
    };

    private string Type => _step.Value("steptype").Trim().ToLowerInvariant();

    private void Rebuild()
    {
        foreach (var undo in _cleanup) undo();
        _cleanup.Clear();
        _body.Children.Clear();
        var text = _step.Value("stepconfig").Trim();
        _lastWritten = _step.Value("stepconfig");
        _raw = false;
        try { _config = text.Length == 0 ? new JsonObject() : JsonNode.Parse(text) as JsonObject ?? throw new JsonException("not an object"); }
        catch (JsonException)
        {
            _raw = true;
            _config = new JsonObject();
            _body.Children.Add(Note("This step's StepConfig is not a JSON object (it may use the key=value form), so it can only be edited as text below."));
            return;
        }

        var type = Type;
        var description = Describe(type);
        if (description.Length > 0) _body.Children.Add(Note(description));
        if (type == "http") _body.Children.Add(ContentTypeField());
        if (type is "http" or "wcf" or "graphql" or "caseql") _body.Children.Add(AuthField());
        switch (type)
        {
            case "graphql" or "caseql":
                AddAll(new Spec("OperationName", "Operation name", Kind.Text), new Spec("Source", "Query file", Kind.Path, Hint: "Optional. Overrides the template.", Folder: "Queries"),
                    new Spec("Inline", "Query", Kind.Multi, Hint: "Used when no template is set."), new Spec("Variables", "Variables", Kind.Json));
                break;
            case "wcf":
                _body.Children.Add(WsdlButton());
                AddAll(new Spec("SoapAction", "SOAP action", Kind.Text, Hint: "e.g. http://tempuri.org/IOrders/GetOrder"), new Spec("SoapVersion", "SOAP version", Kind.Combo, ["1.1", "1.2"]),
                    new Spec("Username", "User name", Kind.Text, Hint: "Optional. Adds a WS-Security UsernameToken. Can use _column_ placeholders."), new Spec("Password", "Password", Kind.Text, Hint: "Can be a _column_ placeholder; masked in logs and results."),
                    new Spec("HeaderXml", "Extra header XML", Kind.Multi, Hint: "Optional. Put inside the SOAP header."));
                break;
            case "sql":
                AddAll(new Spec("ConnectionProfile", "Connection", Kind.Text, Hint: "Profile name from appsettings.json (SQL Server or SQLite)"), new Spec("CommandType", "Command type", Kind.Combo, ["Text", "StoredProcedure"]),
                    new Spec("CommandText", "Command", Kind.Multi, Visible: c => !IsProcedure(c)), new Spec("Procedure", "Procedure", Kind.Text, Visible: IsProcedure),
                    new Spec("Source", "Command file", Kind.Path, Hint: "Optional. Overrides the template.", Folder: "Queries"), new Spec("Parameters", "Parameters", Kind.Json), new Spec("Outputs", "Outputs", Kind.Json, Hint: "Output name → variable name"));
                break;
            case "file":
                AddAll(new Spec("Action", "Action", Kind.Combo, ["Read", "Json", "Info", "ReadBinary", "Write", "Append", "WriteBinary", "AppendBinary"]));
                _body.Children.Add(FileSourceField());
                AddAll(new Spec("Path", "File", Kind.Path),
                    new Spec("Content", "Content", Kind.Multi, Visible: c => Writes(c)), new Spec("JsonPath", "JSON path", Kind.Text, Visible: c => Is(c, "Action", "Json")), new Spec("Gps", "Include GPS position", Kind.Combo, ["no", "yes"], Visible: c => Is(c, "Action", "Info")),
                    new Spec("ReturnAs", "Return as", Kind.Combo, ["", "rows", "csv", "text", "paragraphs", "tables", "markdown", "pages"], Hint: "Read of Excel/CSV: rows (JSON list of objects, default for Excel), csv, text. Word: text (default), paragraphs, tables, markdown. PDF: text (default), pages. Empty = plain text for text files.", Visible: c => Is(c, "Action", "Read")),
                    new Spec("Sheet", "Sheet (Excel)", Kind.Text, Hint: "Read: name or number, empty = first sheet. Write: the name of the new sheet (default Sheet1)", Visible: c => Is(c, "Action", "Read") || Is(c, "Action", "Write")),
                    new Spec("Range", "Cell range (Excel)", Kind.Text, Hint: "For example A1:D20; empty = all used cells", Visible: c => Is(c, "Action", "Read")),
                    new Spec("Header", "First row has names", Kind.Combo, ["yes", "no"], Visible: c => Is(c, "Action", "Read")),
                    new Spec("Delimiter", "Delimiter (CSV)", Kind.Text, Hint: "Empty = found by itself", Visible: c => Is(c, "Action", "Read")),
                    new Spec("MaxRows", "Max rows", Kind.Number, Hint: "Default 10000; a bigger sheet fails with a message (use a ForEach with Skip/Take instead)", Visible: c => Is(c, "Action", "Read")),
                    new Spec("OutputVariable", "Output variable", Kind.Text), new Spec("OutputMode", "Output mode", Kind.Combo, ["Reference", "Value", "Base64"]),
                    new Spec("ContentType", "Content type", Kind.Combo, ContentTypes), new Spec("MaxBytes", "Max bytes", Kind.Number));
                break;
            case "script":
                AddAll(new Spec("Source", "Script file", Kind.Path, Folder: "Scripts"), new Spec("Inline", "Inline script", Kind.Multi, Hint: "Used when no file is set. Ctrl+Space or a dot suggests C#.", CSharp: true));
                break;
            case "http" when string.Equals(_step.Value("contenttype").Trim(), "multipart/form-data", StringComparison.OrdinalIgnoreCase):
                _body.Children.Add(BuildParts());
                break;
        }
    }

    private static readonly string[] BodyTypes = ["application/json", "application/xml", "text/plain", "application/graphql", "application/x-www-form-urlencoded", "multipart/form-data", "application/octet-stream"];

    /// <summary>The body format; choosing multipart/form-data brings up the parts below.</summary>
    private Control ContentTypeField()
    {
        var current = _step.Value("contenttype").Trim();
        var options = BodyTypes.ToList();
        if (current.Length > 0 && !options.Contains(current, StringComparer.OrdinalIgnoreCase)) options.Add(current);
        var combo = new ComboBox { ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedItem = options.FirstOrDefault(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase)) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string chosen && !string.Equals(chosen, _step.Value("contenttype"), StringComparison.Ordinal)) _step.Write("contenttype", chosen); };
        return new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "Body format (content type)", FontSize = 12, Foreground = Muted }, combo } };
    }

    /// <summary>Which authentication profile the step uses: the instance's default, none, or one of the profiles (instance or global). Saved as "auth" in the step settings.</summary>
    private Control AuthField()
    {
        var names = _step.Editor?.AuthProfileNames?.Invoke() ?? [];
        var current = _config["auth"] is JsonValue ? _config["auth"]!.ToString() : string.Empty;
        var options = new List<string> { "default", "none" }; options.AddRange(names);
        var inline = _config["auth"] is JsonObject;
        if (inline) options.Add("(written in the settings)");
        else if (current.Length > 0 && !options.Contains(current, StringComparer.OrdinalIgnoreCase)) options.Add(current);
        var shown = inline ? "(written in the settings)" : options.FirstOrDefault(o => string.Equals(o, current.Length == 0 ? "default" : current, StringComparison.OrdinalIgnoreCase)) ?? "default";
        var combo = new ComboBox { ItemsSource = options, SelectedItem = shown, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "step-auth" };
        var error = new TextBlock { IsVisible = false };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string chosen || chosen == "(written in the settings)") return;
            Commit(new Spec("auth", "Authentication", Kind.Text), chosen.Equals("default", StringComparison.OrdinalIgnoreCase) ? string.Empty : chosen, error);
        };
        return new StackPanel { Spacing = 4, Children =
        {
            new TextBlock { Text = "Authentication", FontSize = 12, Foreground = Muted }, combo,
            new TextBlock { Text = names.Count == 0 ? "default = the instance's default profile (none defined yet: right-click the instance > Authentication…). none = send no credentials." : "default = the instance's default profile, none = send no credentials, or one of the profiles (right-click the instance > Authentication… to change them).", FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap },
        } };
    }

    private static bool Is(JsonObject c, string key, string value) => string.Equals(c[key]?.ToString(), value, StringComparison.OrdinalIgnoreCase);
    private static bool IsProcedure(JsonObject c) => Is(c, "CommandType", "StoredProcedure");
    private static bool Writes(JsonObject c) => (c["Action"]?.ToString() ?? "").StartsWith("Write", StringComparison.OrdinalIgnoreCase) || (c["Action"]?.ToString() ?? "").StartsWith("Append", StringComparison.OrdinalIgnoreCase);

    private void AddAll(params Spec[] specs)
    {
        foreach (var spec in specs)
            if (spec.Visible is null || spec.Visible(_config)) _body.Children.Add(Field(spec));
    }

    // ---- generic fields ------------------------------------------------------------------------

    private Control Field(Spec spec)
    {
        var current = _config[spec.Key];
        var text = current is null ? string.Empty : spec.Kind == Kind.Json ? current.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) : current.ToString();
        var error = new TextBlock { FontSize = 11, Foreground = Brush.Parse("#FF7A7A"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        Control input;
        switch (spec.Kind)
        {
            case Kind.Combo:
                var options = spec.Options!.ToList();
                if (text.Length > 0 && !options.Contains(text, StringComparer.OrdinalIgnoreCase)) options.Add(text);
                var combo = new ComboBox { ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch, SelectedItem = options.FirstOrDefault(o => string.Equals(o, text, StringComparison.OrdinalIgnoreCase)) };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not string chosen) return;
                    Commit(spec, chosen, error);
                    if (spec.Key is "CommandType" or "Action") Dispatcher.UIThread.Post(Rebuild);
                };
                input = combo;
                break;
            default:
                var box = new TextBox
                {
                    Text = text, HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = spec.Hint,
                    AcceptsReturn = spec.Kind is Kind.Multi or Kind.Json, TextWrapping = spec.Kind is Kind.Multi or Kind.Json ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MinHeight = spec.Kind is Kind.Multi or Kind.Json ? 70 : 0, FontFamily = spec.Kind is Kind.Multi or Kind.Json ? new FontFamily("Consolas,Menlo,monospace") : FontFamily.Default,
                };
                box.LostFocus += (_, _) => Commit(spec, box.Text ?? string.Empty, error);
                if (spec.Kind is not (Kind.Multi or Kind.Json))
                    box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(spec, box.Text ?? string.Empty, error); e.Handled = true; } };
                if (spec.Kind == Kind.Path)
                {
                    var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
                    browse.Click += async (_, _) => { if (await PickFileAsync(spec.Folder, spec.Key == "Path") is { } picked) { box.Text = picked; Commit(spec, picked, error); } };
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    row.Children.Add(box); Grid.SetColumn(browse, 1); row.Children.Add(browse);
                    input = row;
                }
                else if (spec.CSharp)
                {
                    // a script: C# suggestions (members after a dot, names, keywords, signatures) and the compiler's first error while typing paused
                    var assist = new ScriptAssist(box, LoadTestingTool.Editing.ScriptKind.Template, () => _step.Editor?.MenuSources().RequestHeaders ?? [], "step-script-suggestions");
                    assist.Idle += async () =>
                    {
                        var code = box.Text ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(code)) { error.IsVisible = false; return; }
                        var found = await Task.Run(() => LoadTestingTool.Editing.ScriptTemplates.Validate(code));
                        if (!string.Equals(code, box.Text ?? string.Empty, StringComparison.Ordinal)) return;
                        var first = found.FirstOrDefault(d => d.IsError);
                        error.Text = first is null ? string.Empty : $"line {first.Line}, column {first.Column}: {first.Message}";
                        error.IsVisible = first is not null;
                    };
                    var addVariable = new Button { Content = "Add variable…", HorizontalAlignment = HorizontalAlignment.Left, Tag = "script-add-variable" };
                    ToolTip.SetTip(addVariable, "Adds a line like  var orderId = \"_orderId_\";  at the top of the script, filled from a request column or a value of an earlier step");
                    addVariable.Click += async (_, _) =>
                    {
                        if (_step.Editor is not { } editor || TopLevel.GetTopLevel(this) is not Window owner) return;
                        var sources = editor.MenuSources();
                        var chosen = await new ScriptVariableWindow(sources.RequestHeaders, editor.ReferenceTargets(_step.CurrentName), box.Text ?? string.Empty).ShowDialog<(string Name, string Placeholder)?>(owner);
                        if (chosen is not { } pick) return;
                        box.Text = LoadTestingTool.Editing.ScriptVariableLines.AddToTop(box.Text, pick.Name, pick.Placeholder);
                        Commit(spec, box.Text, error);
                    };
                    input = new StackPanel { Spacing = 4, Children = { addVariable, box, assist.Signature, assist.Suggestions } };
                }
                else input = box;
                break;
        }
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = spec.Label, FontSize = 12, Foreground = Muted });
        stack.Children.Add(input);
        stack.Children.Add(error);
        return stack;
    }

    private void Commit(Spec spec, string text, TextBlock error)
    {
        error.IsVisible = false;
        if (text.Trim().Length == 0) _config.Remove(spec.Key);
        else if (spec.Kind == Kind.Json)
        {
            try { _config[spec.Key] = JsonNode.Parse(text) is JsonObject parsed ? parsed : throw new JsonException("must be a JSON object, for example {\"name\": \"value\"}"); }
            catch (JsonException ex) { error.Text = $"Not saved: {ex.Message}"; error.IsVisible = true; return; }
        }
        else if (spec.Kind == Kind.Number)
        {
            if (!long.TryParse(text.Trim(), out var number)) { error.Text = "Not saved: enter a whole number."; error.IsVisible = true; return; }
            _config[spec.Key] = number;
        }
        else _config[spec.Key] = text;
        Save();
    }

    /// <summary>"Fixed path" or "The data row": the second sets the file to <c>_file_</c> and gives the step a request column <c>Step.file</c>, one file per data row.</summary>
    private Control FileSourceField()
    {
        bool FromRow() => string.Equals(_config["Path"]?.ToString()?.Trim(), "_file_", StringComparison.OrdinalIgnoreCase);
        var combo = new ComboBox { ItemsSource = new[] { "Fixed path", "The data row" }, SelectedIndex = FromRow() ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "file-source" };
        combo.SelectionChanged += (_, _) =>
        {
            var wantRow = combo.SelectedIndex == 1;
            if (wantRow == FromRow()) return;
            if (wantRow) { _config["Path"] = "_file_"; _step.Editor?.AddFileColumn(_step.CurrentName); }
            else { _config.Remove("Path"); _step.Editor?.RemoveFileColumnIfEmpty(_step.CurrentName); }
            Save();
            Rebuild();
        };
        var note = Note("Fixed path: one file for every row. The data row: each row of the request sheet names its own file in the column " + _step.CurrentName + ".file (a bare name is read from the instance's Files folder).");
        return new StackPanel { Spacing = 4, Children = { new TextBlock { Text = "File comes from", FontSize = 12, Foreground = Muted }, combo, note } };
    }

    private Control WsdlButton()
    {
        var button = new Button { Content = "Import from WSDL…", Tag = "wsdl-import" };
        ToolTip.SetTip(button, "Read a WSDL (URL or file) and fill this step from one of its operations: the request template, SOAP action, version and address");
        button.Click += (_, _) =>
            new WsdlImportWindow("Import a WCF step from a WSDL", "Use this operation", false, (_, chosen) => ApplyWsdl(chosen[0])).Show(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
        return button;
    }

    /// <summary>Fills the step from a WSDL operation: the request template becomes a new file in Templates, the address, SOAP action and version are set.</summary>
    public void ApplyWsdl(LoadTestingTool.Editing.WsdlOperation operation)
    {
        if (_step.Editor is { } editor)
        {
            var folder = editor.TemplatesFolder;
            Directory.CreateDirectory(folder);
            var stem = string.Concat(operation.Name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-')); if (stem.Length == 0) stem = "Soap";
            var relative = stem + ".xml"; var n = 1;
            while (File.Exists(TemplateEditorWindow.ResolveInside(folder, relative))) relative = $"{stem}_{++n}.xml";
            File.WriteAllText(TemplateEditorWindow.ResolveInside(folder, relative), operation.Template);
            _step.Write("templetsource", relative);
        }
        if (operation.Endpoint.Length > 0) _step.Write("targeturl", operation.Endpoint);
        if (operation.SoapAction.Length > 0) _config["SoapAction"] = operation.SoapAction; else _config.Remove("SoapAction");
        _config["SoapVersion"] = operation.SoapVersion;
        Save();
        Rebuild();
    }

    private void Save()
    {
        var json = _config.Count == 0 ? (_step.Value("stepconfig").Trim().Length == 0 ? string.Empty : "{}") : _config.ToJsonString();
        _lastWritten = json;
        _step.Write("stepconfig", json);
    }

    // ---- multipart -----------------------------------------------------------------------------

    private sealed class PartControls
    {
        public ComboBox Type { get; } = new() { ItemsSource = new[] { "Text", "File" }, MinWidth = 80 };
        public TextBox Name { get; } = new() { Watermark = "Form field name" };
        public ComboBox Source { get; } = new() { ItemsSource = new[] { "Fixed value", "Request column" } };
        public TextBox Value { get; } = new();
        public TextBox FileName { get; } = new() { Watermark = "File name sent (optional)" };
        public TextBox ContentType { get; } = new() { Watermark = "Content type, e.g. application/pdf" };
    }

    private static readonly Regex Token = new(@"^_\*(.+)\*_$", RegexOptions.Compiled);

    private Control BuildParts()
    {
        var panel = new StackPanel { Spacing = 8 };
        var rows = new List<PartControls>();
        var list = new StackPanel { Spacing = 8 };

        void Write()
        {
            var parts = new JsonArray();
            foreach (var row in rows)
            {
                var isFile = row.Type.SelectedItem?.ToString() == "File";
                var column = row.Source.SelectedItem?.ToString() == "Request column";
                var value = (row.Value.Text ?? string.Empty).Trim();
                var token = column ? $"_*{value}*_" : row.Value.Text ?? string.Empty;
                var part = new JsonObject { ["Name"] = (row.Name.Text ?? string.Empty).Trim(), ["Type"] = isFile ? "file" : "text" };
                if (isFile)
                {
                    part["Path"] = token;
                    if (!string.IsNullOrWhiteSpace(row.FileName.Text)) part["FileName"] = column ? $"_*{row.FileName.Text.Trim()}*_" : row.FileName.Text.Trim();
                    part["ContentType"] = string.IsNullOrWhiteSpace(row.ContentType.Text) ? "application/octet-stream" : row.ContentType.Text!.Trim();
                }
                else part["Value"] = token;
                parts.Add(part);
            }
            if (parts.Count == 0) _config.Remove("Parts"); else _config["Parts"] = parts;
            Save();
        }

        void AddRow(JsonObject? existing, string kind)
        {
            var row = new PartControls();
            var isFile = existing is null ? kind == "File" : string.Equals(existing["Type"]?.ToString(), "file", StringComparison.OrdinalIgnoreCase);
            var raw = existing is null ? string.Empty : (isFile ? existing["Path"] : existing["Value"])?.ToString() ?? string.Empty;
            var match = Token.Match(raw);
            row.Type.SelectedItem = isFile ? "File" : "Text";
            row.Name.Text = existing?["Name"]?.ToString() ?? string.Empty;
            row.Source.SelectedItem = match.Success ? "Request column" : "Fixed value";
            row.Value.Text = match.Success ? match.Groups[1].Value : raw;
            row.Value.Watermark = isFile ? "Path, e.g. Files/sample.pdf" : "Value";
            var fileName = existing?["FileName"]?.ToString() ?? string.Empty;
            row.FileName.Text = Token.Match(fileName) is { Success: true } fm ? fm.Groups[1].Value : fileName;
            row.ContentType.Text = existing?["ContentType"]?.ToString() ?? "application/octet-stream";
            rows.Add(row);

            var browse = new Button { Content = "Browse…", IsVisible = isFile, Margin = new Thickness(6, 0, 0, 0) };
            browse.Click += async (_, _) =>
            {
                if (await PickFileAsync("Files") is not { } picked) return;
                row.Source.SelectedItem = "Fixed value"; row.Value.Text = picked;
                if (string.IsNullOrWhiteSpace(row.FileName.Text)) row.FileName.Text = System.IO.Path.GetFileName(picked);
                Write();
            };
            var remove = new Button { Content = "✕", Margin = new Thickness(6, 0, 0, 0) };
            ToolTip.SetTip(remove, "Remove this part");

            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Type.Margin = new Thickness(0, 0, 6, 0);
            top.Children.Add(row.Type); Grid.SetColumn(row.Name, 1); top.Children.Add(row.Name); Grid.SetColumn(remove, 2); top.Children.Add(remove);
            var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Source.Margin = new Thickness(0, 0, 6, 0);
            middle.Children.Add(row.Source); Grid.SetColumn(row.Value, 1); middle.Children.Add(row.Value); Grid.SetColumn(browse, 2); middle.Children.Add(browse);
            var file = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), IsVisible = isFile };
            row.ContentType.Margin = new Thickness(6, 0, 0, 0);
            file.Children.Add(row.FileName); Grid.SetColumn(row.ContentType, 1); file.Children.Add(row.ContentType);
            row.ContentType.HorizontalAlignment = HorizontalAlignment.Stretch;

            var card = new Border { BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Child = new StackPanel { Spacing = 6, Children = { top, middle, file } } };
            row.Type.SelectionChanged += (_, _) =>
            {
                var nowFile = row.Type.SelectedItem?.ToString() == "File";
                browse.IsVisible = nowFile; file.IsVisible = nowFile;
                row.Value.Watermark = nowFile ? "Path, e.g. Files/sample.pdf" : "Value";
                Write();
            };
            row.Name.LostFocus += (_, _) => Write();
            row.Value.LostFocus += (_, _) => Write();
            row.FileName.LostFocus += (_, _) => Write();
            row.Source.SelectionChanged += (_, _) => Write();
            row.ContentType.LostFocus += (_, _) => Write();
            remove.Click += (_, _) => { rows.Remove(row); list.Children.Remove(card); Write(); };
            list.Children.Add(card);
        }

        if (_config["Parts"] is JsonArray existing)
            foreach (var part in existing.OfType<JsonObject>()) AddRow(part, string.Empty);

        var addText = new Button { Content = "＋ Text field" };
        var addFile = new Button { Content = "＋ File", Margin = new Thickness(6, 0, 0, 0) };
        addText.Click += (_, _) => AddRow(null, "Text");
        addFile.Click += (_, _) => AddRow(null, "File");
        panel.Children.Add(new TextBlock { Text = "MULTIPART FORM PARTS", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Muted });
        panel.Children.Add(Note("Each part is a text field or a file. Use \"Request column\" to take the value, or the file path, from a column of the Data tab. Files are sent as bytes and never written to the logs."));
        panel.Children.Add(list);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { addText, addFile } });
        return panel;
    }

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>Opens the picker in the instance's folder for this kind of file (Files, Queries or Scripts); the path comes back relative to the instance, copying a file from elsewhere in first.</summary>
    private async Task<string?> PickFileAsync(string folderName, bool openPath = false)
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null) return null;
        var instance = _step.Editor is { } editor ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(editor.WorkbookPath)) : null;
        Avalonia.Platform.Storage.IStorageFolder? start = null;
        if (instance is not null)
        {
            var folder = System.IO.Path.Combine(instance, folderName);
            Directory.CreateDirectory(folder);
            start = await provider.TryGetFolderFromPathAsync(folder);
        }
        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a file", SuggestedStartLocation = start, AllowMultiple = false });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return null;
        if (instance is null) return path.Replace('\\', '/');
        try
        {
            // The File step reads from anywhere: a file outside the instance keeps its full path (writing still stays inside the workspace).
            var inside = System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.GetFullPath(instance).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (openPath && !inside) { _step.Editor?.Notify("The file is outside this instance, so the step keeps its full path. Reading works from anywhere; writing is limited to the workspace.", false); return System.IO.Path.GetFullPath(path).Replace('\\', '/'); }
            var (relative, note) = PickedFile.PrepareForStep(instance, path, folderName);
            if (note is not null) _step.Editor?.Notify(note, false);
            return relative;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _step.Editor?.Notify($"Could not use that file: {ex.Message}", true); return null; }
    }

    private static TextBlock Note(string text) => new() { Text = text, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap };
}
