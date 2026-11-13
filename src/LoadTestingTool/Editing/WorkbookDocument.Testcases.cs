using System.Text.RegularExpressions;

namespace LoadTestingTool.Editing;

public sealed record TestcaseCopyResult(int NewIndex, int Steps, int DataRows, int ColumnsAdded, int AbsoluteReferences);
public sealed record TestcaseDeleteResult(int Steps, int DataRows, int LeftoverColumns);

// Testcase-level operations: rename, run order, copy and delete. They only edit the in-memory tables (so they undo),
// and refuse, changing nothing, when they cannot be done safely.
public sealed partial class WorkbookDocument
{
    private IEnumerable<TableRow> RowsOf(SheetTable table, int testcaseIndex)
        => table.Rows.Where(r => !r.IsBlank && EffectiveIndex(r) == testcaseIndex);

    /// <summary>Gives every row of the testcase in the config, request and response sheets the new name.</summary>
    public void RenameTestcase(int testcaseIndex, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) throw new InvalidOperationException("A testcase name cannot be empty.");
        if (!GetTestcases().Any(t => t.Index == testcaseIndex)) throw new InvalidOperationException($"There is no testcase {testcaseIndex}.");
        foreach (var table in new[] { Config, Request, Response })
        {
            if (table.FindColumn("testcase") < 0) continue;
            foreach (var row in RowsOf(table, testcaseIndex)) row.Set("testcase", newName);
        }
    }

    private void EnsureNumericIndexes()
    {
        foreach (var table in new[] { Config, Request, Response })
            foreach (var row in table.Rows.Where(r => !r.IsBlank))
                if (RowIndex(row) is null)
                    throw new InvalidOperationException($"{table.SheetName} row {row.SourceRow?.ToString() ?? "(new)"} has no numeric testcaseindex, so testcases cannot be reordered. Give it one first.");
    }

    /// <summary>
    /// Moves a testcase one place earlier (-1) or later (+1) in run order by swapping testcase numbers with its neighbour in all
    /// three sheets; the runner orders testcases by that number. Returns the testcase's new number.
    /// </summary>
    public int MoveTestcase(int testcaseIndex, int direction)
    {
        var all = GetTestcases();
        var at = all.ToList().FindIndex(t => t.Index == testcaseIndex);
        if (at < 0) throw new InvalidOperationException($"There is no testcase {testcaseIndex}.");
        var target = at + Math.Sign(direction);
        if (target < 0) throw new InvalidOperationException("That testcase already runs first.");
        if (target >= all.Count) throw new InvalidOperationException("That testcase already runs last.");
        EnsureNumericIndexes();
        var other = all[target].Index;
        var mine = testcaseIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var theirs = other.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var table in new[] { Config, Request, Response })
        {
            var a = RowsOf(table, testcaseIndex).ToList();
            var b = RowsOf(table, other).ToList();
            foreach (var row in a) row.Set("testcaseindex", theirs);
            foreach (var row in b) row.Set("testcaseindex", mine);
        }
        return other;
    }

    /// <summary>Moves a step one place earlier (-1) or later (+1) within its testcase by swapping its config row's values with its neighbour's.</summary>
    public bool MoveStep(TableRow step, int direction)
    {
        var index = EffectiveIndex(step);
        var steps = RowsOf(Config, index).ToList();
        var at = steps.IndexOf(step);
        var target = at + Math.Sign(direction);
        if (at < 0 || target < 0 || target >= steps.Count) return false;
        var other = steps[target];
        for (var i = 0; i < Config.ColumnCount; i++)
        {
            var mine = step.GetAt(i);
            step.SetAt(i, other.GetAt(i));
            other.SetAt(i, mine);
        }
        return true;
    }

    /// <summary>
    /// Copies a testcase under the next free number: its steps (renamed so names stay unique), their data columns, and every data row
    /// with its expected values. Runtime references, dontrun lists and extractvariable mappings follow the new step names.
    /// </summary>
    public TestcaseCopyResult CopyTestcase(int testcaseIndex, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) throw new InvalidOperationException("A testcase name cannot be empty.");
        var info = GetTestcases().FirstOrDefault(t => t.Index == testcaseIndex) ?? throw new InvalidOperationException($"There is no testcase {testcaseIndex}.");
        EnsureNumericIndexes();
        var newIndex = GetTestcases().Max(t => t.Index) + 1;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(Config.Rows.Where(r => !r.IsBlank).Select(r => r.Get("stepname").Trim()), StringComparer.OrdinalIgnoreCase);
        var headers = Request.Headers.Concat(Response.Headers).ToList();
        foreach (var step in info.Steps)
        {
            var candidate = step + "Copy";
            for (var n = 2; taken.Contains(candidate) || headers.Any(h => TryRenameHeader(h, candidate, "x", out _)); n++) candidate = step + "Copy" + n;
            taken.Add(candidate);
            map[step] = candidate;
        }

        // columns first, so every new row is created at its final width
        var added = 0;
        var columnMap = new Dictionary<SheetTable, List<(int From, int To)>>();
        foreach (var table in new[] { Request, Response })
        {
            var pairs = new List<(int, int)>();
            var existing = table.Headers;
            for (var i = 0; i < existing.Count; i++)
                foreach (var (oldName, newStep) in map)
                    if (TryRenameHeader(existing[i], oldName, newStep, out var renamed))
                    {
                        var before = table.ColumnCount;
                        pairs.Add((i, table.EnsureColumn(renamed)));
                        added += table.ColumnCount - before;
                        break;
                    }
            columnMap[table] = pairs;
        }

        var index = newIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var references = 0;
        string Rewrite(SheetTable table, string header, string text)
        {
            if (text.Length == 0) return text;
            var result = text;
            foreach (var (oldName, newStep) in map)
            {
                if (result.Contains("<From_", StringComparison.OrdinalIgnoreCase))
                    result = Regex.Replace(result, $@"(<From_response_){Regex.Escape(oldName)}(\.)", $"${{1}}{newStep}$2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (ReferenceEquals(table, Request) && header.Trim().Equals("dontrun", StringComparison.OrdinalIgnoreCase))
                    result = Regex.Replace(result, $@"(?<=^|[,|]\s*){Regex.Escape(oldName)}(?=\s*(?:[,|]|$))", newStep, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (ReferenceEquals(table, Response) && header.Trim().Equals("extractvariable", StringComparison.OrdinalIgnoreCase))
                    result = Regex.Replace(result, $@"(?<=^|\|\s*){Regex.Escape(oldName)}(?=\.)", newStep, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            references += RowInsertion.CountRowReferences(result);
            return result;
        }

        TableRow CopyRow(SheetTable table, TableRow source)
        {
            var copy = table.AddRow();
            var headersNow = table.Headers;
            for (var i = 0; i < headersNow.Count && i < table.ColumnCount; i++)
            {
                var value = source.GetAt(i);
                copy.SetAt(i, Rewrite(table, headersNow[i], value));
            }
            // the step's own data moves to its renamed columns, leaving the originals blank for this testcase
            if (columnMap.TryGetValue(table, out var pairs))
                foreach (var (from, to) in pairs)
                {
                    copy.SetAt(to, copy.GetAt(from));
                    copy.SetAt(from, string.Empty);
                }
            copy.Set("testcaseindex", index);
            if (table.FindColumn("testcase") >= 0) copy.Set("testcase", newName);
            return copy;
        }

        var steps = RowsOf(Config, testcaseIndex).ToList();
        foreach (var row in steps)
        {
            var copy = CopyRow(Config, row);
            copy.Set("stepname", map[row.Get("stepname").Trim()]);
        }
        var requestRows = RowsOf(Request, testcaseIndex).ToList();
        foreach (var row in requestRows) CopyRow(Request, row);
        foreach (var row in RowsOf(Response, testcaseIndex).ToList()) CopyRow(Response, row);
        return new TestcaseCopyResult(newIndex, steps.Count, requestRows.Count, added, references);
    }

    /// <summary>
    /// Removes a testcase's steps and data rows (their sheet rows are cleared on save). Refuses when another testcase
    /// refers to its rows by number, or to its steps' responses at run time; the data columns of its steps stay.
    /// </summary>
    public TestcaseDeleteResult DeleteTestcase(int testcaseIndex)
    {
        var info = GetTestcases().FirstOrDefault(t => t.Index == testcaseIndex) ?? throw new InvalidOperationException($"There is no testcase {testcaseIndex}.");
        var doomed = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in new[] { Config, Request, Response })
            doomed[table.SheetName] = RowsOf(table, testcaseIndex).Where(r => r.SourceRow is not null).Select(r => r.SourceRow!.Value).ToHashSet();

        foreach (var reference in FindRowReferences())
        {
            if (!doomed.TryGetValue(reference.TargetSheet, out var targets) || !targets.Contains(reference.TargetRow)) continue;
            var cellRow = int.Parse(new string(reference.Cell.Where(char.IsDigit).ToArray()), System.Globalization.CultureInfo.InvariantCulture);
            if (doomed.TryGetValue(reference.Sheet, out var own) && own.Contains(cellRow)) continue; // the reference dies with the testcase
            throw new InvalidOperationException($"Cannot delete testcase {testcaseIndex}: {reference.Sheet}!{reference.Cell} refers to its row {reference.TargetRow} ({reference.Token}). Change that reference first.");
        }

        var keep = new HashSet<TableRow>();
        foreach (var table in new[] { Config, Request, Response })
            foreach (var row in table.Rows.Where(r => !r.IsBlank && EffectiveIndex(r) != testcaseIndex)) keep.Add(row);
        foreach (var step in info.Steps)
        {
            var needle = "<From_response_" + step + ".";
            foreach (var table in new[] { Config, Request, Response })
                foreach (var row in table.Rows.Where(keep.Contains))
                    for (var i = 0; i < table.ColumnCount; i++)
                        if (row.GetAt(i).Contains(needle, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Cannot delete testcase {testcaseIndex}: {table.SheetName} holds {needle}…> in another testcase, which reads step {step}'s response at run time. Change that first.");
        }

        var stepRows = RowsOf(Config, testcaseIndex).ToList();
        var requestRows = RowsOf(Request, testcaseIndex).ToList();
        var responseRows = RowsOf(Response, testcaseIndex).ToList();
        foreach (var row in stepRows) Config.RemoveRow(row);
        foreach (var row in requestRows) Request.RemoveRow(row);
        foreach (var row in responseRows) Response.RemoveRow(row);

        var leftovers = Request.Headers.Concat(Response.Headers).Count(h => info.Steps.Any(s => TryRenameHeader(h, s, "x", out _)));
        return new TestcaseDeleteResult(stepRows.Count, requestRows.Count, leftovers);
    }
}
