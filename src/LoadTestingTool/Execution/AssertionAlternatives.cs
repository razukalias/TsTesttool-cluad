using System.Text.RegularExpressions;

namespace LoadTestingTool.Execution;

/// <summary>
/// OR / AND checks inside one expected-value cell. After the cell's own check, <c>&lt;or: value&gt;</c> or <c>&lt;and: value&gt;</c> add another check of
/// the same response field; a <c>{verb}</c> in front of the value gives that check its own verb (otherwise it uses the cell's verb):
/// <c>200 &lt;or: 201&gt; &lt;or: {gte}500&gt;</c> passes for 200, 201 or anything from 500. AND binds tighter than OR, so
/// <c>{gt}10 &lt;and: {lt}20&gt; &lt;or: 99&gt;</c> means (more than 10 and less than 20) or 99. Only verbs that compare with a value can be combined.
/// A closing angle bracket inside a value is written <c>&amp;gt;</c> (and a literal ampersand <c>&amp;amp;</c>); the editor does this for you.
/// A header or cell verb can itself be an OR list, <c>{eq|contains}</c>: the value passes when any of those verbs accepts it.
/// </summary>
public static class AssertionAlternatives
{
    public sealed record Alternative(bool IsOr, string Verb, string Value);

    private static readonly Regex Pattern = new(@"<\s*(?<kind>or|and)\s*:\s*(?<body>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Verbs that can be combined: every check except <c>size</c>. exists / notexists / empty / notempty fit best as OR ("is empty OR equals 5"); with AND they are rarely useful but allowed.</summary>
    public static readonly IReadOnlySet<string> Combinable = new HashSet<string>(["eq", "ne", "lt", "lte", "gt", "gte", "contains", "notcontains", "startswith", "endswith", "regex", "in", "exists", "notexists", "empty", "notempty"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The verbs of a header or cell verb: <c>eq|contains</c> gives eq and contains.</summary>
    public static string[] Split(string verb) => (string.IsNullOrWhiteSpace(verb) ? "eq" : verb).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool CanCombine(string verb) => Split(verb).All(v => Combinable.Contains(v));

    /// <summary>A verb list is true when any of its verbs accepts the value.</summary>
    public static bool TestAny(string verb, Func<string, bool> test) => Split(verb).Any(v => test(v.ToLowerInvariant()));

    public static string Escape(string value) => value.Replace("&", "&amp;").Replace(">", "&gt;");
    public static string Unescape(string value) => value.Replace("&gt;", ">").Replace("&amp;", "&");

    public static bool HasAny(string? cell) => !string.IsNullOrEmpty(cell) && Pattern.IsMatch(cell);

    /// <summary>The cell's own value (tags taken out) and the extra checks in the order written. <paramref name="defaultVerb"/> is used for a check without its own verb.</summary>
    public static (string Primary, List<Alternative> Alternatives) Parse(string? cell, string defaultVerb)
    {
        var text = cell ?? string.Empty;
        var list = new List<Alternative>();
        foreach (Match match in Pattern.Matches(text))
        {
            var body = match.Groups["body"].Value.Trim();
            var verb = defaultVerb;
            if (body.StartsWith('{') && body.IndexOf('}') is > 1 and var close) { verb = body[1..close].Trim().ToLowerInvariant(); body = body[(close + 1)..].Trim(); }
            list.Add(new Alternative(match.Groups["kind"].Value.Equals("or", StringComparison.OrdinalIgnoreCase), verb, Unescape(body)));
        }
        return (Pattern.Replace(text, string.Empty).Trim(), list);
    }

    /// <summary>Writes a check as a tag: <c>&lt;or: 201&gt;</c> or <c>&lt;and: {gte}500&gt;</c>.</summary>
    public static string Tag(bool isOr, string verb, string value, string cellVerb)
        => $"<{(isOr ? "or" : "and")}: " + (verb.Equals(cellVerb, StringComparison.OrdinalIgnoreCase) ? string.Empty : $"{{{verb.ToLowerInvariant()}}}") + Escape(value) + ">";

    /// <summary>Evaluates the cell's check and the extra ones. AND binds tighter than OR.</summary>
    public static bool Evaluate(string verb, string primary, IReadOnlyList<Alternative> alternatives, Func<string, string, bool> test)
    {
        var any = false;
        var current = test(verb, primary);
        foreach (var alternative in alternatives)
        {
            var result = test(alternative.Verb, alternative.Value);
            if (alternative.IsOr) { any |= current; current = result; }
            else current &= result;
        }
        return any || current;
    }

    /// <summary>The checks as text for a failure message: <c>eq '200' or eq '201'</c>.</summary>
    public static string Describe(string verb, string primary, IReadOnlyList<Alternative> alternatives)
        => $"{verb.Replace("|", " or ")} '{primary}'" + string.Concat(alternatives.Select(a => $" {(a.IsOr ? "or" : "and")} {a.Verb.Replace("|", " or ")} '{a.Value}'"));
}
