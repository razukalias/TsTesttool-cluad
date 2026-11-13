using System.Net;
using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using LoadTestingTool.Domain;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using LoadTestingTool.Validation;
using Microsoft.Extensions.Configuration;

namespace LoadTestingTool;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        AppConfig? loadedConfig = null;
        string? loadedRunId = null;
        try
        {
            var settingsPath = GetCommandLineValue(args, "--config", "--settings");
            var instanceKey = GetCommandLineValue(args, "--instance");
            if (args.Any(x => x.Equals("--run-all", StringComparison.OrdinalIgnoreCase))) return await RunAllInstancesAsync(settingsPath);
            var config = LoadConfig(settingsPath, instanceKey);
            loadedConfig = config;
            var workbookPath = GetCommandLineValue(args, "--excel", "--workbook") ?? config.ExcelFilePath;
            if (string.IsNullOrWhiteSpace(workbookPath)) throw new InvalidOperationException("No Excel workbook was specified.");
            if (Path.GetFileName(workbookPath).StartsWith("~$", StringComparison.Ordinal)) throw new InvalidOperationException($"The selected workbook is an Excel temporary lock file: '{workbookPath}'. Close Excel or select the real workbook file.");
            config.TemplatesFolder = GetCommandLineValue(args, "--templates") ?? config.TemplatesFolder;
            config.HistoryFolder = GetCommandLineValue(args, "--history") ?? config.HistoryFolder;
            config.LogsFolder = GetCommandLineValue(args, "--logs") ?? config.LogsFolder;
            config.ResultsFolder = GetCommandLineValue(args, "--results") ?? config.ResultsFolder;
            if (bool.TryParse(GetCommandLineValue(args, "--generate-results"), out var generateResults)) config.GenerateResults = generateResults;
            if (bool.TryParse(GetCommandLineValue(args, "--internal-log"), out var internalLogging)) config.InternalLoggingEnabled = internalLogging;
            var parallelValue = GetCommandLineValue(args, "--environments-parallel") ?? GetCommandLineValue(args, "--scenarios-parallel");
            if (bool.TryParse(parallelValue, out var environmentsParallel)) config.RunEnvironmentsParallel = environmentsParallel;
            config.InstanceId = GetCommandLineValue(args, "--instance-id") ?? config.InstanceId;
            config.RunId = GetCommandLineValue(args, "--run-id") ?? config.RunId;
            var uiLaunchId = GetCommandLineValue(args, "--ui-launch-id") ?? "not-provided";
            var uiLaunchAt = GetCommandLineValue(args, "--ui-launch-at") ?? "not-provided";
            config.EnvironmentSelection = GetCommandLineValue(args, "--environments") ?? config.EnvironmentSelection;
            config.RowsExecutionMode = GetCommandLineValue(args, "--rows-execution-mode") ?? GetCommandLineValue(args, "--execution-mode") ?? config.RowsExecutionMode;
            var dataIdSelection = GetCommandLineValue(args, "--dataids") ?? config.DataIdSelection;
            var stepSelection = GetCommandLineValue(args, "--steps") ?? config.StepSelection;
            if (!config.RowsExecutionMode.Equals("parallel", StringComparison.OrdinalIgnoreCase) && !config.RowsExecutionMode.Equals("sequential", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("rows-execution-mode must be 'parallel' or 'sequential'.");
            var runId = string.IsNullOrWhiteSpace(config.RunId) ? $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..25] : config.RunId;
            loadedRunId = runId;
            if (config.GenerateResults) Directory.CreateDirectory(config.ResultsFolder);
            Directory.CreateDirectory(config.HistoryFolder);
            Directory.CreateDirectory(config.LogsFolder);
            var runtimeFolder = config.GenerateResults
                ? config.ResultsFolder
                : Path.Combine(config.LogsFolder, ".runtime");
            Directory.CreateDirectory(runtimeFolder);
            var runEventFolder = Path.Combine(runtimeFolder, $"Run_{InternalLogger.SafeFolderName(runId)}");
            Directory.CreateDirectory(runEventFolder);
            var stopFile = Path.Combine(runtimeFolder, $"run_{InternalLogger.SafeFolderName(runId)}.stop");
            using var cancellation = new CancellationTokenSource();
            using var stopMonitorCancellation = new CancellationTokenSource();
            using var runEvents = new LiveEventWriter(runEventFolder, config.InstanceId, runId, "_aggregate");
            ConsoleCancelEventHandler? cancelHandler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); Console.Error.WriteLine("Cancellation requested. Finishing the current operation..."); };
            Console.CancelKeyPress += cancelHandler;
            var stopMonitor = MonitorStopFileAsync(stopFile, cancellation, stopMonitorCancellation.Token);
            using var http = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true, AutomaticDecompression = DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan };
            WorkbookModel workbook;
            try
            {
                workbook = new WorkbookReader().Read(workbookPath);
            }
            catch (Exception ex)
            {
                var validationLog = WriteStartupFailureLog(config, runId, workbookPath, args, ex);
                runEvents.WriteIssue("Error", "Validation", ex.Message, config.EnvironmentSelection, "", "", "<startup>");
                Console.Error.WriteLine($"[Fatal] {ex.Message}\n[Internal log] {validationLog}");
                return 1;
            }
            var expression = GetCommandLineValue(args, "--testcases") ?? config.TestcaseSelection;
            if (string.IsNullOrWhiteSpace(expression) && config.PromptForTestcaseSelection) { Console.Write("Testcase selection (0=all): "); expression = Console.ReadLine(); }
            var indexes = new TestcaseSelectionParser().Parse(expression, workbook.Testcases.Select(t => t.TestcaseIndex));
            Console.WriteLine($"Selected testcase indexes: {string.Join(", ", indexes)}");
            Console.WriteLine($"Testcase execution order: {(config.RunEnvironmentsParallel ? "parallel" : "sequential")}");
            var selection = new RuntimeSelection { Input = string.IsNullOrWhiteSpace(expression) ? "0" : expression!, Indexes = indexes, Source = GetCommandLineValue(args, "--testcases") is not null ? "command-line" : "appsettings.json" };
            var overall = new RunResult { RunId = runId, Selection = selection, StartedAt = DateTimeOffset.Now };
            var work = new RunSelectionPlanner().Build(workbook, indexes, stepSelection, config.EnvironmentSelection, GetCommandLineValue(args, "--environments-json"), config.Environments);
            Console.WriteLine("Selected work: " + string.Join("; ", work.Select(x => $"{x.Testcase.TestcaseIndex} [{(string.IsNullOrEmpty(x.Environment) ? "default" : x.Environment)}]: {string.Join(", ", x.Testcase.Steps.Select(s => s.StepName))}")));
            var filters = ParseDataIdSelection(dataIdSelection);
            try
            {
                if (config.RunEnvironmentsParallel) await Task.WhenAll(work.Select(x => RunTestcaseAsync(x.Testcase, x.Environment, workbook, overall, http, config, runId, selection, filters, uiLaunchId, uiLaunchAt, cancellation.Token)));
                else foreach (var item in work)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    await RunTestcaseAsync(item.Testcase, item.Environment, workbook, overall, http, config, runId, selection, filters, uiLaunchId, uiLaunchAt, cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                overall.Cancelled = true;
                Console.Error.WriteLine("Run cancelled.");
            }
            finally
            {
                overall.Cancelled |= cancellation.IsCancellationRequested;
                overall.EndedAt = DateTimeOffset.Now;
                var overallResult = overall.Cancelled ? "cancelled" : overall.Testcases.Any(t => t.Result == "FAILED") ? "failed" : "passed";
                var historyPath = string.Empty;
                var historyFiles = Array.Empty<string>();
                try
                {
                    var historyWriter = new HistoryWriter(config);
                    historyFiles = config.GenerateRowHistory ? historyWriter.WritePerTestcase(overall, workbook).ToArray() : Array.Empty<string>();
                    if (config.GenerateAggregateHistory)
                    {
                        var aggregateFolder = Path.Combine(config.HistoryFolder, "_aggregate", overallResult);
                        var aggregateName = $"Execution_History_{InternalLogger.SafeFolderName(runId)}.xlsx";
                        historyFiles = historyFiles.Append(historyWriter.Write(overall, aggregateFolder, aggregateName, workbook)).ToArray();
                    }
                    historyPath = historyFiles.FirstOrDefault() ?? string.Empty;
                }
                catch (Exception ex) { runEvents.WriteIssue("Warning", "History", ex.Message); Console.Error.WriteLine($"[Warning] History generation failed: {ex.Message}"); }
                if (config.GenerateResults) WriteMetrics(config.ResultsFolder, runId, overall);
                runEvents.WriteRunCompleted(overall, historyPath);
                Console.WriteLine($"[Run complete] result={(overall.Cancelled ? "CANCELLED" : overall.Testcases.Any(t => t.Result == "FAILED") ? "FAILED" : "PASSED")}; requests={overall.Metrics.TotalRequests}; p95={overall.Metrics.P95DurationMs}ms; rps={overall.Metrics.RequestsPerSecond:F2}; historyFiles={historyFiles.Length}; history={historyPath}");
                try { if (File.Exists(stopFile)) File.Delete(stopFile); } catch { }
                stopMonitorCancellation.Cancel();
                await stopMonitor;
                Console.CancelKeyPress -= cancelHandler;
            }
            return overall.Cancelled ? 3 : overall.Testcases.Any(t => t.Result == "FAILED") ? 2 : 0;
        }
        catch (Exception ex)
        {
            var location = loadedConfig is null || string.IsNullOrWhiteSpace(loadedRunId) ? null : WriteStartupFailureLog(loadedConfig, loadedRunId, "<startup>", args, ex);
            Console.Error.WriteLine(location is null ? $"[Fatal] {ex.Message}" : $"[Fatal] {ex.Message}\n[Internal log] {location}"); return 1;
        }
    }

    private static async Task MonitorStopFileAsync(string stopFile, CancellationTokenSource cancellation, CancellationToken monitorToken)
    {
        try
        {
            while (!monitorToken.IsCancellationRequested)
            {
                if (File.Exists(stopFile)) { cancellation.Cancel(); return; }
                await Task.Delay(250, monitorToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static void WriteMetrics(string resultsFolder, string runId, RunResult run)
    {
        var metricsPath = Path.Combine(resultsFolder, $"Run_{InternalLogger.SafeFolderName(runId)}", "metrics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metricsPath)!);
        File.WriteAllText(metricsPath, JsonSerializer.Serialize(new { runId, run.Cancelled, startedAt = run.StartedAt, endedAt = run.EndedAt, metrics = run.Metrics }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string WriteStartupFailureLog(AppConfig config, string runId, string workbookPath, string[] args, Exception exception)
    {
        var folder = Path.Combine(config.LogsFolder, $"run_{InternalLogger.SafeFolderName(runId)}", "startup");
        using var logger = new InternalLogger(folder, config.InstanceId, runId, true);
        logger.Info($"Runner startup/preflight failure. Workbook={workbookPath}; arguments={string.Join(' ', args)}");
        logger.Error("Runner did not reach testcase execution.", exception);
        return logger.FilePath ?? Path.Combine(folder, "internal.log");
    }

    private static async Task RunTestcaseAsync(TestcaseDefinition testcase, string environment, WorkbookModel workbook, RunResult overall, HttpClient http, AppConfig config, string runId, RuntimeSelection selection, IReadOnlyDictionary<int, IReadOnlySet<string>> filters, string uiLaunchId, string uiLaunchAt, CancellationToken cancellationToken)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var testcaseName = InternalLogger.SafeFolderName(testcase.Testcase);
        var environmentName = string.IsNullOrWhiteSpace(environment) ? "default" : InternalLogger.SafeFolderName(environment);
        var threadCount = Math.Max(1, config.Threads);
        var iterationCount = Math.Max(1, config.Iterations);
        var shortRunId = ShortRunId(runId);
        var logStem = $"{testcaseName}_{environmentName}_{{0}}_{stamp}_T{threadCount}_I{iterationCount}_Run_{shortRunId}";
        var runningLogName = string.Format(logStem, "running") + ".log";
        var runningProcessLogName = string.Format(logStem, "running") + "_process.log";
        var folderName = $"{testcaseName}_{environmentName}_{stamp}";
        var resultFolder = config.GenerateResults
            ? Path.Combine(config.ResultsFolder, cancellationToken.IsCancellationRequested ? "cancelled" : "running", folderName + "_" + Guid.NewGuid().ToString("N"))
            : string.Empty;
        if (config.GenerateResults) Directory.CreateDirectory(resultFolder);
        var logFolder = config.LogsFolder;
        Directory.CreateDirectory(logFolder);
        var logger = new InternalLogger(logFolder, config.InstanceId, runId, config.InternalLoggingEnabled, null, runningLogName);
        var processLogger = new InternalLogger(logFolder, config.InstanceId, runId, config.InternalLoggingEnabled, null, runningProcessLogName);
        var eventRoot = config.GenerateResults ? config.ResultsFolder : Path.Combine(config.LogsFolder, ".runtime");
        var eventFolder = Path.Combine(eventRoot, $"Run_{InternalLogger.SafeFolderName(runId)}");
        using var events = new LiveEventWriter(eventFolder, config.InstanceId, runId, $"_{InternalLogger.SafeFolderName(testcase.Testcase)}_{Guid.NewGuid():N}");
        var spoolRoot = Path.Combine(eventRoot, $"Run_{InternalLogger.SafeFolderName(runId)}");
        using var spool = config.SpoolResultsToDisk
            ? new RunResultSpool(Path.Combine(spoolRoot, $"rows_{testcaseName}_{environmentName}.ndjson"))
            : null;
        var local = new RunResult { RunId = runId, Selection = selection, StartedAt = DateTimeOffset.Now, Environment = environment };
        logger.Info($"UI diagnostic correlation. uiLaunchId={uiLaunchId}; uiLaunchAt={uiLaunchAt}; runnerStartedAt={DateTimeOffset.Now:O}; processId={Environment.ProcessId}");
        processLogger.Info($"Testcase process started. Index={testcase.TestcaseIndex}; name={testcase.Testcase}; environment={environment}");
        void WriteRequestEvent(RequestResult result)
        {
            events.WriteRequest(result);
            if (result.StepType.Equals("ActionManual", StringComparison.OrdinalIgnoreCase)) events.WriteManualCompleted(result);
        }
        var runner = new TestRunner(http, new AssertionEngine(), config.TemplatesFolder, config.RequestTimeoutSeconds, WriteRequestEvent, logger, new FeatureConfig
        {
            WorkspaceRoot = Path.GetFullPath(config.WorkspaceRoot),
            TemplatesFolder = Path.GetFullPath(config.TemplatesFolder),
            Connections = config.Connections
        }, events.WriteManualPending, eventFolder, string.Equals(uiLaunchId, "not-provided", StringComparison.OrdinalIgnoreCase));
        filters.TryGetValue(testcase.TestcaseIndex, out var selectedDataIds);
        try
        {
                await runner.RunAsync(testcase, workbook, local, cancellationToken, environment, config.RowsExecutionMode, selectedDataIds, config.Threads, config.Iterations, config.ParallelRowStartIntervalMs, config.IterationIntervalMs, config.MaxParallelRows, spool is null ? null : new Action<TestcaseResult>(spool.Append));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            local.Cancelled = true;
            logger.Warn($"Testcase cancelled. testcase={testcase.Testcase}; environment={environment}");
        }
        catch (Exception ex)
        {
            logger.Error($"Unhandled testcase execution exception. testcase={testcase.Testcase}; environment={environment}", ex);
            events.WriteIssue("Error", "Unexpected error", ex.Message, environment, testcase.Testcase, selectedDataIds?.FirstOrDefault() ?? "", "<runner>");
            local.Testcases.Add(new TestcaseResult
            {
                RunId = runId, TestcaseIndex = testcase.TestcaseIndex, Testcase = testcase.Testcase,
                DataId = selectedDataIds?.FirstOrDefault() ?? "", Thread = 0, Iteration = 0,
                StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now, Environment = environment,
                Requests = { new RequestResult
                {
                    RunId = runId, RequestId = $"FATAL-{Guid.NewGuid():N}", TestcaseIndex = testcase.TestcaseIndex,
                    Testcase = testcase.Testcase, DataId = selectedDataIds?.FirstOrDefault() ?? "", StepName = "<runner>",
                    StartedAt = DateTimeOffset.Now, EndedAt = DateTimeOffset.Now, HttpStatus = 500,
                    ErrorMessage = ex.Message, FailureCategory = FailureCategories.Infrastructure, Environment = environment
                } }
            });
        }
        local.EndedAt = DateTimeOffset.Now;
        var failed = local.Cancelled || local.Testcases.Any(t => t.Result == "FAILED");
        events.WriteTestcaseCompleted(local);
        logger.Info($"Testcase completed. Result={(local.Cancelled ? "CANCELLED" : failed ? "FAILED" : "PASSED")}; requests={local.Metrics.TotalRequests}; p95={local.Metrics.P95DurationMs}ms");
        processLogger.Info($"Testcase process completed. Result={(local.Cancelled ? "CANCELLED" : failed ? "FAILED" : "PASSED")}");
        logger.Dispose();
        processLogger.Dispose();
        var finalResult = local.Cancelled ? "cancelled" : failed ? "failed" : "passed";
        MoveInternalLog(logger.FilePath, Path.Combine(config.LogsFolder, string.Format(logStem, finalResult) + ".log"));
        MoveInternalLog(processLogger.FilePath, Path.Combine(config.LogsFolder, string.Format(logStem, finalResult) + "_process.log"));
        if (Directory.Exists(resultFolder))
        {
            var environmentFolder = string.IsNullOrWhiteSpace(environment) ? string.Empty : InternalLogger.SafeFolderName(environment);
            var finalFolder = string.IsNullOrWhiteSpace(environmentFolder)
                ? Path.Combine(config.ResultsFolder, finalResult, Path.GetFileName(resultFolder))
                : Path.Combine(config.ResultsFolder, environmentFolder, finalResult, Path.GetFileName(resultFolder));
            Directory.CreateDirectory(Path.GetDirectoryName(finalFolder)!);
            Directory.Move(resultFolder, finalFolder);
        }
        lock (overall.Testcases) overall.Testcases.AddRange(local.Testcases);
        Console.WriteLine($"[Done] {testcase.Testcase} [{(string.IsNullOrWhiteSpace(environment) ? "default" : environment)}] -> {(config.GenerateResults ? resultFolder : "result artifacts disabled")}");
    }

    private static string ShortRunId(string runId)
    {
        var safe = InternalLogger.SafeFolderName(runId);
        return safe.Length <= 8 ? safe : safe[^8..];
    }

    private static void MoveInternalLog(string? source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return;
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(source, destination);
    }

    private static string DataIdFileName(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return "dataid_unknown.xlsx";
        var tokens = ids.Select(InternalLogger.SafeFolderName).ToList();
        return "dataid_" + string.Join("_", tokens) + ".xlsx";
    }

    private static IReadOnlyDictionary<int, IReadOnlySet<string>> ParseDataIdSelection(string? value)
    {
        var result = new Dictionary<int, IReadOnlySet<string>>();
        if (string.IsNullOrWhiteSpace(value) || value == "0") return result;
        foreach (var group in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = group.Split(':', 2); if (parts.Length != 2 || !int.TryParse(parts[0], out var index)) throw new InvalidDataException($"Invalid data ID selection group '{group}'. Expected testcase:dataid1,dataid2.");
            var ids = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (ids.Count > 0) result[index] = ids;
        }
        return result;
    }

    private static AppConfig LoadConfig(string? settingsPath = null, string? instanceKey = null)
    {
        var path = Path.GetFullPath(settingsPath ?? "appsettings.json", Directory.GetCurrentDirectory());
        var baseDirectory = Path.GetDirectoryName(path)!;
        var root = new ConfigurationBuilder().SetBasePath(baseDirectory).AddJsonFile(Path.GetFileName(path), optional: false, reloadOnChange: false).Build();
        var config = new AppConfig(); root.Bind(config);
        if (root["MaxParallelRows"] is null && int.TryParse(root["MaxConcurrentRows"], out var legacyMaxRows)) config.MaxParallelRows = Math.Clamp(legacyMaxRows, 1, 100);
        if (root["ParallelRowStartIntervalMs"] is null && int.TryParse(root["ThreadIntervalMs"], out var legacyStartInterval)) config.ParallelRowStartIntervalMs = Math.Max(0, legacyStartInterval);
        config.RowsExecutionMode = NormalizeRowsExecutionMode(root["RowsExecutionMode"]?.ToString() ?? root["ExecutionMode"]?.ToString() ?? config.RowsExecutionMode);
        // Migrate older central settings that used RunScenariosInParallel.
        if (root["RunEnvironmentsParallel"] is null && bool.TryParse(root["RunScenariosInParallel"], out var legacyParallel))
            config.RunEnvironmentsParallel = legacyParallel;
        if (string.IsNullOrWhiteSpace(instanceKey)) return config;
        if (!config.Instances.TryGetValue(instanceKey, out var instance)) throw new InvalidDataException($"Instance '{instanceKey}' was not found in central settings '{path}'.");
        var instancesRoot = ResolveCentralPath(baseDirectory, config.InstancesRoot, "Instances");
        var derivedFolder = Path.GetFullPath(instanceKey, instancesRoot);
        var legacyFolder = string.IsNullOrWhiteSpace(instance.Folder) ? string.Empty : ResolveCentralPath(baseDirectory, instance.Folder, instanceKey);
        var instanceFolder = Directory.Exists(derivedFolder) || string.IsNullOrWhiteSpace(legacyFolder) ? derivedFolder : legacyFolder;
        config.InstanceId = instanceKey;
        config.WorkspaceRoot = ResolveCentralPath(instanceFolder, instance.WorkspaceRoot, instanceFolder);
        var globalWorkbook = string.IsNullOrWhiteSpace(config.WorkbookName) ? Path.GetFileName(config.ExcelFilePath) : config.WorkbookName;
        var preferredWorkbook = ResolveCentralPath(instanceFolder, globalWorkbook, config.ExcelFilePath);
        var legacyWorkbook = string.IsNullOrWhiteSpace(instance.Workbook) ? string.Empty : ResolveCentralPath(instanceFolder, instance.Workbook, globalWorkbook);
        config.ExcelFilePath = File.Exists(preferredWorkbook) ? preferredWorkbook
            : File.Exists(legacyWorkbook) ? legacyWorkbook
            : Directory.Exists(instanceFolder)
                ? Directory.GetFiles(instanceFolder, "*.xlsx", SearchOption.TopDirectoryOnly).FirstOrDefault(x => !Path.GetFileName(x).StartsWith("~$", StringComparison.OrdinalIgnoreCase)) ?? preferredWorkbook
                : preferredWorkbook;
        config.TemplatesFolder = ResolveCentralPath(instanceFolder, instance.TemplatesFolder, config.TemplatesFolder);
        config.LogsFolder = ResolveCentralPath(instanceFolder, instance.LogsFolder, config.LogsFolder);
        config.HistoryFolder = ResolveCentralPath(instanceFolder, instance.HistoryFolder, config.HistoryFolder);
        config.ResultsFolder = ResolveCentralPath(instanceFolder, instance.ResultsFolder, config.ResultsFolder);
        if (instance.GenerateResults.HasValue) config.GenerateResults = instance.GenerateResults.Value;
        if (instance.GenerateAggregateHistory.HasValue) config.GenerateAggregateHistory = instance.GenerateAggregateHistory.Value;
        if (instance.GenerateRowHistory.HasValue) config.GenerateRowHistory = instance.GenerateRowHistory.Value;
        if (!string.IsNullOrWhiteSpace(instance.TestcaseSelection)) config.TestcaseSelection = instance.TestcaseSelection;
        if (!string.IsNullOrWhiteSpace(instance.StepSelection)) config.StepSelection = instance.StepSelection;
        if (!string.IsNullOrWhiteSpace(instance.EnvironmentSelection)) config.EnvironmentSelection = instance.EnvironmentSelection;
        if (!string.IsNullOrWhiteSpace(instance.DataIdSelection)) config.DataIdSelection = instance.DataIdSelection;
        if (!string.IsNullOrWhiteSpace(instance.RunId)) config.RunId = instance.RunId;
        config.RowsExecutionMode = NormalizeRowsExecutionMode(instance.RowsExecutionMode ?? instance.ExecutionMode ?? config.RowsExecutionMode);
        if (instance.Threads.HasValue) config.Threads = instance.Threads.Value;
        if (instance.Iterations.HasValue) config.Iterations = instance.Iterations.Value;
        if (instance.ParallelRowStartIntervalMs.HasValue || instance.ThreadIntervalMs.HasValue) config.ParallelRowStartIntervalMs = Math.Max(0, instance.ParallelRowStartIntervalMs ?? instance.ThreadIntervalMs ?? 0);
        if (instance.IterationIntervalMs.HasValue) config.IterationIntervalMs = instance.IterationIntervalMs.Value;
        if (instance.MaxParallelRows.HasValue || instance.MaxConcurrentRows.HasValue) config.MaxParallelRows = Math.Clamp(instance.MaxParallelRows ?? instance.MaxConcurrentRows ?? 100, 1, 100);
        if (instance.SpoolResultsToDisk.HasValue) config.SpoolResultsToDisk = instance.SpoolResultsToDisk.Value;
        if (instance.InternalLoggingEnabled.HasValue) config.InternalLoggingEnabled = instance.InternalLoggingEnabled.Value;
        if (instance.SaveRequestBodyToHistory.HasValue) config.SaveRequestBodyToHistory = instance.SaveRequestBodyToHistory.Value;
        if (instance.SaveResponseBodyToHistory.HasValue) config.SaveResponseBodyToHistory = instance.SaveResponseBodyToHistory.Value;
        if (instance.MaskSensitiveData.HasValue) config.MaskSensitiveData = instance.MaskSensitiveData.Value;
        if (instance.HistorySheets is not null) config.HistorySheets = instance.HistorySheets;
        return config;
    }
    private static string NormalizeRowsExecutionMode(string? value)
        => value?.Equals("sequential", StringComparison.OrdinalIgnoreCase) == true || value?.Equals("loop", StringComparison.OrdinalIgnoreCase) == true
            ? "sequential"
            : "parallel";

    private static string ResolveCentralPath(string baseDirectory, string? value, string fallback)
    {
        var selected = string.IsNullOrWhiteSpace(value) ? fallback : value;
        return Path.GetFullPath(selected, baseDirectory);
    }
    private static async Task<int> RunAllInstancesAsync(string? settingsPath)
    {
        var path = Path.GetFullPath(settingsPath ?? "appsettings.json", Directory.GetCurrentDirectory());
        var config = LoadConfig(path);
        var enabledInstances = config.Instances.Where(pair => pair.Value.Enabled).ToList();
        if (enabledInstances.Count == 0) throw new InvalidDataException($"No enabled instances are registered in '{path}'.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not locate the runner executable.");
        var processes = new List<Process>();
        foreach (var pair in enabledInstances)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(path)!, UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(path);
            start.ArgumentList.Add("--instance"); start.ArgumentList.Add(pair.Key);
            var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start instance '{pair.Key}'.");
            processes.Add(process);
            Console.WriteLine($"[Started] {pair.Key}; process={process.Id}");
        }
        await Task.WhenAll(processes.Select(async process => { await process.WaitForExitAsync(); process.Dispose(); }));
        return 0;
    }
    private static string? GetCommandLineValue(string[] args, params string[] names)
    {
        for (var i = 0; i < args.Length; i++)
            if (names.Any(name => args[i].Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for {args[i]}.");
                return args[i + 1];
            }
        return null;
    }
}
