using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace LoadTestingTool.Execution;

/// <summary>
/// Values for pipelines: <c>${NAME}</c> in appsettings.json and in the cells of the workbook is replaced by <c>--set NAME=value</c> or by the environment variable NAME
/// (<c>${NAME:-fallback}</c> uses the fallback when neither exists; a missing value without fallback stops the run before it starts). Every value that was filled in is masked
/// as *** in the internal log and in the results of the steps, so a token never reaches the history.
/// </summary>
public static class Secrets
{
    private static readonly Regex Placeholder = new(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_.\-]*)(?::-(?<fallback>[^}]*))?\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly ConcurrentDictionary<string, string> Used = new(StringComparer.Ordinal);
    private static volatile IReadOnlyDictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>--set name=value</c> pairs of this run; they win over environment variables.</summary>
    public static void SetOverrides(IReadOnlyDictionary<string, string> overrides) => _overrides = new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> Overrides => _overrides;

    /// <summary>Text with every <c>${NAME}</c> filled in. <paramref name="where"/> names the place in the error text.</summary>
    public static string Expand(string? text, string where = "")
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("${", StringComparison.Ordinal)) return text ?? string.Empty;
        return Placeholder.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            var value = _overrides.TryGetValue(name, out var set) ? set : Environment.GetEnvironmentVariable(name);
            if (value is null)
            {
                if (match.Groups["fallback"].Success) return match.Groups["fallback"].Value;
                throw new InvalidDataException($"The value for ${{{name}}} is not set{(where.Length > 0 ? $" (used in {where})" : string.Empty)}. Give it with --set {name}=… or set the environment variable {name}.");
            }
            if (value.Length >= 4) Used[value] = name;
            return value;
        });
    }

    /// <summary>The text with every value that was filled in from a placeholder replaced by ***.</summary>
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text) || Used.IsEmpty) return text ?? string.Empty;
        foreach (var value in Used.Keys.OrderByDescending(v => v.Length))
            if (text.Contains(value, StringComparison.Ordinal)) text = text.Replace(value, "***", StringComparison.Ordinal);
        return text;
    }

    public static bool Any => !Used.IsEmpty;

    internal static void ResetForTests() { Used.Clear(); _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
}
