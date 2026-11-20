using System.Text.RegularExpressions;

namespace LoadTestingTool.Editing;

/// <summary>
/// Reads and rewrites the <c>source</c> file named in a step's <c>stepconfig</c> (the query file of a graphql or sql step, the code file of a
/// script step). The text is JSON or <c>key=value;key=value</c>; only the value is replaced, everything else stays as typed.
/// </summary>
public static class StepConfigFiles
{
    private static readonly Regex Json = new("(\"source\"\\s*:\\s*\")((?:[^\"\\\\]|\\\\.)*)(\")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Pairs = new("(^|;)(\\s*source\\s*=)([^;]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The relative file the step reads, or null when none is named or the name holds a variable (<c>_x_</c>, <c>&lt;…&gt;</c>, <c>{…}</c>) that only the runner can resolve.</summary>
    public static string? SourceOf(string? stepConfig)
    {
        if (string.IsNullOrWhiteSpace(stepConfig)) return null;
        string? value = null;
        var json = Json.Match(stepConfig);
        if (json.Success) value = json.Groups[2].Value.Replace("\\\\", "/").Replace("\\/", "/");
        else if (!stepConfig.TrimStart().StartsWith('{') && Pairs.Match(stepConfig) is { Success: true } pair) value = pair.Groups[3].Value;
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Contains('<') || value.Contains('{') || Regex.IsMatch(value, "(^|[/\\\\])_[^/\\\\]*_([/\\\\.]|$)")) return null;
        return value;
    }

    /// <summary>The same stepconfig text with the source file replaced.</summary>
    public static string WithSource(string stepConfig, string newSource)
    {
        if (Json.IsMatch(stepConfig)) return Json.Replace(stepConfig, m => m.Groups[1].Value + newSource.Replace("\\", "/") + m.Groups[3].Value, 1);
        return Pairs.Replace(stepConfig, m => m.Groups[1].Value + m.Groups[2].Value + newSource, 1);
    }
}
