using System.Text.RegularExpressions;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LoadTestingTool.Editing;

/// <summary>Which script the code belongs to: what it can use decides what is suggested.</summary>
public enum ScriptKind
{
    /// <summary>A <c>.csx</c> template, an inline <c>&lt;runscript&gt;</c> template or a Script step: <c>Runtime</c>, <c>_data_</c>, <c>_name_</c>.</summary>
    Template,
    /// <summary>A <c>cs:</c> condition: <c>V</c>, <c>Has</c>, <c>Num</c>, <c>Is</c>, <c>True</c>, and <c>&lt;From_…&gt;</c> / <c>_name_</c> references.</summary>
    Condition,
}

/// <summary>One suggestion: what the list shows, what replaces the typed text, and a short explanation. <see cref="Kind"/> is method, property, field, local, type, namespace, keyword, snippet, verb or variable.</summary>
public sealed record ScriptSuggestion(string Label, string Insert, string Detail, string Kind);

/// <summary>Suggestions for the code before the caret: replace <see cref="Start"/>..caret with the chosen item's <see cref="ScriptSuggestion.Insert"/>.</summary>
public sealed record ScriptSuggestions(int Start, IReadOnlyList<ScriptSuggestion> Items);

/// <summary>The call the caret is inside: its signature, its parameter names and which one is being typed.</summary>
public sealed record ScriptSignature(string Text, IReadOnlyList<string> Parameters, int Active);

/// <summary>
/// C# completion for the places a script can be written: after a dot (members of what is in front of it, from the real compiler), a typed name (locals,
/// what the script can use, types of the imported namespaces, keywords, snippets), the verbs inside <c>Is("</c>, and variable names inside <c>Runtime.Get("</c>.
/// Nothing is suggested inside a comment or an ordinary string. <c>_name_</c> and <c>&lt;From_…&gt;</c> references are replaced by a string of the same length first, so every position stays valid.
/// </summary>
public static class ScriptCompletion
{
    private const int MaxItems = 40;
    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() => ScriptMetadataReferences.CreateOptions(typeof(ScriptGlobals).Assembly).MetadataReferences.ToList());
    private static readonly Regex Variable = new(@"(?<![\p{L}\p{N}])_[\p{L}][\p{L}\p{N}._-]*_(?![\p{L}\p{N}])", RegexOptions.Compiled);
    private static readonly Regex Reference = new(@"<From_[^<>\r\n]+>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] Keywords =
    [
        "if", "else", "for", "foreach", "while", "do", "switch", "case", "break", "continue", "return", "var", "new", "null", "true", "false", "try", "catch", "finally", "throw",
        "string", "int", "long", "double", "decimal", "bool", "object", "char", "byte", "in", "is", "as", "using", "const", "static", "default", "typeof", "await", "async", "lock",
    ];

    private static readonly (string Name, string Insert, string Detail)[] Snippets =
    [
        ("if", "if (condition)\n{\n    \n}", "if statement"),
        ("ifelse", "if (condition)\n{\n    \n}\nelse\n{\n    \n}", "if / else"),
        ("foreach", "foreach (var item in items)\n{\n    \n}", "loop over a collection"),
        ("for", "for (var i = 0; i < count; i++)\n{\n    \n}", "counting loop"),
        ("try", "try\n{\n    \n}\ncatch (Exception ex)\n{\n    \n}", "try / catch"),
        ("condition", "V(\"name\") == \"value\" && Has(\"other\")", "a condition on values"),
    ];

    /// <summary>Loads the compiler's references now (the first suggestion would otherwise wait for them); safe to call from a background thread.</summary>
    public static void WarmUp() { try { Suggest(ScriptKind.Template, "Runtime.", 8, true); Suggest(ScriptKind.Condition, "V(", 2, true); } catch (Exception ex) when (ex is not OutOfMemoryException) { } }

    public static ScriptSuggestions? Suggest(ScriptKind kind, string code, int caret, bool forced = false, IReadOnlyList<string>? variables = null)
    {
        try { return SuggestCore(kind, code ?? string.Empty, caret, forced, variables ?? []); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public static ScriptSignature? Signature(ScriptKind kind, string code, int caret)
    {
        try { return SignatureCore(kind, code ?? string.Empty, caret); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    // ---- compile -----------------------------------------------------------------------------

    /// <summary>The code with the <c>cs:</c> prefix, <c>_name_</c> and <c>&lt;From_…&gt;</c> replaced by blanks / strings of the same length.</summary>
    public static string Prepare(ScriptKind kind, string code)
    {
        if (kind == ScriptKind.Condition)
        {
            var lead = code.Length - code.TrimStart().Length;
            if (code.AsSpan(lead).StartsWith("cs:", StringComparison.OrdinalIgnoreCase)) code = code[..lead] + "   " + code[(lead + 3)..];
            code = Reference.Replace(code, m => Literal(m.Length));
        }
        return Variable.Replace(code, m => Literal(m.Length));
    }

    private static string Literal(int length) => "\"" + new string('x', Math.Max(0, length - 2)) + "\"";

    private static (SyntaxTree Tree, SemanticModel Model) Compile(ScriptKind kind, string prepared)
    {
        var tree = CSharpSyntaxTree.ParseText(prepared, new CSharpParseOptions(kind: SourceCodeKind.Script));
        var usings = kind == ScriptKind.Condition
            ? new[] { "System", "System.Linq", "System.Collections.Generic", "System.Text.RegularExpressions" }
            : ["System", "System.Linq", "System.Collections.Generic", "LoadTestingTool.Domain"];
        var compilation = CSharpCompilation.CreateScriptCompilation("completion", tree, References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: usings), globalsType: kind == ScriptKind.Condition ? typeof(ConditionGlobals) : typeof(ScriptGlobals));
        return (tree, compilation.GetSemanticModel(tree));
    }

    // ---- suggestions -------------------------------------------------------------------------

    private static ScriptSuggestions? SuggestCore(ScriptKind kind, string code, int caret, bool forced, IReadOnlyList<string> variables)
    {
        caret = Math.Clamp(caret, 0, code.Length);
        var prepared = Prepare(kind, code);
        var (tree, model) = Compile(kind, prepared);
        var root = tree.GetRoot();

        if (caret > 0 && root.FindTrivia(caret - 1) is { } trivia && trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
            && (caret < trivia.Span.End || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || !prepared[..caret].EndsWith("*/", StringComparison.Ordinal)))
            return null;

        if (InString(root, prepared, caret) is { } literal)
            return StringArgument(kind, literal, prepared, caret, variables);

        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(prepared[start - 1]) || prepared[start - 1] == '_')) start--;
        var prefix = prepared[start..caret];

        if (start > 0 && prepared[start - 1] == '.')
        {
            // "1." is the start of a decimal number, not a member access
            if (start > 1 && root.FindToken(start - 2).IsKind(SyntaxKind.NumericLiteralToken)) return null;
            return Members(model, root, prepared, start - 1, start, prefix);
        }

        if (!forced && prefix.Length < 2) return null;
        if (prefix.Length > 0 && char.IsDigit(prefix[0])) return null;
        return Names(model, prepared, caret, start, prefix);
    }

    /// <summary>The string literal the caret is inside (typed so far, not finished), or null.</summary>
    private static LiteralExpressionSyntax? InString(SyntaxNode root, string prepared, int caret)
    {
        if (caret == 0) return null;
        var token = root.FindToken(caret - 1);
        if (!token.IsKind(SyntaxKind.StringLiteralToken) || token.Parent is not LiteralExpressionSyntax literal) return null;
        var closed = token.Text.Length >= 2 && token.Text[^1] == '"' && !token.Text.EndsWith("\\\"", StringComparison.Ordinal);
        return caret > token.SpanStart && (caret < token.Span.End || !closed) ? literal : null;
    }

    private static ScriptSuggestions? StringArgument(ScriptKind kind, LiteralExpressionSyntax literal, string prepared, int caret, IReadOnlyList<string> variables)
    {
        if (literal.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax list } || list.Arguments.IndexOf((ArgumentSyntax)literal.Parent) != 0 || list.Parent is not InvocationExpressionSyntax call) return null;
        var name = call.Expression switch { IdentifierNameSyntax id => id.Identifier.Text, MemberAccessExpressionSyntax m => m.Expression.ToString() + "." + m.Name.Identifier.Text, _ => "" };
        var start = literal.SpanStart + 1;
        var typed = prepared[start..Math.Max(start, caret)];
        IEnumerable<ScriptSuggestion> items;
        if (name == "Is" && kind == ScriptKind.Condition)
            items = AssertionEngine.Verbs.Select(v => new ScriptSuggestion(v, v, "condition verb", "verb"));
        else if (name is "Runtime.Get" or "Runtime.Set" && kind == ScriptKind.Template)
            items = variables.Select(v => new ScriptSuggestion(v, v, "variable", "variable"));
        else return null;
        var list2 = items.Where(i => i.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ToList();
        return list2.Count == 0 ? null : new ScriptSuggestions(start, list2);
    }

    private static ScriptSuggestions? Members(SemanticModel model, SyntaxNode root, string prepared, int dot, int start, string prefix)
    {
        var token = root.FindToken(dot);
        ExpressionSyntax? expression = token.Parent switch
        {
            MemberAccessExpressionSyntax m => m.Expression,
            MemberBindingExpressionSyntax b => (b.Parent as ConditionalAccessExpressionSyntax)?.Expression ?? (b.Parent?.Parent as ConditionalAccessExpressionSyntax)?.Expression,
            _ => null,
        };
        if (expression is null) return null;
        var symbol = model.GetSymbolInfo(expression).Symbol;
        IEnumerable<ISymbol> members;
        bool isStatic;
        if (symbol is INamespaceOrTypeSymbol container)
        {
            isStatic = true;
            members = model.LookupSymbols(dot, container, includeReducedExtensionMethods: false);
        }
        else
        {
            var type = model.GetTypeInfo(expression).Type;
            if (type is null || type.TypeKind == TypeKind.Error || type.TypeKind == TypeKind.Dynamic) return null;
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable) type = nullable.TypeArguments[0];
            isStatic = false;
            members = model.LookupSymbols(dot, type, includeReducedExtensionMethods: true);
        }
        var shown = members.Where(s => Visible(model, dot, s, isStatic) && s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        var items = shown.GroupBy(s => s.Name).Select(g => Describe(g.ToList())).OrderBy(i => i.Label, StringComparer.OrdinalIgnoreCase).Take(MaxItems * 4).ToList();
        return items.Count == 0 ? null : new ScriptSuggestions(start, items);
    }

    private static bool Visible(SemanticModel model, int position, ISymbol symbol, bool isStatic)
    {
        if (symbol.IsImplicitlyDeclared || symbol.Name.Length == 0 || !char.IsLetter(symbol.Name[0]) && symbol.Name[0] != '_') return false;
        if (symbol is IMethodSymbol { MethodKind: not (MethodKind.Ordinary or MethodKind.ReducedExtension) }) return false;
        if (symbol.Name is "Finalize" or "MemberwiseClone") return false;
        if (!model.IsAccessible(position, symbol)) return false;
        if (symbol is INamedTypeSymbol or INamespaceSymbol) return isStatic;
        return isStatic ? symbol.IsStatic : !symbol.IsStatic || symbol is IMethodSymbol { MethodKind: MethodKind.ReducedExtension };
    }

    private static ScriptSuggestion Describe(IReadOnlyList<ISymbol> overloads)
    {
        var first = overloads[0];
        var format = SymbolDisplayFormat.MinimallyQualifiedFormat;
        var detail = first switch
        {
            IMethodSymbol m => m.ToDisplayString(format) + (overloads.Count > 1 ? $"  (+{overloads.Count - 1} overload{(overloads.Count > 2 ? "s" : "")})" : ""),
            IPropertySymbol p => $"{p.Type.ToDisplayString(format)} {p.Name}" + (p.SetMethod is null ? " { get; }" : " { get; set; }"),
            IFieldSymbol f => $"{f.Type.ToDisplayString(format)} {f.Name}",
            IEventSymbol e => $"event {e.Type.ToDisplayString(format)} {e.Name}",
            INamedTypeSymbol t => $"{t.TypeKind.ToString().ToLowerInvariant()} {t.ToDisplayString(format)}",
            INamespaceSymbol n => $"namespace {n.ToDisplayString(format)}",
            _ => first.ToDisplayString(format),
        };
        var kind = first switch { IMethodSymbol => "method", IPropertySymbol => "property", IFieldSymbol => "field", IEventSymbol => "event", INamedTypeSymbol => "type", INamespaceSymbol => "namespace", ILocalSymbol or IParameterSymbol => "local", _ => "symbol" };
        return new ScriptSuggestion(first.Name, first.Name, detail, kind);
    }

    private static ScriptSuggestions? Names(SemanticModel model, string prepared, int caret, int start, string prefix)
    {
        var found = new List<(int Rank, ScriptSuggestion Item)>();
        foreach (var group in model.LookupSymbols(caret).Where(s => !s.IsImplicitlyDeclared && s.Name.Length > 0 && (char.IsLetter(s.Name[0]) || s.Name[0] == '_')
                     && s is not IMethodSymbol { MethodKind: not MethodKind.Ordinary }
                     && s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).GroupBy(s => s.Name))
        {
            var item = Describe(group.ToList());
            var rank = item.Kind switch { "local" => 0, "method" or "property" or "field" => 1, "type" => 3, _ => 4 };
            found.Add((rank, item));
        }
        foreach (var keyword in Keywords.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            found.Add((2, new ScriptSuggestion(keyword, keyword, "keyword", "keyword")));
        foreach (var (name, insert, detail) in Snippets.Where(s => s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            found.Add((2, new ScriptSuggestion(name, insert, "snippet: " + detail, "snippet")));
        var items = found.OrderBy(f => f.Rank).ThenBy(f => f.Item.Label.Length).ThenBy(f => f.Item.Label, StringComparer.OrdinalIgnoreCase).Select(f => f.Item).Take(MaxItems).ToList();
        return items.Count == 0 ? null : new ScriptSuggestions(start, items);
    }

    // ---- signature help ----------------------------------------------------------------------

    private static ScriptSignature? SignatureCore(ScriptKind kind, string code, int caret)
    {
        caret = Math.Clamp(caret, 0, code.Length);
        var prepared = Prepare(kind, code);
        var (tree, model) = Compile(kind, prepared);
        var root = tree.GetRoot();
        var token = root.FindToken(Math.Max(0, caret - 1));
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (node is not ArgumentListSyntax list || list.Parent is not InvocationExpressionSyntax call) continue;
            if (caret <= list.OpenParenToken.Span.End - 1 || (!list.CloseParenToken.IsMissing && caret > list.CloseParenToken.SpanStart)) continue;
            var active = list.Arguments.GetSeparators().Count(s => s.Span.End <= caret);
            var overloads = model.GetMemberGroup(call.Expression).OfType<IMethodSymbol>().ToList();
            if (overloads.Count == 0 && model.GetSymbolInfo(call).Symbol is IMethodSymbol one) overloads.Add(one);
            if (overloads.Count == 0) return null;
            var pick = overloads.OrderBy(m => m.Parameters.Length < active + 1 ? 1 : 0).ThenBy(m => m.Parameters.Length).First();
            var format = SymbolDisplayFormat.MinimallyQualifiedFormat.WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName | SymbolDisplayParameterOptions.IncludeDefaultValue);
            return new ScriptSignature(pick.ToDisplayString(format), pick.Parameters.Select(p => p.Name).ToList(), Math.Min(active, Math.Max(0, pick.Parameters.Length - 1)));
        }
        return null;
    }
}
