using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LoadTestingTool.Editing;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.UI.DataEditor;

public enum JsonTab { Response, Template, Config }

/// <summary>How one cell fared for one environment (Environment is empty when the run had a single environment).</summary>
public sealed record JsonCellResult(string Environment, string State, string Actual, string Message);

/// <summary>One item of the JSON view's tree: a response field, a template variable or a config column, with what the latest run says about it.</summary>
public sealed class JsonViewNode
{
    public string Name { get; init; } = string.Empty;
    /// <summary>Path below the step (<c>body.id</c>); for request items the variable name.</summary>
    public string Path { get; init; } = string.Empty;
    public JsonViewNode? Parent { get; init; }
    public List<JsonViewNode> Children { get; } = [];
    /// <summary>The column that holds the item (null for an item that only exists in the source).</summary>
    public GridColumn? Column { get; set; }
    public string Verb { get; set; } = string.Empty;
    /// <summary>In the last response (or the template) but no column yet.</summary>
    public bool Extra { get; set; }
    /// <summary>A column, but the last responses do not have the field.</summary>
    public bool Missing { get; set; }
    public bool Shared { get; set; }
    /// <summary>For a request item: the <c>_variable_</c> of the template that this item is (its column is <c>Step.variable</c> or the shared <c>variable</c>).</summary>
    public string Variable { get; set; } = string.Empty;
    /// <summary>Text of the template that has no variable in it.</summary>
    public bool Constant { get; set; }
    public string Sample { get; set; } = string.Empty;
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Errors { get; set; }
    public int Bugs { get; set; }
    public bool EnvDiffers { get; set; }
    public bool IsLeaf => Children.Count == 0;
    /// <summary>Set on the top node of a step when the view shows all steps; the items below belong to that step.</summary>
    public string StepName { get; set; } = string.Empty;
    /// <summary>The step an item belongs to in the all-steps tree (empty in the one-step tree).</summary>
    public string StepOf() { for (var n = this; n is not null; n = n.Parent) if (n.StepName.Length > 0) return n.StepName; return string.Empty; }

    public IEnumerable<JsonViewNode> Leaves() => IsLeaf ? [this] : Children.SelectMany(c => c.Leaves());
    public bool HasStatus => Passed + Failed + Errors + Bugs > 0;
}

public sealed partial class DataEditorViewModel
{
    /// <summary>JSON view: the data rows as a tree of items (response fields, template variables, config columns) with the rows below each item.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty, CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(ShowJsonView), nameof(ShowTableGrid), nameof(ShowFailedCellsList), nameof(ViewModeIndex))]
    private bool _showJson;

    public bool ShowJsonView => ShowDataGrid && ShowJson;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenJsonTab()
    {
        ShowSteps = false; ShowRecord = false;
        ForgetJsonSamples();
        if (StepFilter is null or AllSteps && JsonSteps().FirstOrDefault() is { } first) StepFilter = first;   // opens on the first step; "All steps" is one pick away
        ShowJson = true;
    }

    private string _jsonSamplesStep = string.Empty;
    private Dictionary<string, ResponseSample> _jsonSamples = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The steps of the selected testcase (or the instance) the JSON view can show.</summary>
    public IReadOnlyList<string> JsonSteps() => StepFilterOptions.Where(s => s != AllSteps).ToList();

    private IReadOnlyCollection<ResponseField> SampleFields(string step)
    {
        if (!string.Equals(_jsonSamplesStep, step, StringComparison.OrdinalIgnoreCase))
        {
            _jsonSamplesStep = step;
            _jsonSamples = ResponseSamples.LatestPerDataId(HistoryFolderPath, step, 20);
        }
        var seen = new Dictionary<string, ResponseField>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in _jsonSamples.Values.Take(6))
            foreach (var field in ResponseSamples.Flatten(sample.Body)) seen.TryAdd(field.Path, field);
        return seen.Values;
    }

    /// <summary>
    /// Takes pasted JSON or XML as the response of one row of the step, so its fields show up as extra items (and "Add columns" can make the columns and fill the
    /// cells from it). Nothing is written to the workbook. Returns an empty string when it worked, otherwise what is wrong with the text.
    /// </summary>
    public string UsePastedResponse(string step, string dataId, string text)
    {
        if (step.Length == 0) return "Choose a step first.";
        if (dataId.Length == 0) return "There is no row to attach the response to.";
        if (string.IsNullOrWhiteSpace(text)) return "The clipboard has no text.";
        if (ResponseTree.ParseAny(text, out var note) is null) return note.Length > 0 ? note : "The text is neither JSON nor XML.";
        SampleFields(step);
        _jsonSamples[dataId] = new ResponseSample(text, "pasted", dataId, "pasted");
        return string.Empty;
    }

    /// <summary>Forget what the JSON view read from the history (after a run, or when the view opens).</summary>
    public void ForgetJsonSamples() { _jsonSamplesStep = string.Empty; _jsonSamples = new(StringComparer.OrdinalIgnoreCase); }

    /// <summary>The text of the step's template file; null when the step has none or it cannot be read.</summary>
    private string? TemplateText(string step)
    {
        var row = _document.Config.Rows.FirstOrDefault(r => !r.IsBlank && string.Equals(r.Get("stepname").Trim(), step, StringComparison.OrdinalIgnoreCase)
            && (SelectedTestcase is not { } t || WorkbookDocument.EffectiveIndex(r) == t.Index));
        var source = row?.Get("templetsource").Trim() ?? string.Empty;
        if (source.Length == 0) return null;
        try
        {
            var file = TemplateEditorWindow.ResolveInside(TemplatesFolder, source);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    private static readonly Regex PlaceholderAt = new(@"\G" + LoadTestingTool.Execution.TemplateRenderer.PlaceholderPattern, RegexOptions.Compiled);

    private static string PlaceholderName(string text) => text.Trim('_').Trim('*');

    /// <summary>The names of the <c>_variables_</c> the step's template uses; null when the step has no readable template.</summary>
    private HashSet<string>? TemplateVariableNames(string step)
        => TemplateText(step) is { } text
            ? Regex.Matches(text, LoadTestingTool.Execution.TemplateRenderer.PlaceholderPattern).Select(m => PlaceholderName(m.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

    /// <summary>JSON with the placeholders that stand alone (a number or a boolean position, <c>"isPrimary": _isPrimary[0]_</c>) put in quotes so it parses; placeholders inside strings stay as they are.</summary>
    internal static string QuoteBarePlaceholders(string text)
    {
        var result = new System.Text.StringBuilder(text.Length + 16);
        var inString = false;
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (inString)
            {
                result.Append(c);
                if (c == '\\' && i + 1 < text.Length) { result.Append(text[i + 1]); i += 2; continue; }
                if (c == '"') inString = false;
                i++;
                continue;
            }
            if (c == '"') { inString = true; result.Append(c); i++; continue; }
            var m = PlaceholderAt.Match(text, i);
            if (m.Success && m.Length > 0) { result.Append('"').Append(m.Value).Append('"'); i += m.Length; continue; }
            result.Append(c);
            i++;
        }
        return result.ToString();
    }

    /// <summary>
    /// The structure of a template (JSON properties and array elements, XML elements and attributes) with each <c>_variable_</c> as a leaf that points at its column.
    /// Null when the text is neither JSON nor XML, so the caller falls back to a plain list of variables.
    /// </summary>
    private JsonViewNode? TemplateStructure(string step, string text)
    {
        var root = new JsonViewNode { Name = step, Path = string.Empty };
        var prefix = step + ".";
        (GridColumn? Column, bool Shared) ColumnFor(string name)
        {
            var own = Columns.FirstOrDefault(c => c.Group == ColumnGroup.Request && string.Equals(c.Title, prefix + name, StringComparison.OrdinalIgnoreCase));
            if (own is not null) return (own, false);
            var shared = Columns.FirstOrDefault(c => c.Group == ColumnGroup.Request && string.Equals(c.Title, name, StringComparison.OrdinalIgnoreCase) && RequestStepPrefix(c.Title) is null);
            return (shared, shared is not null);
        }
        void Bind(JsonViewNode node, string name)
        {
            node.Variable = name;
            var (column, shared) = ColumnFor(name);
            node.Column = column; node.Shared = shared; node.Extra = column is null;
        }
        void Leaf(JsonViewNode node, string value)
        {
            var names = Regex.Matches(value, LoadTestingTool.Execution.TemplateRenderer.PlaceholderPattern).Select(m => PlaceholderName(m.Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) { node.Constant = true; node.Sample = value; }
            else if (names.Count == 1) Bind(node, names[0]);
            else foreach (var name in names) { var child = new JsonViewNode { Name = name, Path = node.Path + "#" + name, Parent = node }; node.Children.Add(child); Bind(child, name); }
        }
        JsonViewNode Make(JsonViewNode parent, string name, string path)
        {
            var node = new JsonViewNode { Name = name, Path = path, Parent = parent };
            parent.Children.Add(node);
            return node;
        }
        string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;

        void FillJson(JsonViewNode node, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject()) FillJson(Make(node, property.Name, Join(node.Path, property.Name)), property.Value);
                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray()) { FillJson(Make(node, $"[{index}]", node.Path + $"[{index}]"), item); index++; }
                    break;
                case JsonValueKind.String: Leaf(node, element.GetString() ?? string.Empty); break;
                default: Leaf(node, element.GetRawText()); break;
            }
        }
        void FillXml(JsonViewNode node, XElement element)
        {
            foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
                Leaf(Make(node, "@" + attribute.Name.LocalName, Join(node.Path, "@" + attribute.Name.LocalName)), attribute.Value);
            var children = element.Elements().ToList();
            if (children.Count == 0) { if (element.Value.Trim().Length > 0 || !element.Attributes().Any(a => !a.IsNamespaceDeclaration)) Leaf(node.Children.Count == 0 ? node : Make(node, "#text", Join(node.Path, "#text")), element.Value.Trim()); return; }
            foreach (var group in children.GroupBy(c => c.Name.LocalName))
            {
                var items = group.ToList();
                if (items.Count == 1) { FillXml(Make(node, group.Key, Join(node.Path, group.Key)), items[0]); continue; }
                var container = Make(node, group.Key, Join(node.Path, group.Key));
                for (var i = 0; i < items.Count; i++) FillXml(Make(container, $"[{i}]", container.Path + $"[{i}]"), items[i]);
            }
        }

        try
        {
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                using var document = JsonDocument.Parse(QuoteBarePlaceholders(text));
                FillJson(root, document.RootElement);
            }
            else if (trimmed.StartsWith('<') && XDocument.Parse(text).Root is { } top) FillXml(Make(root, top.Name.LocalName, top.Name.LocalName), top);
            else return null;
        }
        catch (Exception ex) when (ex is JsonException or XmlException) { return null; }
        return root;
    }

    private static JsonViewNode Child(JsonViewNode parent, string name, string path)
    {
        var node = parent.Children.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (node is null) { node = new JsonViewNode { Name = name, Path = path, Parent = parent }; parent.Children.Add(node); }
        return node;
    }

    private static readonly Regex Segment = new(@"^(?<name>[^\[\]]*)(?<indexes>(?:\[\d+\])*)$", RegexOptions.Compiled);

    /// <summary>
    /// Puts a path in the tree. A name with an index (<c>Line[0]</c>) becomes a <c>Line</c> node with a <c>[0]</c> child, so the elements of an array or the
    /// repeated elements of an XML response sit together; the paths of the nodes stay exactly the runner's paths (<c>Lines.Line[0].@sku</c>).
    /// </summary>
    private static JsonViewNode Insert(JsonViewNode root, string path)
    {
        var node = root;
        var built = string.Empty;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Segment.Match(part);
            if (!m.Success) { built = built.Length == 0 ? part : built + "." + part; node = Child(node, part, built); continue; }
            var name = m.Groups["name"].Value;
            if (name.Length > 0 || m.Groups["indexes"].Length == 0)
            {
                built = built.Length == 0 ? name : built + "." + name;
                node = Child(node, name, built);
            }
            foreach (System.Text.RegularExpressions.Capture index in m.Groups["indexes"].Captures)
            {
                if (index.Length == 0) continue;   // the group also "captures" nothing when the name has no index
                built += index.Value;
                node = Child(node, index.Value, built);
            }
        }
        return node;
    }

    /// <summary>
    /// The tree of one tab for one step. Response: the step's expected columns merged with the fields of its last responses (fields without a column are Extra,
    /// columns the responses lack are Missing). Template: the variables the template uses (shared ones marked). Config: the other request columns.
    /// </summary>
    public JsonViewNode BuildJsonTree(JsonTab tab, string step)
    {
        var root = new JsonViewNode { Name = step.Length == 0 ? "(step)" : step, Path = string.Empty };
        if (step.Length == 0) return root;
        if (tab == JsonTab.Response)
        {
            foreach (var column in Columns.Where(c => c.Group == ColumnGroup.Response && string.Equals(ResponseStepPrefix(c.Title), step, StringComparison.OrdinalIgnoreCase)))
            {
                var header = StripVerb(column.Title);
                var node = Insert(root, header[(step.Length + 1)..]);
                node.Column = column;
                var verbEnd = column.Title.IndexOf('}');
                node.Verb = column.Title.StartsWith('{') && verbEnd > 0 ? column.Title[1..verbEnd] : "eq";
            }
            var fields = SampleFields(step);
            if (fields.Count > 0)
            {
                var paths = fields.ToDictionary(f => f.Path, f => f.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var field in fields)
                {
                    var node = Insert(root, field.Path);
                    if (node.Column is null) { node.Extra = true; node.Sample = field.Value; }
                }
                foreach (var leaf in root.Leaves().Where(l => l.Column is not null && l.Path != "statusCode" && !l.Path.StartsWith(LoadTestingTool.Execution.AssertionEngine.ResponseHeaderPrefix, StringComparison.OrdinalIgnoreCase)))
                    if (!paths.ContainsKey(leaf.Path) && !paths.Keys.Any(k => k.StartsWith(leaf.Path + ".", StringComparison.OrdinalIgnoreCase) || k.StartsWith(leaf.Path + "[", StringComparison.OrdinalIgnoreCase))) leaf.Missing = true;
            }
            return root;
        }

        var used = TemplateVariableNames(step);
        var prefix = step + ".";
        var wanted = tab == JsonTab.Template;
        if (wanted && TemplateText(step) is { } templateText && TemplateStructure(step, templateText) is { } structure) return structure;
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in Columns.Where(c => c.Group == ColumnGroup.Request && !ReservedColumns.Contains(c.Title) && c.Title.Length > 0))
        {
            var own = column.Title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            var shared = RequestStepPrefix(column.Title) is null;
            if (!own && !shared) continue;
            var name = own ? column.Title[prefix.Length..] : column.Title;
            var inTemplate = used is null || used.Contains(name);
            if (inTemplate != wanted) continue;
            var node = Insert(root, name);
            node.Column = column; node.Shared = shared;
            named.Add(name);
        }
        if (wanted && used is not null)
            foreach (var variable in used.Where(v => !named.Contains(v)))
            {
                var node = Insert(root, variable);
                node.Extra = true;
            }
        return root;
    }

    /// <summary>What the latest run says about one response cell of one row, per environment (empty when the row has no results).</summary>
    public IReadOnlyList<JsonCellResult> JsonCellResults(DataRowViewModel row, string step, JsonViewNode leaf)
    {
        if (leaf.Column is null || row.Outcome is null) return [];
        var cell = leaf.Column.CellIndex;
        if (cell >= row.Cells.Count || string.IsNullOrWhiteSpace(row.Cells[cell])) return [];
        JsonCellResult Of(string environment, RowOutcome? outcome)
        {
            if (outcome is null) return new JsonCellResult(environment, "—", string.Empty, "did not run");
            var failure = outcome.Failures.FirstOrDefault(f => f.IsAssertion && string.Equals(f.StepName, step, StringComparison.OrdinalIgnoreCase) && string.Equals(f.ResponsePath, leaf.Path, StringComparison.OrdinalIgnoreCase));
            if (failure is not null) return new JsonCellResult(environment, IsKnownBug(row, failure) ? "BUG" : "FAIL", failure.Actual ?? string.Empty, failure.Message);
            var broke = outcome.Failures.FirstOrDefault(f => f.IsError && string.Equals(f.StepName, step, StringComparison.OrdinalIgnoreCase));
            if (broke is not null) return new JsonCellResult(environment, "ERROR", string.Empty, broke.Message);
            return new JsonCellResult(environment, "PASS", string.Empty, string.Empty);
        }
        if (row.HasEnvChips && row.AllOutcome is { } all) return row.RunEnvironments.Select(e => Of(e, all.In(e))).ToList();
        return [Of(string.Empty, row.Outcome)];
    }

    /// <summary>Adds the passed / failed / error / bug counts of the rows shown to every item of a Response tree, and marks the items whose environments disagree.</summary>
    public void TallyJsonTree(JsonViewNode root, string step)
    {
        foreach (var node in Walk(root)) { node.Passed = node.Failed = node.Errors = node.Bugs = 0; node.EnvDiffers = false; }
        foreach (var leaf in root.Leaves().Where(l => l.Column is not null))
            foreach (var row in VisibleRows)
            {
                var results = JsonCellResults(row, step, leaf);
                if (results.Count == 0) continue;
                var differs = results.Select(r => r.State).Where(s => s != "—").Distinct().Count() > 1;
                var worst = results.Any(r => r.State == "ERROR") ? "ERROR" : results.Any(r => r.State == "FAIL") ? "FAIL" : results.Any(r => r.State == "BUG") ? "BUG" : results.Any(r => r.State == "PASS") ? "PASS" : "—";
                for (var n = leaf; n is not null; n = n.Parent)
                {
                    switch (worst) { case "PASS": n.Passed++; break; case "FAIL": n.Failed++; break; case "ERROR": n.Errors++; break; case "BUG": n.Bugs++; break; }
                    if (differs) n.EnvDiffers = true;
                }
            }
    }

    private static IEnumerable<JsonViewNode> Walk(JsonViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
}

public sealed partial class DataEditorViewModel
{
    /// <summary>Adds the column of an item that only exists in the last response or in the template (the "＋" of an extra item).</summary>
    public void AddJsonItemColumn(JsonTab tab, string step, JsonViewNode node)
    {
        if (SelectedTestcase is null || node.Column is not null || step.Length == 0) return;
        OpenAddColumn();
        NewColumnKind = tab == JsonTab.Response ? ExpectedKind : RequestKind;
        NewColumnStep = step;
        NewColumnName = node.Variable.Length > 0 ? node.Variable : node.Path;
        NewColumnVerb = "eq";
        NewColumnCount = 1;
        NewColumnPlacement = PlaceAtEnd;
        ConfirmAddColumnCommand.Execute(null);
        AddColumnVisible = false;
    }

    /// <summary>The items under <paramref name="scope"/> that exist in the last response (or the template) but have no column yet.</summary>
    public static IReadOnlyList<JsonViewNode> JsonExtras(JsonViewNode scope)
        => scope.Leaves().Where(l => l.Extra && l.Column is null).GroupBy(l => l.Variable.Length > 0 ? l.Variable : l.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

    /// <summary>
    /// Adds the columns of every extra item under a node. Response items become <c>{eq}Step.path</c> expected columns whose cells are filled from the responses
    /// (none, the row shown, or every row from its own response), the same as the response window does; template variables become request columns.
    /// Returns how many columns were added.
    /// </summary>
    public int AddJsonExtras(JsonTab tab, string step, JsonViewNode scope, ResponseFill fill, string? currentDataId)
    {
        var extras = JsonExtras(scope);
        if (extras.Count == 0 || step.Length == 0 || tab == JsonTab.Config) return 0;
        if (tab == JsonTab.Template)
        {
            foreach (var node in extras) AddJsonItemColumn(tab, step, node);
            return extras.Count;
        }
        var verbs = extras.Select(e => ("eq", e.Path)).ToList();
        LoadTestingTool.Reporting.ResponseNode? TreeFor(string dataId) => _jsonSamples.TryGetValue(dataId, out var sample) ? ResponseTree.ParseAny(sample.Body, out _) : null;
        return AddResponseBodyColumns(step, verbs, fill, currentDataId, TreeFor);
    }

    /// <summary>
    /// The text the Raw toggle shows for one row: the stored response laid out for reading (Response tab), or the template with the row's values in place of the
    /// <c>_variables_</c> (Template tab; a value that is a generator such as a random number stays as written).
    /// </summary>
    public string JsonRawText(JsonTab tab, string step, DataRowViewModel row)
    {
        if (step.Length == 0) return "Choose a step.";
        if (tab == JsonTab.Response)
        {
            SampleFields(step);
            return _jsonSamples.TryGetValue(row.CurrentDataId, out var sample)
                ? ResponseText.Pretty(sample.Body)
                : $"No stored response for {row.CurrentDataId}. Run the testcase with the request history on, then open this again.";
        }
        if (tab == JsonTab.Config) return "The raw view shows the response (Response tab) or the template with this row's values (Template tab).";
        if (TemplateText(step) is not { } text) return "This step has no template file.";
        var prefix = step + ".";
        return Regex.Replace(text, LoadTestingTool.Execution.TemplateRenderer.PlaceholderPattern, m =>
        {
            var name = PlaceholderName(m.Value);
            var column = Columns.FirstOrDefault(c => c.Group == ColumnGroup.Request && (string.Equals(c.Title, prefix + name, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Title, name, StringComparison.OrdinalIgnoreCase)));
            return column is null || column.CellIndex >= row.Cells.Count || string.IsNullOrEmpty(row.Cells[column.CellIndex]) ? m.Value : row.Cells[column.CellIndex];
        });
    }

    /// <summary>The values of one column over some rows, one per line, for the clipboard.</summary>
    public static string CopyColumnValues(IEnumerable<DataRowViewModel> rows, int cellIndex)
        => string.Join(Environment.NewLine, rows.Select(r => r.Cells[cellIndex] ?? string.Empty));

    /// <summary>
    /// Pastes lines into one column: one line goes to every one of the rows, several lines go to the rows in order (the last line repeats no further).
    /// Returns how many cells changed.
    /// </summary>
    public int PasteColumnValues(IReadOnlyList<DataRowViewModel> rows, int cellIndex, string text)
    {
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (rows.Count == 0 || lines.Length == 0 || cellIndex < 1 || cellIndex > Columns.Count) return 0;
        Record($"paste into {Columns[cellIndex - 1].Title}");
        var changed = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var value = lines.Length == 1 ? lines[0] : i < lines.Length ? lines[i] : null;
            if (value is not null && ApplyCell(rows[i], cellIndex, value) == CellResult.Changed) changed++;
        }
        FinishBulk(changed, $"Pasted into {changed} {(changed == 1 ? "cell" : "cells")} of {Columns[cellIndex - 1].Title}", 0, 0);
        return changed;
    }

    /// <summary>Empties one column in the given rows.</summary>
    public int ClearColumnValues(IReadOnlyList<DataRowViewModel> rows, int cellIndex)
    {
        if (rows.Count == 0 || cellIndex < 1 || cellIndex > Columns.Count) return 0;
        Record($"clear {Columns[cellIndex - 1].Title}");
        var changed = rows.Count(r => ApplyCell(r, cellIndex, string.Empty) == CellResult.Changed);
        FinishBulk(changed, $"Cleared {changed} {(changed == 1 ? "cell" : "cells")} of {Columns[cellIndex - 1].Title}", 0, 0);
        return changed;
    }
}
