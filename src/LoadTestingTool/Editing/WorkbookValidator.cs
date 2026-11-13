using System.Globalization;
using LoadTestingTool.Domain;
using LoadTestingTool.Excel;

namespace LoadTestingTool.Editing;

/// <summary>
/// Finds, all at once, the problems <see cref="WorkbookReader"/> would report one at a time
/// (it stops at the first), and flags settings it silently ignores or defaults.
/// </summary>
public static class WorkbookValidator
{
    private static readonly string[] BooleanColumns = ["enabled", "stoponfailure", "ignoreempty", "assert", "assertenabled", "assertonlyresponse", "assertonlyinresponse"];
    private static readonly string[] NumberColumns = ["wait", "timeout", "manualtimeoutminutes"];
    private static readonly HashSet<string> ReservedResponseColumns = new(["testcaseindex", "testcase", "dataid", "extractvariable"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<WorkbookProblem> Validate(WorkbookDocument document)
    {
        var problems = new List<WorkbookProblem>();
        var stepsByTestcase = ValidateConfig(document.Config, problems);
        var requestKeys = ValidateDataSheet(document.Request, problems, duplicateSeverity: ProblemSeverity.Error);
        var responseKeys = ValidateDataSheet(document.Response, problems, duplicateSeverity: ProblemSeverity.Warning);
        ValidateResponseColumns(document.Response, stepsByTestcase, problems);
        ValidateCrossSheet(document, stepsByTestcase, requestKeys, responseKeys, problems);
        return problems;
    }

    private static Dictionary<int, HashSet<string>> ValidateConfig(SheetTable table, List<WorkbookProblem> problems)
    {
        var steps = new Dictionary<int, HashSet<string>>();
        RequireColumns(table, ["testcaseindex", "testcase", "stepname"], problems);
        var seen = new HashSet<(int, string)>();
        foreach (var row in table.Rows)
        {
            if (row.IsBlank) continue;
            var index = EffectiveIndex(table, row, problems);
            var testcase = Text(row, "testcase");
            var stepName = Text(row, "stepname");
            if (testcase.Length == 0 || stepName.Length == 0)
                Add(problems, ProblemSeverity.Error, table, row, "The row is missing a testcase or stepname.");

            var type = StepType.Http;
            try { type = StepTypeParser.Parse(Text(row, "steptype")); }
            catch (InvalidDataException ex) { Add(problems, ProblemSeverity.Error, table, row, ex.Message); }
            try { ExecutionStageParser.Parse(Text(row, "stage")); }
            catch (InvalidDataException ex) { Add(problems, ProblemSeverity.Error, table, row, ex.Message); }

            if (type is StepType.Http or StepType.GraphQL or StepType.CaseQL && Text(row, "targeturl").Length == 0)
                Add(problems, ProblemSeverity.Error, table, row, $"{type.WireName()} steps need a targetUrl.");
            if (type == StepType.Http && Text(row, "templetsource").Length == 0)
                Add(problems, ProblemSeverity.Error, table, row, "HTTP steps need a template (templetsource).");
            if (type is not StepType.Http and not StepType.ActionManual && Text(row, "stepconfig").Length == 0)
                Add(problems, ProblemSeverity.Error, table, row, $"{type.WireName()} steps need a StepConfig.");

            foreach (var name in BooleanColumns)
            {
                var value = Text(row, name);
                if (value.Length > 0 && !bool.TryParse(value, out _))
                    Add(problems, ProblemSeverity.Warning, table, row, $"{name} '{value}' is not True or False; the runner will use its default instead.");
            }
            foreach (var name in NumberColumns)
            {
                var value = Text(row, name);
                if (value.Length > 0 && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    Add(problems, ProblemSeverity.Warning, table, row, $"{name} '{value}' is not a whole number; the runner will use 0 instead.");
            }

            if (stepName.Length == 0) continue;
            if (!seen.Add((index, stepName.ToLowerInvariant())))
                Add(problems, ProblemSeverity.Error, table, row, $"Duplicate config testcaseindex + stepname: {index} + {stepName}.");
            if (!steps.TryGetValue(index, out var names)) steps[index] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names.Add(stepName);
        }
        return steps;
    }

    private static HashSet<(int, string)> ValidateDataSheet(SheetTable table, List<WorkbookProblem> problems, ProblemSeverity duplicateSeverity)
    {
        var keys = new HashSet<(int, string)>();
        RequireColumns(table, ["testcaseindex", "testcase", "dataid"], problems);
        foreach (var row in table.Rows)
        {
            if (row.IsBlank) continue;
            var index = EffectiveIndex(table, row, problems);
            var dataId = Text(row, "dataid");
            if (dataId.Length == 0)
            {
                Add(problems, ProblemSeverity.Error, table, row, "The row has an empty dataid.");
                continue;
            }
            if (!keys.Add((index, dataId.ToLowerInvariant())))
                Add(problems, duplicateSeverity, table, row, $"Duplicate row for testcaseindex + dataid: {index} + {dataId}.");
        }
        return keys;
    }

    private static void ValidateResponseColumns(SheetTable table, Dictionary<int, HashSet<string>> stepsByTestcase, List<WorkbookProblem> problems)
    {
        var assertionColumns = new List<(int Column, string Header, string Step, string Path, string Verb)>();
        for (var column = 0; column < table.ColumnCount; column++)
        {
            var header = table.Headers[column];
            if (ReservedResponseColumns.Contains(header)) continue;
            if (AssertionHeaderError(header) is { } error)
            {
                problems.Add(new WorkbookProblem(ProblemSeverity.Error, table.SheetName, null, error));
                continue;
            }
            var (step, path, verb) = SplitAssertionHeader(header);
            assertionColumns.Add((column, header, step, path, verb));
        }

        foreach (var row in table.Rows)
        {
            if (row.IsBlank) continue;
            var index = WorkbookDocument.RowIndex(row) ?? row.SourceRow ?? -1;
            foreach (var column in assertionColumns)
            {
                if (row.GetAt(column.Column).Trim().Length == 0) continue;
                if (stepsByTestcase.TryGetValue(index, out var steps) && !steps.Contains(column.Step))
                    Add(problems, ProblemSeverity.Warning, table, row, $"Assertion column '{column.Header}' refers to step '{column.Step}', which is not in testcase {index}, so it is never checked.");
            }

            var extract = Text(row, "extractvariable");
            foreach (var mapping in extract.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = mapping.Split(':', 2);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                {
                    Add(problems, ProblemSeverity.Error, table, row, $"Invalid extractvariable mapping '{mapping}'. Expected stepname.responsepath:alias.");
                    continue;
                }
                var target = parts[0].Trim();
                var separator = target.IndexOf('.');
                if (separator <= 0 || separator == target.Length - 1)
                {
                    Add(problems, ProblemSeverity.Error, table, row, $"Response column '{target}' must use stepname.responsepath format.");
                    continue;
                }
                var step = target[..separator].Trim();
                var path = target[(separator + 1)..].Trim();
                var matches = assertionColumns.Any(c => c.Step.Equals(step, StringComparison.OrdinalIgnoreCase) && c.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
                    && (row.GetAt(c.Column).Trim().Length > 0 || WorkbookReader.AllowsBlankExpectedValue(c.Verb)));
                if (!matches)
                    Add(problems, ProblemSeverity.Error, table, row, $"extractvariable mapping '{mapping}' does not match an assertion column.");
            }
        }
    }

    private static void ValidateCrossSheet(WorkbookDocument document, Dictionary<int, HashSet<string>> stepsByTestcase, HashSet<(int, string)> requestKeys, HashSet<(int, string)> responseKeys, List<WorkbookProblem> problems)
    {
        foreach (var index in stepsByTestcase.Keys.OrderBy(x => x))
            if (!requestKeys.Any(k => k.Item1 == index))
                problems.Add(new WorkbookProblem(ProblemSeverity.Error, document.Request.SheetName, null, $"No request rows found for testcaseindex {index}. Add at least one request row with a unique dataid."));

        foreach (var row in document.Request.Rows)
        {
            if (row.IsBlank || Text(row, "dataid").Length == 0) continue;
            var index = WorkbookDocument.RowIndex(row) ?? row.SourceRow ?? -1;
            var dataId = Text(row, "dataid");
            if (!responseKeys.Contains((index, dataId.ToLowerInvariant())))
                Add(problems, ProblemSeverity.Error, document.Request, row, $"No response row found for testcaseindex {index} and dataid '{dataId}'.");
            if (!stepsByTestcase.ContainsKey(index))
                Add(problems, ProblemSeverity.Warning, document.Request, row, $"Testcase {index} has no steps in the config sheet, so this row never runs.");
        }

        foreach (var row in document.Response.Rows)
        {
            if (row.IsBlank || Text(row, "dataid").Length == 0) continue;
            var index = WorkbookDocument.RowIndex(row) ?? row.SourceRow ?? -1;
            if (!requestKeys.Contains((index, Text(row, "dataid").ToLowerInvariant())))
                Add(problems, ProblemSeverity.Warning, document.Response, row, $"There is no request row for testcaseindex {index} and dataid '{Text(row, "dataid")}', so these expected values are never used.");
        }
    }

    private static string? AssertionHeaderError(string header)
    {
        header = header.Trim();
        if (header.StartsWith('{'))
        {
            var closing = header.IndexOf('}');
            if (closing <= 1 || !WorkbookReader.IsVerb(header[1..closing]))
                return $"Response header '{header}' has an invalid assertion verb. Use {{verb}}step.path.";
            header = header[(closing + 1)..].Trim();
        }
        var separator = header.IndexOf('.');
        return separator <= 0 || separator == header.Length - 1
            ? $"Response column '{header}' must use stepname.responsepath format."
            : null;
    }

    private static (string Step, string Path, string Verb) SplitAssertionHeader(string header)
    {
        header = header.Trim();
        var verb = "eq";
        if (header.StartsWith('{'))
        {
            var closing = header.IndexOf('}');
            verb = header[1..closing].Trim().ToLowerInvariant();
            header = header[(closing + 1)..].Trim();
        }
        var separator = header.IndexOf('.');
        return (header[..separator].Trim(), header[(separator + 1)..].Trim(), verb);
    }

    private static void RequireColumns(SheetTable table, string[] required, List<WorkbookProblem> problems)
    {
        foreach (var name in required)
            if (table.FindColumn(name) < 0)
                problems.Add(new WorkbookProblem(ProblemSeverity.Error, table.SheetName, null, $"Worksheet '{table.SheetName}' is missing required column '{name}'."));
    }

    /// <summary>The testcase index the runner will use: the cell value, or the sheet row number when it is not a number.</summary>
    private static int EffectiveIndex(SheetTable table, TableRow row, List<WorkbookProblem> problems)
    {
        var text = Text(row, "testcaseindex");
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) return index;
        var fallback = row.SourceRow ?? -1;
        if (table.FindColumn("testcaseindex") >= 0)
            Add(problems, ProblemSeverity.Warning, table, row, $"testcaseindex '{text}' is not a whole number; the runner will use the sheet row number ({fallback}) instead.");
        return fallback;
    }

    private static string Text(TableRow row, string header) => row.Get(header).Trim();

    private static void Add(List<WorkbookProblem> problems, ProblemSeverity severity, SheetTable table, TableRow row, string message)
        => problems.Add(new WorkbookProblem(severity, table.SheetName, row.SourceRow, message));
}
