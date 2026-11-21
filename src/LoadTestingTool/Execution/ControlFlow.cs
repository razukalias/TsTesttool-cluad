using System.Text.Json;

namespace LoadTestingTool.Execution;

/// <summary>The settings of a control step, from its <c>stepconfig</c> cell: JSON (<c>{"from":1,"to":5}</c>) or <c>from=1;to=5</c>.</summary>
/// <summary>
/// What a relative reference points at while a step runs: <c>&lt;From_response_@prev.path_r&gt;</c> is the response of the step before the current one,
/// <c>&lt;From_response_@lastround.Step.path_r&gt;</c> the value Step had at the end of the previous round of the loop the current step is in.
/// </summary>
public sealed class RelativeScope
{
    private static readonly AsyncLocal<RelativeScope?> Holder = new();
    public static RelativeScope? Current { get => Holder.Value; set => Holder.Value = value; }
    public string? PreviousStep { get; init; }
    public IReadOnlyDictionary<string, string>? LastRound { get; init; }

    /// <summary>The real key for a key that starts with @, or null when it is not relative.</summary>
    public static (string? Key, IReadOnlyDictionary<string, string>? Source, string? Problem) Map(string key)
    {
        if (!key.StartsWith('@')) return (key, null, null);
        var dot = key.IndexOf('.');
        var word = (dot < 0 ? key : key[..dot]).ToLowerInvariant();
        var rest = dot < 0 ? string.Empty : key[(dot + 1)..];
        switch (word)
        {
            case "@prev":
                return Current?.PreviousStep is { Length: > 0 } step ? ($"{step}.{rest}", null, null) : (null, null, "There is no step before this one that sends a request.");
            case "@lastround":
                return Current?.LastRound is { } last ? (rest, last, null) : (null, null, "There is no previous round: this is the first round, or the step is not inside a loop.");
            default:
                return (null, null, $"'{word}' is not a relative reference. Use @prev or @lastround.");
        }
    }
}

public static class StepOutputs
{
    private static readonly System.Text.RegularExpressions.Regex Name = new(@"^[A-Za-z][A-Za-z0-9]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The (name, source) pairs of a step's <c>outputs</c> setting: <c>orderId=items[0].id,code=statusCode,loc=header.Location</c>.</summary>
    public static IReadOnlyList<(string Name, string Source)> Parse(string? stepConfig)
    {
        var text = ControlConfig.Parse(stepConfig).GetValueOrDefault("outputs");
        var list = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = part.IndexOf('=');
            if (at > 0) list.Add((part[..at].Trim(), part[(at + 1)..].Trim()));
        }
        return list;
    }

    public static bool IsValidName(string name) => Name.IsMatch(name);

    /// <summary>The pairs as the <c>outputs</c> value: <c>name=source,name=source</c>.</summary>
    public static string Format(this IEnumerable<(string Name, string Source)> outputs) => string.Join(",", outputs.Select(o => $"{o.Name}={o.Source}"));

    /// <summary>The key the runner stores a value under: <c>Step.items[0].id</c>, <c>Step.statusCode</c>, <c>Step.responseHeader.Location</c>.</summary>
    public static string KeyFor(string step, string source)
    {
        var path = source.Trim();
        if (path.StartsWith("$.")) path = path[2..];
        if (path.StartsWith("header.", StringComparison.OrdinalIgnoreCase)) path = AssertionEngine.ResponseHeaderPrefix + path["header.".Length..];
        if (path.Equals("status", StringComparison.OrdinalIgnoreCase)) path = "statusCode";
        return $"{step}.{path}";
    }

    /// <summary>The problem with an outputs entry, or null.</summary>
    public static string? Check(string name, string source) =>
        !IsValidName(name) ? $"Output name '{name}' must start with a letter and use only letters and digits, so _{name}_ and <From_response_{name}_r> work." :
        string.IsNullOrWhiteSpace(source) ? $"Output '{name}' has no source." : null;
}

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
/// The condition of an If or While step (and the stop-when / skip-when of For and ForEach). One comparison is <c>left verb right</c> with the same verbs as
/// assertions (<c>_status_ eq 200</c>, <c>"&lt;From_response_Poll.state_r&gt;" ne DONE</c>, <c>_name_ notempty</c>); a single word is true when it is true, 1 or yes.
/// Comparisons combine like in code: <c>not</c>, <c>and</c>, <c>xor</c>, <c>or</c> (in that order of precedence; also <c>!</c> <c>&amp;&amp;</c> <c>^</c> <c>||</c>) and
/// brackets: <c>(_status_ eq 200 and Poll.state ne DONE) or not _skip_ eq yes</c>. Quotes keep a value with spaces (or the words and/or/xor/not) together.
/// References and variables must be resolved before the text gets here.
/// </summary>
public static class ConditionEvaluator
{
    public static bool Evaluate(string text) => Parse(text)();

    /// <summary>Reads the condition without evaluating it. Returns null when it is well formed, otherwise what is wrong (unknown verb, missing bracket, nothing after an operator).</summary>
    public static string? Check(string text)
    {
        try { Parse(text); return null; }
        catch (InvalidDataException ex) { return ex.Message; }
    }

    public static bool IsTrue(string value) => value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "y";

    internal sealed record Token(string Text, bool Quoted);

    private static Func<bool> Parse(string text)
    {
        var tokens = Tokenize(text ?? string.Empty);
        if (tokens.Count == 0) return () => false;
        var parser = new Parser(text!, tokens);
        var result = parser.ParseAll();
        return result;
    }

    private sealed class Parser(string source, List<Token> tokens)
    {
        private int _at;
        private int _depth;
        private int _pendingClose;

        private bool End => _at >= tokens.Count;
        private Token? Peek => End ? null : tokens[_at];
        private static bool IsWord(Token? t, params string[] words) => t is { Quoted: false } && words.Contains(t.Text, StringComparer.OrdinalIgnoreCase);
        // while a bracket was just closed by the last token, the group has to end before the next operator is read
        private bool AtOr => _pendingClose == 0 && IsWord(Peek, "or", "||");
        private bool AtXor => _pendingClose == 0 && IsWord(Peek, "xor", "^");
        private bool AtAnd => _pendingClose == 0 && IsWord(Peek, "and", "&&");
        private bool AtOperator => AtOr || AtXor || AtAnd;
        private bool AtClose => _pendingClose > 0 || Peek is { Quoted: false, Text: ")" };   // a lone ')' always ends a comparison (without an open '(' it is reported)
        private InvalidDataException Fail(string message) => new($"Condition '{source}': {message}");

        public Func<bool> ParseAll()
        {
            var result = ParseOr();
            if (!End)
            {
                if (_depth == 0 && Peek is { Quoted: false, Text: ")" }) throw Fail("there is a ')' without a matching '('.");
                throw Fail($"did not expect '{Peek!.Text}' here.");
            }
            return result;
        }

        private Func<bool> ParseOr()
        {
            var left = ParseXor();
            while (AtOr) { _at++; var l = left; var r = ParseXor(); left = () => l() || r(); }
            return left;
        }

        private Func<bool> ParseXor()
        {
            var left = ParseAnd();
            while (AtXor) { _at++; var l = left; var r = ParseAnd(); left = () => l() ^ r(); }
            return left;
        }

        private Func<bool> ParseAnd()
        {
            var left = ParseNot();
            while (AtAnd) { _at++; var l = left; var r = ParseNot(); left = () => l() && r(); }
            return left;
        }

        private Func<bool> ParseNot()
        {
            if (IsWord(Peek, "not", "!")) { _at++; var inner = ParseNot(); return () => !inner(); }
            return ParsePrimary();
        }

        private Func<bool> ParsePrimary()
        {
            if (End) throw Fail("it ends where a comparison was expected (after and, or, xor, not or '(').");
            var token = tokens[_at];
            if (!token.Quoted && token.Text.StartsWith('('))
            {
                var rest = token.Text[1..];
                if (rest.Length == 0) _at++; else tokens[_at] = new Token(rest, false);
                _depth++;
                var inner = ParseOr();
                if (_pendingClose > 0) _pendingClose--;
                else if (Peek is { Quoted: false, Text: ")" }) _at++;
                else throw Fail("a '(' is not closed.");
                _depth--;
                return inner;
            }
            if (AtOperator) throw Fail($"'{token.Text}' needs a comparison before it.");
            return ParseComparison();
        }

        /// <summary>Takes a token's text; a closing bracket at its end that closes an open group is split off.</summary>
        private string Take()
        {
            var token = tokens[_at++];
            if (token.Quoted || _depth == 0) return token.Text;
            var text = token.Text;
            var excess = text.Count(c => c == ')') - text.Count(c => c == '(');
            var strip = Math.Min(Math.Min(excess, _depth - _pendingClose), text.Length - text.TrimEnd(')').Length);
            if (strip <= 0) return text;
            _pendingClose += strip;
            return text[..^strip];
        }

        private Func<bool> ParseComparison()
        {
            var actual = Take();
            if (End || AtOperator || AtClose) return () => ConditionEvaluator.IsTrue(actual);
            var verbText = Take();
            var verb = verbText.Trim('{', '}').ToLowerInvariant();
            if (!AssertionEngine.Verbs.Contains(verb)) throw Fail($"'{verbText}' is not a comparison. Use one of: {string.Join(", ", AssertionEngine.Verbs.Order())}.");
            var value = new List<string>();
            while (!End && !AtOperator && !AtClose)
            {
                var piece = Take();
                if (piece.Length > 0 || value.Count > 0) value.Add(piece);
                if (_pendingClose > 0) break;
            }
            var expected = string.Join(" ", value);
            return () => AssertionEngine.Test(verb, actual, expected, exists: actual.Length > 0);
        }
    }

    internal static List<Token> Tokenize(string text)
    {
        var parts = new List<Token>();
        var current = new System.Text.StringBuilder();
        char? quote = null;
        var hasToken = false;
        var quoted = false;
        for (var at = 0; at < text.Length; at++)
        {
            var c = text[at];
            if (quote is { } q) { if (c == q) quote = null; else current.Append(c); continue; }
            if (c == '!' && !hasToken && at + 1 < text.Length && text[at + 1] == '(') { parts.Add(new Token("!", false)); continue; }   // "!(" is "not ("
            if (c is '"' or '\'') { quote = c; hasToken = true; quoted = true; continue; }
            if (char.IsWhiteSpace(c)) { if (hasToken) { parts.Add(new Token(current.ToString(), quoted)); current.Clear(); hasToken = false; quoted = false; } continue; }
            current.Append(c); hasToken = true;
        }
        if (hasToken) parts.Add(new Token(current.ToString(), quoted));
        return parts;
    }

    internal static List<string> Split(string text) => Tokenize(text ?? string.Empty).Select(t => t.Text).ToList();
}
