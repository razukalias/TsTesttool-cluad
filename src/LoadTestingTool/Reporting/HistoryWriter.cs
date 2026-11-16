using LoadTestingTool.Execution;
using ClosedXML.Excel;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Reporting;

public sealed class HistoryWriter
{
    private readonly AppConfig _config;
    public HistoryWriter(AppConfig config) => _config = config;

    /// <summary>What this run remembered for a re-run; written into each history file as the hidden RunState sheet.</summary>
    public RunValueMemory.Snapshot? RunState { get; set; }

    /// <summary>The run this one re-runs, if any (recorded in RunState and in RunSummary).</summary>
    public string? RerunOf { get; set; }

    public IReadOnlyList<string> WritePerTestcase(RunResult run, WorkbookModel? workbookModel = null)
    {
        var paths = new List<string>();
        foreach (var testcase in run.Testcases.OrderBy(x => x.StartedAt))
        {
            var environmentValue = testcase.Environment;
            if (string.IsNullOrWhiteSpace(environmentValue))
                environmentValue = testcase.Requests.Select(x => x.Environment).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
            var environment = string.IsNullOrWhiteSpace(environmentValue) ? string.Empty : InternalLogger.SafeFolderName(environmentValue);
            var result = run.Cancelled ? "cancelled" : testcase.Result switch
            {
                "FAILED" => "failed",
                "SKIPPED" => "cancelled",
                _ => "passed"
            };
            var environmentFolder = string.IsNullOrWhiteSpace(environment) ? "default" : environment;
            var folder = Path.Combine(_config.HistoryFolder, result, environmentFolder);
            var row = testcase.TestcaseRowNumber > 0 ? testcase.TestcaseRowNumber.ToString() : "unknown";
            var stamp = testcase.StartedAt.ToLocalTime().ToString("yyyyMMdd_HHmmss_fff");
            var name = $"{InternalLogger.SafeFolderName(testcase.Testcase)}_Row{row}_{stamp}_T{testcase.Thread}_I{testcase.Iteration}.xlsx";
            var single = new RunResult
            {
                RunId = run.RunId,
                Selection = run.Selection,
                StartedAt = testcase.StartedAt,
                EndedAt = testcase.EndedAt,
                Environment = testcase.Environment,
                Cancelled = run.Cancelled
            };
            single.Testcases.Add(testcase);
            paths.Add(Write(single, folder, name, workbookModel));
        }
        return paths;
    }

    public string Write(RunResult run, string? outputFolder = null, string? fileName = null, WorkbookModel? workbookModel = null)
    {
        var folder = outputFolder ?? _config.HistoryFolder;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName ?? $"Execution_History_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        ValueSpill.Folder = Path.Combine(_config.HistoryFolder, "_values", InternalLogger.SafeFolderName(run.RunId));
        try { return WriteWorkbook(run, path, workbookModel); }
        finally { ValueSpill.Folder = null; }
    }

    private string WriteWorkbook(RunResult run, string path, WorkbookModel? workbookModel)
    {
        using var workbook = new XLWorkbook();
        var requests = run.Testcases.SelectMany(t => t.Requests).ToList();
        var metrics = run.Metrics;
        var passed = requests.Count(r => r.Result == "PASS"); var failed = requests.Count(r => r.Result == "FAIL");
        if (Enabled("RunSummary"))
        {
            var summary = workbook.AddWorksheet("RunSummary");
            WriteTable(summary, "runid", "starttime", "endtime", "durationms", "selectionSource", "selectionInput", "selectedIndexes", "totalTestcases", "totalRequests", "passedRequests", "failedRequests", "p95DurationMs", "p99DurationMs", "requestsPerSecond", "overallResult", "rerunof");
            summary.AddRow(run.RunId, run.StartedAt, run.EndedAt, (run.EndedAt - run.StartedAt).TotalMilliseconds, run.Selection.Source, run.Selection.Input, string.Join(",", run.Selection.Indexes), run.Testcases.Count, requests.Count, passed, failed, metrics.P95DurationMs, metrics.P99DurationMs, metrics.RequestsPerSecond, run.Cancelled ? "CANCELLED" : failed == 0 ? "PASSED" : "FAILED", RerunOf ?? string.Empty);
        }
        if (Enabled("Metrics"))
        {
            var metricsSheet = workbook.AddWorksheet("Metrics");
            WriteTable(metricsSheet, "metric", "value");
            metricsSheet.AddRow("totalRequests", metrics.TotalRequests); metricsSheet.AddRow("passedRequests", metrics.PassedRequests); metricsSheet.AddRow("failedRequests", metrics.FailedRequests); metricsSheet.AddRow("cancelledRequests", metrics.CancelledRequests); metricsSheet.AddRow("assertionFailures", metrics.AssertionFailures); metricsSheet.AddRow("averageDurationMs", metrics.AverageDurationMs); metricsSheet.AddRow("p50DurationMs", metrics.P50DurationMs); metricsSheet.AddRow("p95DurationMs", metrics.P95DurationMs); metricsSheet.AddRow("p99DurationMs", metrics.P99DurationMs); metricsSheet.AddRow("maxDurationMs", metrics.MaxDurationMs); metricsSheet.AddRow("requestsPerSecond", metrics.RequestsPerSecond);
        }
        if (Enabled("TestcaseSummary"))
        {
            var tc = workbook.AddWorksheet("TestcaseSummary");
            WriteTable(tc, "runid", "testcaseindex", "testcase", "dataid", "thread", "iteration", "starttime", "endtime", "durationms", "totalsteps", "passedsteps", "failedsteps", "skippedsteps", "result");
            foreach (var t in run.Testcases) tc.AddRow(run.RunId, t.TestcaseIndex, t.Testcase, t.DataId, t.Thread, t.Iteration, t.StartedAt, t.EndedAt, (t.EndedAt - t.StartedAt).TotalMilliseconds, t.Requests.Count, t.Requests.Count(r => r.Result == "PASS"), t.Requests.Count(r => r.Result == "FAIL"), t.Requests.Count(r => r.Result == "SKIPPED"), t.Result);
        }

        if (Enabled("RequestHistory"))
        {
            var rh = workbook.AddWorksheet("RequestHistory");
            WriteTable(rh, "runid", "requestid", "testcaseindex", "testcase", "dataid", "stepname", "steptype", "thread", "iteration", "sequenceorder", "starttime", "endtime", "waitms", "verb", "targeturl", "templetsource", "stepconfig", "contenttype", "requestheaders", "requestbody", "httpstatus", "responseheaders", "responsebody", "durationms", "assertioncount", "passedassertions", "failedassertions", "assertionresult", "requestresult", "failurecategory", "runtimereference", "runtimereferencekey", "errormessage", "manualinfo", "manualcomment", "failureaction");
            foreach (var r in requests) rh.AddRow(run.RunId, r.RequestId, r.TestcaseIndex, r.Testcase, r.DataId, r.StepName, r.StepType, r.Thread, r.Iteration, r.SequenceOrder, r.StartedAt, r.EndedAt, r.WaitMs, r.Verb, r.TargetUrl, r.TemplateSource, Mask(r.StepConfig), r.ContentType, MaskVariableContent(r.RequestHeaders, r.RedactedVariables, r.Variables), _config.SaveRequestBodyToHistory ? MaskVariableContent(r.RequestBody, r.RedactedVariables, r.Variables) : "[omitted]", r.HttpStatus, Mask(r.ResponseHeaders), _config.SaveResponseBodyToHistory ? Mask(r.ResponseBody) : "[omitted]", r.DurationMs, r.Assertions.Count, r.Assertions.Count(a => a.Result == "PASS"), r.Assertions.Count(a => a.Result == "FAIL"), r.Assertions.Count == 0 || r.Assertions.All(a => a.Result == "PASS") ? "PASS" : "FAIL", r.Result, r.FailureCategory, r.RuntimeReference, r.RuntimeReferenceKey, r.ErrorMessage, Mask(r.ManualInfo), Mask(r.ManualComment), r.Result == "FAIL" ? (r.FailureCategory == "RuntimeReference" ? "Current data row stopped; remaining rows continued" : "Sequence stopped when configured") : "");
        }
        if (Enabled("RuntimeReferenceErrors"))
        {
            var referenceErrors = workbook.AddWorksheet("RuntimeReferenceErrors");
            WriteTable(referenceErrors, "runid", "testcaseindex", "testcase", "dataid", "thread", "iteration", "sequenceorder", "stepname", "steptype", "environment", "requestid", "reference", "referencekey", "error", "timestamp");
            foreach (var r in requests.Where(r => r.FailureCategory.Equals("RuntimeReference", StringComparison.OrdinalIgnoreCase))) referenceErrors.AddRow(run.RunId, r.TestcaseIndex, r.Testcase, r.DataId, r.Thread, r.Iteration, r.SequenceOrder, r.StepName, r.StepType, r.Environment, r.RequestId, r.RuntimeReference, r.RuntimeReferenceKey, r.ErrorMessage, r.EndedAt);
        }
        if (Enabled("AssertionHistory"))
        {
            var ah = workbook.AddWorksheet("AssertionHistory");
            WriteTable(ah, "runid", "requestid", "testcaseindex", "testcase", "dataid", "stepname", "responsepath", "assertionverb", "assertiontype", "assertionmeaning", "expectedvalue", "actualvalue", "result", "failuremessage");
            foreach (var a in requests.SelectMany(r => r.Assertions)) ah.AddRow(run.RunId, a.RequestId, a.TestcaseIndex, a.Testcase, a.DataId, a.StepName, a.ResponsePath, a.AssertionVerb, a.AssertionType, a.AssertionMeaning, a.ExpectedValue, a.ActualValue, a.Result, a.FailureMessage);
        }

        IXLWorksheet? vars = null;
        if (Enabled("Variables")) { vars = workbook.AddWorksheet("Variables"); WriteTable(vars, "runid", "requestid", "testcaseindex", "testcase", "dataid", "stepname", "variable", "value", "source"); }
        IXLWorksheet? corr = null;
        if (Enabled("CorrelationHistory")) { corr = workbook.AddWorksheet("CorrelationHistory"); WriteTable(corr, "runid", "requestid", "testcaseindex", "dataid", "sourceStepName", "responsepath", "variable", "extractedvalue", "usedByStepName"); }
        foreach (var r in requests)
        {
            if (vars is not null) foreach (var variable in r.Variables)
                vars.AddRow(run.RunId, r.RequestId, r.TestcaseIndex, r.Testcase, r.DataId, r.StepName, variable.Key, MaskVariableContent($"{variable.Key}={variable.Value}", r.RedactedVariables, r.Variables), "request/runtime");
            if (corr is not null) foreach (var correlation in r.Correlations)
                corr.AddRow(run.RunId, r.RequestId, r.TestcaseIndex, r.DataId, correlation.SourceStepName, correlation.ResponsePath, correlation.Variable, MaskVariableContent($"{correlation.Variable}={correlation.ExtractedValue}", r.RedactedVariables, r.Variables), correlation.UsedByStepName);
        }
        if (workbookModel is not null)
        {
            if (Enabled("DataEngineRequestRows"))
            {
                var data = workbook.AddWorksheet("DataEngineRequestRows");
                WriteTable(data, "testcaseindex", "testcase", "dataid", "dontrun", "variable", "value", "source");
                foreach (var row in workbookModel.RequestData.Values.OrderBy(x => x.TestcaseIndex).ThenBy(x => x.DataId))
                {
                    if (row.Variables.Count == 0) data.AddRow(row.TestcaseIndex, row.Testcase, row.DataId, string.Join(',', row.Dontrun), "", "", "DataEngine request row");
                    foreach (var variable in row.Variables.OrderBy(x => x.Key)) data.AddRow(row.TestcaseIndex, row.Testcase, row.DataId, string.Join(',', row.Dontrun), variable.Key, row.RedactedVariables.Contains(variable.Key) ? $"[redacted:{variable.Key}]" : Mask(GeneratedTokens.ToDisplay(variable.Value)), "DataEngine request row");
                }
            }
            if (Enabled("DataEngineConfig"))
            {
                var config = workbook.AddWorksheet("DataEngineConfig");
                WriteTable(config, "testcaseindex", "testcase", "stepname", "steptype", "stage", "enabled", "sequence", "templetsource", "targeturl", "verb", "contenttype", "manualtimeoutminutes", "stepconfig", "headers", "stoponfailure", "assertonlyresponse");
                foreach (var testcase in workbookModel.Testcases)
                    foreach (var step in testcase.Steps)
                        config.AddRow(step.TestcaseIndex, testcase.Testcase, step.StepName, step.StepType.WireName(), step.Stage.ToString(), step.Enabled, step.Sequence, step.TemplateSource, GeneratedTokens.ToDisplay(step.TargetUrl), step.Verb, step.ContentType, step.ManualTimeoutMinutes, Mask(GeneratedTokens.ToDisplay(step.StepConfig)), Mask(GeneratedTokens.ToDisplay(string.Join("; ", step.Headers.Select(h => $"{h.Key}={h.Value}")))), step.StopOnFailure, step.AssertOnlyResponse);
            }
        }
        WriteResolvedCopies(workbook, run, requests);
        HistoryRunStateStore.WriteSheet(workbook, RunState, run, RerunOf);
        foreach (var sheet in workbook.Worksheets) { sheet.Columns().AdjustToContents(1, 80); sheet.SheetView.FreezeRows(1); sheet.Row(1).Style.Font.Bold = true; sheet.Row(1).Style.Fill.SetBackgroundColor(XLColor.LightBlue); }
        workbook.SaveAs(path); return path;
    }

    /// <summary>
    /// Exact copies of what ran, after every value was resolved: Config (one row per executed step), Request (one row per executed step with its
    /// cell values), Response (the expected cells), shaped like the workbook plus runid / iteration / environment. ResolvedValues is the same
    /// request values in long form and is always written, because a re-run with old data reads it.
    /// </summary>
    private void WriteResolvedCopies(XLWorkbook workbook, RunResult run, List<RequestResult> requests)
    {
        string Cell(RequestResult r, string key, string value) => MaskVariableContent(value, r.RedactedVariables, r.Variables);
        var executed = requests.OrderBy(r => r.TestcaseIndex).ThenBy(r => r.DataId).ThenBy(r => r.Iteration).ThenBy(r => r.SequenceOrder).ToList();

        var values = workbook.AddWorksheet("ResolvedValues");
        WriteTable(values, "runid", "testcaseindex", "testcase", "dataid", "iteration", "environment", "stepname", "cell", "value");
        foreach (var r in executed)
        {
            foreach (var variable in r.Variables.Where(v => IsRequestCell(r, v.Key)))
                values.AddRow(run.RunId, r.TestcaseIndex, r.Testcase, r.DataId, r.Iteration, r.Environment, r.StepName, variable.Key, Cell(r, variable.Key, variable.Value));
        }

        if (Enabled("Config"))
        {
            var config = workbook.AddWorksheet("Config");
            WriteTable(config, "runid", "iteration", "environment", "testcaseindex", "testcase", "stepname", "steptype", "sequenceorder", "templetsource", "targeturl", "verb", "contenttype", "wait", "stepconfig");
            foreach (var r in executed)
                config.AddRow(run.RunId, r.Iteration, r.Environment, r.TestcaseIndex, r.Testcase, r.StepName, r.StepType, r.SequenceOrder, r.TemplateSource, Mask(GeneratedTokens.ToDisplay(r.TargetUrl)), r.Verb, r.ContentType, r.WaitMs, Mask(r.StepConfig));
        }
        if (Enabled("Request"))
        {
            var sheet = workbook.AddWorksheet("Request");
            var names = executed.SelectMany(r => r.Variables.Keys.Where(k => IsRequestCell(r, k)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            WriteTable(sheet, ["runid", "iteration", "environment", "testcaseindex", "testcase", "dataid", "stepname", .. names]);
            foreach (var r in executed)
            {
                var row = (sheet.LastRowUsed()?.RowNumber() ?? 1) + 1;
                object?[] head = [run.RunId, r.Iteration, r.Environment, r.TestcaseIndex, r.Testcase, r.DataId, r.StepName];
                for (var i = 0; i < head.Length; i++) sheet.Cell(row, i + 1).Value = XLCellValue.FromObject(head[i]);
                for (var i = 0; i < names.Count; i++)
                    if (r.Variables.TryGetValue(names[i], out var value)) sheet.Cell(row, head.Length + i + 1).Value = ValueSpill.Fit(Cell(r, names[i], value));
            }
        }
        if (Enabled("Response"))
        {
            var sheet = workbook.AddWorksheet("Response");
            WriteTable(sheet, "runid", "iteration", "environment", "testcaseindex", "testcase", "dataid", "stepname", "responsepath", "assertionverb", "expectedvalue", "extractvariable");
            foreach (var r in executed)
                foreach (var a in r.Assertions)
                    sheet.AddRow(run.RunId, r.Iteration, r.Environment, a.TestcaseIndex, a.Testcase, a.DataId, a.StepName, a.ResponsePath, a.AssertionVerb, Mask(a.ExpectedValue), a.ExtractVariable);
        }
    }

    /// <summary>True for a step's own request cell; false for <c>env</c> and for values taken from earlier responses (named like Step.path).</summary>
    private static bool IsRequestCell(RequestResult r, string name)
        => !name.Equals("env", StringComparison.OrdinalIgnoreCase) && !name.Contains('.') && !r.Correlations.Any(c => c.Variable.Equals(name, StringComparison.OrdinalIgnoreCase));

    private string Mask(string value) => InternalLogger.MaskSensitiveData(value, _config.MaskSensitiveData);
    private string MaskVariableContent(string value, IReadOnlySet<string> redactedVariables, IReadOnlyDictionary<string, string> variables)
        => Mask(InternalLogger.RedactVariableValues(value, redactedVariables, variables));
    private bool Enabled(string sheetName) => HistorySheetCatalog.IsEnabled(_config.HistorySheets, sheetName);
    private static void WriteTable(IXLWorksheet sheet, params string[] headers) { for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i]; }
}

internal static class WorksheetExtensions
{
    public static void AddRow(this IXLWorksheet sheet, params object?[] values)
    { var row = sheet.LastRowUsed()?.RowNumber() + 1 ?? 2; for (var i = 0; i < values.Length; i++) sheet.Cell(row, i + 1).Value = XLCellValue.FromObject(ValueSpill.Fit(values[i])); }
}

/// <summary>Removes the value files (see <see cref="ValueSpill"/>) of history that Storage &amp; Cleanup has deleted.</summary>
public static class HistoryValueFiles
{
    /// <summary>
    /// Deletes each run's value folder that is older than every history workbook still there (all of them when none is left),
    /// because the workbooks that pointed at those files are gone.
    /// </summary>
    public static int Prune(string historyFolder)
    {
        var root = Path.Combine(historyFolder, "_values");
        if (!Directory.Exists(root)) return 0;
        var remaining = Directory.EnumerateFiles(historyFolder, "*.xlsx", SearchOption.AllDirectories).Select(File.GetCreationTimeUtc).ToList();
        var oldest = remaining.Count == 0 ? DateTime.MaxValue : remaining.Min().AddMinutes(-1);
        var removed = 0;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (Directory.GetCreationTimeUtc(folder) >= oldest) continue;
            try { Directory.Delete(folder, recursive: true); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}

/// <summary>
/// An Excel cell holds at most 32,767 characters. A longer value (a big response, a file read by a step) is saved as a text file next to the history
/// and the cell holds <c>&lt;from_filepath&gt;</c> and the file's path instead, so the full value is still there and a re-run can read it back.
/// </summary>
internal static class ValueSpill
{
    public const int CellLimit = 32767;

    /// <summary>Where the files of the history being written go; set by <see cref="HistoryWriter"/> while it writes.</summary>
    [ThreadStatic] public static string? Folder;

    public static object? Fit(object? value) => value is string text ? Fit(text) : value;

    public static string Fit(string text)
    {
        if (text.Length <= CellLimit || Folder is null) return text.Length <= CellLimit ? text : text[..CellLimit];
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant() + ".txt";
        var path = Path.Combine(Folder, name);
        Directory.CreateDirectory(Folder);
        if (!File.Exists(path)) File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        return "<from_filepath>" + Path.GetFullPath(path);
    }
}
