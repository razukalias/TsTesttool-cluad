using System.Text.RegularExpressions;

namespace LoadTestingTool.Editing;

/// <summary>What a step rename changed.</summary>
public sealed record StepRenameResult(int ColumnsRenamed, int CellsRewritten, int ElsewhereMentions);

public sealed partial class WorkbookDocument
{
    private static readonly char[] ForbiddenStepNameCharacters = ['.', '<', '>', '{', '}', '|', ',', ':'];

    /// <summary>
    /// Renames a step everywhere the runner ties data to its name: the config row, the <c>Step.field</c> request and response
    /// columns (the expected-value verb prefix is kept), runtime references <c>&lt;From_response_Step.path_N&gt;</c>, the step lists in
    /// request <c>dontrun</c> cells and the <c>Step.path:alias</c> mappings in response <c>extractvariable</c> cells.
    /// Throws <see cref="InvalidOperationException"/> before changing anything if the new name cannot be used.
    /// </summary>
    public StepRenameResult RenameStep(string oldName, string newName)
    {
        oldName = oldName.Trim();
        newName = newName.Trim();
        if (newName.Length == 0) throw new InvalidOperationException("A step name cannot be empty.");
        if (newName.IndexOfAny(ForbiddenStepNameCharacters) >= 0)
            throw new InvalidOperationException($"A step name cannot contain any of: {string.Join(' ', ForbiddenStepNameCharacters)}");
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return new(0, 0, 0);
        if (Config.Rows.Any(r => !r.IsBlank && string.Equals(r.Get("stepname").Trim(), newName, StringComparison.OrdinalIgnoreCase) && !string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A step named '{newName}' already exists.");

        var plan = new List<(SheetTable Table, int Column, string Header)>();
        foreach (var table in new[] { Request, Response })
        {
            var headers = table.Headers;
            for (var i = 0; i < headers.Count; i++)
            {
                if (!TryRenameHeader(headers[i], oldName, newName, out var renamed)) continue;
                var clash = table.FindColumn(renamed);
                if (clash >= 0 && clash != i)
                    throw new InvalidOperationException($"The {table.SheetName} sheet already has a column '{renamed}', so '{headers[i]}' cannot be renamed to it.");
                plan.Add((table, i, renamed));
            }
        }

        foreach (var row in Config.Rows.Where(r => string.Equals(r.Get("stepname").Trim(), oldName, StringComparison.OrdinalIgnoreCase)))
            row.Set("stepname", newName);
        foreach (var (table, column, header) in plan) table.RenameColumn(column, header);

        var runtime = new Regex($@"(<From_response_){Regex.Escape(oldName)}(\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var dontrun = new Regex($@"(?<=^|[,|]\s*){Regex.Escape(oldName)}(?=\s*(?:[,|]|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var extract = new Regex($@"(?<=^|\|\s*){Regex.Escape(oldName)}(?=\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var cells = 0;
        foreach (var table in new[] { Config, Request, Response })
        {
            var headers = table.Headers;
            foreach (var row in table.Rows)
            {
                if (row.IsBlank) continue;
                for (var i = 0; i < headers.Count; i++)
                {
                    var text = row.GetAt(i);
                    if (text.Length == 0) continue;
                    var updated = text.Contains("<From_", StringComparison.OrdinalIgnoreCase) ? runtime.Replace(text, $"${{1}}{newName}$2") : text;
                    if (ReferenceEquals(table, Request) && headers[i].Trim().Equals("dontrun", StringComparison.OrdinalIgnoreCase)) updated = dontrun.Replace(updated, newName);
                    if (ReferenceEquals(table, Response) && headers[i].Trim().Equals("extractvariable", StringComparison.OrdinalIgnoreCase)) updated = extract.Replace(updated, newName);
                    if (updated == text) continue;
                    row.SetAt(i, updated);
                    cells++;
                }
            }
        }
        return new(plan.Count, cells, CountMentionsInOtherSheets(oldName));
    }

    private static bool TryRenameHeader(string header, string oldName, string newName, out string renamed)
    {
        renamed = header;
        var text = header.Trim();
        var verb = string.Empty;
        if (text.StartsWith('{') && text.IndexOf('}') is var closing and > 0)
        {
            verb = text[..(closing + 1)];
            text = text[(closing + 1)..];
        }
        if (!text.StartsWith(oldName + ".", StringComparison.OrdinalIgnoreCase)) return false;
        renamed = verb + newName + text[oldName.Length..];
        return true;
    }

    /// <summary>Cells on worksheets the editor does not edit that still refer to the step by name, from the last saved file.</summary>
    private int CountMentionsInOtherSheets(string oldName)
    {
        using var stream = new MemoryStream(_baselineBytes, writable: false);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var needle = "<From_response_" + oldName + ".";
        return workbook.Worksheets
            .Where(w => OtherSheets.Contains(w.Name, StringComparer.OrdinalIgnoreCase))
            .SelectMany(w => w.CellsUsed())
            .Count(c => c.GetString().Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
