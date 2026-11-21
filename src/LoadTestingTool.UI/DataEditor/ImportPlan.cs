namespace LoadTestingTool.UI.DataEditor;

/// <summary>What an import does with the chosen cells.</summary>
public enum ImportMode
{
    /// <summary>Every ticked file row becomes a new row at the end.</summary>
    AppendRows,
    /// <summary>New rows directly below the selected row (at the end when none is selected).</summary>
    InsertRowsBelow,
    /// <summary>Removes the existing rows first, then adds the file's rows. Asks before.</summary>
    ReplaceRows,
    /// <summary>Writes the matching columns into rows that exist (matched by Data ID or by position); adds nothing. This also replaces the values of existing columns.</summary>
    FillExistingRows,
    /// <summary>Creates the ticked file columns as new columns and fills them for the existing rows (matched by Data ID or by position).</summary>
    NewColumns,
}

public enum ImportMatch { ByHeader, ByPosition, Manual }
public enum ImportRowMatch { DataId, Position }
public enum ImportPlacement { End, BeforeSelected, AfterSelected }

/// <summary>One column the file's columns can go into: <see cref="Key"/> is the grid cell index (testcase table) or column index (other sheet).</summary>
public sealed record ImportTarget(int Key, string Display, string[] Names);

/// <summary>Everything the import dialog decided; applied by <see cref="DataEditorViewModel.ApplyImport"/> (testcase rows) or <see cref="DataEditorViewModel.ApplySheetImport"/> (other sheets).</summary>
public sealed class ImportPlan
{
    public const int Skip = -1;
    public const int Create = -2;

    public required ImportTable Table { get; init; }
    public required bool[] TakeColumn { get; init; }
    public required bool[] TakeRow { get; init; }
    public ImportMode Mode { get; init; } = ImportMode.AppendRows;
    public ImportMatch Match { get; init; } = ImportMatch.ByHeader;
    /// <summary>For <see cref="ImportMatch.Manual"/>: per file column the target key, <see cref="Skip"/> or <see cref="Create"/>.</summary>
    public int[]? Manual { get; init; }
    /// <summary>With header matching, file columns that match nothing become new columns instead of being skipped.</summary>
    public bool CreateUnmatched { get; init; }
    /// <summary>An empty file cell clears the existing value (otherwise it leaves it as it is).</summary>
    public bool ClearOnEmpty { get; init; }
    public ImportRowMatch RowMatch { get; init; } = ImportRowMatch.DataId;
    /// <summary>The step a new column belongs to when its header does not say (<c>Step.name</c>).</summary>
    public string? Step { get; init; }
    public bool Expected { get; init; }
    public string Verb { get; init; } = "eq";
    public ImportPlacement Placement { get; init; } = ImportPlacement.End;

    public int TakenRowCount => TakeRow.Count(t => t);
}

public sealed class ImportReport
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int CellsChanged { get; set; }
    public int CellsRefused { get; set; }
    public int NotFound { get; set; }
    public List<string> ColumnsCreated { get; } = [];
    public List<string> Notes { get; } = [];
    public string? Error { get; set; }
    public bool Changed => Added + Updated + Removed + CellsChanged + ColumnsCreated.Count > 0;

    public string Describe()
    {
        if (Error is not null) return Error;
        var parts = new List<string>();
        if (Removed > 0) parts.Add($"removed {Removed} {(Removed == 1 ? "row" : "rows")}");
        if (Added > 0) parts.Add($"{Added} new {(Added == 1 ? "row" : "rows")}");
        if (Updated > 0) parts.Add($"{Updated} {(Updated == 1 ? "row" : "rows")} updated");
        if (ColumnsCreated.Count > 0) parts.Add($"{ColumnsCreated.Count} new {(ColumnsCreated.Count == 1 ? "column" : "columns")} ({string.Join(", ", ColumnsCreated.Take(4))}{(ColumnsCreated.Count > 4 ? "…" : "")})");
        if (CellsChanged > 0) parts.Add($"{CellsChanged} {(CellsChanged == 1 ? "cell" : "cells")} changed");
        if (NotFound > 0) parts.Add($"{NotFound} file {(NotFound == 1 ? "row" : "rows")} found no row to fill");
        if (CellsRefused > 0) parts.Add($"{CellsRefused} {(CellsRefused == 1 ? "cell" : "cells")} refused");
        var text = parts.Count == 0 ? "Nothing changed: the file matches the current data." : "Import: " + string.Join(", ", parts);
        return text + (Notes.Count == 0 ? "." : "; " + string.Join("; ", Notes) + ".") + (Changed ? " One Undo reverts it; nothing is saved until you save." : string.Empty);
    }
}

/// <summary>Decides which column of the sheet each file column goes into.</summary>
public static class ImportMapper
{
    /// <summary>Per file column: the target key, <see cref="ImportPlan.Skip"/> or <see cref="ImportPlan.Create"/>. Notes say what could not be mapped.</summary>
    public static int[] Resolve(ImportPlan plan, IReadOnlyList<ImportTarget> targets, List<string> notes)
    {
        var table = plan.Table;
        var result = new int[table.ColumnCount];
        var used = new HashSet<int>();
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ImportPlan.Skip;
            if (!plan.TakeColumn[i]) continue;
            var wanted = plan.Match switch
            {
                ImportMatch.Manual => plan.Manual is { } m && i < m.Length ? m[i] : ImportPlan.Skip,
                ImportMatch.ByPosition => i < targets.Count ? targets[i].Key : (plan.CreateUnmatched ? ImportPlan.Create : ImportPlan.Skip),
                _ => ByHeader(table.Headers[i], targets) ?? (plan.CreateUnmatched && table.Headers[i].Length > 0 ? ImportPlan.Create : ImportPlan.Skip),
            };
            if (wanted >= 0 && !used.Add(wanted))
            {
                notes.Add($"“{table.Headers[i]}” maps to the same column as an earlier file column and was skipped");
                wanted = ImportPlan.Skip;
            }
            result[i] = wanted;
        }
        return result;
    }

    public static int? ByHeader(string header, IReadOnlyList<ImportTarget> targets)
    {
        header = header.Trim();
        if (header.Length == 0) return null;
        foreach (var target in targets)
            if (target.Names.Any(n => n.Equals(header, StringComparison.OrdinalIgnoreCase))) return target.Key;
        return null;
    }

    public static int DataIdColumn(ImportTable table)
        => Array.FindIndex(table.Headers, h => h.Trim().Equals("Data ID", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("dataid", StringComparison.OrdinalIgnoreCase));
}
