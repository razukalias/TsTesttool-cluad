using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace LoadTestingTool.Execution;

/// <summary>What a C# condition can use: <c>V("name")</c> for a value, <c>Num("name")</c> for a number, <c>Has("name")</c>.</summary>
public sealed class ConditionGlobals
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string V(string key) => Values.TryGetValue(key, out var value) ? value : string.Empty;
    public bool Has(string key) => Values.TryGetValue(key, out var value) && value.Length > 0;
    /// <summary>One comparison with any of the condition verbs: <c>Is("gt", V("a"), "5")</c>.</summary>
    public bool Is(string verb, string? actual, string expected = "") => AssertionEngine.Test(verb.ToLowerInvariant(), actual ?? string.Empty, expected, exists: !string.IsNullOrEmpty(actual));
    /// <summary>True for true, 1, yes, on; the same test a lone value in a condition gets.</summary>
    public bool True(string? value) => ConditionEvaluator.IsTrue(value ?? string.Empty);
    public double Num(string key) => double.TryParse(V(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : double.NaN;
}

/// <summary>
/// A condition written as C#: <c>cs: &lt;From_response_Login.statusCode_r&gt; == "200" &amp;&amp; _i_ != "3"</c>.
/// References (<c>&lt;From_…&gt;</c>) and variables (<c>_name_</c>) are replaced before the script runs: inside a string
/// they become the value's text, anywhere else they become <c>V("…")</c> (a string). The script ends with an
/// expression, or uses <c>return</c>, and must give true or false.
/// </summary>
public static class ConditionScript
{
    public const string Prefix = "cs:";
    private static readonly Regex Reference = new(@"<From_[^<>\r\n]+>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsScript(string? text) => text is not null && text.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    public static string Body(string text) => text.TrimStart()[Prefix.Length..].Trim();

    /// <summary>The wizard's rows as a C# expression (the same truth, ready to be extended by hand): <c>Is("eq", "&lt;From_response_A.x_r&gt;", "200") &amp;&amp; !Is("empty", "_i_")</c>.</summary>
    public static string FromParts(IEnumerable<ConditionPart> parts)
    {
        static string Lit(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var sb = new StringBuilder();
        var first = true;
        foreach (var p in parts)
        {
            if (p.Left.Length == 0) continue;
            if (!first) sb.Append(p.Join switch { "or" => " || ", "xor" => " ^ ", _ => " && " });
            first = false;
            if (p.NotBefore) sb.Append('!');
            sb.Append('(', p.Open);
            if (p.Not) sb.Append('!');
            var left = p.Left is "\"\"" or "''" ? string.Empty : p.Left;
            var right = p.Right is "\"\"" or "''" ? string.Empty : p.Right;
            if (p.Verb.Length == 0) sb.Append("True(").Append(Lit(left)).Append(')');
            else if (ConditionParts.NoRightSide(p.Verb)) sb.Append("Is(").Append(Lit(p.Verb)).Append(", ").Append(Lit(left)).Append(')');
            else sb.Append("Is(").Append(Lit(p.Verb)).Append(", ").Append(Lit(left)).Append(", ").Append(Lit(right)).Append(')');
            sb.Append(')', p.Close);
        }
        return sb.ToString();
    }

    /// <summary>One reference or variable found in the script and whether it sits inside a string.</summary>
    public readonly record struct Placeholder(int Start, int Length, string Text, bool InString, bool IsVariable);

    public static IReadOnlyList<Placeholder> Find(string code, IReadOnlyCollection<string> variableNames)
    {
        var found = new List<Placeholder>();
        var names = new HashSet<string>(variableNames, StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < code.Length)
        {
            var c = code[i];
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/') { while (i < code.Length && code[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*') { var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal); i = end < 0 ? code.Length : end + 2; continue; }
            if (c == '"' || c == '\'')
            {
                var verbatim = c == '"' && i > 0 && code[i - 1] == '@';
                var start = i++;
                while (i < code.Length)
                {
                    if (!verbatim && code[i] == '\\') { i += 2; continue; }
                    if (code[i] == c) { if (verbatim && i + 1 < code.Length && code[i + 1] == '"') { i += 2; continue; } break; }
                    if (c == '"') Scan(i, true);
                    i++;
                }
                i++;
                continue;
            }
            Scan(i, false);
            i++;
        }
        return found;

        void Scan(int at, bool inString)
        {
            if (code[at] == '<' && Reference.Match(code, at) is { Success: true, Index: var index } match && index == at)
            { found.Add(new Placeholder(at, match.Length, match.Value, inString, false)); return; }
            if (code[at] == '_' && (at == 0 || !(char.IsLetterOrDigit(code[at - 1]) || code[at - 1] == '_')))
            {
                var m = Regex.Match(code[at..], @"^_([\p{L}\p{N}.\-]+)_");
                if (m.Success && names.Contains(m.Groups[1].Value)) found.Add(new Placeholder(at, m.Length, m.Value, inString, true));
            }
        }
    }

    /// <summary>The code with every placeholder replaced: text inside strings, <c>V("…")</c> elsewhere.</summary>
    public static string Rewrite(string code, IReadOnlyList<Placeholder> placeholders, Func<Placeholder, string> resolve, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var output = new StringBuilder();
        var cursor = 0;
        foreach (var p in placeholders.OrderBy(x => x.Start))
        {
            if (p.Start < cursor) continue;
            output.Append(code, cursor, p.Start - cursor);
            var text = resolve(p);
            if (p.InString) output.Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n"));
            else { values[p.Text] = text; output.Append("V(\"").Append(p.Text.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\")"); }
            cursor = p.Start + p.Length;
        }
        output.Append(code, cursor, code.Length - cursor);
        return output.ToString();
    }

    private static ScriptOptions Options() => ScriptMetadataReferences.CreateOptions(typeof(ConditionGlobals).Assembly).AddImports("System", "System.Linq", "System.Collections.Generic", "System.Text.RegularExpressions");

    /// <summary>Null when the script compiles, otherwise "line 2, column 5: …". Values are unknown here, so they are empty.</summary>
    public static string? Check(string text, IReadOnlyCollection<string>? variableNames = null)
    {
        var code = Body(text);
        if (code.Length == 0) return "The script is empty. Write a condition such as  <From_response_Step.statusCode_r> == \"200\".";
        var rewritten = Rewrite(code, Find(code, variableNames ?? []), _ => string.Empty, out _);
        var d = CSharpScript.Create<object?>(rewritten, Options(), typeof(ConditionGlobals)).Compile().FirstOrDefault(x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        if (d is null) return null;
        var at = d.Location.GetLineSpan().StartLinePosition;
        return $"line {at.Line + 1}, column {at.Character + 1}: {d.GetMessage()} ({d.Id})";
    }

    /// <summary>Runs the script. <paramref name="resolve"/> turns one reference or variable into its text.</summary>
    public static async Task<bool> EvaluateAsync(string text, IReadOnlyCollection<string> variableNames, Func<string, Task<string>> resolve, CancellationToken token = default)
    {
        var code = Body(text);
        if (code.Length == 0) throw new InvalidDataException("The C# condition is empty.");
        var placeholders = Find(code, variableNames);
        var resolved = new Dictionary<string, string>();
        foreach (var p in placeholders) if (!resolved.ContainsKey(p.Text)) resolved[p.Text] = await resolve(p.Text);
        var rewritten = Rewrite(code, placeholders, p => resolved[p.Text], out var values);
        var globals = new ConditionGlobals();
        foreach (var pair in values) globals.Values[pair.Key] = pair.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        object? result;
        try { result = await CSharpScript.EvaluateAsync<object?>(rewritten, Options(), globals, typeof(ConditionGlobals), timeout.Token); }
        catch (CompilationErrorException ex)
        {
            var d = ex.Diagnostics.FirstOrDefault(x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            var at = d?.Location.GetLineSpan().StartLinePosition;
            throw new InvalidDataException(d is null ? $"The C# condition does not compile: {ex.Message}" : $"The C# condition does not compile (line {at!.Value.Line + 1}, column {at.Value.Character + 1}): {d.GetMessage()}", ex);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
        { throw new InvalidDataException("The C# condition ran longer than 10 seconds and was stopped."); }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidDataException)
        { throw new InvalidDataException($"The C# condition failed: {ex.GetType().Name}: {ex.Message}", ex); }
        return result switch
        {
            bool b => b,
            string s when s.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
            string s when s.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
            null => throw new InvalidDataException("The C# condition gave no value. End it with a true/false expression or use return."),
            _ => throw new InvalidDataException($"The C# condition must give true or false, but it gave {result.GetType().Name} '{result}'.")
        };
    }
}
