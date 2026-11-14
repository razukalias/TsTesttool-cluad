namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The template search box: terms separated by &amp; are all highlighted ("id &amp; name" marks every id and every name).
/// Matching ignores case.
/// </summary>
public static class TemplateSearch
{
    public static IReadOnlyList<string> Terms(string? query)
        => (query ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Start and length of every occurrence of every term, in text order.</summary>
    public static IReadOnlyList<(int Start, int Length)> Matches(string text, IReadOnlyList<string> terms)
    {
        var result = new List<(int, int)>();
        foreach (var term in terms)
        {
            var at = 0;
            while ((at = text.IndexOf(term, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                result.Add((at, term.Length));
                at += Math.Max(1, term.Length);
            }
        }
        return result.OrderBy(m => m.Item1).ThenByDescending(m => m.Item2).ToList();
    }

    /// <summary>Occurrences of each term, so the box can say which terms were not found.</summary>
    public static IReadOnlyList<(string Term, int Count)> Counts(string text, IReadOnlyList<string> terms)
        => terms.Select(t => (t, Matches(text, [t]).Count)).ToList();

    /// <summary>"id ×3 · name ×1" or "owner not found"; empty when there is no query.</summary>
    public static string Summary(string text, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0) return string.Empty;
        var counts = Counts(text, terms);
        var missing = counts.Where(c => c.Count == 0).Select(c => c.Term).ToList();
        var found = counts.Where(c => c.Count > 0).Select(c => $"{c.Term} ×{c.Count}");
        var parts = string.Join(" · ", found);
        if (missing.Count == 0) return parts;
        var notFound = string.Join(", ", missing) + " not found";
        return parts.Length == 0 ? notFound : $"{parts} · {notFound}";
    }
}
