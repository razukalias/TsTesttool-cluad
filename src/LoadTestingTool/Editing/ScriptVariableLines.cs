using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace LoadTestingTool.Editing;

/// <summary>
/// The "Add variable…" lines of a script: <c>var name = "_placeholder_";</c> put at the top as plain text. The value is always a string; the run fills the placeholder
/// (a request column, or a value of an earlier step) and escapes it for the string.
/// </summary>
public static class ScriptVariableLines
{
    private static readonly Regex Identifier = new(@"^[\p{L}_][\p{L}\p{N}_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Generated = new(@"^var\s+[\p{L}_][\p{L}\p{N}_]*\s*=\s*""(?:[^""\\]|\\.)*"";\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex PlaceholderName = new(@"^[\p{L}\p{N}._\-\[\]]+$", RegexOptions.CultureInvariant);

    /// <summary>Why this is not a usable C# name, or null.</summary>
    public static string? CheckName(string? name, string? code = null)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return "Give the variable a name.";
        if (!Identifier.IsMatch(name)) return "A C# name has letters, digits and _, and does not start with a digit (no spaces).";
        if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) is SyntaxKind.VarKeyword) return $"'{name}' is a C# keyword.";
        if (code is not null && Regex.IsMatch(code, $@"^var\s+{Regex.Escape(name)}\s*=", RegexOptions.Multiline | RegexOptions.CultureInvariant)) return $"'{name}' is already declared in the script.";
        return null;
    }

    /// <summary>The placeholder for a column or step value: <c>_name_</c>. A name with a space cannot be a placeholder.</summary>
    public static string? Placeholder(string? source, out string? problem)
    {
        var name = source?.Trim().Trim('_') ?? string.Empty;
        problem = name.Length == 0 ? "Choose a value." : !PlaceholderName.IsMatch(name) ? $"'{name}' has characters a placeholder cannot hold (no spaces)." : null;
        return problem is null ? "_" + name + "_" : null;
    }

    public static string Line(string name, string placeholder) => $"var {name.Trim()} = \"{placeholder.Replace("\\", "\\\\").Replace("\"", "\\\"")}\";";

    /// <summary>The script with the line added after the variable lines already at its top.</summary>
    public static string AddToTop(string? code, string name, string placeholder)
    {
        var lines = (code ?? string.Empty).Replace("\r\n", "\n").Split('\n').ToList();
        var at = 0;
        while (at < lines.Count && Generated.IsMatch(lines[at])) at++;
        lines.Insert(at, Line(name, placeholder));
        var text = string.Join("\n", lines);
        return (code ?? string.Empty).Contains("\r\n") ? text.Replace("\n", "\r\n") : text;
    }

    /// <summary>A value as the inside of a C# string literal.</summary>
    public static string EscapeForString(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default: sb.Append(c); break;
            }
        return sb.ToString();
    }
}
