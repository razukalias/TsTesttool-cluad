using LoadTestingTool.Domain;

namespace LoadTestingTool.Editing;

/// <summary>A step as the tree shows it: its name, how deep it sits inside control steps and which branch of an If it is in.</summary>
public sealed record StepNesting(string Name, int Depth, bool IsControl, bool InElse, string TypeName = "", bool IsAsync = false, bool IsDisabled = false, bool ParentDisabled = false);

public sealed partial class WorkbookDocument
{
    /// <summary>The enabled cell says the step does not run (false, 0, no, off); an empty cell means it runs.</summary>
    private static bool IsOff(string value) => value.Trim().ToLowerInvariant() is "false" or "0" or "no" or "off";
    private static bool IsOn(string value) => value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";

    private static string ParentName(string parentCell) => parentCell.EndsWith(":else", StringComparison.OrdinalIgnoreCase) ? parentCell[..^5].Trim() : parentCell.Trim();
    private static bool IsElse(string parentCell) => parentCell.EndsWith(":else", StringComparison.OrdinalIgnoreCase);

    private bool IsControlRow(TableRow row)
    {
        try { return StepTypeParser.Parse(row.Get("steptype")).IsControl(); }
        catch (InvalidDataException) { return false; }
    }

    private static string TypeNameOf(TableRow row)
    {
        try { return StepTypeParser.Parse(row.Get("steptype")).WireName(); }
        catch (InvalidDataException) { return string.Empty; }
    }

    /// <summary>How each step of a testcase is nested, in config order. A parent that does not exist counts as the top level.</summary>
    public IReadOnlyList<StepNesting> GetNesting(int testcaseIndex)
    {
        var rows = RowsOf(Config, testcaseIndex).Where(r => !r.IsBlank).ToList();
        var byName = rows.GroupBy(r => r.Get("stepname").Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<StepNesting>();
        foreach (var row in rows)
        {
            var depth = 0;
            var current = row;
            var seen = new HashSet<TableRow> { row };
            var parentOff = false;   // a step inside a disabled control step is never reached
            while (ParentName(current.Get("parent")) is { Length: > 0 } parent && byName.TryGetValue(parent, out var up) && seen.Add(up)) { depth++; current = up; parentOff |= IsOff(up.Get("enabled")); }
            result.Add(new StepNesting(row.Get("stepname").Trim(), depth, IsControlRow(row), IsElse(row.Get("parent")), TypeNameOf(row), IsOn(row.Get("async")), IsOff(row.Get("enabled")), parentOff));
        }
        return result;
    }

    private List<TableRow> Subtree(List<TableRow> rows, TableRow root)
    {
        var result = new List<TableRow> { root };
        for (var i = 0; i < result.Count; i++)
            foreach (var row in rows.Where(r => !result.Contains(r) && ParentName(r.Get("parent")).Equals(result[i].Get("stepname").Trim(), StringComparison.OrdinalIgnoreCase) && result[i].Get("stepname").Trim().Length > 0))
                result.Add(row);
        return rows.Where(result.Contains).ToList();   // in sheet order, so a block keeps its tree order
    }

    /// <summary>
    /// Puts a step (with the steps inside it) into a control step, or back at the top level when <paramref name="parentName"/> is empty.
    /// The step becomes the last one inside its new parent, and the rows are kept in tree order (a control step followed by what is inside it).
    /// Throws <see cref="InvalidOperationException"/> before changing anything when the move is not allowed.
    /// </summary>
    public void SetStepParent(TableRow step, string? parentName, bool elseBranch = false)
    {
        var index = EffectiveIndex(step);
        var rows = RowsOf(Config, index).Where(r => !r.IsBlank).ToList();
        var name = step.Get("stepname").Trim();
        parentName = parentName?.Trim() ?? string.Empty;
        TableRow? parent = null;
        if (parentName.Length > 0)
        {
            parent = rows.FirstOrDefault(r => r.Get("stepname").Trim().Equals(parentName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"There is no step named '{parentName}' in this testcase.");
            if (!IsControlRow(parent)) throw new InvalidOperationException($"'{parentName}' is not an If, While, For or ForEach step, so nothing can be put inside it.");
            if (elseBranch && !StepTypeParser.Parse(parent.Get("steptype")).Equals(StepType.If)) throw new InvalidOperationException($"Only an If step has an else branch; '{parentName}' does not.");
            if (Subtree(rows, step).Contains(parent)) throw new InvalidOperationException($"'{name}' cannot go inside '{parentName}': it is already inside '{name}'.");
        }
        else elseBranch = false;
        // the deepest chain after the move may not exceed what the runner allows
        var nesting = parent is null ? 0 : GetNesting(index).First(n => n.Name.Equals(parentName, StringComparison.OrdinalIgnoreCase)).Depth + 1;
        var below = Subtree(rows, step).Max(r => GetNesting(index).First(n => n.Name.Equals(r.Get("stepname").Trim(), StringComparison.OrdinalIgnoreCase)).Depth) - GetNesting(index).First(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Depth;
        if (nesting + below + 1 > 5) throw new InvalidOperationException("Control steps can be nested at most 5 levels deep.");

        // content moves with the step: the block (step and what is inside it) goes after the last step already inside the new parent
        var contents = rows.ToDictionary(r => r, r => Enumerable.Range(0, Config.ColumnCount).Select(r.GetAt).ToArray());
        var topName = TopAncestor(rows, step);
        step.Set("parent", parentName.Length == 0 ? string.Empty : parentName + (elseBranch ? ":else" : string.Empty));
        contents[step] = Enumerable.Range(0, Config.ColumnCount).Select(step.GetAt).ToArray();
        var block = Subtree(rows, step);
        var rest = rows.Where(r => !block.Contains(r)).ToList();
        int at;
        if (parent is null)
        {
            // back to the top level: right after the top-level step it came out of (the end when it was not inside anything)
            var top = rows.FirstOrDefault(r => topName.Length > 0 && r.Get("stepname").Trim().Equals(topName, StringComparison.OrdinalIgnoreCase) && !block.Contains(r));
            at = top is null ? rest.Count : rest.FindLastIndex(Subtree(rest, top).Contains) + 1;
        }
        else
        {
            var inside = Subtree(rest, parent);
            at = rest.FindLastIndex(inside.Contains) + 1;   // after the last row inside the parent, so the tree order holds
        }
        var ordered = rest.Take(at).Concat(block).Concat(rest.Skip(at)).ToList();
        Reorder(rows, ordered, contents);
    }

    /// <summary>
    /// Adds a step of the given type to a testcase: as the last step inside <paramref name="parentName"/> (the else branch of an If when
    /// <paramref name="elseBranch"/>), right after <paramref name="afterStep"/> at the same level, or at the end of the testcase.
    /// Refuses before changing anything when the parent is not a control step or the nesting would pass 5 levels.
    /// </summary>
    public TableRow AddStep(int testcaseIndex, string testcaseName, string name, string type, string? parentName = null, bool elseBranch = false, string? afterStep = null)
    {
        parentName = parentName?.Trim() ?? string.Empty;
        var rows = RowsOf(Config, testcaseIndex).Where(r => !r.IsBlank).ToList();
        TableRow? anchor = null;
        if (parentName.Length > 0)
        {
            var parent = rows.FirstOrDefault(r => r.Get("stepname").Trim().Equals(parentName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"There is no step named '{parentName}' in this testcase.");
            if (!IsControlRow(parent)) throw new InvalidOperationException($"'{parentName}' is not an If, While, For or ForEach step, so nothing can be added inside it.");
            if (elseBranch && !StepTypeParser.Parse(parent.Get("steptype")).Equals(StepType.If)) throw new InvalidOperationException($"Only an If step has an else branch; '{parentName}' does not.");
            if (GetNesting(testcaseIndex).First(n => n.Name.Equals(parentName, StringComparison.OrdinalIgnoreCase)).Depth + 2 > 5) throw new InvalidOperationException("Control steps can be nested at most 5 levels deep.");
        }
        else if (!string.IsNullOrWhiteSpace(afterStep))
            anchor = rows.FirstOrDefault(r => r.Get("stepname").Trim().Equals(afterStep.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"There is no step named '{afterStep}' in this testcase.");

        var row = Config.AddRow();
        row.Set("testcaseindex", testcaseIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        row.Set("testcase", testcaseName);
        row.Set("stepname", name);
        row.Set("steptype", type);
        row.Set("stage", "Test");
        row.Set("enabled", "True");
        if (parentName.Length > 0) SetStepParent(row, parentName, elseBranch);
        else if (anchor is not null) PlaceStepAfter(row, anchor);
        EnsureOutputColumn(row);
        return row;
    }

    /// <summary>Puts a step at the same level as <paramref name="anchor"/>, right after it and after everything inside it.</summary>
    public void PlaceStepAfter(TableRow step, TableRow anchor)
    {
        var rows = RowsOf(Config, EffectiveIndex(step)).Where(r => !r.IsBlank).ToList();
        var contents = rows.ToDictionary(r => r, r => Enumerable.Range(0, Config.ColumnCount).Select(r.GetAt).ToArray());
        step.Set("parent", anchor.Get("parent"));
        contents[step] = Enumerable.Range(0, Config.ColumnCount).Select(step.GetAt).ToArray();
        var block = Subtree(rows, step);
        var rest = rows.Where(r => !block.Contains(r)).ToList();
        var at = rest.FindLastIndex(Subtree(rest, anchor).Contains) + 1;
        Reorder(rows, rest.Take(at).Concat(block).Concat(rest.Skip(at)).ToList(), contents);
    }

    /// <summary>The steps inside a control step, at any depth (empty for any other step).</summary>
    public IReadOnlyList<string> StepsInside(TableRow step)
    {
        var rows = RowsOf(Config, EffectiveIndex(step)).Where(r => !r.IsBlank).ToList();
        return Subtree(rows, step).Where(r => !ReferenceEquals(r, step)).Select(r => r.Get("stepname").Trim()).ToList();
    }

    /// <summary>Removes a step together with every step inside it (and their data columns, as <see cref="RemoveStep"/> does).</summary>
    public StepRemoveResult RemoveStepWithContents(TableRow step)
    {
        var rows = RowsOf(Config, EffectiveIndex(step)).Where(r => !r.IsBlank).ToList();
        int columns = 0, references = 0;
        foreach (var row in Subtree(rows, step).AsEnumerable().Reverse())
        {
            var result = RemoveStep(row);
            columns += result.ColumnsRemoved; references += result.ReferencesLeft;
        }
        return new(columns, references);
    }

    private string TopAncestor(List<TableRow> rows, TableRow step)
    {
        var current = step;
        var seen = new HashSet<TableRow> { step };
        while (ParentName(current.Get("parent")) is { Length: > 0 } parent && rows.FirstOrDefault(r => r.Get("stepname").Trim().Equals(parent, StringComparison.OrdinalIgnoreCase)) is { } up && seen.Add(up)) current = up;
        return ReferenceEquals(current, step) ? string.Empty : current.Get("stepname").Trim();
    }

    /// <summary>▲ ▼ with control steps: a step swaps places with its neighbour inside the same parent, taking the steps inside it along.</summary>
    private bool MoveStepAmongSiblings(TableRow step, int direction)
    {
        var rows = RowsOf(Config, EffectiveIndex(step)).Where(r => !r.IsBlank).ToList();
        var siblings = rows.Where(r => r.Get("parent").Trim().Equals(step.Get("parent").Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var at = siblings.IndexOf(step);
        var target = at + Math.Sign(direction);
        if (at < 0 || target < 0 || target >= siblings.Count) return false;
        var contents = rows.ToDictionary(r => r, r => Enumerable.Range(0, Config.ColumnCount).Select(r.GetAt).ToArray());
        var block = Subtree(rows, step);
        var rest = rows.Where(r => !block.Contains(r)).ToList();
        var other = siblings[target];
        var otherBlock = Subtree(rest, other);
        var place = direction < 0 ? rest.IndexOf(otherBlock[0]) : rest.FindLastIndex(otherBlock.Contains) + 1;
        Reorder(rows, rest.Take(place).Concat(block).Concat(rest.Skip(place)).ToList(), contents);
        return true;
    }

    /// <summary>Puts a step next to the control step it is inside, in that step's own parent (one level out).</summary>
    public void MoveStepOut(TableRow step)
    {
        var index = EffectiveIndex(step);
        var rows = RowsOf(Config, index).Where(r => !r.IsBlank).ToList();
        var parentCell = step.Get("parent");
        if (ParentName(parentCell).Length == 0) throw new InvalidOperationException("That step is already at the top level.");
        var parent = rows.FirstOrDefault(r => r.Get("stepname").Trim().Equals(ParentName(parentCell), StringComparison.OrdinalIgnoreCase));
        var grand = parent?.Get("parent") ?? string.Empty;
        var contents = rows.ToDictionary(r => r, r => Enumerable.Range(0, Config.ColumnCount).Select(r.GetAt).ToArray());
        step.Set("parent", grand);
        contents[step] = Enumerable.Range(0, Config.ColumnCount).Select(step.GetAt).ToArray();
        var block = Subtree(rows, step);
        var rest = rows.Where(r => !block.Contains(r)).ToList();
        var at = parent is null ? rest.Count : rest.FindLastIndex(Subtree(rest, parent).Contains) + 1;   // right after the control step it came out of
        Reorder(rows, rest.Take(at).Concat(block).Concat(rest.Skip(at)).ToList(), contents);
    }

    private void Reorder(List<TableRow> rows, List<TableRow> ordered, Dictionary<TableRow, string[]> contents)
    {
        // rows are fixed positions in the sheet: write each row's content to the position it now takes
        for (var i = 0; i < rows.Count; i++)
        {
            var source = contents[ordered[i]];
            for (var c = 0; c < source.Length; c++) rows[i].SetAt(c, source[c]);
        }
    }

    /// <summary>Children of a renamed control step keep pointing at it.</summary>
    private void FollowStepRename(string oldName, string newName)
    {
        foreach (var row in Config.Rows.Where(r => !r.IsBlank))
        {
            var parent = row.Get("parent");
            if (ParentName(parent).Equals(oldName, StringComparison.OrdinalIgnoreCase)) row.Set("parent", newName + (IsElse(parent) ? ":else" : string.Empty));
        }
    }
}
