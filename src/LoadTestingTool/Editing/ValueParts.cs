using System.Text.RegularExpressions;

namespace LoadTestingTool.Editing;

public enum ValuePartKind { Text, RandomNumber, Guid, DateTime, Reference, File }

/// <summary>
/// One piece of a cell value. <see cref="Raw"/> is the exact text the workbook holds for the piece, so the parts of a
/// value joined together are always the original text, whatever its case or spacing.
/// </summary>
public sealed record ValuePart(ValuePartKind Kind, string Raw)
{
    public static ValuePart Text(string text) => new(ValuePartKind.Text, text);

    /// <summary><c>&lt;randomnumber&gt;</c>, <c>&lt;randomnumber:6&gt;</c> (digits, zero padded) or <c>&lt;randomnumber:100-999&gt;</c>.</summary>
    public static ValuePart Random(string? format = null) => new(ValuePartKind.RandomNumber, string.IsNullOrWhiteSpace(format) ? "<randomnumber>" : $"<randomnumber:{format.Trim()}>");

    /// <summary><c>&lt;guid&gt;</c> or with a format letter N, D, B or P.</summary>
    public static ValuePart Guid(string? format = null) => new(ValuePartKind.Guid, string.IsNullOrWhiteSpace(format) ? "<guid>" : $"<guid:{format.Trim().ToUpperInvariant()}>");

    /// <summary>One of datum, currentdatum, currenttime, currentdatetime, currenttimestamp, optionally with a .NET format.</summary>
    public static ValuePart Date(string token, string? format = null) => new(ValuePartKind.DateTime, string.IsNullOrWhiteSpace(format) ? $"<{token}>" : $"<{token}:{format}>");

    /// <summary>A value of the same row (<c>r</c>) or of a numbered row of the request, response or config sheet.</summary>
    public static ValuePart From(string sheet, string header, string row = "r") => new(ValuePartKind.Reference, $"<From_{sheet}_{header}_{row}>");

    public static ValuePart File(string path, bool base64 = false) => new(ValuePartKind.File, (base64 ? "<from_filepath_base64>" : "<from_filepath>") + path);

    public string Label => Kind switch
    {
        ValuePartKind.Text => Raw,
        ValuePartKind.File => Raw.StartsWith("<from_filepath_base64>", StringComparison.OrdinalIgnoreCase) ? "file (Base64): " + Raw["<from_filepath_base64>".Length..] : "file: " + Raw["<from_filepath>".Length..],
        _ => Raw.Trim('<', '>'),
    };
}

/// <summary>Reads a cell value as parts and writes parts back as the cell text. Used by the Record view; the stored text is unchanged.</summary>
public static class ValueParts
{
    private static readonly Regex Token = new(
        @"<From_(?:request_|response_|config_)?[^_>]+_(?:r|\d+)>|<From_[^_>]+_\d+>|<randomnumber(?::[^>]*|_\d+)?>|<guid(?::[NnDdBbPp])?>|<(?:datum|currentdatum|currenttime|currentdatetime|currenttimestamp)(?::[^>]*)?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FileCell = new(@"^\s*<from_filepath(?:_base64)?>.+$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static List<ValuePart> Parse(string? value)
    {
        var text = value ?? string.Empty;
        var parts = new List<ValuePart>();
        if (text.Length == 0) return parts;
        if (FileCell.IsMatch(text)) { parts.Add(new ValuePart(ValuePartKind.File, text)); return parts; }
        var at = 0;
        foreach (Match m in Token.Matches(text))
        {
            if (m.Index > at) parts.Add(ValuePart.Text(text[at..m.Index]));
            parts.Add(new ValuePart(KindOf(m.Value), m.Value));
            at = m.Index + m.Length;
        }
        if (at < text.Length) parts.Add(ValuePart.Text(text[at..]));
        return parts;
    }

    public static string Format(IEnumerable<ValuePart> parts) => string.Concat(parts.Select(p => p.Raw));

    private static ValuePartKind KindOf(string token)
    {
        var t = token.ToLowerInvariant();
        if (t.StartsWith("<from_")) return ValuePartKind.Reference;
        if (t.StartsWith("<randomnumber")) return ValuePartKind.RandomNumber;
        if (t.StartsWith("<guid")) return ValuePartKind.Guid;
        return ValuePartKind.DateTime;
    }

    /// <summary>A sample of what the value turns into at run time, with generated parts shown as examples. References show their source.</summary>
    public static string Example(IEnumerable<ValuePart> parts, Func<ValuePart, string?>? resolve = null)
    {
        var text = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            var shown = resolve?.Invoke(part);
            text.Append(shown ?? part.Kind switch
            {
                ValuePartKind.Text => part.Raw,
                ValuePartKind.RandomNumber => "482913",
                ValuePartKind.Guid => "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
                ValuePartKind.DateTime => DateTime.Now.ToString("yyyy-MM-dd"),
                ValuePartKind.File => "(file content)",
                _ => "‹" + part.Label + "›",
            });
        }
        return text.ToString();
    }

    /// <summary>The conditions an expected-value column can use, in plain words, mapped to the verb stored in the header.</summary>
    public static IReadOnlyList<(string Verb, string Words, bool NeedsValue)> Conditions { get; } =
    [
        ("eq", "equals", true), ("ne", "does not equal", true), ("lt", "is less than", true), ("lte", "is at most", true),
        ("gt", "is greater than", true), ("gte", "is at least", true), ("contains", "contains", true), ("notcontains", "does not contain", true),
        ("startswith", "starts with", true), ("endswith", "ends with", true), ("regex", "matches pattern", true),
        ("exists", "exists", false), ("notexists", "does not exist", false), ("empty", "is empty", false), ("notempty", "is not empty", false),
        ("in", "is one of", true), ("size", "has size", true),
    ];

    /// <summary>Splits a response header into its verb and the rest: <c>{contains}Login.token</c> gives ("contains", "Login.token"); no prefix gives ("eq", header).</summary>
    public static (string Verb, string Field) SplitHeader(string header)
    {
        var text = header.Trim();
        if (text.StartsWith('{') && text.IndexOf('}') is > 1 and var close) return (text[1..close].Trim().ToLowerInvariant(), text[(close + 1)..]);
        return ("eq", text);
    }

    private static readonly HashSet<string> Verbs = new(Conditions.Select(c => c.Verb), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Splits an expected-value cell: <c>{contains}abc</c> gives ("contains", "abc"); a cell without a verb gives (null, cell).
    /// A cell verb overrides the header's for that row.
    /// </summary>
    public static (string? Verb, string Expected) SplitCell(string cell)
    {
        var text = cell ?? string.Empty;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') && trimmed.IndexOf('}') is > 1 and var close && Verbs.Contains(trimmed[1..close].Trim()))
            return (trimmed[1..close].Trim().ToLowerInvariant(), trimmed[(close + 1)..]);
        return (null, text);
    }

    /// <summary>Writes an expected value for a verb: nothing extra when it is the header's verb, else <c>{verb}value</c>.</summary>
    public static string JoinCell(string verb, string headerVerb, string expected)
        => verb.Equals(headerVerb, StringComparison.OrdinalIgnoreCase) ? expected : $"{{{verb.ToLowerInvariant()}}}{expected}";

    public static bool NeedsValue(string verb) => Conditions.FirstOrDefault(c => c.Verb.Equals(verb, StringComparison.OrdinalIgnoreCase)).NeedsValue;
}
