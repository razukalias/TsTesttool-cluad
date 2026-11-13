namespace LoadTestingTool.Domain;

public sealed class AppConfig
{
    public string InstancesRoot { get; set; } = "Instances";
    public string WorkbookName { get; set; } = "DataEngine.xlsx";
    public string ExcelFilePath { get; set; } = "DataEngine.xlsx";
    public string TemplatesFolder { get; set; } = "Templates";
    public string LogsFolder { get; set; } = "Logs";
    public bool InternalLoggingEnabled { get; set; }
    public string HistoryFolder { get; set; } = "History";
    public string ResultsFolder { get; set; } = "Results";
    public bool GenerateResults { get; set; } = true;
    public bool GenerateAggregateHistory { get; set; }
    public bool GenerateRowHistory { get; set; } = true;
    public string InstanceId { get; set; } = "default";
    public string RunId { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 300;
    public int Threads { get; set; } = 1;
    public int Iterations { get; set; } = 1;
    public int ParallelRowStartIntervalMs { get; set; }
    public int IterationIntervalMs { get; set; }
    public int MaxParallelRows { get; set; } = 100;
    public bool SpoolResultsToDisk { get; set; } = true;
    public bool RunEnvironmentsParallel { get; set; }
    public string RowsExecutionMode { get; set; } = "parallel";
    public string TestcaseSelection { get; set; } = "0";
    public string StepSelection { get; set; } = "";
    public string EnvironmentSelection { get; set; } = "";
    public string DataIdSelection { get; set; } = "";
    public bool PromptForTestcaseSelection { get; set; }
    public bool SaveRequestBodyToHistory { get; set; } = true;
    public bool SaveResponseBodyToHistory { get; set; } = true;
    public Dictionary<string, bool> HistorySheets { get; set; } = HistorySheetCatalog.CreateDefaults();
    public bool MaskSensitiveData { get; set; } = true;
    public List<string> Environments { get; set; } = [];
    public string WorkspaceRoot { get; set; } = ".";
    public Dictionary<string, SqlConnectionProfile> Connections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CentralInstanceConfig> Instances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CentralInstanceConfig
{
    public bool Enabled { get; set; } = true;
    // Legacy migration fields. New settings derive these values from the instance key and AppConfig.
    public string Folder { get; set; } = string.Empty;
    public string Workbook { get; set; } = string.Empty;
    public string? TemplatesFolder { get; set; }
    public string? LogsFolder { get; set; }
    public string? HistoryFolder { get; set; }
    public string? ResultsFolder { get; set; }
    public bool? GenerateResults { get; set; }
    public bool? GenerateAggregateHistory { get; set; }
    public bool? GenerateRowHistory { get; set; }
    public string? WorkspaceRoot { get; set; }
    public string? TestcaseSelection { get; set; }
    public string? StepSelection { get; set; }
    public string? EnvironmentSelection { get; set; }
    public string? DataIdSelection { get; set; }
    public string? RunId { get; set; }
    public string? RowsExecutionMode { get; set; }
    // Legacy aliases accepted during migration from older appsettings files.
    public string? ExecutionMode { get; set; }
    public int? Threads { get; set; }
    public int? Iterations { get; set; }
    public int? ParallelRowStartIntervalMs { get; set; }
    public int? ThreadIntervalMs { get; set; }
    public int? IterationIntervalMs { get; set; }
    public int? MaxParallelRows { get; set; }
    public int? MaxConcurrentRows { get; set; }
    public bool? SpoolResultsToDisk { get; set; }
    public bool? RunEnvironmentsParallel { get; set; }
    public bool? InternalLoggingEnabled { get; set; }
    public bool? SaveRequestBodyToHistory { get; set; }
    public bool? SaveResponseBodyToHistory { get; set; }
    public bool? MaskSensitiveData { get; set; }
    public Dictionary<string, bool>? HistorySheets { get; set; }
}

public static class HistorySheetCatalog
{
    public static readonly string[] Names =
    [
        "RunSummary", "Metrics", "TestcaseSummary", "RequestHistory", "RuntimeReferenceErrors",
        "AssertionHistory", "Variables", "CorrelationHistory", "DataEngineRequestRows", "DataEngineConfig"
    ];

    public static Dictionary<string, bool> CreateDefaults() => Names.ToDictionary(x => x, _ => true, StringComparer.OrdinalIgnoreCase);

    public static bool IsEnabled(IReadOnlyDictionary<string, bool>? settings, string name)
        => settings is null || !settings.TryGetValue(name, out var enabled) || enabled;
}

public sealed class TestcaseDefinition
{
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public IReadOnlyList<RequestStep> Steps { get; init; } = [];
}

public sealed class RequestStep
{
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public StepType StepType { get; init; } = StepType.Http;
    public ExecutionStage Stage { get; init; } = ExecutionStage.Test;
    public bool Enabled { get; init; } = true;
    public int TimeoutSeconds { get; init; }
    public int ManualTimeoutMinutes { get; init; }
    public string StepConfig { get; init; } = "{}";
    public int Sequence { get; init; }
    public string TemplateSource { get; init; } = string.Empty;
    public string TargetUrl { get; init; } = string.Empty;
    public string Verb { get; init; } = "POST";
    public string ContentType { get; init; } = string.Empty;
    public int WaitMs { get; init; }
    public string ExpectedStatus { get; init; } = string.Empty;
    public bool StopOnFailure { get; init; } = true;
    public bool IgnoreEmpty { get; init; }
    public bool AssertEnabled { get; init; } = true;
    public bool AssertOnlyResponse { get; init; }
    public IReadOnlyList<string> Environments { get; init; } = [];
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class RequestDataRow
{
    public int TestcaseIndex { get; init; }
    public int ExcelRowNumber { get; init; }
    public int TestcaseRowNumber { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public IReadOnlyList<string> Dontrun { get; init; } = [];
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> RedactedVariables { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class AssertionDefinition
{
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string ResponsePath { get; init; } = string.Empty;
    public string AssertionVerb { get; init; } = "eq";
    public string HeaderAssertionVerb { get; init; } = "eq";
    public string ExpectedValue { get; init; } = string.Empty;
    public string? ExtractVariable { get; init; }
    public int ExcelRowNumber { get; init; }
    public int ExpectedValueColumn { get; init; }
    public int ResponseColumn { get; init; }
}

public sealed class WorkbookModel
{
    public IReadOnlyList<TestcaseDefinition> Testcases { get; init; } = [];
    public IReadOnlyDictionary<(int Index, string DataId), RequestDataRow> RequestData { get; init; } = new Dictionary<(int, string), RequestDataRow>();
    public IReadOnlyDictionary<(int Index, string DataId, string StepName), IReadOnlyList<AssertionDefinition>> Assertions { get; init; } = new Dictionary<(int, string, string), IReadOnlyList<AssertionDefinition>>();
}

public sealed class RuntimeSelection
{
    public string Input { get; init; } = "0";
    public IReadOnlyList<int> Indexes { get; init; } = [];
    public string Source { get; init; } = "appsettings.json";
}

public sealed class RunResult
{
    public string RunId { get; init; } = string.Empty;
    public RuntimeSelection Selection { get; init; } = new();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; set; }
    public string Environment { get; init; } = string.Empty;
    public List<TestcaseResult> Testcases { get; } = [];
    public bool Cancelled { get; set; }
    public RunMetrics Metrics => RunMetrics.From(Testcases.SelectMany(t => t.Requests));
}

public sealed class RunMetrics
{
    public int TotalRequests { get; init; }
    public int PassedRequests { get; init; }
    public int FailedRequests { get; init; }
    public int CancelledRequests { get; init; }
    public int AssertionFailures { get; init; }
    public double AverageDurationMs { get; init; }
    public long P50DurationMs { get; init; }
    public long P95DurationMs { get; init; }
    public long P99DurationMs { get; init; }
    public long MaxDurationMs { get; init; }
    public double RequestsPerSecond { get; init; }

    public static RunMetrics From(IEnumerable<RequestResult> source)
    {
        var requests = source.ToList();
        var durations = requests.Select(r => Math.Max(0, r.DurationMs)).OrderBy(x => x).ToList();
        var elapsedMs = requests.Count == 0 ? 0 : (requests.Max(r => r.EndedAt) - requests.Min(r => r.StartedAt)).TotalMilliseconds;
        return new RunMetrics
        {
            TotalRequests = requests.Count,
            PassedRequests = requests.Count(r => r.Result == "PASS"),
            FailedRequests = requests.Count(r => r.Result == "FAIL"),
            CancelledRequests = requests.Count(r => r.FailureCategory == FailureCategories.Cancelled),
            AssertionFailures = requests.Sum(r => r.Assertions.Count(a => a.Result == "FAIL")),
            AverageDurationMs = durations.Count == 0 ? 0 : durations.Average(),
            P50DurationMs = Percentile(durations, 0.50),
            P95DurationMs = Percentile(durations, 0.95),
            P99DurationMs = Percentile(durations, 0.99),
            MaxDurationMs = durations.Count == 0 ? 0 : durations[^1],
            RequestsPerSecond = elapsedMs <= 0 ? 0 : requests.Count / (elapsedMs / 1000d)
        };
    }

    private static long Percentile(IReadOnlyList<long> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var index = Math.Clamp((int)Math.Ceiling(values.Count * percentile) - 1, 0, values.Count - 1);
        return values[index];
    }
}

public static class FailureCategories
{
    public const string Configuration = "Configuration";
    public const string WorkbookValidation = "WorkbookValidation";
    public const string Template = "Template";
    public const string VariableResolution = "VariableResolution";
    public const string RuntimeReference = "RuntimeReference";
    public const string Network = "Network";
    public const string Timeout = "Timeout";
    public const string HttpStatus = "HttpStatus";
    public const string ResponseParse = "ResponseParse";
    public const string Assertion = "Assertion";
    public const string Sql = "Sql";
    public const string FileSystem = "FileSystem";
    public const string Script = "Script";
    public const string Cancelled = "Cancelled";
    public const string Infrastructure = "Infrastructure";
}

public sealed class TestcaseResult
{
    public string RunId { get; init; } = string.Empty;
    public int TestcaseIndex { get; init; }
    public int ExcelRowNumber { get; init; }
    public int TestcaseRowNumber { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public int Thread { get; init; }
    public int Iteration { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; set; }
    public string Environment { get; init; } = string.Empty;
    public List<RequestResult> Requests { get; } = [];
    public string Result => Requests.Any(r => r.Result == "FAIL") ? "FAILED" : Requests.Count == 0 || Requests.All(r => r.Result == "SKIPPED") ? "SKIPPED" : "PASSED";
}

public sealed class RequestResult
{
    public string RunId { get; init; } = string.Empty;
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string StepType { get; init; } = string.Empty;
    public int Thread { get; init; }
    public int Iteration { get; init; }
    public int SequenceOrder { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; set; }
    public int WaitMs { get; init; }
    public string Verb { get; init; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string TemplateSource { get; init; } = string.Empty;
    public string StepConfig { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string RequestHeaders { get; set; } = string.Empty;
    public string RequestBody { get; set; } = string.Empty;
    public int HttpStatus { get; set; }
    public string ResponseHeaders { get; set; } = string.Empty;
    public string ResponseBody { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> RedactedVariables { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public List<CorrelationRecord> Correlations { get; } = [];
    public List<AssertionResult> Assertions { get; } = [];
    public string ErrorMessage { get; set; } = string.Empty;
    public string FailureCategory { get; set; } = string.Empty;
    public string RuntimeReference { get; set; } = string.Empty;
    public string RuntimeReferenceKey { get; set; } = string.Empty;
    public string ManualInfo { get; set; } = string.Empty;
    public string ManualComment { get; set; } = string.Empty;
    public string Environment { get; init; } = string.Empty;
    public string Result => ErrorMessage.Length > 0 || Assertions.Any(a => a.Result == "FAIL") || (HttpStatus is < 200 or >= 300) ? "FAIL" : "PASS";
}

public sealed class CorrelationRecord
{
    public string DataId { get; init; } = string.Empty;
    public string SourceStepName { get; init; } = string.Empty;
    public string ResponsePath { get; init; } = string.Empty;
    public string Variable { get; init; } = string.Empty;
    public string ExtractedValue { get; init; } = string.Empty;
    public string UsedByStepName { get; set; } = string.Empty;
}

public sealed class AssertionResult
{
    public string RunId { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public int TestcaseIndex { get; init; }
    public string Testcase { get; init; } = string.Empty;
    public string DataId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public string ResponsePath { get; init; } = string.Empty;
    public string AssertionVerb { get; init; } = "eq";
    public string AssertionType { get; init; } = "Value assertion";
    public string AssertionMeaning { get; init; } = string.Empty;
    public string ExpectedValue { get; init; } = string.Empty;
    public string ActualValue { get; init; } = string.Empty;
    public string Result { get; init; } = "FAIL";
    public string FailureMessage { get; init; } = string.Empty;
    public string? ExtractVariable { get; init; }
    public int ExcelRowNumber { get; init; }
    public int ExpectedValueColumn { get; init; }
    public string Environment { get; init; } = string.Empty;
}
