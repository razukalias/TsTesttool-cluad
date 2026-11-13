using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LoadTestingTool.Domain;

public enum StepType { Http, GraphQL, CaseQL, Sql, File, Script, ActionManual }
public enum ExecutionStage { Setup, Test, Teardown }
public enum VariableScope { Step, Iteration, VirtualUser, Testcase, Run }

public sealed class ExecutionContext
{
    private readonly ConcurrentDictionary<VariableScope, ConcurrentDictionary<string, object?>> _values = new();

    public ExecutionContext()
    {
        foreach (var scope in Enum.GetValues<VariableScope>())
            _values[scope] = new ConcurrentDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    public void Set(string name, object? value, VariableScope scope = VariableScope.Iteration)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Variable name is required.", nameof(name));
        _values[scope][name] = value;
    }

    public bool TryGet(string name, out object? value)
    {
        foreach (var scope in new[] { VariableScope.Step, VariableScope.Iteration, VariableScope.VirtualUser, VariableScope.Testcase, VariableScope.Run })
            if (_values[scope].TryGetValue(name, out value)) return true;
        value = null;
        return false;
    }

    public string Resolve(string value)
    {
        return Regex.Replace(value ?? string.Empty, @"_([\p{L}\p{N}._-]+)_", match =>
        {
            if (!TryGet(match.Groups[1].Value, out var resolved))
                throw new InvalidDataException($"Missing execution variable '{match.Groups[1].Value}'.");
            return Convert.ToString(resolved, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        });
    }

    public string ResolveUrl(string value)
    {
        var resolved = Regex.Replace(value ?? string.Empty, @"\{env\}", _ =>
        {
            if (!TryGet("env", out var environment))
                throw new InvalidDataException("Missing execution variable 'env'.");
            return Convert.ToString(environment, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }, RegexOptions.IgnoreCase);
        return Resolve(resolved);
    }

    public IReadOnlyDictionary<string, object?> Snapshot()
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in new[] { VariableScope.Run, VariableScope.Testcase, VariableScope.VirtualUser, VariableScope.Iteration, VariableScope.Step })
            foreach (var pair in _values[scope]) result[pair.Key] = pair.Value;
        return result;
    }
}

public sealed class StepResult
{
    public string StepId { get; init; } = Guid.NewGuid().ToString("N");
    public StepType StepType { get; init; }
    public string StepName { get; init; } = string.Empty;
    public bool Succeeded { get; set; }
    public long DurationMs { get; set; }
    public int StatusCode { get; set; }
    public string RequestText { get; set; } = string.Empty;
    public string ResponseText { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public Dictionary<string, object?> Outputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StepExecutionRequest
{
    public required RequestStep Step { get; init; }
    public required ExecutionContext Context { get; init; }
    public required string WorkspaceRoot { get; init; }
    public string TemplateText { get; init; } = string.Empty;
    public required CancellationToken CancellationToken { get; init; }
}

public interface IStepExecutor
{
    StepType StepType { get; }
    Task<StepResult> ExecuteAsync(StepExecutionRequest request);
}

public sealed class FeatureConfig
{
    public string WorkspaceRoot { get; set; } = ".";
    public string TemplatesFolder { get; set; } = "Templates";
    public Dictionary<string, SqlConnectionProfile> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SqlConnectionProfile
{
    public string ConnectionString { get; set; } = string.Empty;
    public bool UseIntegratedSecurity { get; set; } = true;
}

public sealed class GraphQlStepOptions
{
    public string? Source { get; set; }
    public string? Inline { get; set; }
    public string? OperationName { get; set; }
    public Dictionary<string, object?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FileStepOptions
{
    public string Action { get; set; } = "Read";
    public string Path { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string OutputVariable { get; set; } = "fileContent";
    public string? JsonPath { get; set; }
    public string Encoding { get; set; } = "Base64";
    public string OutputMode { get; set; } = "Reference";
    public long MaxBytes { get; set; } = 52428800;
    public string ContentType { get; set; } = "application/octet-stream";
}

public sealed class BinaryFileReference
{
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public override string ToString() => $"[binary file: {SizeBytes} bytes, sha256={Sha256}]";
}

public sealed class SqlStepOptions
{
    public string ConnectionProfile { get; set; } = string.Empty;
    public string CommandType { get; set; } = "Text";
    public string? Source { get; set; }
    public string? CommandText { get; set; }
    public string? Procedure { get; set; }
    public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ScriptStepOptions
{
    public string? Source { get; set; }
    public string? Inline { get; set; }
}

public sealed class MultipartPart
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string? Value { get; set; }
    public string? Path { get; set; }
    public string? FileName { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
}

public sealed class MultipartStepOptions
{
    public List<MultipartPart> Parts { get; set; } = [];
}

public static class StepTypeParser
{
    public static StepType Parse(string? value) => string.IsNullOrWhiteSpace(value) ? StepType.Http : value.Trim().ToLowerInvariant() switch
    {
        "http" or "rest" => StepType.Http,
        "graphql" or "graph-ql" => StepType.GraphQL,
        "caseql" or "case-ql" => StepType.CaseQL,
        "sql" or "sqlserver" or "sql-server" => StepType.Sql,
        "file" or "fileio" or "file-io" => StepType.File,
        "script" or "csharp" or "c#" => StepType.Script,
        "actionmanual" or "manual" or "manualhandling" or "manual-action" => StepType.ActionManual,
        _ => throw new InvalidDataException($"Unsupported StepType '{value}'.")
    };
}

public static class ExecutionStageParser
{
    public static ExecutionStage Parse(string? value) => string.IsNullOrWhiteSpace(value) ? ExecutionStage.Test : value.Trim().ToLowerInvariant() switch
    {
        "setup" => ExecutionStage.Setup,
        "test" or "measure" => ExecutionStage.Test,
        "teardown" or "cleanup" => ExecutionStage.Teardown,
        _ => throw new InvalidDataException($"Unsupported Stage '{value}'.")
    };
}

public static class StepConfig
{
    public static JsonDocument Parse(RequestStep step)
    {
        var raw = string.IsNullOrWhiteSpace(step.StepConfig) ? "{}" : step.StepConfig.Trim();
        if (!raw.StartsWith("{"))
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0) throw new InvalidDataException($"Invalid StepConfig entry '{part}'. Expected key=value.");
                values[part[..separator].Trim()] = part[(separator + 1)..].Trim();
            }
            raw = JsonSerializer.Serialize(values);
        }
        try { return JsonDocument.Parse(raw); }
        catch (JsonException ex) { throw new InvalidDataException($"Invalid StepConfig for '{step.StepName}': {ex.Message}", ex); }
    }

    public static T Read<T>(RequestStep step)
    {
        return Read<T>(step, null);
    }

    public static T Read<T>(RequestStep step, string? templateText)
    {
        using var document = Parse(step);
        var config = JsonNode.Parse(document.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
        if (!string.IsNullOrWhiteSpace(templateText))
        {
            var template = JsonNode.Parse(templateText) as JsonObject ?? throw new InvalidDataException($"Template for '{step.StepName}' must contain a JSON object.");
            foreach (var property in config) template[property.Key] = property.Value?.DeepClone();
            config = template;
        }
        return JsonSerializer.Deserialize<T>(config.ToJsonString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"StepConfig for '{step.StepName}' is empty.");
    }
}

public static class WorkspaceGuard
{
    public static string Resolve(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("File path is required.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Path '{path}' is outside the configured workspace.");
        return fullPath;
    }
}

public static class StepJson
{
    public static object? ToObject(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(x => x.Name, x => ToObject(x.Value), StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => value.EnumerateArray().Select(ToObject).ToList(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var i) => i,
        JsonValueKind.Number when value.TryGetDecimal(out var d) => d,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}

public static class StepExecutionResultMapper
{
    public static void PublishOutputs(StepResult result, ExecutionContext context)
    {
        foreach (var output in result.Outputs) context.Set(output.Key, output.Value, VariableScope.Iteration);
    }
}

public sealed class StepExecutorRegistry
{
    private readonly IReadOnlyDictionary<StepType, IStepExecutor> _executors;
    public StepExecutorRegistry(IEnumerable<IStepExecutor> executors) => _executors = executors.ToDictionary(x => x.StepType);
    public IStepExecutor Get(StepType type) => _executors.TryGetValue(type, out var executor) ? executor : throw new InvalidOperationException($"No executor registered for step type '{type}'.");
}

public sealed class ExecutionPipeline
{
    private readonly StepExecutorRegistry _registry;
    public ExecutionPipeline(StepExecutorRegistry registry) => _registry = registry;
    public Task<StepResult> ExecuteAsync(StepExecutionRequest request) => _registry.Get(request.Step.StepType).ExecuteAsync(request);
}

public sealed class ScriptRuntime
{
    private readonly ExecutionContext _context;
    public ScriptRuntime(ExecutionContext context, object? response) { _context = context; Response = response; }
    public object? Response { get; }
    public object? Get(string name)
    {
        if (!_context.TryGet(name, out var value)) return null;
        if (value is string text && (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("[")))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                return StepJson.ToObject(document.RootElement);
            }
            catch (JsonException) { /* Keep non-JSON strings unchanged. */ }
        }
        return value;
    }
    public void Set(string name, object? value) => _context.Set(name, value, VariableScope.Iteration);
    public void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public Action<string>? Logger { get; set; }
    public void Log(string message) => Logger?.Invoke(message);
}

public sealed class ScriptGlobals
{
    public required ScriptRuntime Runtime { get; init; }
    public object? _data_ { get; init; }
    public object? _data1_ { get; init; }
}

public static class JsonPath
{
    public static object? Select(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Trim().TrimStart('$', '.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) return null;
        }
        return StepJson.ToObject(current);
    }
}

public static class StepTypeExtensions
{
    public static string WireName(this StepType type) => type switch
    {
        StepType.GraphQL => "GraphQL",
        StepType.CaseQL => "CaseQL",
        StepType.Sql => "SQL",
        StepType.File => "File",
        StepType.Script => "Script",
        StepType.ActionManual => "ActionManual",
        _ => "HTTP"
    };
}

public static class ExecutionStageExtensions
{
    public static string WireName(this ExecutionStage stage) => stage.ToString();
}
