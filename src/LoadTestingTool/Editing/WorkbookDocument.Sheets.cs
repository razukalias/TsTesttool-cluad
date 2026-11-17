using ClosedXML.Excel;

namespace LoadTestingTool.Editing;

// The worksheets the runner does not read: edit their cells, rows and columns like any table, and add, rename or remove whole sheets.
// Like every other edit these are only written when the document is saved.
public sealed partial class WorkbookDocument
{
    private static readonly char[] ForbiddenSheetNameCharacters = [':', '\\', '/', '?', '*', '[', ']'];

    private bool SheetsChanged => _removedSheets.Count > 0 || _others.Any(t => t.OriginalSheetName is null || !string.Equals(t.OriginalSheetName, t.SheetName, StringComparison.Ordinal));

    /// <summary>The editable table behind one of the other sheets, or null.</summary>
    public SheetTable? FindOtherSheet(string name)
        => _others.FirstOrDefault(t => t.SheetName.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Why this would not be a good name for a sheet (Excel's rules plus uniqueness), or null when it is fine. <paramref name="renaming"/> is the sheet being renamed, which may keep its own name.</summary>
    public string? SheetNameProblem(string name, string? renaming = null)
    {
        var wanted = name.Trim();
        if (wanted.Length == 0) return "Enter a name for the sheet.";
        if (wanted.Length > 31) return "Excel sheet names can be at most 31 characters.";
        if (wanted.IndexOfAny(ForbiddenSheetNameCharacters) >= 0) return "A sheet name cannot contain : \\ / ? * [ ]";
        if (wanted.StartsWith('\'') || wanted.EndsWith('\'')) return "A sheet name cannot start or end with an apostrophe.";
        if (new[] { Config.SheetName, Request.SheetName, Response.SheetName }.Any(n => n.Trim().Equals(wanted, StringComparison.OrdinalIgnoreCase)))
            return $"'{wanted}' is one of the sheets the runner reads; choose another name.";
        if (_others.Any(t => t.SheetName.Equals(wanted, StringComparison.OrdinalIgnoreCase) && !t.SheetName.Equals(renaming, StringComparison.OrdinalIgnoreCase)))
            return $"The workbook already has a sheet '{wanted}'.";
        return null;
    }

    /// <summary>Adds an empty sheet at the end. Throws <see cref="InvalidOperationException"/> for a bad or taken name.</summary>
    public SheetTable AddSheet(string name)
    {
        if (SheetNameProblem(name) is { } problem) throw new InvalidOperationException(problem);
        var table = SheetTable.Create(name.Trim());
        _others.Add(table);
        return table;
    }

    /// <summary>Renames a sheet. Refused while other cells refer to it by name, because those would stop working.</summary>
    public void RenameSheet(string name, string newName)
    {
        var table = FindOtherSheet(name) ?? throw new InvalidOperationException($"There is no sheet '{name}' to rename; the sheets the runner reads cannot be renamed.");
        if (SheetNameProblem(newName, table.SheetName) is { } problem) throw new InvalidOperationException(problem);
        var mentions = MentionsOf(table);
        if (mentions > 0) throw new InvalidOperationException($"{mentions} {(mentions == 1 ? "cell refers" : "cells refer")} to sheet '{table.SheetName}' by name (<From_…> references or formulas); renaming it would break {(mentions == 1 ? "it" : "them")}. Change {(mentions == 1 ? "that cell" : "those cells")} first.");
        table.SheetName = newName.Trim();
    }

    /// <summary>Removes a sheet. Refused while other cells refer to it by name.</summary>
    public void RemoveSheet(string name)
    {
        var table = FindOtherSheet(name) ?? throw new InvalidOperationException($"There is no sheet '{name}' to remove; the sheets the runner reads cannot be removed.");
        var mentions = MentionsOf(table);
        if (mentions > 0) throw new InvalidOperationException($"{mentions} {(mentions == 1 ? "cell refers" : "cells refer")} to sheet '{table.SheetName}' by name (<From_…> references or formulas), so it cannot be removed. Change {(mentions == 1 ? "that cell" : "those cells")} first.");
        _others.Remove(table);
        if (table.OriginalSheetName is { } original) _removedSheets.Add(original);
    }

    /// <summary>Cells outside the sheet that refer to it by name, in the last saved file: <c>&lt;From_Sheet_header_N&gt;</c> references and formulas such as <c>Sheet!A1</c>.</summary>
    private int MentionsOf(SheetTable table)
    {
        if (table.OriginalSheetName is not { } original) return 0;   // never saved, so nothing can refer to it yet
        var count = FindRowReferences().Count(r => r.TargetSheet.Equals(original, StringComparison.OrdinalIgnoreCase) && !r.Sheet.Equals(original, StringComparison.OrdinalIgnoreCase));
        using var stream = new MemoryStream(_baselineBytes, writable: false);
        using var workbook = new XLWorkbook(stream);
        foreach (var sheet in workbook.Worksheets.Where(w => !w.Name.Equals(original, StringComparison.OrdinalIgnoreCase)))
            foreach (var cell in sheet.CellsUsed(XLCellsUsedOptions.Contents).Where(c => c.HasFormula))
            {
                var formula = cell.FormulaA1;
                if (formula.Contains(original + "!", StringComparison.OrdinalIgnoreCase) || formula.Contains("'" + original + "'!", StringComparison.OrdinalIgnoreCase)) count++;
            }
        return count;
    }

    private void ApplyOtherSheets(XLWorkbook workbook)
    {
        foreach (var gone in _removedSheets)
            if (workbook.TryGetWorksheet(gone, out var sheet)) sheet.Delete();

        // Two passes so that renames which swap or chain names never meet a name that is still in use.
        var renamed = _others.Where(t => t.OriginalSheetName is not null && !string.Equals(t.OriginalSheetName, t.SheetName, StringComparison.Ordinal)).ToList();
        var holding = new Dictionary<SheetTable, string>();
        foreach (var table in renamed)
        {
            var temporary = "tmp-" + Guid.NewGuid().ToString("N")[..20];
            workbook.Worksheet(table.OriginalSheetName!).Name = temporary;
            holding[table] = temporary;
        }
        foreach (var table in _others)
        {
            IXLWorksheet sheet;
            if (table.OriginalSheetName is null) sheet = workbook.AddWorksheet(table.SheetName);
            else if (holding.TryGetValue(table, out var temporary)) { sheet = workbook.Worksheet(temporary); sheet.Name = table.SheetName; }
            else sheet = workbook.Worksheet(table.OriginalSheetName);
            table.ApplyTo(sheet);
        }
    }

    private void AcceptOtherSheets()
    {
        foreach (var table in _others)
        {
            table.AcceptChanges();
            table.OriginalSheetName = table.SheetName;
        }
        _removedSheets.Clear();
    }
}
