using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Editing;

/// <summary>A problem found in a script template, with its position inside the .csx (1-based).</summary>
public sealed record ScriptDiagnostic(int Line, int Column, string Id, string Message, bool IsError);

/// <summary>One thing a template can use: what to type, and what it does.</summary>
public sealed record ScriptHelpItem(string Insert, string Name, string Detail);

/// <summary>
/// The inline-script templates (<c>.csx</c> files) the runner looks up for <c>&lt;runscript(template:name, parameter:value)&gt;</c>:
/// where they live, a compile-only check, the parameters a template reads, and the help text for writing one.
/// </summary>
public static class ScriptTemplates
{
    /// <summary>The template names found in the three places the runner searches: the workspace folder, its Templates folder and the configured templates folder.</summary>
    public static IReadOnlyList<string> List(string workspaceRoot, string templatesFolder)
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            folders.Add(workspaceRoot);
            folders.Add(Path.Combine(workspaceRoot, "Templates"));
            if (!string.IsNullOrWhiteSpace(templatesFolder)) folders.Add(Path.GetFullPath(templatesFolder, workspaceRoot));
        }
        return folders.Where(Directory.Exists).SelectMany(f => Directory.EnumerateFiles(f, "*.csx", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileNameWithoutExtension).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The file of a template, searched the way the runner does it, or null.</summary>
    public static string? PathOf(string name, string workspaceRoot, string templatesFolder)
    {
        var file = name.EndsWith(".csx", StringComparison.OrdinalIgnoreCase) ? name : name + ".csx";
        var candidates = new List<string> { Path.Combine(workspaceRoot, file), Path.Combine(workspaceRoot, "Templates", file) };
        if (!string.IsNullOrWhiteSpace(templatesFolder)) candidates.Add(Path.Combine(Path.GetFullPath(templatesFolder, workspaceRoot), file));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Where a new template is written: the Templates folder of the workspace.</summary>
    public static string NewTemplatePath(string workspaceRoot, string name)
        => Path.Combine(workspaceRoot, "Templates", (name.EndsWith(".csx", StringComparison.OrdinalIgnoreCase) ? name : name + ".csx"));

    /// <summary>True for a name that is safe to use as a file name (letters, digits, dash, underscore, dot; no folders).</summary>
    public static bool IsValidName(string name) => Regex.IsMatch(name.Trim(), @"^[\p{L}\p{N}_.-]{1,60}$") && !name.Contains("..");

    /// <summary>Compiles the script without running it and returns what the compiler found, with line and column inside the template.</summary>
    public static IReadOnlyList<ScriptDiagnostic> Validate(string code)
    {
        try
        {
            var options = ScriptOptions.Default
                .WithReferences(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).Select(a => MetadataReference.CreateFromFile(a.Location)).Append(MetadataReference.CreateFromFile(typeof(ScriptRuntime).Assembly.Location)))
                .AddImports("System", "System.Linq", "System.Collections.Generic", "LoadTestingTool.Domain");
            var script = CSharpScript.Create<object?>(ExpandPlaceholders(code), options, typeof(ScriptGlobals));
            return script.Compile().Select(Convert).Where(d => d.IsError || d.Id != "CS8019").ToList();
        }
        catch (Exception ex) { return [new ScriptDiagnostic(1, 1, "ERR", ex.Message, true)]; }
    }

    private static ScriptDiagnostic Convert(Diagnostic d)
    {
        var at = d.Location.GetLineSpan().StartLinePosition;
        return new ScriptDiagnostic(at.Line + 1, at.Character + 1, d.Id, d.GetMessage(), d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Same expansion the runner does: <c>_name_</c> reads the variable <c>name</c>.</summary>
    public static string ExpandPlaceholders(string code)
        => Regex.Replace(code, @"_([\p{L}\p{N}._-]+)_", m => $"Runtime.Get(\"{m.Groups[1].Value}\")?.ToString() ?? string.Empty");

    private static readonly Regex Placeholder = new(@"(?<![\p{L}\p{N}])_([\p{L}][\p{L}\p{N}._-]*)_(?![\p{L}\p{N}])", RegexOptions.Compiled);
    private static readonly Regex GetCall = new(@"Runtime\s*\.\s*Get\s*\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled);

    /// <summary>The variable names a template reads (<c>_name_</c> and <c>Runtime.Get("name")</c>), so the parameter list can start filled.</summary>
    public static IReadOnlyList<string> ParametersUsed(string code)
        => Placeholder.Matches(code).Select(m => m.Groups[1].Value).Concat(GetCall.Matches(code).Select(m => m.Groups[1].Value))
            .Where(n => !n.Equals("data", StringComparison.OrdinalIgnoreCase) && !n.Equals("data1", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The text of a new template, with comments that show how to read a parameter and return the value.</summary>
    public static string Starter(string name) => """
        // NAME: an inline script. A cell calls it as  <runscript(template:NAME, env:<From_request_env_r>)>
        // Whatever the script returns is written into the cell (null becomes an empty value).
        var env = (Runtime.Get("env")?.ToString() ?? "").Trim().ToLowerInvariant();   // a parameter passed by the cell: env:...
        Runtime.Log("env is '" + env + "'");                                             // goes to the internal log, with the template name
        return env == "production" ? "200" : "500";
        """.Replace("NAME", name);

    /// <summary>Builds <c>&lt;runscript(template:name, a:1, b:&lt;From_…&gt;)&gt;</c>.</summary>
    public static string Token(string template, IEnumerable<(string Name, string Value)> parameters)
        => "<runscript(template:" + template.Trim() + string.Concat(parameters.Where(p => p.Name.Trim().Length > 0).Select(p => $", {p.Name.Trim()}:{p.Value.Trim()}")) + ")>";

    /// <summary>Reads a <c>&lt;runscript(…)&gt;</c> token back into its template and parameters; null when it is not one.</summary>
    public static (string Template, IReadOnlyList<(string Name, string Value)> Parameters)? ParseToken(string token)
    {
        var text = token.Trim();
        var m = Regex.Match(text, @"^<runscript\s*\((.*)\)>$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        var parts = SplitTop(m.Groups[1].Value);
        string? template = null; var parameters = new List<(string, string)>();
        foreach (var part in parts)
        {
            var colon = part.IndexOf(':');
            if (colon <= 0) continue;
            var name = part[..colon].Trim(); var value = part[(colon + 1)..].Trim();
            if (name.Equals("template", StringComparison.OrdinalIgnoreCase)) template = value; else parameters.Add((name, value));
        }
        return template is null ? null : (template, parameters);
    }

    private static List<string> SplitTop(string value)
    {
        var result = new List<string>(); var start = 0; var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '<') depth++;
            else if (value[i] == '>' && depth > 0) depth--;
            else if (value[i] == ',' && depth == 0) { result.Add(value[start..i].Trim()); start = i + 1; }
        }
        if (start < value.Length) result.Add(value[start..].Trim());
        return result.Where(x => x.Length > 0).ToList();
    }

    /// <summary>Everything a template can use, for the autocomplete list of the template form.</summary>
    public static IReadOnlyList<ScriptHelpItem> Help { get; } =
    [
        new("Runtime.Get(\"name\")", "Runtime.Get(\"name\")", "the value of a variable or a parameter of the call (null when it does not exist); JSON text comes back as an object"),
        new("_name_", "_name_", "short form of Runtime.Get(\"name\")?.ToString() ?? \"\" — the text of the variable, empty when missing"),
        new("Runtime.Set(\"name\", value)", "Runtime.Set(\"name\", value)", "store a variable for the following steps of this row"),
        new("Runtime.Log(\"text\")", "Runtime.Log(\"text\")", "write a line to the internal log (shown with the template name)"),
        new("Runtime.Assert(condition, \"message\")", "Runtime.Assert(condition, \"message\")", "stop the script with that message when the condition is false"),
        new("Runtime.Response", "Runtime.Response", "the response of the step, when the script runs after a request (null in a cell)"),
        new("return ", "return", "the value that is written into the cell; a script must return something"),
        new("DateTime.UtcNow", "DateTime.UtcNow", "the current time; System, System.Linq and System.Collections.Generic are already imported"),
        new("Guid.NewGuid().ToString()", "Guid.NewGuid()", "a new GUID"),
        new("Convert.ToBase64String(", "Convert.ToBase64String(", "Base64 of bytes, for example Encoding.UTF8.GetBytes(text)"),
        new("string.Join(\",\", ", "string.Join(", "join values with a separator"),
        new("(Runtime.Get(\"env\")?.ToString() ?? \"\").Trim().ToLowerInvariant()", "environment name", "the environment of the run, trimmed and lower case (variable env)"),
    ];

    /// <summary>Short explanations and examples shown beside the template editor.</summary>
    public static string HelpText { get; } = """
        HOW IT WORKS
        A cell calls a template:  <runscript(template:name, parameter:value, other:<From_request_header_r>)>
        The runner runs name.csx, gives it the parameters as variables, and writes what it returns into the cell.
        Templates are looked up in the workspace folder, its Templates folder and the configured templates folder.
        A script that runs longer than 30 seconds is stopped.

        PARAMETERS
        Read a parameter with Runtime.Get("env") or the short form _env_.
        The value can be text, a number, or a reference such as <From_request_userid_r> that is filled in before the script runs.

        EXAMPLES
        1) Value by environment
           var env = (Runtime.Get("env")?.ToString() ?? "").ToLowerInvariant();
           return env == "production" ? "200" : "500";

        2) Build a value from two parameters
           return $"{_first_}.{_last_}@example.com".ToLowerInvariant();

        3) Date in the future
           return DateTime.UtcNow.AddDays(int.Parse(_days_)).ToString("yyyy-MM-dd");

        4) Log and return
           Runtime.Log("user is " + _user_);
           return Guid.NewGuid().ToString("N");

        TIPS
        Always return a value; a script that returns nothing gives an empty cell (the log warns about it).
        Press Validate to compile the script without running it; errors show the line and column.
        """;
}
