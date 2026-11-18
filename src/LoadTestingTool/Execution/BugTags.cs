using System.Text.RegularExpressions;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

/// <summary>
/// The known-bug tag <c>&lt;bug: dev,test; description&gt;</c> in an expected-value cell. In a listed environment (or <c>all</c>) a failing assertion of
/// that cell is a known bug instead of a failure; elsewhere the tag does nothing. The tag is taken out of the cell before the value is used.
/// </summary>
public sealed record BugTag(IReadOnlyList<string> Environments, string Description)
{
    private static readonly Regex Pattern = new(@"<\s*bug\s*:\s*(?<envs>[^;>]*)(?:;(?<text>[^>]*))?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public bool AppliesTo(string environment)
    {
        var current = string.IsNullOrWhiteSpace(environment) ? "default" : environment.Trim();
        return Environments.Any(e => e is "all" or "*" || e.Equals(current, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The cell text without its tag, and the tag (null when the cell has none).</summary>
    public static (string Value, BugTag? Tag) Parse(string cell)
    {
        if (string.IsNullOrEmpty(cell)) return (cell ?? string.Empty, null);
        var match = Pattern.Match(cell);
        if (!match.Success) return (cell, null);
        var environments = match.Groups["envs"].Value.Split([',', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant()).ToList();
        var value = Pattern.Replace(cell, string.Empty, 1).Trim();
        return (value, new BugTag(environments, match.Groups["text"].Value.Trim()));
    }

    /// <summary>
    /// The cell text with the tag removed, or (with <paramref name="environment"/>) with only that environment taken out of the tag; the tag goes
    /// when no environment is left. A tag for <c>all</c> cannot lose one environment, so it is removed as a whole.
    /// </summary>
    public static string Without(string cell, string? environment = null)
    {
        var (value, tag) = Parse(cell);
        if (tag is null) return cell;
        var remaining = environment is null || tag.Environments.Any(e => e is "all" or "*")
            ? []
            : tag.Environments.Where(e => !e.Equals(string.IsNullOrWhiteSpace(environment) ? "default" : environment.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (remaining.Count == 0) return value;
        var rewritten = $"<bug: {string.Join(",", remaining)}" + (tag.Description.Length > 0 ? $"; {tag.Description}" : string.Empty) + ">";
        return value.Length == 0 ? rewritten : value + " " + rewritten;
    }

    /// <summary>Takes the tags out of the definitions' expected values. The returned map holds the tags that apply in this environment, by expected-value cell.</summary>
    public static (IReadOnlyList<AssertionDefinition> Definitions, Dictionary<(int Row, int Column), BugTag> Tagged) Strip(IReadOnlyList<AssertionDefinition> definitions, string environment)
    {
        var tagged = new Dictionary<(int, int), BugTag>();
        var result = new List<AssertionDefinition>(definitions.Count);
        foreach (var definition in definitions)
        {
            var (value, tag) = Parse(definition.ExpectedValue);
            if (tag is null) { result.Add(definition); continue; }
            if (tag.AppliesTo(environment)) tagged[(definition.ExcelRowNumber, definition.ExpectedValueColumn)] = tag;
            result.Add(definition.WithExpectedValue(value));
        }
        return (result, tagged);
    }

    /// <summary>Marks the results of tagged cells: a failure becomes KNOWN_BUG; a pass stays PASS but is flagged (the bug may be fixed).</summary>
    public static void Apply(IEnumerable<AssertionResult> results, IReadOnlyDictionary<(int Row, int Column), BugTag> tagged)
    {
        if (tagged.Count == 0) return;
        foreach (var result in results)
        {
            if (!tagged.TryGetValue((result.ExcelRowNumber, result.ExpectedValueColumn), out var tag)) continue;
            result.KnownBug = true;
            result.BugDescription = tag.Description;
            result.BugEnvironments = string.Join(",", tag.Environments);
            if (result.Result == "FAIL") result.Result = "KNOWN_BUG";
        }
    }
}
