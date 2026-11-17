using System.Text.RegularExpressions;

namespace LoadTestingTool.Editing;

/// <summary>The <c>_name_</c> and <c>_*name*_</c> placeholders of a request template, which a step reads from its own data columns.</summary>
public static class TemplateVariables
{
    private static readonly Regex Placeholder = new(@"_(?:\*([\p{L}\p{N}._\-]+)\*|([\p{L}\p{N}._\-]+))_", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The distinct names, in order of appearance. Names with a dot (<c>Login.token</c>) are values from an earlier response,
    /// not data columns, so they are left out.
    /// </summary>
    public static List<string> Find(string template)
        => Placeholder.Matches(template ?? string.Empty)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .Where(n => !n.Contains('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
