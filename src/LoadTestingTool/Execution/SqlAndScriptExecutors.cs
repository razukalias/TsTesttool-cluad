using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Data.SqlClient;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

public sealed class SqlStepExecutor : IStepExecutor
{
    private readonly FeatureConfig _features;
    public SqlStepExecutor(FeatureConfig features) => _features = features;
    public StepType StepType => StepType.Sql;

    public async Task<StepResult> ExecuteAsync(StepExecutionRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        var result = new StepResult { StepType = StepType, StepName = request.Step.StepName };
        try
        {
            var options = StepConfig.Read<SqlStepOptions>(request.Step);
            if (!_features.Connections.TryGetValue(options.ConnectionProfile, out var profile)) throw new InvalidDataException($"SQL connection profile '{options.ConnectionProfile}' was not found.");
            var commandText = string.IsNullOrWhiteSpace(request.TemplateText) ? options.CommandText ?? options.Procedure : request.TemplateText;
            if (!string.IsNullOrWhiteSpace(options.Source)) commandText = await File.ReadAllTextAsync(WorkspaceGuard.Resolve(request.WorkspaceRoot, options.Source), request.CancellationToken);
            if (string.IsNullOrWhiteSpace(commandText)) throw new InvalidDataException("SQL requires CommandText, Procedure, or Source.");
            commandText = request.Context.Resolve(commandText);
            result.RequestText = JsonSerializer.Serialize(new { commandType = options.CommandType, commandText, parameters = options.Parameters });
            await using var connection = new SqlConnection(request.Context.Resolve(profile.ConnectionString));
            await connection.OpenAsync(request.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            command.CommandType = options.CommandType.Equals("StoredProc", StringComparison.OrdinalIgnoreCase) || options.CommandType.Equals("StoredProcedure", StringComparison.OrdinalIgnoreCase) ? System.Data.CommandType.StoredProcedure : System.Data.CommandType.Text;
            foreach (var parameter in options.Parameters)
            {
                var sqlParameter = command.Parameters.AddWithValue(parameter.Key.StartsWith("@", StringComparison.Ordinal) ? parameter.Key : "@" + parameter.Key, Resolve(parameter.Value, request.Context) ?? DBNull.Value);
                _ = sqlParameter;
            }
            await using var reader = await command.ExecuteReaderAsync(request.CancellationToken);
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(request.CancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            result.ResponseText = JsonSerializer.Serialize(rows);
            result.Outputs["rows"] = rows;
            if (rows.Count > 0) foreach (var mapping in options.Outputs)
            {
                var value = rows[0].TryGetValue(mapping.Value, out var direct) ? direct : null;
                result.Outputs[mapping.Key] = value;
            }
            result.Succeeded = true;
        }
        catch (Exception ex) { result.ErrorMessage = ex.Message; }
        result.DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return result;
    }

    private static object? Resolve(object? value, LoadTestingTool.Domain.ExecutionContext context) => value is string text ? context.Resolve(text) : value;
}

public sealed class CSharpScriptStepExecutor : IStepExecutor
{
    private const int ScriptTimeoutSeconds = 30;
    public StepType StepType => StepType.Script;

    public async Task<StepResult> ExecuteAsync(StepExecutionRequest request)
    {
        var started = Stopwatch.GetTimestamp();
        var result = new StepResult { StepType = StepType, StepName = request.Step.StepName };
        try
        {
            var options = StepConfig.Read<ScriptStepOptions>(request.Step);
            var code = string.IsNullOrWhiteSpace(request.TemplateText) ? options.Inline : request.TemplateText;
            if (!string.IsNullOrWhiteSpace(options.Source)) code = await File.ReadAllTextAsync(WorkspaceGuard.Resolve(request.WorkspaceRoot, options.Source), request.CancellationToken);
            if (string.IsNullOrWhiteSpace(code)) throw new InvalidDataException("Script requires Inline or Source.");
            result.RequestText = code;
            var scriptCode = ExpandRuntimePlaceholders(code);
            var runtime = new ScriptRuntime(request.Context, null);
            request.Context.TryGet("data", out var previousData);
            request.Context.Set("data1", previousData, VariableScope.Iteration);
            var globals = new ScriptGlobals { Runtime = runtime, _data_ = previousData, _data1_ = previousData };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(ScriptTimeoutSeconds));
            var scriptOptions = ScriptMetadataReferences.CreateOptions(typeof(ScriptRuntime).Assembly)
                .AddImports("System", "System.Linq", "System.Collections.Generic", "LoadTestingTool.Domain");
            var value = await CSharpScript.EvaluateAsync<object?>(scriptCode, scriptOptions, globals, typeof(ScriptGlobals), timeout.Token);
            result.Outputs["result"] = value;
            result.ResponseText = value is null ? string.Empty : JsonSerializer.Serialize(value);
            result.Succeeded = true;
        }
        catch (Exception ex) { result.ErrorMessage = ex.Message; }
        result.DurationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return result;
    }

    private static string ExpandRuntimePlaceholders(string code)
    {
        var output = new System.Text.StringBuilder(code.Length + 32);
        var i = 0;
        var lineComment = false;
        var blockComment = false;
        var stringLiteral = false;
        var charLiteral = false;
        var verbatimString = false;
        while (i < code.Length)
        {
            var c = code[i];
            if (lineComment)
            {
                output.Append(c); i++;
                if (c == '\n') lineComment = false;
                continue;
            }
            if (blockComment)
            {
                output.Append(c); i++;
                if (c == '*' && i < code.Length && code[i] == '/') { output.Append('/'); i++; blockComment = false; }
                continue;
            }
            if (stringLiteral)
            {
                output.Append(c); i++;
                if (verbatimString)
                {
                    if (c == '"' && i < code.Length && code[i] == '"') { output.Append('"'); i++; }
                    else if (c == '"') stringLiteral = false;
                }
                else if (c == '\\' && i < code.Length) { output.Append(code[i]); i++; }
                else if (c == '"') stringLiteral = false;
                continue;
            }
            if (charLiteral)
            {
                output.Append(c); i++;
                if (c == '\\' && i < code.Length) { output.Append(code[i]); i++; }
                else if (c == '\'') charLiteral = false;
                continue;
            }
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/') { output.Append("//"); i += 2; lineComment = true; continue; }
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*') { output.Append("/*"); i += 2; blockComment = true; continue; }
            if (c == '"') { output.Append(c); i++; stringLiteral = true; verbatimString = i >= 2 && code[i - 2] == '@'; continue; }
            if (c == '\'') { output.Append(c); i++; charLiteral = true; continue; }
            if (c == '_')
            {
                var end = code.IndexOf('_', i + 1);
                if (end > i + 1)
                {
                    var name = code[(i + 1)..end];
                    if (name.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-'))
                    {
                        output.Append("Runtime.Get(\"").Append(name).Append("\")"); i = end + 1; continue;
                    }
                }
            }
            output.Append(c); i++;
        }
        return output.ToString();
    }
}
