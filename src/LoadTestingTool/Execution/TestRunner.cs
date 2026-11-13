using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Security.Cryptography;
using LoadTestingTool.Domain;
using LoadTestingTool.Reporting;

namespace LoadTestingTool.Execution;

internal sealed class RuntimeReferenceException : Exception
{
    public RuntimeReferenceException(string key, string reference)
        : base($"Missing runtime response value '{key}' for From_ reference '{reference}'.")
    {
        Key = key;
        Reference = reference;
    }

    public string Key { get; }
    public string Reference { get; }
}

public sealed class TestRunner
{
    private readonly HttpClient _client; private readonly AssertionEngine _assertions; private readonly string _templatesFolder; private readonly int _timeoutSeconds; private readonly Action<RequestResult>? _requestCompleted; private readonly Action<RequestResult, string, int>? _manualPending; private readonly string _manualControlFolder; private readonly bool _allowConsoleManual; private readonly InternalLogger _logger; private readonly FeatureConfig _features; private readonly ExecutionPipeline _pipeline; private int _sequence;
    public TestRunner(HttpClient client, AssertionEngine assertions, string templatesFolder, int timeoutSeconds, Action<RequestResult>? requestCompleted = null, InternalLogger? logger = null, FeatureConfig? features = null, Action<RequestResult, string, int>? manualPending = null, string? manualControlFolder = null, bool allowConsoleManual = false)
    {
        _client = client; _assertions = assertions; _templatesFolder = templatesFolder; _timeoutSeconds = Math.Max(1, timeoutSeconds); _requestCompleted = requestCompleted; _manualPending = manualPending; _manualControlFolder = manualControlFolder ?? string.Empty; _allowConsoleManual = allowConsoleManual; _logger = logger ?? new InternalLogger(string.Empty, "disabled", "disabled", false); _features = features ?? new FeatureConfig { WorkspaceRoot = Directory.GetCurrentDirectory() };
        _pipeline = new ExecutionPipeline(new StepExecutorRegistry([new GraphQlStepExecutor(_client), new GraphQlStepExecutor(_client, flattenCaseQl: true), new FileStepExecutor(), new SqlStepExecutor(_features), new CSharpScriptStepExecutor()]));
    }

    public async Task RunAsync(TestcaseDefinition testcase, WorkbookModel workbook, RunResult run, CancellationToken cancellationToken, string environment = "", string executionMode = "parallel", IReadOnlySet<string>? selectedDataIds = null, int threads = 1, int iterations = 1, int threadIntervalMs = 0, int iterationIntervalMs = 0, int maxConcurrentRows = 100, Action<TestcaseResult>? rowCompleted = null)
    {
        var dataRows = workbook.RequestData.Values.Where(r => r.TestcaseIndex == testcase.TestcaseIndex && (selectedDataIds is null || selectedDataIds.Contains(r.DataId))).OrderBy(r => r.DataId, StringComparer.OrdinalIgnoreCase).ToList();
        _logger.Section($"TESTCASE {testcase.TestcaseIndex}: {testcase.Testcase} - START");
        _logger.Info($"Testcase summary | dataRows={dataRows.Count} | steps={testcase.Steps.Count} | environment={environment}");
        var sequential = executionMode.Equals("loop", StringComparison.OrdinalIgnoreCase) || executionMode.Equals("sequential", StringComparison.OrdinalIgnoreCase);
        var workerCount = sequential ? 1 : Math.Clamp(maxConcurrentRows, 1, 100);
        var iterationCount = Math.Max(1, iterations);
        var workerInterval = Math.Max(0, threadIntervalMs);
        var iterationInterval = Math.Max(0, iterationIntervalMs);
        _logger.Info($"Execution mode: {(sequential ? "sequential" : "parallel rows")}; maxConcurrentRows={workerCount}; configuredThreads={Math.Max(1, threads)}; iterations={iterationCount}; threadIntervalMs={workerInterval}; iterationIntervalMs={iterationInterval}");
        var nextWorker = 0;
        var executionUnits = dataRows.SelectMany(data => Enumerable.Range(1, iterationCount).Select(iteration => (data, iteration))).ToList();
        await Parallel.ForEachAsync(executionUnits, new ParallelOptions { MaxDegreeOfParallelism = workerCount, CancellationToken = cancellationToken }, async (unit, token) =>
        {
                var data = unit.data;
                var iteration = unit.iteration;
                var threadId = (Interlocked.Increment(ref nextWorker) - 1) % workerCount + 1;
                if (threadId > 1 && workerInterval > 0) await Task.Delay((threadId - 1) * workerInterval, token);
                _logger.Section($"DATA ROW {data.DataId} | thread={threadId} | iteration={iteration}");
                var testcaseResult = new TestcaseResult { RunId = run.RunId, TestcaseIndex = testcase.TestcaseIndex, ExcelRowNumber = data.ExcelRowNumber, TestcaseRowNumber = data.TestcaseRowNumber, Testcase = testcase.Testcase, DataId = data.DataId, Thread = threadId, Iteration = iteration, Environment = environment, StartedAt = DateTimeOffset.Now };
                var correlation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var correlationSources = new Dictionary<string, CorrelationRecord>(StringComparer.OrdinalIgnoreCase); var requestReferences = data.Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase); var stepOrder = 0;
                var executionContext = new LoadTestingTool.Domain.ExecutionContext();
                foreach (var request in testcase.Steps)
                {
                    _logger.Info($"---- STEP {stepOrder + 1}: {request.StepName} | type={request.StepType.WireName()} | dataid={data.DataId} | iteration={iteration} ----");
                    if (data.Dontrun.Contains(request.StepName, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.Info($"STEP SKIPPED | reason=request.dontrun | step={request.StepName}");
                        continue;
                    }
                    if (!request.Enabled) { _logger.Info($"STEP SKIPPED | reason=disabled | step={request.StepName}"); continue; }
                    if (request.WaitMs > 0) { _logger.Info($"Waiting {request.WaitMs} ms before dataid={data.DataId}, step {request.StepName}."); await Task.Delay(request.WaitMs, token); }
                    Dictionary<string, string> variables;
                    IReadOnlySet<string> redactedVariables;
                    try
                    {
                        variables = StepVariables(data, request.StepName);
                        redactedVariables = StepRedactedVariables(data, request.StepName);
                        if (!string.IsNullOrWhiteSpace(environment)) variables["env"] = environment;
                        foreach (var pair in correlation)
                        {
                            // A binary File step publishes a typed reference into the execution
                            // context. Do not replace it with the serialized response/correlation
                            // string before a later HTTP step consumes _variable_, _*variable*_,
                            // or BodyFileVariable.
                            if (executionContext.TryGet(pair.Key, out var existing) && existing is not string) continue;
                            variables[pair.Key] = pair.Value;
                        }
                        foreach (var variable in variables) executionContext.Set(variable.Key, variable.Value, VariableScope.Iteration);
                        foreach (var key in variables.Keys.ToList()) variables[key] = await ResolveDynamicValueAsync(variables[key], correlation, requestReferences, executionContext, token);
                    }
                    catch (RuntimeReferenceException ex)
                    {
                        var failure = CreateRuntimeReferenceFailure(run, testcase, data.DataId, request, ++stepOrder, threadId, iteration, environment, ex);
                        testcaseResult.Requests.Add(failure);
                        _requestCompleted?.Invoke(failure);
                        _logger.Warn($"Stopping current data row after missing runtime reference. testcase={testcase.TestcaseIndex}; dataid={data.DataId}; stepName={request.StepName}; iteration={iteration}; reference={ex.Reference}");
                        break;
                    }
                    foreach (var variable in variables) executionContext.Set(variable.Key, variable.Value, VariableScope.Iteration);
                    foreach (var source in correlationSources.Values) if (string.IsNullOrWhiteSpace(source.UsedByStepName) || !source.UsedByStepName.Split(',', StringSplitOptions.TrimEntries).Contains(request.StepName, StringComparer.OrdinalIgnoreCase)) source.UsedByStepName = string.IsNullOrWhiteSpace(source.UsedByStepName) ? request.StepName : $"{source.UsedByStepName},{request.StepName}";
                    var definitions = workbook.Assertions.GetValueOrDefault((request.TestcaseIndex, data.DataId, request.StepName)) ?? [];
                    var result = request.StepType == StepType.ActionManual
                        ? await ExecuteActionManualAsync(run, testcase, data.DataId, request, variables, ++stepOrder, threadId, iteration, token, environment)
                        : await ExecuteRequestAsync(run, testcase, data.DataId, request, variables, redactedVariables, definitions, requestReferences, ++stepOrder, threadId, iteration, correlation, correlationSources, executionContext, token, environment);
                    testcaseResult.Requests.Add(result);
                    if (result.FailureCategory.Equals("RuntimeReference", StringComparison.OrdinalIgnoreCase)) { _logger.Warn($"Stopping current data row after missing runtime reference. dataid={data.DataId}; step={request.StepName}; iteration={iteration}; remaining rows will continue."); break; }
                    if (result.Result == "FAIL" && (request.StopOnFailure || request.StepType == StepType.ActionManual)) { _logger.Warn($"Stopping current data row after failed dataid={data.DataId}, step {request.StepName} because {(request.StepType == StepType.ActionManual ? "ActionManual always stops immediately" : "stopOnFailure=true")}."); break; }
                }
                testcaseResult.EndedAt = DateTimeOffset.Now;
                lock (run.Testcases) run.Testcases.Add(testcaseResult);
                rowCompleted?.Invoke(testcaseResult);
                if (sequential && iteration < iterationCount && iterationInterval > 0) await Task.Delay(iterationInterval, token);
        });
        _logger.Section($"TESTCASE {testcase.TestcaseIndex}: {testcase.Testcase} - END");
    }

    private async Task<RequestResult> ExecuteActionManualAsync(RunResult run, TestcaseDefinition testcase, string dataId, RequestStep step, Dictionary<string, string> variables, int sequence, int thread, int iteration, CancellationToken cancellationToken, string environment)
    {
        var started = DateTimeOffset.Now;
        var environmentKey = string.IsNullOrWhiteSpace(environment) ? "default" : environment;
        var request = new RequestResult { RunId = run.RunId, RequestId = $"MANUAL-{SafeManualId(environmentKey)}-{Guid.NewGuid():N}", TestcaseIndex = step.TestcaseIndex, Testcase = testcase.Testcase, DataId = dataId, StepName = step.StepName, StepType = step.StepType.WireName(), Thread = thread, Iteration = iteration, SequenceOrder = sequence, StartedAt = started, EndedAt = started, Verb = step.StepType.WireName(), StepConfig = step.StepConfig, HttpStatus = 200, Environment = environment };
        request.ManualInfo = variables.TryGetValue("manualinfo", out var info) ? Substitute(info, variables) : string.Empty;
        var timeoutMinutes = step.ManualTimeoutMinutes > 0 ? step.ManualTimeoutMinutes : Math.Max(1, step.TimeoutSeconds / 60);
        _logger.Info($"ACTION MANUAL PENDING [{request.RequestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; environment={environment}; timeoutMinutes={timeoutMinutes}; manualInfo={request.ManualInfo}");
        _manualPending?.Invoke(request, request.ManualInfo, timeoutMinutes);
        if (_allowConsoleManual && !Console.IsInputRedirected)
        {
            Console.WriteLine($"\nACTION MANUAL REQUIRED\nEnvironment: {environment}\nTestcase: {testcase.Testcase}\nRow: {dataId}\nStep: {step.StepName}\n\n{request.ManualInfo}\n\nEnter P for Pass or F for Fail:");
            var answer = Console.ReadLine();
            request.ManualComment = "Console response";
            request.HttpStatus = answer?.Trim().Equals("P", StringComparison.OrdinalIgnoreCase) == true ? 200 : 500;
        }
        else
        {
            var deadline = DateTimeOffset.Now.AddMinutes(timeoutMinutes);
            var responded = false;
            while (!cancellationToken.IsCancellationRequested && DateTimeOffset.Now < deadline)
            {
                if (!string.IsNullOrWhiteSpace(_manualControlFolder) && ManualActionControl.TryReadResponse(_manualControlFolder, request.RequestId, out var response))
                {
                    request.HttpStatus = response.Result.Equals("pass", StringComparison.OrdinalIgnoreCase) ? 200 : 500;
                    request.ManualComment = response.Comment;
                    responded = true;
                    break;
                }
                await Task.Delay(250, cancellationToken);
            }
            if (!responded && DateTimeOffset.Now >= deadline)
            {
                request.HttpStatus = 500;
                request.FailureCategory = "ActionManual";
                request.ErrorMessage = $"ActionManual timed out after {timeoutMinutes} minute(s).";
            }
            else if (request.HttpStatus == 500 && string.IsNullOrWhiteSpace(request.ErrorMessage)) { request.FailureCategory = "ActionManual"; request.ErrorMessage = "ActionManual failed by operator."; }
        }
        request.EndedAt = DateTimeOffset.Now;
        request.DurationMs = (long)(request.EndedAt - request.StartedAt).TotalMilliseconds;
        _logger.Info($"ACTION MANUAL COMPLETED [{request.RequestId}] result={request.Result}; comment={request.ManualComment}; durationMs={request.DurationMs}{OptionalError(request.ErrorMessage)}");
        _requestCompleted?.Invoke(request);
        return request;
    }

    private static Dictionary<string, string> StepVariables(RequestDataRow data, string stepName)
    {
        var prefix = stepName + ".";
        var variables = new Dictionary<string, string>(data.Variables.Where(x => !x.Key.Contains('.', StringComparison.Ordinal)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var variable in data.Variables.Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            variables[variable.Key[prefix.Length..]] = variable.Value;
        return variables;
    }
    private static IReadOnlySet<string> StepRedactedVariables(RequestDataRow data, string stepName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix = stepName + ".";
        foreach (var name in data.RedactedVariables)
        {
            names.Add(name);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) names.Add(name[prefix.Length..]);
        }
        return names;
    }

    private RequestResult CreateRuntimeReferenceFailure(RunResult run, TestcaseDefinition testcase, string dataId, RequestStep step, int sequence, int thread, int iteration, string environment, RuntimeReferenceException exception)
    {
        var now = DateTimeOffset.Now;
        var requestId = $"REF-{Interlocked.Increment(ref _sequence):D8}";
        var message = exception.Message;
        _logger.Error($"RUNTIME REFERENCE FAILURE [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; thread={thread}; iteration={iteration}; reference={exception.Reference}; key={exception.Key}", exception);
        return new RequestResult
        {
            RunId = run.RunId,
            RequestId = requestId,
            TestcaseIndex = step.TestcaseIndex,
            Testcase = testcase.Testcase,
            DataId = dataId,
            StepName = step.StepName,
            StepType = step.StepType.WireName(),
            Thread = thread,
            Iteration = iteration,
            SequenceOrder = sequence,
            StartedAt = now,
            EndedAt = now,
            Verb = step.StepType.WireName(),
            TemplateSource = step.TemplateSource,
            StepConfig = step.StepConfig,
            HttpStatus = 0,
            ErrorMessage = message,
            FailureCategory = "RuntimeReference",
            RuntimeReference = exception.Reference,
            RuntimeReferenceKey = exception.Key,
            Environment = environment
        };
    }

    private async Task<RequestResult> ExecuteRequestAsync(RunResult run, TestcaseDefinition testcase, string dataId, RequestStep step, Dictionary<string, string> variables, IReadOnlySet<string> redactedVariables, IReadOnlyList<AssertionDefinition> definitions, IReadOnlyDictionary<string, string> requestReferences, int sequence, int thread, int iteration, Dictionary<string, string> correlation, Dictionary<string, CorrelationRecord> correlationSources, LoadTestingTool.Domain.ExecutionContext executionContext, CancellationToken cancellationToken, string environment)
    {
        if (step.StepType != StepType.Http) return await ExecuteNonHttpAsync(run, testcase, dataId, step, variables, redactedVariables, definitions, requestReferences, sequence, thread, iteration, correlation, correlationSources, executionContext, cancellationToken, environment);
        var started = DateTimeOffset.Now; var sw = Stopwatch.StartNew(); var requestId = $"REQ-{Interlocked.Increment(ref _sequence):D8}"; var templatePath = Path.Combine(_templatesFolder, step.TemplateSource);
        _logger.Info($"Request starting. RequestId={requestId}; testcase={testcase.TestcaseIndex}; dataid={dataId}; stepName={step.StepName}; thread={thread}; iteration={iteration}; verb={step.Verb}; url={step.TargetUrl}; template={templatePath}; ignoreempty={step.IgnoreEmpty}");
        var result = new RequestResult { RunId = run.RunId, RequestId = requestId, TestcaseIndex = step.TestcaseIndex, Testcase = testcase.Testcase, DataId = dataId, StepName = step.StepName, StepType = step.StepType.WireName(), Thread = thread, Iteration = iteration, SequenceOrder = sequence, StartedAt = started, WaitMs = step.WaitMs, Verb = step.Verb, TargetUrl = step.TargetUrl, TemplateSource = step.TemplateSource, StepConfig = step.StepConfig, ContentType = ResolveContentType(step), RequestHeaders = string.Join("; ", step.Headers.Select(h => $"{h.Key}={h.Value}")), Variables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase), RedactedVariables = redactedVariables, Environment = environment };
        try
        {
            foreach (var runtimeVariable in executionContext.Snapshot())
                if (runtimeVariable.Value is string runtimeText)
                    variables[runtimeVariable.Key] = runtimeText;
            var resolvedUrl = await ResolveDynamicValueAsync(step.TargetUrl.Replace("{env}", environment, StringComparison.OrdinalIgnoreCase), correlation, requestReferences, executionContext, cancellationToken);
            result.TargetUrl = Substitute(resolvedUrl, variables); var originalTemplate = string.IsNullOrWhiteSpace(step.TemplateSource) ? string.Empty : await File.ReadAllTextAsync(templatePath, cancellationToken); var resolvedTemplate = string.IsNullOrWhiteSpace(originalTemplate) ? string.Empty : await ResolveDynamicValueAsync(originalTemplate, correlation, requestReferences, executionContext, cancellationToken);
            var streamedTemplate = PrepareStreamingBinaryTemplate(resolvedTemplate, result.ContentType, variables, executionContext);
            var body = streamedTemplate is null ? TemplateRenderer.Render(resolvedTemplate, result.ContentType, variables) : string.Empty;
            var resolvedTemplateForLog = streamedTemplate is null ? body : resolvedTemplate;
            _logger.Info($"STEP RESOLUTION [{requestId}]\nORIGINAL URL: {step.TargetUrl}\nRESOLVED URL: {result.TargetUrl}\nORIGINAL TEMPLATE:\n{originalTemplate}\nRESOLVED TEMPLATE:\n{InternalLogger.RedactVariableValues(resolvedTemplateForLog, redactedVariables, variables)}\nVARIABLES:\n{FormatVariables(variables, redactedVariables)}");
            using var request = new HttpRequestMessage(new HttpMethod(step.Verb), result.TargetUrl);
            var binaryBody = ResolveBinaryHttpBody(step, executionContext);
            if (result.ContentType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                var multipart = CreateMultipartContent(step, _features.WorkspaceRoot, variables, executionContext);
                request.Content = multipart.Content;
                result.RequestBody = multipart.Summary;
            }
            else if (streamedTemplate is not null)
            {
                request.Content = new StreamingBase64FileJsonContent(streamedTemplate.Value.Prefix, streamedTemplate.Value.Suffix, streamedTemplate.Value.File, ResolveHttpMediaType(result.ContentType));
                result.RequestBody = streamedTemplate.Value.Summary;
            }
            else if (binaryBody is not null)
            {
                request.Content = new StreamContent(File.OpenRead(binaryBody.Path));
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(binaryBody.ContentType);
                result.RequestBody = $"[binary file body omitted: {binaryBody.SizeBytes} bytes, sha256={binaryBody.Sha256}]";
            }
            else
            {
                result.RequestBody = InternalLogger.RedactVariableValues(body, redactedVariables, variables);
                if (!step.Verb.Equals("HEAD", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(body)) request.Content = new StringContent(body, Encoding.UTF8, result.ContentType.Equals("xml", StringComparison.OrdinalIgnoreCase) ? "application/xml" : "application/json");
            }
            var resolvedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in step.Headers)
            {
                var value = await ResolveDynamicValueAsync(header.Value, correlation, requestReferences, executionContext, cancellationToken);
                resolvedHeaders[header.Key] = Substitute(value, variables);
            }
            result.RequestHeaders = InternalLogger.RedactVariableValues(string.Join("; ", resolvedHeaders.Select(h => $"{h.Key}={h.Value}")), redactedVariables, variables); foreach (var h in resolvedHeaders) if (!h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && !request.Headers.TryAddWithoutValidation(h.Key, h.Value)) request.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value);
            _logger.Info($"REQUEST SENT [{requestId}]\nURL: {result.TargetUrl}\nMETHOD: {step.Verb}\nHEADERS:\n{result.RequestHeaders}\nBODY:\n{result.RequestBody}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(step.TimeoutSeconds > 0 ? step.TimeoutSeconds : _timeoutSeconds)); using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            result.HttpStatus = (int)response.StatusCode; result.ResponseHeaders = string.Join("; ", response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}={string.Join(",", h.Value)}")); result.ResponseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            PublishResponseValues(step.StepName, result.ResponseBody, result.ContentType, correlation);
            _logger.Info($"RESPONSE RECEIVED [{requestId}]\nSTATUS: {result.HttpStatus}\nHEADERS:\n{result.ResponseHeaders}\nBODY:\n{result.ResponseBody}");
            if (step.AssertEnabled)
            {
                var resolvedDefinitions = await ResolveAssertionReferencesAsync(definitions, correlation, requestReferences, executionContext, cancellationToken);
                result.Assertions.AddRange(_assertions.Evaluate(result.ResponseBody, result.ContentType, resolvedDefinitions, run.RunId, requestId, result.HttpStatus, step.IgnoreEmpty, step.AssertOnlyResponse));
            }
            var failedAssertions = result.Assertions.Where(a => a.Result == "FAIL").ToList();
            if (failedAssertions.Count > 0) result.FailureCategory = FailureCategories.Assertion;
            _logger.Info($"ASSERTIONS COMPLETED [{requestId}]\nPASSED: {result.Assertions.Count(a => a.Result == "PASS")}\nFAILED: {failedAssertions.Count}");
            foreach (var assertion in failedAssertions)
                _logger.Warn($"ASSERTION FAILED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; responsePath={assertion.ResponsePath}; operator={assertion.AssertionVerb}; expected={assertion.ExpectedValue}; actual={assertion.ActualValue}; message={assertion.FailureMessage}");
            foreach (var assertion in result.Assertions.Where(a => a.Result == "PASS" && !string.IsNullOrWhiteSpace(a.ExtractVariable))) { var record = new CorrelationRecord { DataId = dataId, SourceStepName = step.StepName, ResponsePath = assertion.ResponsePath, Variable = assertion.ExtractVariable!, ExtractedValue = assertion.ActualValue }; result.Correlations.Add(record); correlation[record.Variable] = record.ExtractedValue; correlationSources[record.Variable] = record; }
            var hasExpectedStatus = !string.IsNullOrWhiteSpace(step.ExpectedStatus);
            var expectedStatusMatches = hasExpectedStatus && StatusMatches(result.HttpStatus, step.ExpectedStatus);
            if ((hasExpectedStatus && !expectedStatusMatches) || (!hasExpectedStatus && !response.IsSuccessStatusCode))
            {
                result.ErrorMessage = hasExpectedStatus
                    ? $"Expected status '{step.ExpectedStatus}' but received '{result.HttpStatus}'."
                    : $"Unexpected HTTP status {(int)response.StatusCode} {response.StatusCode}.";
                result.FailureCategory = FailureCategories.HttpStatus;
                _logger.Error($"HTTP STATUS FAILURE [{requestId}] | testcase={testcase.TestcaseIndex} | dataid={dataId} | step={step.StepName} | actual={result.HttpStatus} | expected={(hasExpectedStatus ? step.ExpectedStatus : "2xx/3xx")}");
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = FormatException("HTTP request preparation/execution failed", ex);
            if (ex is RuntimeReferenceException referenceException)
            {
                result.FailureCategory = FailureCategories.RuntimeReference;
                result.RuntimeReference = referenceException.Reference;
                result.RuntimeReferenceKey = referenceException.Key;
            }
            else result.FailureCategory = ClassifyException(ex, FailureCategories.Network, cancellationToken);
            result.HttpStatus = 0;
            _logger.Error($"REQUEST FAILED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; template={templatePath}; templateExists={File.Exists(templatePath)}; url={result.TargetUrl}; verb={step.Verb}; contentType={result.ContentType}; requestBody={result.RequestBody}; requestHeaders={result.RequestHeaders}", ex);
        }
        finally
        {
            sw.Stop(); result.DurationMs = sw.ElapsedMilliseconds; result.EndedAt = DateTimeOffset.Now;
            _logger.Info($"REQUEST COMPLETED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; result={result.Result}; status={result.HttpStatus}; durationMs={result.DurationMs}{OptionalError(result.ErrorMessage)}");
        }
        _requestCompleted?.Invoke(result); return result;
    }

    private async Task<RequestResult> ExecuteNonHttpAsync(RunResult run, TestcaseDefinition testcase, string dataId, RequestStep step, Dictionary<string, string> variables, IReadOnlySet<string> redactedVariables, IReadOnlyList<AssertionDefinition> definitions, IReadOnlyDictionary<string, string> requestReferences, int sequence, int thread, int iteration, Dictionary<string, string> correlation, Dictionary<string, CorrelationRecord> correlationSources, LoadTestingTool.Domain.ExecutionContext context, CancellationToken cancellationToken, string environment)
    {
        var started = DateTimeOffset.Now; var requestId = $"STEP-{Interlocked.Increment(ref _sequence):D8}";
        var templatePath = string.IsNullOrWhiteSpace(step.TemplateSource) ? string.Empty : Path.Combine(_templatesFolder, step.TemplateSource);
        var result = new RequestResult { RunId = run.RunId, RequestId = requestId, TestcaseIndex = step.TestcaseIndex, Testcase = testcase.Testcase, DataId = dataId, StepName = step.StepName, StepType = step.StepType.WireName(), Thread = thread, Iteration = iteration, SequenceOrder = sequence, StartedAt = started, WaitMs = step.WaitMs, Verb = step.StepType.WireName(), TargetUrl = step.TargetUrl, TemplateSource = step.TemplateSource, StepConfig = step.StepConfig, ContentType = step.ContentType, Variables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase), RedactedVariables = redactedVariables, Environment = environment };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(step.TimeoutSeconds > 0 ? step.TimeoutSeconds : _timeoutSeconds));
            var templateText = string.Empty;
            var originalTemplate = string.Empty;
            result.TargetUrl = context.ResolveUrl(step.TargetUrl);
            _logger.Info($"STEP TEMPLATE LOAD [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; environment={environment}; template={templatePath}; templateExists={(!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))}; configuredTargetUrl={step.TargetUrl}; resolvedTargetUrl={result.TargetUrl}; ignoreempty={step.IgnoreEmpty}; stepConfig={step.StepConfig}");
            foreach (var runtimeVariable in context.Snapshot())
                if (runtimeVariable.Value is string runtimeText)
                    variables[runtimeVariable.Key] = runtimeText;
            if (!string.IsNullOrWhiteSpace(step.TemplateSource))
            {
                originalTemplate = await File.ReadAllTextAsync(templatePath, timeout.Token);
                templateText = await ResolveDynamicValueAsync(originalTemplate, correlation, requestReferences, context, timeout.Token);
                templateText = TemplateRenderer.Render(templateText, result.ContentType, variables);
            }
            _logger.Info($"STEP RESOLUTION [{requestId}]\nSTEP TYPE: {step.StepType.WireName()}\nORIGINAL TARGET: {step.TargetUrl}\nRESOLVED TARGET: {result.TargetUrl}\nORIGINAL TEMPLATE:\n{originalTemplate}\nRESOLVED TEMPLATE:\n{InternalLogger.RedactVariableValues(templateText, redactedVariables, variables)}\nVARIABLES:\n{FormatVariables(variables, redactedVariables)}");
            _logger.Info($"STEP REQUEST [{requestId}]\nTYPE: {step.StepType.WireName()}\nSTEP: {step.StepName}\nENVIRONMENT: {environment}\nCONFIGURED URL: {step.TargetUrl}\nRESOLVED URL: {result.TargetUrl}\nTEMPLATE: {step.TemplateSource}\nREQUEST:\n{InternalLogger.RedactVariableValues(templateText, redactedVariables, variables)}\nMETADATA:\n{step.StepConfig}");
            var execution = await _pipeline.ExecuteAsync(new StepExecutionRequest { Step = step, Context = context, WorkspaceRoot = _features.WorkspaceRoot, TemplateText = templateText, CancellationToken = timeout.Token });
            StepExecutionResultMapper.PublishOutputs(execution, context);
            result.RequestBody = InternalLogger.RedactVariableValues(execution.RequestText, redactedVariables, variables); result.ResponseBody = execution.ResponseText; result.HttpStatus = execution.StatusCode == 0 && execution.Succeeded ? 200 : execution.StatusCode; result.ErrorMessage = execution.ErrorMessage;
            PublishResponseValues(step.StepName, result.ResponseBody, "json", correlation);
            _logger.Info($"STEP EXECUTED INPUT [{requestId}]\nTYPE: {step.StepType.WireName()}\nPAYLOAD:\n{result.RequestBody}\nSTEP RESPONSE [{requestId}]\nSTATUS: {result.HttpStatus}\nREQUEST PAYLOAD:\n{result.RequestBody}\nRESPONSE PAYLOAD:\n{execution.ResponseText}{OptionalErrorLine(execution.ErrorMessage)}");
            if (!execution.Succeeded || !string.IsNullOrWhiteSpace(execution.ErrorMessage))
                _logger.Error($"STEP EXECUTION FAILURE [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; template={templatePath}; templateExists={(!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))}; targetUrl={step.TargetUrl}; status={result.HttpStatus}; error={execution.ErrorMessage}");
            foreach (var output in execution.Outputs) { var text = output.Value is string s ? s : JsonSerializer.Serialize(output.Value); var record = new CorrelationRecord { DataId = dataId, SourceStepName = step.StepName, ResponsePath = output.Key, Variable = output.Key, ExtractedValue = text }; correlation[output.Key] = text; correlationSources[output.Key] = record; result.Correlations.Add(record); }
            if (step.AssertEnabled)
            {
                var resolvedDefinitions = await ResolveAssertionReferencesAsync(definitions, correlation, requestReferences, context, cancellationToken);
                result.Assertions.AddRange(_assertions.Evaluate(result.ResponseBody, "json", resolvedDefinitions, run.RunId, requestId, result.HttpStatus, step.IgnoreEmpty, step.AssertOnlyResponse));
            }
            foreach (var assertion in result.Assertions.Where(a => a.Result == "FAIL"))
            {
                result.FailureCategory = FailureCategories.Assertion;
                _logger.Warn($"ASSERTION FAILED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; responsePath={assertion.ResponsePath}; operator={assertion.AssertionVerb}; expected={assertion.ExpectedValue}; actual={assertion.ActualValue}; message={assertion.FailureMessage}");
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = FormatException("Step preparation/execution failed", ex);
            if (ex is RuntimeReferenceException referenceException)
            {
                result.FailureCategory = FailureCategories.RuntimeReference;
                result.RuntimeReference = referenceException.Reference;
                result.RuntimeReferenceKey = referenceException.Key;
            }
            else result.FailureCategory = ClassifyException(ex, step.StepType switch
            {
                StepType.Sql => FailureCategories.Sql,
                StepType.File => FailureCategories.FileSystem,
                StepType.Script => FailureCategories.Script,
                _ => FailureCategories.Infrastructure
            }, cancellationToken);
            _logger.Error($"STEP FAILED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; template={templatePath}; templateExists={(!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))}; targetUrl={step.TargetUrl}; verb={step.Verb}; contentType={step.ContentType}; stepConfig={step.StepConfig}; requestPayload={result.RequestBody}; responsePayload={result.ResponseBody}; status={result.HttpStatus}", ex);
        }
        finally
        {
            result.EndedAt = DateTimeOffset.Now; result.DurationMs = (long)(result.EndedAt - result.StartedAt).TotalMilliseconds;
            _logger.Info($"STEP COMPLETED [{requestId}] testcase={testcase.TestcaseIndex}; testcaseName={testcase.Testcase}; dataid={dataId}; stepName={step.StepName}; stepType={step.StepType.WireName()}; result={result.Result}; status={result.HttpStatus}; durationMs={result.DurationMs}{OptionalError(result.ErrorMessage)}");
        }
        _requestCompleted?.Invoke(result); return result;
    }
    private static string SafeManualId(string value)
        => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

    private static string OptionalError(string? error) => string.IsNullOrWhiteSpace(error) ? string.Empty : $"; error={error}";
    private static string OptionalErrorLine(string? error) => string.IsNullOrWhiteSpace(error) ? string.Empty : $"\nERROR: {error}";
    private static string FormatVariables(IReadOnlyDictionary<string, string> variables, IReadOnlySet<string> redactedVariables)
    {
        if (variables.Count == 0) return "(none)";
        return string.Join(Environment.NewLine, variables.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key}={InternalLogger.RedactVariableValues(x.Value, redactedVariables, variables)}"));
    }
    private static string FormatException(string context, Exception exception) => $"{context}: {exception.GetType().Name}: {exception.Message}";
    private static string ClassifyException(Exception exception, string fallback, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return FailureCategories.Cancelled;
        if (exception is TimeoutException or TaskCanceledException) return FailureCategories.Timeout;
        if (exception is HttpRequestException) return FailureCategories.Network;
        if (exception is JsonException) return FailureCategories.ResponseParse;
        if (exception is InvalidDataException) return FailureCategories.VariableResolution;
        return fallback;
    }
    private static string ResolveRuntimeReferences(string value, IReadOnlyDictionary<string, string> correlation, IReadOnlyDictionary<string, string>? requestReferences, LoadTestingTool.Domain.ExecutionContext context)
    {
        return Regex.Replace(value ?? string.Empty, @"<From_(?:(request|response)_)?([^_>]+)_(r|\d+)>", match =>
        {
            var source = match.Groups[1].Value;
            var key = match.Groups[2].Value;
            var contextKey = key;
            if (!context.TryGet(contextKey, out var typedValue) && key.Contains('.', StringComparison.Ordinal))
            {
                var lastPart = key[(key.LastIndexOf('.') + 1)..];
                if (context.TryGet(lastPart, out typedValue)) contextKey = key;
            }
            if (typedValue is BinaryFileReference)
            {
                // Keep the binary reference typed in the execution context and
                // convert only the internal template representation into the
                // existing streaming marker. The workbook author still writes
                // <From_response_Step.output_r>.
                context.Set(contextKey, typedValue, VariableScope.Iteration);
                return $"_*{contextKey}*_";
            }
            var values = source.Equals("request", StringComparison.OrdinalIgnoreCase) ? requestReferences : correlation;
            return values is not null && values.TryGetValue(key, out var actual)
                ? actual
                : throw new RuntimeReferenceException(key, match.Value);
        }, RegexOptions.IgnoreCase);
    }
    private async Task<string> ResolveDynamicValueAsync(string value, IReadOnlyDictionary<string, string> correlation, IReadOnlyDictionary<string, string>? requestReferences, LoadTestingTool.Domain.ExecutionContext context, CancellationToken cancellationToken)
    {
        var resolved = ResolveRuntimeReferences(value, correlation, requestReferences, context);
        return await InlineScriptRunner.ResolveTokensAsync(resolved, context, _features.WorkspaceRoot, _features, _logger, cancellationToken);
    }
    private async Task<IReadOnlyList<AssertionDefinition>> ResolveAssertionReferencesAsync(IReadOnlyList<AssertionDefinition> definitions, IReadOnlyDictionary<string, string> correlation, IReadOnlyDictionary<string, string>? requestReferences, LoadTestingTool.Domain.ExecutionContext context, CancellationToken cancellationToken)
    {
        var result = new List<AssertionDefinition>(definitions.Count);
        foreach (var definition in definitions)
        {
            result.Add(new AssertionDefinition
            {
                TestcaseIndex = definition.TestcaseIndex, Testcase = definition.Testcase, DataId = definition.DataId, StepName = definition.StepName,
                ResponsePath = definition.ResponsePath, AssertionVerb = definition.AssertionVerb, HeaderAssertionVerb = definition.HeaderAssertionVerb,
                ExpectedValue = await ResolveDynamicValueAsync(definition.ExpectedValue, correlation, requestReferences, context, cancellationToken),
                ExtractVariable = definition.ExtractVariable, ExcelRowNumber = definition.ExcelRowNumber, ExpectedValueColumn = definition.ExpectedValueColumn,
                ResponseColumn = definition.ResponseColumn
            });
        }
        return result;
    }
    private static void PublishResponseValues(string stepName, string body, string contentType, Dictionary<string, string> correlation)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        try
        {
            if (contentType.Equals("json", StringComparison.OrdinalIgnoreCase) || body.TrimStart().StartsWith("{"))
            {
                using var document = JsonDocument.Parse(body);
                PublishJsonRuntimeNodes(stepName, document.RootElement, string.Empty, correlation);
            }
            else
            {
                var values = new AssertionEngine().Normalize(body, contentType);
                foreach (var pair in values) correlation[$"{stepName}.{pair.Key}"] = pair.Value;
            }
        }
        catch (Exception) { /* Assertion evaluation records malformed payloads; preserve the original step result. */ }
    }

    private static void PublishJsonRuntimeNodes(string stepName, JsonElement element, string path, Dictionary<string, string> correlation)
    {
        if (!string.IsNullOrEmpty(path))
        {
            var value = element.ValueKind switch
            {
                JsonValueKind.Null => string.Empty,
                JsonValueKind.String => element.GetString() ?? string.Empty,
                _ => element.GetRawText()
            };
            correlation[$"{stepName}.{path}"] = value;
        }
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                PublishJsonRuntimeNodes(stepName, property.Value, string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}", correlation);
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) PublishJsonRuntimeNodes(stepName, item, $"{path}[{index++}]", correlation);
        }
    }
    private static string Substitute(string value, IReadOnlyDictionary<string, string> vars) => Regex.Replace(value ?? "", @"_([\p{L}\p{N}._-]+)_", m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : throw new InvalidDataException($"Missing request variable '{m.Groups[1].Value}'."));
    private static (string Prefix, string Suffix, BinaryFileReference File, string Summary)? PrepareStreamingBinaryTemplate(string template, string contentType, IReadOnlyDictionary<string, string> variables, LoadTestingTool.Domain.ExecutionContext context)
    {
        var source = template ?? string.Empty;
        var tokenPattern = @"_(?:\*(?<star>[\p{L}\p{N}._-]+)\*|(?<plain>[\p{L}\p{N}._-]+))_";
        Match? match = null;
        BinaryFileReference? file = null;
        foreach (Match candidate in Regex.Matches(source, tokenPattern, RegexOptions.CultureInvariant))
        {
            var candidateName = candidate.Groups["star"].Success ? candidate.Groups["star"].Value : candidate.Groups["plain"].Value;
            var explicitMarker = candidate.Groups["star"].Success;
            if (context.TryGet(candidateName, out var candidateValue) && candidateValue is BinaryFileReference candidateFile)
            {
                match = candidate;
                file = candidateFile;
                break;
            }
            // A normal underscore token remains an ordinary string token. The
            // explicit _*variable*_ form still fails fast when it is not binary.
            if (explicitMarker) throw new InvalidDataException($"Streaming template variable '{candidateName}' is missing or is not a binary file reference.");
        }
        if (match is null || file is null) return null;
        var variableName = match.Groups["star"].Success ? match.Groups["star"].Value : match.Groups["plain"].Value;
        if (!File.Exists(file.Path)) throw new FileNotFoundException($"Streaming template file for '{variableName}' was not found.", file.Path);
        var marker = match.Value;
        var protectedTemplate = source.Replace(marker, "STREAMEDBINARYMARKERTOKEN", StringComparison.Ordinal);
        protectedTemplate = TemplateRenderer.Render(protectedTemplate, contentType, variables);
        var markerIndex = protectedTemplate.IndexOf("STREAMEDBINARYMARKERTOKEN", StringComparison.Ordinal);
        if (markerIndex < 0) throw new InvalidDataException($"Streaming template marker for '{variableName}' was altered during template rendering.");
        var prefix = protectedTemplate[..markerIndex];
        var suffix = protectedTemplate[(markerIndex + "STREAMEDBINARYMARKERTOKEN".Length)..];
        var encodedLength = 4L * ((file.SizeBytes + 2L) / 3L);
        var summary = $"[NoLog streamed Base64 variable: {variableName}; bytes={file.SizeBytes}; base64Bytes={encodedLength}; sha256={file.Sha256}; contentType={file.ContentType}]";
        return (prefix, suffix, file, summary);
    }
    private static string ResolveHttpMediaType(string contentType) => contentType.Equals("json", StringComparison.OrdinalIgnoreCase) ? "application/json" : contentType.Equals("xml", StringComparison.OrdinalIgnoreCase) ? "application/xml" : contentType;
    private static BinaryFileReference? ResolveBinaryHttpBody(RequestStep step, LoadTestingTool.Domain.ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(step.StepConfig)) return null;
        using var config = StepConfig.Parse(step);
        var root = config.RootElement;
        var property = root.EnumerateObject().FirstOrDefault(p => p.NameEquals("BodyFileVariable") || p.NameEquals("BinaryBodyVariable") || p.NameEquals("bodyFileVariable") || p.NameEquals("binaryBodyVariable"));
        if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString())) return null;
        var variable = property.Value.GetString()!;
        if (!context.TryGet(variable, out var value) || value is not BinaryFileReference reference)
            throw new InvalidDataException($"HTTP binary body variable '{variable}' is missing or is not a binary file reference.");
        if (!File.Exists(reference.Path)) throw new FileNotFoundException("The binary body file reference no longer exists.", reference.Path);
        var contentType = root.EnumerateObject().FirstOrDefault(p => p.NameEquals("ContentType") || p.NameEquals("contentType"));
        return contentType.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(contentType.Value.GetString())
            ? new BinaryFileReference { Path = reference.Path, SizeBytes = reference.SizeBytes, Sha256 = reference.Sha256, ContentType = contentType.Value.GetString()! }
            : reference;
    }
    private static (MultipartFormDataContent Content, string Summary) CreateMultipartContent(RequestStep step, string workspaceRoot, IReadOnlyDictionary<string, string> variables, LoadTestingTool.Domain.ExecutionContext context)
    {
        var options = StepConfig.Read<MultipartStepOptions>(step);
        if (options.Parts.Count == 0) throw new InvalidDataException($"Multipart HTTP step '{step.StepName}' must define at least one part.");
        var content = new MultipartFormDataContent(); var files = new List<string>(); var names = new List<string>();
        foreach (var part in options.Parts)
        {
            var name = RenderMultipartValue(part.Name ?? string.Empty, variables, context).Trim();
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"Multipart step '{step.StepName}' contains a part without a name.");
            names.Add(name);
            if (part.Type.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                var relative = RenderMultipartValue(part.Path ?? string.Empty, variables, context);
                var path = WorkspaceGuard.Resolve(workspaceRoot, relative);
                if (!File.Exists(path)) throw new FileNotFoundException($"Multipart file part '{relative}' was not found.", path);
                var info = new FileInfo(path); using var hash = SHA256.Create(); using var stream = File.OpenRead(path); var digest = Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant();
                var fileContent = new StreamContent(File.OpenRead(path));
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(part.ContentType) ? "application/octet-stream" : part.ContentType);
                var fileName = string.IsNullOrWhiteSpace(part.FileName) ? info.Name : RenderMultipartValue(part.FileName, variables, context);
                content.Add(fileContent, name, fileName); files.Add($"{name}:{fileName}; bytes={info.Length}; sha256={digest}; contentType={fileContent.Headers.ContentType}");
            }
            else
            {
                var value = RenderMultipartValue(part.Value ?? string.Empty, variables, context);
                content.Add(new StringContent(value, Encoding.UTF8), name);
            }
        }
        var summary = $"[multipart body omitted; parts={string.Join(", ", names)}; files={(files.Count == 0 ? "none" : string.Join(" | ", files))}]";
        return (content, summary);
    }
    private static string RenderMultipartValue(string value, IReadOnlyDictionary<string, string> variables, LoadTestingTool.Domain.ExecutionContext context)
    {
        var resolved = context.Resolve(value ?? string.Empty);
        return TemplateRenderer.Render(resolved, "text/plain", variables);
    }
    private static string ResolveContentType(RequestStep step) => !string.IsNullOrWhiteSpace(step.ContentType) ? step.ContentType : Path.GetExtension(step.TemplateSource).TrimStart('.').ToLowerInvariant() switch { "xml" => "xml", _ => "json" };
    private static bool StatusMatches(int actual, string expected) => expected.Contains('-') ? int.TryParse(expected.Split('-')[0], out var min) && int.TryParse(expected.Split('-')[1], out var max) && actual >= min && actual <= max : expected.EndsWith("xx", StringComparison.OrdinalIgnoreCase) && int.TryParse(expected[..1], out var family) ? actual / 100 == family : int.TryParse(expected, out var exact) && actual == exact;
}
