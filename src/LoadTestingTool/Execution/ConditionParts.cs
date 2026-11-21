namespace LoadTestingTool.Execution;

/// <summary>
/// One comparison of a condition as the condition wizard shows it: how it joins the one before (and, or, xor), a "not" in front of its opening brackets,
/// the opening brackets, a "not" in front of the comparison itself, the comparison (<c>Left Verb Right</c>; an empty verb means the left side alone is
/// true, 1 or yes), and the closing brackets after it.
/// </summary>
public sealed record ConditionPart(string Join, bool NotBefore, int Open, bool Not, string Left, string Verb, string Right, int Close);

/// <summary>Turns a condition's text into the wizard's rows and back. The text stays the truth: a condition the rows cannot show (null) is edited as text or script.</summary>
public static class ConditionParts
{
    public static readonly IReadOnlyList<string> Joins = ["and", "or", "xor"];

    /// <summary>The rows of a condition, or null when the text is not well formed or has a shape the rows cannot show (for example a "not" between two opening brackets).</summary>
    public static IReadOnlyList<ConditionPart>? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (ConditionEvaluator.Check(text) is not null) return null;
        var tokens = ConditionEvaluator.Tokenize(text);
        var parts = new List<ConditionPart>();
        var at = 0;
        var depth = 0;
        bool Word(ConditionEvaluator.Token t, params string[] words) => !t.Quoted && words.Contains(t.Text, StringComparer.OrdinalIgnoreCase);
        int StripClosing(ref string value, bool quoted)
        {
            if (quoted || depth == 0) return 0;
            var excess = value.Count(c => c == ')') - value.Count(c => c == '(');
            var strip = Math.Min(Math.Min(excess, depth), value.Length - value.TrimEnd(')').Length);
            if (strip <= 0) return 0;
            value = value[..^strip];
            depth -= strip;
            return strip;
        }

        while (at < tokens.Count)
        {
            var join = string.Empty;
            if (parts.Count > 0)
            {
                var op = tokens[at++];
                join = Word(op, "and", "&&") ? "and" : Word(op, "or", "||") ? "or" : Word(op, "xor", "^") ? "xor" : string.Empty;
                if (join.Length == 0) return null;
            }
            bool notBefore = false, not = false;
            var open = 0;
            while (at < tokens.Count)
            {
                var t = tokens[at];
                if (Word(t, "not", "!")) { not = !not; at++; continue; }
                if (!t.Quoted && t.Text.StartsWith('('))
                {
                    if (not && open == 0) { notBefore = true; not = false; }
                    else if (not) return null;   // "( not (" cannot be shown
                    var rest = t.Text.TrimStart('(');
                    open += t.Text.Length - rest.Length;
                    depth += t.Text.Length - rest.Length;
                    if (rest.Length == 0) at++; else tokens[at] = new ConditionEvaluator.Token(rest, false);
                    continue;
                }
                break;
            }
            if (at >= tokens.Count) return null;
            var close = 0;
            var leftToken = tokens[at++];
            var left = leftToken.Text;
            close += StripClosing(ref left, leftToken.Quoted);
            if (left.Length == 0 && leftToken.Quoted) left = "\"\"";   // an empty value, as opposed to a row nothing was typed in
            var verb = string.Empty;
            var value = new List<string>();
            if (close == 0 && at < tokens.Count && !Word(tokens[at], "and", "&&", "or", "||", "xor", "^") && !(tokens[at] is { Quoted: false, Text: ")" }))
            {
                var verbToken = tokens[at++];
                var verbText = verbToken.Text;
                close += StripClosing(ref verbText, verbToken.Quoted);
                verb = verbText.Trim('{', '}').ToLowerInvariant();
                while (close == 0 && at < tokens.Count && !Word(tokens[at], "and", "&&", "or", "||", "xor", "^") && !(tokens[at] is { Quoted: false, Text: ")" }))
                {
                    var valueToken = tokens[at++];
                    var piece = valueToken.Text;
                    close += StripClosing(ref piece, valueToken.Quoted);
                    if (piece.Length > 0 || value.Count > 0) value.Add(piece);
                }
            }
            while (at < tokens.Count && tokens[at] is { Quoted: false, Text: ")" }) { at++; close++; depth--; }
            parts.Add(new ConditionPart(join, notBefore, open, not, left, verb, string.Join(" ", value), close));
        }
        return depth == 0 ? parts : null;
    }

    /// <summary>The condition text of the rows. Values that could be misread (spaces, brackets, the words and/or/xor/not, references that will hold any text) are put in quotes.</summary>
    public static string Build(IEnumerable<ConditionPart> parts)
    {
        var text = new System.Text.StringBuilder();
        var first = true;
        foreach (var part in parts)
        {
            if (part.Left.Length == 0) continue;   // a row nothing was entered in yet adds nothing
            if (!first) text.Append(' ').Append(part.Join is "or" or "xor" ? part.Join : "and").Append(' ');
            first = false;
            if (part.NotBefore) text.Append("not ");
            var opens = Math.Max(0, part.Open);
            text.Append('(', opens);
            var left = Quote(part.Left, left: true);
            var body = new System.Text.StringBuilder();
            if (part.Not) body.Append("not ");
            body.Append(left);
            if (part.Verb.Length > 0)
            {
                body.Append(' ').Append(part.Verb);
                if (!NoRightSide(part.Verb) && part.Right.Length > 0) body.Append(' ').Append(Quote(part.Right, left: false));
            }
            // a bracket glued to a quote would make the quoted word swallow it, so a space keeps them apart
            if (opens > 0 && body.Length > 0 && body[0] is '"' or '\'') text.Append(' ');
            text.Append(body);
            var closes = Math.Max(0, part.Close);
            if (closes > 0 && body.Length > 0 && body[^1] is '"' or '\'') text.Append(' ');
            text.Append(')', closes);
        }
        return text.ToString();
    }

    /// <summary>Verbs that take no right side.</summary>
    public static bool NoRightSide(string verb) => verb is "exists" or "notexists" or "empty" or "notempty";

    private static bool Plain(string value) => value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '$' or '/' or ':' or '@' or '+' or '*' or '%' or '#' or '!' or '?' or '[' or ']' or '{' or '}' or '|' or '^' or '~' or ',' or '=' or '\\')
        && !value.Equals("and", StringComparison.OrdinalIgnoreCase) && !value.Equals("or", StringComparison.OrdinalIgnoreCase) && !value.Equals("xor", StringComparison.OrdinalIgnoreCase) && !value.Equals("not", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith('!') && !value.StartsWith('^') && !value.StartsWith('{')
        && !(value.StartsWith('_') && value.EndsWith('_'));

    /// <summary>Quotes a value unless it is a plain word or number. A value with both kinds of quote cannot be kept together and is written as it is.</summary>
    internal static string Quote(string value, bool left)
    {
        if (value is "\"\"" or "''") return value;   // an empty quoted value written as it is
        if (Plain(value) && !(left && value.Length == 0)) return value;
        if (!value.Contains('"')) return $"\"{value}\"";
        if (!value.Contains('\'')) return $"'{value}'";
        return value;
    }
}
