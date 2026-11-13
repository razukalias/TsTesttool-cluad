using ClosedXML.Excel;
using LoadTestingTool.Domain;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LoadTestingTool.Excel;

public sealed class WorkbookReader
{
    public WorkbookModel Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Excel workbook was not found.", path);
        using var workbookFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var workbook = new XLWorkbook(workbookFile);
        var compiledCellCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var config = RequireSheet(workbook, "config");
        var configHeaders = Headers(config);
        foreach (var required in new[] { "testcaseindex", "testcase", "stepname" }) Require(configHeaders, required, "config");
        var steps = new List<RequestStep>();
        foreach (var row in config.RowsUsed().Skip(1))
        {
            if (IsBlank(row)) continue;
            var index = Int(configHeaders, row, "testcaseindex", row.RowNumber());
            var testcase = Text(configHeaders, row, "testcase");
            var stepName = Text(configHeaders, row, "stepname");
            var template = Text(configHeaders, row, "templetsource");
            var url = Compile(Text(configHeaders, row, "targeturl"), workbook, config, compiledCellCache, row.RowNumber());
            var stepType = StepTypeParser.Parse(Text(configHeaders, row, "steptype", "HTTP"));
            if (string.IsNullOrWhiteSpace(testcase) || string.IsNullOrWhiteSpace(stepName)) throw new InvalidDataException($"config row {row.RowNumber()} is missing testcase or stepname.");
            if ((stepType is StepType.Http or StepType.GraphQL or StepType.CaseQL) && string.IsNullOrWhiteSpace(url)) throw new InvalidDataException($"config row {row.RowNumber()} requires targetUrl for {stepType.WireName()}.");
            if (stepType == StepType.Http && string.IsNullOrWhiteSpace(template)) throw new InvalidDataException($"config row {row.RowNumber()} requires templetsource for HTTP.");
            if (stepType != StepType.Http && stepType != StepType.ActionManual && string.IsNullOrWhiteSpace(Text(configHeaders, row, "stepconfig"))) throw new InvalidDataException($"config row {row.RowNumber()} requires StepConfig for {stepType.WireName()}.");
            steps.Add(new RequestStep
            {
                TestcaseIndex = index, Testcase = testcase, StepName = stepName, Sequence = row.RowNumber(), TemplateSource = template, TargetUrl = url,
                StepType = stepType, Stage = ExecutionStageParser.Parse(Text(configHeaders, row, "stage", "Test")), Enabled = Bool(configHeaders, row, "enabled", true), TimeoutSeconds = Math.Max(0, Int(configHeaders, row, "timeout", 0)), ManualTimeoutMinutes = Math.Max(0, Int(configHeaders, row, "manualtimeoutminutes", 15)), StepConfig = Compile(Text(configHeaders, row, "stepconfig", "{}"), workbook, config, compiledCellCache, row.RowNumber()),
                Verb = Text(configHeaders, row, "verb", "POST").ToUpperInvariant(), ContentType = Text(configHeaders, row, "contenttype"),
                WaitMs = Math.Max(0, Int(configHeaders, row, "wait", 0)), ExpectedStatus = Compile(Text(configHeaders, row, "expectedstatus"), workbook, config, compiledCellCache, row.RowNumber()),
                StopOnFailure = Bool(configHeaders, row, "stoponfailure", true), IgnoreEmpty = Bool(configHeaders, row, "ignoreempty", false),
                AssertEnabled = Bool(configHeaders, row, "assert", Bool(configHeaders, row, "assertenabled", true)),
                AssertOnlyResponse = Bool(configHeaders, row, "assertonlyresponse", Bool(configHeaders, row, "assertonlyinresponse", false)),
                Environments = [], Headers = ExtractHeaders(configHeaders, row, workbook, config, compiledCellCache)
            });
        }
        var duplicate = steps.GroupBy(s => (s.TestcaseIndex, s.StepName), StepTupleComparer.Instance).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Duplicate config testcaseindex + stepname: {duplicate.Key.Item1} + {duplicate.Key.Item2}.");
        var testcases = steps.GroupBy(s => (s.TestcaseIndex, Testcase: s.Testcase), TestcaseTupleComparer.Instance).OrderBy(g => g.Key.TestcaseIndex).Select(g => new TestcaseDefinition { TestcaseIndex = g.Key.TestcaseIndex, Testcase = g.Key.Testcase, Steps = g.OrderBy(s => s.Sequence).ToList() }).ToList();
        var requestData = ReadRequests(workbook, compiledCellCache);
        var assertions = ReadAssertions(workbook, compiledCellCache);
        foreach (var testcase in testcases)
        {
            var rows = requestData.Values.Where(r => r.TestcaseIndex == testcase.TestcaseIndex).ToList();
            if (rows.Count == 0) throw new InvalidDataException($"No request rows found for testcaseindex {testcase.TestcaseIndex}. Add at least one request row with a unique dataid.");
            foreach (var row in rows) if (!assertions.Keys.Any(k => k.Item1 == testcase.TestcaseIndex && k.Item2.Equals(row.DataId, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException($"No response row found for testcaseindex {testcase.TestcaseIndex} and dataid '{row.DataId}'.");
        }
        return new WorkbookModel { Testcases = testcases, RequestData = requestData, Assertions = assertions };
    }

    private static Dictionary<(int, string), RequestDataRow> ReadRequests(XLWorkbook workbook, Dictionary<string, string> compiledCellCache)
    {
        var result = new Dictionary<(int, string), RequestDataRow>(DataKeyComparer.Instance);
        var testcaseRowNumbers = new Dictionary<int, int>();
        var sheet = RequireSheet(workbook, "request");
        var h = Headers(sheet); Require(h, "testcaseindex", "request"); Require(h, "testcase", "request"); Require(h, "dataid", "request");
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            if (IsBlank(row)) continue;
            var index = Int(h, row, "testcaseindex", row.RowNumber()); var dataId = Text(h, row, "dataid");
            if (string.IsNullOrWhiteSpace(dataId)) throw new InvalidDataException($"request row {row.RowNumber()} has an empty dataid.");
            var key = (index, dataId);
            if (result.ContainsKey(key)) throw new InvalidDataException($"Duplicate request row for testcaseindex + dataid: {index} + {dataId}.");
            var testcaseRowNumber = testcaseRowNumbers.TryGetValue(index, out var lastRowNumber) ? lastRowNumber + 1 : 1;
            testcaseRowNumbers[index] = testcaseRowNumber;
            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var redacted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in h.OrderBy(x => x.Value))
            {
                if (column.Key is "testcaseindex" or "testcase" or "dataid" or "dontrun") continue;
                var variableName = CanonicalRequestVariableName(column.Key);
                var cellKey = $"{sheet.Name}!{column.Key}!{row.RowNumber()}";
                if (!compiledCellCache.TryGetValue(cellKey, out var compiledValue))
                {
                    compiledValue = Compile(Text(h, row, column.Key), workbook, sheet, compiledCellCache, row.RowNumber());
                    compiledCellCache[cellKey] = compiledValue;
                }
                vars[variableName] = compiledValue;
                if (ShouldRedactRequestVariable(column.Key)) redacted.Add(variableName);
            }
            result[key] = new RequestDataRow { TestcaseIndex = index, ExcelRowNumber = row.RowNumber(), TestcaseRowNumber = testcaseRowNumber, Testcase = Text(h, row, "testcase"), DataId = dataId, Dontrun = SplitValues(Text(h, row, "dontrun")), Variables = vars, RedactedVariables = redacted };
        }
        return result;
    }

    private static Dictionary<(int, string, string), IReadOnlyList<AssertionDefinition>> ReadAssertions(XLWorkbook workbook, Dictionary<string, string> compiledCellCache)
    {
        var result = new Dictionary<(int, string, string), IReadOnlyList<AssertionDefinition>>(AssertionKeyComparer.Instance);
        var sheet = RequireSheet(workbook, "response");
        var h = Headers(sheet); Require(h, "testcaseindex", "response"); Require(h, "testcase", "response"); Require(h, "dataid", "response");
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            if (IsBlank(row)) continue;
            var index = Int(h, row, "testcaseindex", row.RowNumber()); var testcase = Text(h, row, "testcase"); var dataId = Text(h, row, "dataid");
            if (string.IsNullOrWhiteSpace(dataId)) throw new InvalidDataException($"response row {row.RowNumber()} has an empty dataid.");
            var assertions = new List<AssertionDefinition>();
            foreach (var column in h.OrderBy(x => x.Value))
            {
                if (column.Key is "testcaseindex" or "testcase" or "dataid" or "extractvariable") continue;
                var (stepName, path, headerVerb) = ParseAssertionHeader(column.Key);
                var cell = Compile(row.Cell(column.Value).GetString().Trim(), workbook, sheet, compiledCellCache, row.RowNumber());
                // Empty cells normally mean that no equality/content assertion was configured.
                // Structural assertions are different: {empty}, {notempty}, {exists}, and
                // {notexists} intentionally use a blank expected value.
                if (string.IsNullOrWhiteSpace(cell) && !AllowsBlankExpectedValue(headerVerb)) continue;
                var (cellVerb, expected) = ParseAssertionCell(cell);
                assertions.Add(new AssertionDefinition { TestcaseIndex = index, Testcase = testcase, DataId = dataId, StepName = stepName, ResponsePath = path, HeaderAssertionVerb = headerVerb, AssertionVerb = string.IsNullOrWhiteSpace(cellVerb) ? headerVerb : cellVerb, ExpectedValue = expected, ExcelRowNumber = row.RowNumber(), ExpectedValueColumn = column.Value, ResponseColumn = column.Value });
            }
            var extractText = Text(h, row, "extractvariable");
            foreach (var mapping in extractText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = mapping.Split(':', 2);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) throw new InvalidDataException($"Invalid extractvariable mapping '{mapping}'. Expected stepname.responsepath:alias.");
                var (stepName, path) = SplitStepPath(parts[0].Trim()); var match = assertions.FirstOrDefault(a => a.StepName.Equals(stepName, StringComparison.OrdinalIgnoreCase) && a.ResponsePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (match is null) throw new InvalidDataException($"extractvariable mapping '{mapping}' does not match an assertion column.");
                assertions[assertions.IndexOf(match)] = new AssertionDefinition { TestcaseIndex = match.TestcaseIndex, Testcase = match.Testcase, DataId = match.DataId, StepName = match.StepName, ResponsePath = match.ResponsePath, HeaderAssertionVerb = match.HeaderAssertionVerb, AssertionVerb = match.AssertionVerb, ExpectedValue = match.ExpectedValue, ExtractVariable = parts[1].Trim(), ExcelRowNumber = match.ExcelRowNumber, ExpectedValueColumn = match.ExpectedValueColumn, ResponseColumn = match.ResponseColumn };
            }
            foreach (var group in assertions.GroupBy(a => (a.TestcaseIndex, a.DataId, a.StepName), AssertionKeyComparer.Instance)) result[group.Key] = group.ToList();
            // Keep the response-row identity even when the row intentionally has no
            // assertions. Workbook validation must distinguish an empty response row
            // from a missing response row.
            if (assertions.Count == 0) result[(index, dataId, string.Empty)] = [];
        }
        return result;
    }

    private static (string StepName, string Path, string HeaderVerb) ParseAssertionHeader(string header)
    {
        header = header.Trim();
        var verb = "eq";
        if (header.StartsWith("{", StringComparison.Ordinal))
        {
            var closing = header.IndexOf('}');
            if (closing <= 1 || !IsVerb(header[1..closing])) throw new InvalidDataException($"Response header '{header}' has an invalid assertion verb. Use {{verb}}step.path.");
            verb = header[1..closing].Trim().ToLowerInvariant();
            header = header[(closing + 1)..].Trim();
        }
        var (step, path) = SplitStepPath(header);
        return (step, path, verb);
    }
    private static (string StepName, string Path) SplitStepPath(string value) { var separator = value.IndexOf('.'); if (separator <= 0 || separator == value.Length - 1) throw new InvalidDataException($"Response column '{value}' must use stepname.responsepath format."); return (value[..separator].Trim(), value[(separator + 1)..].Trim()); }
    private static (string Verb, string Expected) ParseAssertionCell(string cell)
    {
        cell = cell.Trim();
        if (cell.StartsWith("{", StringComparison.Ordinal))
        {
            var closing = cell.IndexOf('}');
            if (closing > 1 && IsVerb(cell[1..closing]))
                return (cell[1..closing].Trim().ToLowerInvariant(), cell[(closing + 1)..]);
        }
        return (string.Empty, cell);
    }
    private static IXLWorksheet RequireSheet(XLWorkbook workbook, string name) => workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException($"Required worksheet '{name}' was not found.");
    private static Dictionary<string, int> Headers(IXLWorksheet sheet) => sheet.FirstRowUsed()?.CellsUsed().ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase) ?? throw new InvalidDataException($"Worksheet '{sheet.Name}' has no header row.");
    private static void Require(Dictionary<string, int> h, string key, string sheet) { if (!h.ContainsKey(key)) throw new InvalidDataException($"Worksheet '{sheet}' is missing required column '{key}'."); }
    private static string Text(Dictionary<string, int> h, IXLRow row, string key, string fallback = "") => h.TryGetValue(key, out var col) ? row.Cell(col).GetString().Trim() : fallback;
    private static string CanonicalRequestVariableName(string header)
    {
        var value = header.Trim();
        if (value.EndsWith("_Log", StringComparison.OrdinalIgnoreCase) || value.EndsWith("_NoLog", StringComparison.OrdinalIgnoreCase))
            value = value[..value.LastIndexOf('_')];
        return value;
    }
    private static bool ShouldRedactRequestVariable(string header)
    {
        var value = header.Trim();
        if (value.EndsWith("_Log", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.EndsWith("_NoLog", StringComparison.OrdinalIgnoreCase)) return true;
        return CanonicalRequestVariableName(value).StartsWith("fileData", StringComparison.OrdinalIgnoreCase);
    }
    private static int Int(Dictionary<string, int> h, IXLRow row, string key, int fallback) => int.TryParse(Text(h, row, key), out var value) ? value : fallback;
    private static bool Bool(Dictionary<string, int> h, IXLRow row, string key, bool fallback) => bool.TryParse(Text(h, row, key), out var value) ? value : fallback;
    private static bool IsBlank(IXLRow row) => row.CellsUsed().All(c => string.IsNullOrWhiteSpace(c.GetString()));
    private static IReadOnlyList<string> SplitValues(string value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static bool IsVerb(string value) => new[] { "eq", "ne", "lt", "lte", "gt", "gte", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "in", "size" }.Contains(value.Trim().ToLowerInvariant());
    private static bool AllowsBlankExpectedValue(string value) => value.Equals("empty", StringComparison.OrdinalIgnoreCase)
        || value.Equals("notempty", StringComparison.OrdinalIgnoreCase)
        || value.Equals("exists", StringComparison.OrdinalIgnoreCase)
        || value.Equals("notexists", StringComparison.OrdinalIgnoreCase);
    private static string Compile(string value, XLWorkbook workbook, IXLWorksheet currentSheet, Dictionary<string, string> compiledCellCache, int? currentRowNumber = null) => Compile(value, workbook, currentSheet, compiledCellCache, new HashSet<string>(StringComparer.OrdinalIgnoreCase), currentRowNumber);
    private static string Compile(string value, XLWorkbook workbook, IXLWorksheet currentSheet, Dictionary<string, string> compiledCellCache, HashSet<string> resolving, int? currentRowNumber)
    {
        var result = value ?? string.Empty;
        result = Regex.Replace(result, @"<From_([^_>]+)_([^_>]+)_(\d+|r)>", m =>
        {
            var sheetName = m.Groups[1].Value;
            var header = m.Groups[2].Value;
            if (m.Groups[3].Value.Equals("r", StringComparison.OrdinalIgnoreCase))
            {
                if (sheetName.Equals("response", StringComparison.OrdinalIgnoreCase) && header.Contains('.', StringComparison.Ordinal)) return m.Value;
                if (!currentRowNumber.HasValue) throw new InvalidDataException($"Cannot resolve corresponding row for <From_{sheetName}_{header}_r> without an active workbook row.");
                var correspondingRow = CorrespondingRow(workbook, currentSheet, currentSheet.Row(currentRowNumber.Value), sheetName);
                return Cell(workbook, sheetName, header, correspondingRow, compiledCellCache, resolving);
            }
            return sheetName.Equals("response", StringComparison.OrdinalIgnoreCase) && header.Contains('.', StringComparison.Ordinal)
                ? m.Value
                : Cell(workbook, sheetName, header, int.Parse(m.Groups[3].Value), compiledCellCache, resolving);
        }, RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<From_([^_>]+)_(\d+)>", m =>
            m.Groups[1].Value.StartsWith("response_", StringComparison.OrdinalIgnoreCase)
                ? m.Value
                : Cell(workbook, currentSheet.Name, m.Groups[1].Value, int.Parse(m.Groups[2].Value), compiledCellCache, resolving), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<(datum|currentdatum|currenttime|currentdatetime|currenttimestamp)(?::([^>]+))?>", m => FormatDateToken(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<randomnumber(?::([^>]+))?>", m => FormatRandomNumberToken(m.Groups[1].Success ? m.Groups[1].Value : null), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<guid(?::([NnDdBbPp]))?>", m => FormatGuidToken(m.Groups[1].Success ? m.Groups[1].Value : null), RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"<randomnumber_(\d+)>", m => FormatRandomNumberToken(m.Groups[1].Value), RegexOptions.IgnoreCase);
        result = result.Replace("<datum>", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase).Replace("<currentdatum>", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase).Replace("<currenttime>", DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase).Replace("<currentdatetime>", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase).Replace("<currenttimestamp>", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static string FormatDateToken(string token, string? format)
    {
        var now = DateTime.Now;
        if (token.Equals("currenttimestamp", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(format))
            return DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(format)) format = token.Equals("datum", StringComparison.OrdinalIgnoreCase) || token.Equals("currentdatum", StringComparison.OrdinalIgnoreCase) ? "yyyy-MM-dd" : token.Equals("currenttime", StringComparison.OrdinalIgnoreCase) ? "HH:mm:ss" : "yyyy-MM-dd HH:mm:ss";
        return now.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatRandomNumberToken(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return Random.Shared.Next(0, 1_000_000).ToString(CultureInfo.InvariantCulture);
        format = format.Trim();
        var range = Regex.Match(format, "^(\\d+)-(\\d+)$");
        if (range.Success && int.TryParse(range.Groups[1].Value, out var min) && int.TryParse(range.Groups[2].Value, out var max) && min <= max)
            return Random.Shared.NextInt64(min, (long)max + 1).ToString(CultureInfo.InvariantCulture);
        if (format.All(char.IsDigit))
        {
            var digits = format.TrimStart('0').Length == 0 ? format.Length : int.Parse(format, CultureInfo.InvariantCulture);
            if (digits == 0) return "";
            var value = Random.Shared.NextInt64(0, (long)Math.Pow(10, Math.Min(digits, 18)));
            return value.ToString(new string('0', digits), CultureInfo.InvariantCulture);
        }
        throw new InvalidDataException($"Invalid randomnumber format '{format}'. Use digits, 000000, or min-max.");
    }

    private static string FormatGuidToken(string? format) => Guid.NewGuid().ToString(string.IsNullOrWhiteSpace(format) ? "D" : format.Trim().ToUpperInvariant());
    private static int CorrespondingRow(XLWorkbook workbook, IXLWorksheet currentSheet, IXLRow currentRow, string sourceSheetName)
    {
        var source = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(sourceSheetName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException($"Referenced sheet '{sourceSheetName}' was not found.");
        var currentHeaders = Headers(currentSheet); var sourceHeaders = Headers(source);
        if (!currentHeaders.TryGetValue("testcaseindex", out var currentIndexColumn) || !currentHeaders.TryGetValue("dataid", out var currentDataIdColumn)
            || !sourceHeaders.TryGetValue("testcaseindex", out var sourceIndexColumn) || !sourceHeaders.TryGetValue("dataid", out var sourceDataIdColumn))
            throw new InvalidDataException($"Cannot resolve corresponding row for <From_{sourceSheetName}_..._r>; both sheets must contain testcaseindex and dataid columns.");
        var testcaseIndex = currentRow.Cell(currentIndexColumn).GetString().Trim(); var dataId = currentRow.Cell(currentDataIdColumn).GetString().Trim();
        var match = source.RowsUsed().Skip(1).FirstOrDefault(row => row.Cell(sourceIndexColumn).GetString().Trim().Equals(testcaseIndex, StringComparison.OrdinalIgnoreCase) && row.Cell(sourceDataIdColumn).GetString().Trim().Equals(dataId, StringComparison.OrdinalIgnoreCase));
        return match?.RowNumber() ?? throw new InvalidDataException($"No corresponding row found in sheet '{sourceSheetName}' for testcaseindex '{testcaseIndex}' and dataid '{dataId}'.");
    }
    private static string Cell(XLWorkbook workbook, string sheetName, string header, int rowNumber, Dictionary<string, string> compiledCellCache, HashSet<string> resolving)
    {
        var sheet = workbook.Worksheets.FirstOrDefault(x => x.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException($"Referenced sheet '{sheetName}' was not found.");
        var column = sheet.FirstRowUsed()?.CellsUsed().FirstOrDefault(c => c.GetString().Trim().Equals(header, StringComparison.OrdinalIgnoreCase))?.Address.ColumnNumber ?? throw new InvalidDataException($"Referenced header '{header}' was not found in sheet '{sheetName}'.");
        var key = $"{sheet.Name}!{header}!{rowNumber}";
        if (compiledCellCache.TryGetValue(key, out var cachedValue)) return cachedValue;
        if (!resolving.Add(key)) throw new InvalidDataException($"Circular workbook reference detected at '{key}'.");
        try
        {
            var compiledValue = Compile(sheet.Cell(rowNumber, column).GetString(), workbook, sheet, compiledCellCache, resolving, rowNumber);
            compiledCellCache[key] = compiledValue;
            return compiledValue;
        }
        finally { resolving.Remove(key); }
    }
    private static Dictionary<string, string> ExtractHeaders(Dictionary<string, int> h, IXLRow row, XLWorkbook workbook, IXLWorksheet currentSheet, Dictionary<string, string> compiledCellCache) => h.Where(x => x.Key.StartsWith("header_", StringComparison.OrdinalIgnoreCase)).Select(x => (Name: x.Key[7..], Value: Compile(Text(h, row, x.Key), workbook, currentSheet, compiledCellCache, row.RowNumber()))).Where(x => !string.IsNullOrWhiteSpace(x.Value) || x.Value == "\"\"").ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);

    private sealed class DataKeyComparer : IEqualityComparer<(int, string)> { public static readonly DataKeyComparer Instance = new(); public bool Equals((int, string) x, (int, string) y) => x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase); public int GetHashCode((int, string) x) => HashCode.Combine(x.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(x.Item2)); }
    private sealed class StepTupleComparer : IEqualityComparer<(int, string)> { public static readonly StepTupleComparer Instance = new(); public bool Equals((int, string) x, (int, string) y) => x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase); public int GetHashCode((int, string) x) => HashCode.Combine(x.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(x.Item2)); }
    private sealed class TestcaseTupleComparer : IEqualityComparer<(int TestcaseIndex, string Testcase)> { public static readonly TestcaseTupleComparer Instance = new(); public bool Equals((int TestcaseIndex, string Testcase) x, (int TestcaseIndex, string Testcase) y) => x.TestcaseIndex == y.TestcaseIndex && string.Equals(x.Testcase, y.Testcase, StringComparison.OrdinalIgnoreCase); public int GetHashCode((int TestcaseIndex, string Testcase) x) => HashCode.Combine(x.TestcaseIndex, StringComparer.OrdinalIgnoreCase.GetHashCode(x.Testcase)); }
    private sealed class AssertionKeyComparer : IEqualityComparer<(int, string, string)> { public static readonly AssertionKeyComparer Instance = new(); public bool Equals((int, string, string) x, (int, string, string) y) => x.Item1 == y.Item1 && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Item3, y.Item3, StringComparison.OrdinalIgnoreCase); public int GetHashCode((int, string, string) x) => HashCode.Combine(x.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(x.Item2), StringComparer.OrdinalIgnoreCase.GetHashCode(x.Item3)); }
}
