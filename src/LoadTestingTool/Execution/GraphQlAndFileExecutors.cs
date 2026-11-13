using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Security.Cryptography;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

public sealed class GraphQlStepExecutor : IStepExecutor
{
    private static readonly JsonSerializerOptions ReadableJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly HttpClient _client;
    private readonly bool _flattenCaseQl;
    public GraphQlStepExecutor(HttpClient client, bool flattenCaseQl = false) { _client = client; _flattenCaseQl = flattenCaseQl; }
    public StepType StepType => _flattenCaseQl ? StepType.CaseQL : StepType.GraphQL;

    public async Task<StepResult> ExecuteAsync(StepExecutionRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        var result = new StepResult { StepType = StepType, StepName = request.Step.StepName };
        try
        {
            var options = StepConfig.Read<GraphQlStepOptions>(request.Step);
            var query = string.IsNullOrWhiteSpace(request.TemplateText) ? options.Inline : request.TemplateText;
            if (!string.IsNullOrWhiteSpace(options.Source)) query = await File.ReadAllTextAsync(WorkspaceGuard.Resolve(request.WorkspaceRoot, options.Source), request.CancellationToken);
            if (string.IsNullOrWhiteSpace(query)) throw new InvalidDataException("GraphQL requires Inline or Source.");
            query = request.Context.Resolve(query);
            var variables = options.Variables.ToDictionary(x => x.Key, x => ContextValue(x.Value, request.Context), StringComparer.OrdinalIgnoreCase);
            var payload = new Dictionary<string, object?> { ["query"] = query, ["variables"] = variables };
            if (!string.IsNullOrWhiteSpace(options.OperationName)) payload["operationName"] = request.Context.Resolve(options.OperationName);
            var url = request.Context.ResolveUrl(request.Step.TargetUrl);
            using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(JsonSerializer.Serialize(payload, ReadableJsonOptions), Encoding.UTF8, "application/json") };
            foreach (var header in request.Step.Headers) message.Headers.TryAddWithoutValidation(header.Key, request.Context.Resolve(header.Value));
            using var response = await _client.SendAsync(message, request.CancellationToken);
            result.StatusCode = (int)response.StatusCode;
            result.RequestText = JsonSerializer.Serialize(payload, ReadableJsonOptions);
            result.ResponseText = await response.Content.ReadAsStringAsync(request.CancellationToken);
            using var document = JsonDocument.Parse(result.ResponseText);
            if (document.RootElement.TryGetProperty("data", out var data))
            {
                object? output = StepJson.ToObject(data);
                if (_flattenCaseQl)
                {
                    output = CaseQlTransformer.Flatten(output);
                    result.ResponseText = JsonSerializer.Serialize(new { data = output }, ReadableJsonOptions);
                }
                result.Outputs["data"] = output;
            }
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                result.ErrorMessage = string.Join("; ", errors.EnumerateArray().Select(x => x.TryGetProperty("message", out var messageProperty) ? messageProperty.GetString() : x.ToString()));
            result.Succeeded = ResultStatus.IsSuccessful(result.StatusCode) && string.IsNullOrWhiteSpace(result.ErrorMessage);
        }
        catch (Exception ex) { result.ErrorMessage = ex.Message; }
        result.DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return result;
    }

    private static object? ContextValue(object? value, LoadTestingTool.Domain.ExecutionContext context) => value switch
    {
        string text => context.Resolve(text),
        JsonElement { ValueKind: JsonValueKind.String } element => context.Resolve(element.GetString() ?? string.Empty),
        _ => value
    };
}

internal static class CaseQlTransformer
{
    public static object? Flatten(object? value)
    {
        if (value is IDictionary<string, object?> dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in dictionary)
            {
                if (pair.Key.Equals("forms", StringComparison.OrdinalIgnoreCase) && pair.Value is IEnumerable forms)
                {
                    var flattened = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    var foundFields = false;
                    foreach (var form in forms)
                    {
                        if (form is not IDictionary<string, object?> formObject) continue;
                        if (formObject.TryGetValue("fields", out var fields) && fields is IEnumerable fieldItems)
                        {
                            foundFields = true;
                            foreach (var field in fieldItems)
                            {
                                if (field is not IDictionary<string, object?> fieldObject || !fieldObject.TryGetValue("name", out var nameValue)) continue;
                                var name = Convert.ToString(nameValue, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                fieldObject.TryGetValue("value", out var fieldValue);
                                flattened[name] = fieldValue is null ? null : Convert.ToString(fieldValue, CultureInfo.InvariantCulture);
                            }
                            foreach (var formProperty in formObject)
                                if (!formProperty.Key.Equals("fields", StringComparison.OrdinalIgnoreCase)) flattened[formProperty.Key] = Flatten(formProperty.Value);
                        }
                    }
                    result[pair.Key] = foundFields ? flattened : Flatten(pair.Value);
                }
                else result[pair.Key] = Flatten(pair.Value);
            }
            return result;
        }
        if (value is IEnumerable list && value is not string)
        {
            var result = new List<object?>();
            foreach (var item in list) result.Add(Flatten(item));
            return result;
        }
        return value;
    }
}

public sealed class FileStepExecutor : IStepExecutor
{
    public StepType StepType => StepType.File;

    public async Task<StepResult> ExecuteAsync(StepExecutionRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        var result = new StepResult { StepType = StepType, StepName = request.Step.StepName };
        try
        {
            var options = StepConfig.Read<FileStepOptions>(request.Step, request.TemplateText);
            var path = WorkspaceGuard.Resolve(request.WorkspaceRoot, request.Context.Resolve(options.Path));
            var action = options.Action.Trim().ToLowerInvariant();
            var binaryAction = action is "readbinary" or "writebinary" or "appendbinary";
            result.RequestText = JsonSerializer.Serialize(new { action = options.Action, path, content = binaryAction ? "[binary content omitted]" : options.Content is null ? null : request.Context.Resolve(options.Content) });
            switch (action)
            {
                case "read":
                case "json":
                    result.ResponseText = await File.ReadAllTextAsync(path, request.CancellationToken);
                    object? output = result.ResponseText;
                    if (options.Action.Equals("json", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(options.JsonPath))
                    {
                        using var document = JsonDocument.Parse(result.ResponseText);
                        output = string.IsNullOrWhiteSpace(options.JsonPath) ? StepJson.ToObject(document.RootElement) : JsonPath.Select(document.RootElement, options.JsonPath);
                    }
                    result.Outputs[request.Context.Resolve(options.OutputVariable)] = output;
                    break;
                case "readbinary":
                    var info = new FileInfo(path);
                    EnsureSize(info.Length, options.MaxBytes, path);
                    var reference = await CreateReferenceAsync(path, info, options.ContentType, request.CancellationToken);
                    if (options.OutputMode.Equals("Base64", StringComparison.OrdinalIgnoreCase))
                    {
                        var bytes = await File.ReadAllBytesAsync(path, request.CancellationToken);
                        result.Outputs[request.Context.Resolve(options.OutputVariable)] = Convert.ToBase64String(bytes);
                        result.ResponseText = $"[binary content materialized as Base64: {bytes.Length} bytes]";
                    }
                    else
                    {
                        result.Outputs[request.Context.Resolve(options.OutputVariable)] = reference;
                        result.ResponseText = JsonSerializer.Serialize(reference);
                    }
                    break;
                case "write":
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllTextAsync(path, request.Context.Resolve(options.Content ?? string.Empty), request.CancellationToken);
                    break;
                case "append":
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.AppendAllTextAsync(path, request.Context.Resolve(options.Content ?? string.Empty), request.CancellationToken);
                    break;
                case "writebinary":
                case "appendbinary":
                    var bytesToWrite = await ResolveBinaryContentAsync(options.Content, request.Context, options.MaxBytes, request.CancellationToken);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using (var stream = new FileStream(path, action == "appendbinary" ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
                        await stream.WriteAsync(bytesToWrite, request.CancellationToken);
                    result.ResponseText = $"[binary {(action == "appendbinary" ? "appended" : "written")}: {bytesToWrite.Length} bytes]";
                    break;
                default: throw new InvalidDataException($"Unsupported file action '{options.Action}'.");
            }
            result.Metadata["path"] = path;
            result.Succeeded = true;
        }
        catch (Exception ex) { result.ErrorMessage = ex.Message; }
        result.DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return result;
    }

    private static void EnsureSize(long size, long maxBytes, string path) { if (maxBytes > 0 && size > maxBytes) throw new InvalidDataException($"Binary file '{path}' is {size} bytes, exceeding MaxBytes={maxBytes}."); }
    private static async Task<BinaryFileReference> CreateReferenceAsync(string path, FileInfo info, string contentType, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new BinaryFileReference { Path = path, SizeBytes = info.Length, Sha256 = Convert.ToHexString(hash).ToLowerInvariant(), ContentType = contentType };
    }
    private static async Task<byte[]> ResolveBinaryContentAsync(string? content, LoadTestingTool.Domain.ExecutionContext context, long maxBytes, CancellationToken cancellationToken)
    {
        var raw = content ?? string.Empty;
        var trimmed = raw.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(trimmed, "^_\\*?([\\p{L}\\p{N}._-]+)\\*?_$");
        var variableName = match.Success ? match.Groups[1].Value : string.Empty;
        if (string.IsNullOrWhiteSpace(variableName) && trimmed.Length > 2 && trimmed[0] == '_' && trimmed[^1] == '_')
            variableName = trimmed[1..^1].Trim('*').Trim();
        if (!string.IsNullOrWhiteSpace(variableName) && context.TryGet(variableName, out var value) && value is BinaryFileReference reference)
        {
            EnsureSize(reference.SizeBytes, maxBytes, reference.Path);
            return await File.ReadAllBytesAsync(reference.Path, cancellationToken);
        }
        var resolved = context.Resolve(raw);
        try { var bytes = Convert.FromBase64String(resolved); EnsureSize(bytes.Length, maxBytes, "inline Base64 content"); return bytes; }
        catch (FormatException ex) { throw new InvalidDataException("Binary Content must be valid Base64 or a binary-file variable reference.", ex); }
    }
}


public static class ResultStatus
{
    public static bool IsSuccessful(int statusCode) => statusCode is >= 200 and < 300;
}
