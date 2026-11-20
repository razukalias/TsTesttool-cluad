using System.Text.Json;

namespace LoadTestingTool.Execution;

/// <summary>The settings of a control step, from its <c>stepconfig</c> cell: JSON (<c>{"from":1,"to":5}</c>) or <c>from=1;to=5</c>.</summary>
public static class ControlConfig
{
    public static Dictionary<string, string> Parse(string? text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return values;
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                foreach (var property in document.RootElement.EnumerateObject())
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText();
                return values;
            }
            catch (JsonException) { /* fall through: read it as key=value text */ }
        }
        foreach (var part in trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = part.IndexOf('=');
            if (at > 0) values[part[..at].Trim()] = part[(at + 1)..].Trim();
        }
        return values;
    }
}

/// <summary>
/// The condition of an If or While step: <c>left verb right</c> with the same verbs as assertions (<c>_status_ eq 200</c>,
/// <c>"<From_response_Poll.state_r>" ne DONE</c>, <c>_name_ notempty</c>). A single word is true when it is true, 1 or yes.
/// Quotes keep a value with spaces together. References and variables must be resolved before the text gets here.
/// </summary>
public static class ConditionEvaluator
{
    public static bool Evaluate(string text)
    {
        var parts = Split(text);
        if (parts.Count == 0) return false;
        if (parts.Count == 1) return IsTrue(parts[0]);
        var verb = parts[1].Trim('{', '}').ToLowerInvariant();
        if (!AssertionEngine.Verbs.Contains(verb)) throw new InvalidDataException($"Condition '{text}': '{parts[1]}' is not a comparison. Use one of: {string.Join(", ", AssertionEngine.Verbs.Order())}.");
        var expected = string.Join(" ", parts.Skip(2));
        var actual = parts[0];
        return AssertionEngine.Test(verb, actual, expected, exists: actual.Length > 0);
    }

    public static bool IsTrue(string value) => value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "y";

    internal static List<string> Split(string text)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;
        var hasToken = false;
        foreach (var c in text ?? string.Empty)
        {
            if (quote is { } q) { if (c == q) quote = null; else current.Append(c); continue; }
            if (c is '"' or '\'') { quote = c; hasToken = true; continue; }
            if (char.IsWhiteSpace(c)) { if (hasToken) { parts.Add(current.ToString()); current.Clear(); hasToken = false; } continue; }
            current.Append(c); hasToken = true;
        }
        if (hasToken) parts.Add(current.ToString());
        return parts;
    }
}
