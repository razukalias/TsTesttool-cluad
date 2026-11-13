using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using LoadTestingTool.Domain;
using LoadTestingTool.Reporting;
using RunnerExecutionContext = LoadTestingTool.Domain.ExecutionContext;

namespace LoadTestingTool.Execution;

internal static class InlineScriptRunner
{
    private const int ScriptTimeoutSeconds = 30;
    private static readonly Regex TokenStart = new(@"<runscript\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<string> ResolveTokensAsync(string value, RunnerExecutionContext context, string workspaceRoot, FeatureConfig features, InternalLogger logger, CancellationToken cancellationToken)
    {
        var input = value ?? string.Empty;
        var output = new StringBuilder(input.Length);
        var cursor = 0;
        while (cursor < input.Length)
        {
            var match = TokenStart.Match(input, cursor);
            if (!match.Success)
            {
                output.Append(input, cursor, input.Length - cursor);
                break;
            }
            output.Append(input, cursor, match.Index - cursor);
            var end = FindTokenEnd(input, match.Index + match.Length);
            if (end < 0) throw new InvalidDataException("Unclosed runscript token. Expected <runscript(template:name, parameter:value)>.");
            var expression = input[(match.Index + match.Length)..end];
            var arguments = SplitArguments(expression);
            var template = GetArgument(arguments, "template");
            if (string.IsNullOrWhiteSpace(template)) throw new InvalidDataException("runscript requires a template:name argument.");
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in arguments)
            {
                var separator = argument.IndexOf(':');
                if (separator <= 0) throw new InvalidDataException($"Invalid runscript argument '{argument}'. Expected name:value.");
                var name = argument[..separator].Trim();
                if (name.Equals("template", StringComparison.OrdinalIgnoreCase)) continue;
                var rawValue = argument[(separator + 1)..].Trim();
                var resolvedValue = await ResolveArgumentAsync(rawValue, context, workspaceRoot, features, logger, cancellationToken);
                parameters[name] = resolvedValue;
            }
            var result = await ExecuteAsync(template.Trim(), parameters, context, workspaceRoot, features, logger, cancellationToken);
            output.Append(result);
            cursor = end + 2;
        }
        return output.ToString();
    }

    private static async Task<string> ResolveArgumentAsync(string value, RunnerExecutionContext context, string workspaceRoot, FeatureConfig features, InternalLogger logger, CancellationToken cancellationToken)
    {
        var resolved = await ResolveTokensAsync(value, context, workspaceRoot, features, logger, cancellationToken);
        return context.Resolve(resolved);
    }

    private static async Task<string> ExecuteAsync(string template, IReadOnlyDictionary<string, string> parameters, RunnerExecutionContext context, string workspaceRoot, FeatureConfig features, InternalLogger logger, CancellationToken cancellationToken)
    {
        var path = ResolveScriptPath(template, workspaceRoot, features.TemplatesFolder);
        var code = await File.ReadAllTextAsync(path, cancellationToken);
        foreach (var parameter in parameters) context.Set(parameter.Key, parameter.Value, VariableScope.Iteration);
        var runtime = new ScriptRuntime(context, null) { Logger = message => logger.Info($"INLINE SCRIPT LOG | template={template}; {message}") };
        var globals = new ScriptGlobals { Runtime = runtime, _data_ = null, _data1_ = null };
        var scriptCode = ExpandRuntimePlaceholders(code);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ScriptTimeoutSeconds));
        var options = ScriptMetadataReferences.CreateOptions(typeof(ScriptRuntime).Assembly)
            .AddImports("System", "System.Linq", "System.Collections.Generic", "LoadTestingTool.Domain");
        logger.Info($"INLINE SCRIPT START | template={template}; parameters={string.Join(", ", parameters.Select(x => $"{x.Key}={x.Value}"))}");
        var value = await CSharpScript.EvaluateAsync<object?>(scriptCode, options, globals, typeof(ScriptGlobals), timeout.Token);
        var result = value is null ? string.Empty : value is string text ? text : JsonSerializer.Serialize(value);
        logger.Info($"INLINE SCRIPT END | template={template}; result={result}");
        return result;
    }

    private static string ResolveScriptPath(string template, string workspaceRoot, string templatesFolder)
    {
        var fileName = template.EndsWith(".csx", StringComparison.OrdinalIgnoreCase) ? template : template + ".csx";
        var workspaceCandidates = new[]
        {
            template,
            fileName,
            Path.Combine("Templates", template),
            Path.Combine("Templates", fileName)
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        var attempted = new List<string>();
        foreach (var candidate in workspaceCandidates)
        {
            var path = WorkspaceGuard.Resolve(workspaceRoot, candidate);
            attempted.Add(path);
            if (File.Exists(path)) return path;
        }
        // TemplatesFolder is an explicit configured root. Validate the script
        // relative to that root instead of validating its absolute path against
        // WorkspaceRoot, which may come from an older instance configuration.
        var configuredTemplateRoot = Path.GetFullPath(templatesFolder, workspaceRoot);
        foreach (var candidate in new[] { template, fileName }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = WorkspaceGuard.Resolve(configuredTemplateRoot, candidate);
            attempted.Add(path);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException($"Inline script template '{template}' was not found. Searched: {string.Join("; ", attempted)}");
    }

    private static string? GetArgument(IReadOnlyList<string> arguments, string name) => arguments.Select(argument => (Text: argument, Separator: argument.IndexOf(':'))).Where(x => x.Separator > 0 && x.Text[..x.Separator].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Text[(x.Separator + 1)..].Trim()).FirstOrDefault();

    private static int FindTokenEnd(string value, int start)
    {
        var depth = 1;
        for (var i = start; i < value.Length - 1; i++)
        {
            if (value[i] == '<') depth++;
            else if (value[i] == '>' && depth > 1) depth--;
            else if (value[i] == ')' && value[i + 1] == '>' && depth == 1) return i;
        }
        return -1;
    }

    private static IReadOnlyList<string> SplitArguments(string value)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '<') depth++;
            else if (value[i] == '>' && depth > 0) depth--;
            else if (value[i] == ',' && depth == 0)
            {
                result.Add(value[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < value.Length) result.Add(value[start..].Trim());
        return result.Where(x => x.Length > 0).ToList();
    }

    private static string ExpandRuntimePlaceholders(string code)
    {
        return Regex.Replace(code, @"_([\p{L}\p{N}._-]+)_", match => $"Runtime.Get(\"{match.Groups[1].Value}\")?.ToString() ?? string.Empty");
    }
}
