using System.Text.RegularExpressions;
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
    public string Sample { get; set; } = string.Empty;
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int Errors { get; set; }
    public int Bugs { get; set; }
    public bool EnvDiffers { get; set; }
    public bool IsLeaf => Children.Count == 0;

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
        if (StepFilter is null or AllSteps && JsonSteps().FirstOrDefault() is { } first) StepFilter = first;   // the view works on one step at a time
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

    /// <summary>Forget what the JSON view read from the history (after a run, or when the view opens).</summary>
    public void ForgetJsonSamples() { _jsonSamplesStep = string.Empty; _jsonSamples = new(StringComparer.OrdinalIgnoreCase); }

    /// <summary>The names of the <c>_variables_</c> the step's template uses; null when the step has no readable template.</summary>
    private HashSet<string>? TemplateVariableNames(string step)
    {
        var row = _document.Config.Rows.FirstOrDefault(r => !r.IsBlank && string.Equals(r.Get("stepname").Trim(), step, StringComparison.OrdinalIgnoreCase)
            && (SelectedTestcase is not { } t || WorkbookDocument.EffectiveIndex(r) == t.Index));
        var source = row?.Get("templetsource").Trim() ?? string.Empty;
        if (source.Length == 0) return null;
        try
        {
            var file = TemplateEditorWindow.ResolveInside(TemplatesFolder, source);
            if (!File.Exists(file)) return null;
            return Regex.Matches(File.ReadAllText(file), LoadTestingTool.Execution.TemplateRenderer.PlaceholderPattern)
                .Select(m => m.Value.Trim('_').Trim('*')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    private static JsonViewNode Child(JsonViewNode parent, string name, string path)
    {
        var node = parent.Children.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (node is null) { node = new JsonViewNode { Name = name, Path = path, Parent = parent }; parent.Children.Add(node); }
        return node;
    }

    private static JsonViewNode Insert(JsonViewNode root, string path)
    {
        var node = root;
        var built = string.Empty;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            built = built.Length == 0 ? part : built + "." + part;
            node = Child(node, part, built);
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
        NewColumnName = node.Path;
        NewColumnVerb = "eq";
        NewColumnCount = 1;
        NewColumnPlacement = PlaceAtEnd;
        ConfirmAddColumnCommand.Execute(null);
        AddColumnVisible = false;
    }

    /// <summary>The items under <paramref name="scope"/> that exist in the last response (or the template) but have no column yet.</summary>
    public static IReadOnlyList<JsonViewNode> JsonExtras(JsonViewNode scope) => scope.Leaves().Where(l => l.Extra && l.Column is null).ToList();

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
