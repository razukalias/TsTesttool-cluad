using System.Globalization;
using System.Text.RegularExpressions;

namespace LoadTestingTool.Editing;

public sealed record StepTransferResult(string NewName, int DataRows, int ColumnsAdded, string? TemplateSource);
public sealed record TestcaseTransferResult(int NewIndex, string NewName, int Steps, int DataRows, IReadOnlyList<string> TemplateSources);

// Creating and copying testcases and steps: a blank testcase, a duplicated step, and copies into another workbook.
// Like the other testcase operations they only edit the in-memory tables, so they undo.
public sealed partial class WorkbookDocument
{
    /// <summary>Adds a testcase with one HTTP step and one empty data row under the next free number; returns its number.</summary>
    public int AddBlankTestcase(string? name = null)
    {
        EnsureNumericIndexes();
        var all = GetTestcases();
        var index = all.Select(t => t.Index).DefaultIfEmpty(0).Max() + 1;
        var taken = all.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        name = string.IsNullOrWhiteSpace(name) ? "NewTestcase" + index.ToString(CultureInfo.InvariantCulture) : name.Trim();
        var unique = name;
        for (var n = 2; taken.Contains(unique); n++) unique = name + n.ToString(CultureInfo.InvariantCulture);
        var number = index.ToString(CultureInfo.InvariantCulture);

        var stepName = "Step1";
        var steps = new HashSet<string>(Config.Rows.Where(r => !r.IsBlank).Select(r => r.Get("stepname").Trim()), StringComparer.OrdinalIgnoreCase);
        for (var n = 2; steps.Contains(stepName); n++) stepName = "Step" + n.ToString(CultureInfo.InvariantCulture);

        var step = Config.AddRow();
        step.Set("testcaseindex", number);
        step.Set("testcase", unique);
        step.Set("stepname", stepName);
        step.Set("steptype", "HTTP");
        step.Set("stage", "Test");
        step.Set("enabled", "True");
        foreach (var table in new[] { Request, Response })
        {
            if (table.FindColumn("dataid") < 0) continue;
            var row = table.AddRow();
            row.Set("testcaseindex", number);
            if (table.FindColumn("testcase") >= 0) row.Set("testcase", unique);
            row.Set("dataid", "1");
        }
        return index;
    }

    /// <summary>Copies a step inside its own testcase, right after the original, with its data columns and values.</summary>
    public StepTransferResult DuplicateStep(TableRow step, string? newName = null)
    {
        var index = EffectiveIndex(step);
        var position = RowsOf(Config, index).ToList().IndexOf(step);
        if (position < 0) throw new InvalidOperationException("That step is not in the config sheet.");
        var result = CopyStepTo(this, step, index, newName);
        // MoveStep swaps cell values between neighbouring rows, so walk the row at each position upwards rather than one row object.
        var steps = RowsOf(Config, index).ToList();
        for (var at = steps.Count - 1; at > position + 1; at--)
            if (!MoveStep(steps[at], -1)) break;
        return result;
    }

    /// <summary>The name a duplicate of <paramref name="stepName"/> would get (the one offered in the duplicate panel).</summary>
    public string SuggestCopyName(string stepName) => UniqueStepName(stepName);

    private bool NameInUse(string stepName)
        => Config.Rows.Any(r => !r.IsBlank && string.Equals(r.Get("stepname").Trim(), stepName, StringComparison.OrdinalIgnoreCase))
           || Request.Headers.Concat(Response.Headers).Any(h => TryRenameHeader(h, stepName, "x", out _));

    private string UniqueStepName(string wanted, ISet<string>? extraTaken = null)
    {
        if (!NameInUse(wanted) && extraTaken?.Contains(wanted) != true) return wanted;
        var candidate = wanted + "Copy";
        for (var n = 2; NameInUse(candidate) || extraTaken?.Contains(candidate) == true; n++) candidate = wanted + "Copy" + n.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    private static string RewriteStepNames(SheetTable table, SheetTable request, SheetTable response, string header, string text, IReadOnlyDictionary<string, string> map)
    {
        if (text.Length == 0 || map.Count == 0) return text;
        var result = text;
        foreach (var (oldName, newName) in map)
        {
            if (result.Contains("<From_", StringComparison.OrdinalIgnoreCase))
                result = Regex.Replace(result, $@"(<From_response_){Regex.Escape(oldName)}(\.)", $"${{1}}{newName}$2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (ReferenceEquals(table, request) && header.Trim().Equals("dontrun", StringComparison.OrdinalIgnoreCase))
                result = Regex.Replace(result, $@"(?<=^|[,|]\s*){Regex.Escape(oldName)}(?=\s*(?:[,|]|$))", newName, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (ReferenceEquals(table, response) && header.Trim().Equals("extractvariable", StringComparison.OrdinalIgnoreCase))
                result = Regex.Replace(result, $@"(?<=^|\|\s*){Regex.Escape(oldName)}(?=\.)", newName, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return result;
    }

    private static void CopyStepData(WorkbookDocument from, int fromIndex, WorkbookDocument to, int toIndex, SheetTable source, SheetTable target, string oldName, string newName, ref int dataRows, ref int columnsAdded)
    {
        var sourceRows = from.RowsOf(source, fromIndex).ToList();
        var targetRows = to.RowsOf(target, toIndex).ToList();
        var headers = source.Headers;
        var columns = new List<(int From, string To)>();
        for (var i = 0; i < headers.Count; i++)
            if (TryRenameHeader(headers[i], oldName, newName, out var renamed)) columns.Add((i, renamed));
        if (columns.Count == 0) return;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [oldName] = newName };
        var byId = target.FindColumn("dataid") >= 0 && source.FindColumn("dataid") >= 0;
        foreach (var (from_, to_) in columns)
        {
            var before = target.ColumnCount;
            var column = target.EnsureColumn(to_);
            columnsAdded += target.ColumnCount - before;
            for (var r = 0; r < sourceRows.Count; r++)
            {
                var row = byId
                    ? targetRows.FirstOrDefault(t => string.Equals(t.Get("dataid").Trim(), sourceRows[r].Get("dataid").Trim(), StringComparison.OrdinalIgnoreCase))
                    : (r < targetRows.Count ? targetRows[r] : null);
                if (row is null) continue;
                row.SetAt(column, RewriteStepNames(source, from.Request, from.Response, headers[from_], sourceRows[r].GetAt(from_), map));
                if (from_ == columns[0].From) dataRows++;
            }
        }
    }

    /// <summary>
    /// Copies one step into a testcase of <paramref name="target"/> (this or another workbook), at the end of its steps. The step's
    /// data columns come along; their values are matched to the target testcase's data rows by dataid, or by position when there is none.
    /// A name already in use in the target gets "Copy" appended.
    /// </summary>
    public StepTransferResult CopyStepTo(WorkbookDocument target, TableRow step, int targetTestcaseIndex, string? wantedName = null)
    {
        var sourceIndex = EffectiveIndex(step);
        var oldName = step.Get("stepname").Trim();
        if (oldName.Length == 0) throw new InvalidOperationException("The step has no name.");
        var info = target.GetTestcases().FirstOrDefault(t => t.Index == targetTestcaseIndex) ?? throw new InvalidOperationException($"The target has no testcase {targetTestcaseIndex}.");
        wantedName = wantedName?.Trim();
        if (!string.IsNullOrEmpty(wantedName) && target.NameInUse(wantedName)) throw new InvalidOperationException($"A step called {wantedName} already exists. Choose another name.");
        var newName = string.IsNullOrEmpty(wantedName) ? target.UniqueStepName(oldName) : wantedName;

        var copy = target.Config.AddRow();
        var headers = Config.Headers;
        for (var i = 0; i < headers.Count; i++)
        {
            var value = step.GetAt(i);
            if (value.Length > 0 || target.Config.FindColumn(headers[i]) >= 0) copy.Set(headers[i], value);
        }
        copy.Set("testcaseindex", targetTestcaseIndex.ToString(CultureInfo.InvariantCulture));
        copy.Set("testcase", info.Name);
        copy.Set("stepname", newName);

        int dataRows = 0, added = 0;
        CopyStepData(this, sourceIndex, target, targetTestcaseIndex, Request, target.Request, oldName, newName, ref dataRows, ref added);
        CopyStepData(this, sourceIndex, target, targetTestcaseIndex, Response, target.Response, oldName, newName, ref dataRows, ref added);
        CopyExtractMappings(target, sourceIndex, targetTestcaseIndex, oldName, newName);
        var template = step.Get("templetsource").Trim();
        return new StepTransferResult(newName, dataRows, added, template.Length == 0 ? null : template);
    }

    /// <summary>
    /// The response sheet's <c>extractvariable</c> cell holds <c>Step.path:variable</c> mappings separated by <c>|</c>. A copied step gets the
    /// same mappings under its own name (so it fills the same variables), on the data row with the same dataid.
    /// </summary>
    private void CopyExtractMappings(WorkbookDocument target, int sourceIndex, int targetIndex, string oldName, string newName)
    {
        if (Response.FindColumn("extractvariable") < 0) return;
        var targetRows = target.RowsOf(target.Response, targetIndex).ToList();
        var sourceRows = RowsOf(Response, sourceIndex).ToList();
        for (var r = 0; r < sourceRows.Count; r++)
        {
            var mappings = sourceRows[r].Get("extractvariable").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(m => m.StartsWith(oldName + ".", StringComparison.OrdinalIgnoreCase))
                .Select(m => newName + m[oldName.Length..]).ToList();
            if (mappings.Count == 0) continue;
            var row = target.Response.FindColumn("dataid") >= 0 && Response.FindColumn("dataid") >= 0
                ? targetRows.FirstOrDefault(t => string.Equals(t.Get("dataid").Trim(), sourceRows[r].Get("dataid").Trim(), StringComparison.OrdinalIgnoreCase))
                : (r < targetRows.Count ? targetRows[r] : null);
            if (row is null) continue;
            var existing = row.Get("extractvariable");
            var have = existing.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var m in mappings) if (!have.Contains(m, StringComparer.OrdinalIgnoreCase)) have.Add(m);
            row.Set("extractvariable", string.Join("|", have));
        }
    }

    /// <summary>Copies a whole testcase into <paramref name="target"/> under its next free number, with steps, data columns and data rows.</summary>
    public TestcaseTransferResult CopyTestcaseTo(WorkbookDocument target, int testcaseIndex)
    {
        var info = GetTestcases().FirstOrDefault(t => t.Index == testcaseIndex) ?? throw new InvalidOperationException($"There is no testcase {testcaseIndex}.");
        target.EnsureNumericIndexes();
        var existing = target.GetTestcases();
        var newIndex = existing.Select(t => t.Index).DefaultIfEmpty(0).Max() + 1;
        var number = newIndex.ToString(CultureInfo.InvariantCulture);
        var newName = existing.Any(t => string.Equals(t.Name, info.Name, StringComparison.OrdinalIgnoreCase)) ? info.Name + " copy" : info.Name;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in info.Steps)
        {
            var unique = target.UniqueStepName(step, taken);
            taken.Add(unique);
            if (!string.Equals(unique, step, StringComparison.OrdinalIgnoreCase)) map[step] = unique;
        }
        var finalNames = info.Steps.ToDictionary(s => s, s => map.GetValueOrDefault(s, s), StringComparer.OrdinalIgnoreCase);

        var templates = new List<string>();
        int rowsCopied = 0;

        void CopyRows(SheetTable from, SheetTable to, bool isConfig)
        {
            var headers = from.Headers;
            foreach (var row in RowsOf(from, testcaseIndex).ToList())
            {
                var copy = to.AddRow();
                for (var i = 0; i < headers.Count; i++)
                {
                    var value = row.GetAt(i);
                    if (value.Length == 0 && to.FindColumn(headers[i]) < 0) continue;
                    var header = headers[i];
                    var targetHeader = header;
                    foreach (var (oldName, newName2) in map)
                        if (TryRenameHeader(header, oldName, newName2, out var renamed)) { targetHeader = renamed; break; }
                    copy.Set(targetHeader, RewriteStepNames(from, Request, Response, header, value, map));
                }
                copy.Set("testcaseindex", number);
                if (to.FindColumn("testcase") >= 0 || isConfig) copy.Set("testcase", newName);
                if (isConfig)
                {
                    var old = row.Get("stepname").Trim();
                    copy.Set("stepname", finalNames.GetValueOrDefault(old, old));
                    var template = row.Get("templetsource").Trim();
                    if (template.Length > 0 && !templates.Contains(template, StringComparer.OrdinalIgnoreCase)) templates.Add(template);
                }
                else if (ReferenceEquals(from, Request)) rowsCopied++;
            }
        }

        CopyRows(Config, target.Config, true);
        CopyRows(Request, target.Request, false);
        CopyRows(Response, target.Response, false);
        return new TestcaseTransferResult(newIndex, newName, info.Steps.Count, rowsCopied, templates);
    }
}
