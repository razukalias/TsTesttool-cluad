using System.Text.RegularExpressions;

namespace LoadTestingTool.UI.DataEditor;

public sealed record CompletionItem(string Label, string Insert, string Detail);

/// <summary>Suggestions for the text before the caret: replace <see cref="Start"/>..caret with the chosen item's <see cref="CompletionItem.Insert"/>.</summary>
public sealed record Completion(int Start, IReadOnlyList<CompletionItem> Items);

/// <summary>
/// Autocomplete for workbook cells. Typing <c>&lt;</c> offers the functions, <c>&lt;From_</c> the sheets,
/// <c>&lt;From_request_</c> that sheet's headers and <c>&lt;From_request_title_</c> the <c>r</c> (same data row) form,
/// and <c>{</c> at the start of an expected-value cell the assertion verbs.
/// </summary>
public sealed class CellCompletion
{
    public static IReadOnlyList<string> Verbs { get; } =
        ["eq", "ne", "lt", "lte", "gt", "gte", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "in", "size"];

    private static readonly (string Name, string Insert, string Detail)[] Functions =
    [
        ("From_", "From_", "value from another cell: <From_sheet_header_row> or _r for the same data row"),
        ("datum", "datum>", "today, yyyy-MM-dd (or <datum:format>)"),
        ("currentdatum", "currentdatum>", "today, yyyy-MM-dd"),
        ("currenttime", "currenttime>", "now, time of day"),
        ("currentdatetime", "currentdatetime>", "now, date and time"),
        ("currenttimestamp", "currenttimestamp>", "Unix time in milliseconds"),
        ("randomnumber", "randomnumber>", "random number (or <randomnumber:6>, <randomnumber:1-100>)"),
        ("guid", "guid>", "new GUID (or <guid:N>)"),
        ("runscript", "runscript(", "run an inline script"),
    ];

    private static readonly Regex RowPart = new(@"<From_([^_>]+)_([^_>]+)_([^_>]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HeaderPart = new(@"<From_([^_>]+)_([^_>]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SheetPart = new(@"<From_([^_>]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FunctionPart = new(@"<([A-Za-z]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex VerbPart = new(@"^\s*\{([A-Za-z]*)$", RegexOptions.CultureInvariant);

    private readonly Func<IReadOnlyList<string>> _sheets;
    private readonly Func<string, IReadOnlyList<string>> _headers;

    public CellCompletion(Func<IReadOnlyList<string>> sheets, Func<string, IReadOnlyList<string>> headers)
    {
        _sheets = sheets;
        _headers = headers;
    }

    /// <param name="allowVerbs">True for expected-value cells, where <c>{eq}</c> may start the cell.</param>
    public Completion? Suggest(string text, int caret, bool allowVerbs)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var before = text[..caret];

        if (RowPart.Match(before) is { Success: true } row)
        {
            var typed = row.Groups[3];
            return Make(typed.Index, [new CompletionItem("r", "r>", "the same data row (matched by testcase index and data ID)")], typed.Value);
        }
        if (HeaderPart.Match(before) is { Success: true } header)
        {
            var sheet = header.Groups[1].Value;
            var typed = header.Groups[2];
            var items = _headers(sheet).Select(h => Strip(h)).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(h => new CompletionItem(h, h + "_", $"column in {sheet}")).ToList();
            return Make(typed.Index, items, typed.Value);
        }
        if (SheetPart.Match(before) is { Success: true } sheetMatch)
        {
            var typed = sheetMatch.Groups[1];
            return Make(typed.Index, _sheets().Select(s => new CompletionItem(s, s + "_", "worksheet")).ToList(), typed.Value);
        }
        if (FunctionPart.Match(before) is { Success: true } function)
        {
            var typed = function.Groups[1];
            return Make(typed.Index, Functions.Select(f => new CompletionItem(f.Name, f.Insert, f.Detail)).ToList(), typed.Value);
        }
        if (allowVerbs && VerbPart.Match(before) is { Success: true } verb)
        {
            var typed = verb.Groups[1];
            return Make(typed.Index, Verbs.Select(v => new CompletionItem(v, v + "}", AllowsBlank(v) ? "assertion; needs no expected value" : "assertion; value follows")).ToList(), typed.Value);
        }
        return null;
    }

    /// <summary>Applies an item to the text; returns the new text and caret position.</summary>
    public static (string Text, int Caret) Apply(string text, int caret, Completion completion, CompletionItem item)
    {
        caret = Math.Clamp(caret, completion.Start, text.Length);
        var result = text[..completion.Start] + item.Insert + text[caret..];
        return (result, completion.Start + item.Insert.Length);
    }

    private static Completion? Make(int start, List<CompletionItem> items, string typed)
    {
        var filtered = items
            .Where(i => i.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Nothing left to offer once the typed text already equals the only suggestion (for example "<guid" before ">").
        return filtered.Count == 0 ? null : new Completion(start, filtered);
    }

    private static string Strip(string header)
    {
        var text = header.Trim();
        return text.StartsWith('{') && text.IndexOf('}') is var closing and > 0 ? text[(closing + 1)..].Trim() : text;
    }

    private static bool AllowsBlank(string verb) => verb is "empty" or "notempty" or "exists" or "notexists";
}
