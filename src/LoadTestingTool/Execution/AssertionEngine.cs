using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

public sealed class AssertionEngine
{
    public IReadOnlyList<AssertionResult> Evaluate(string body, string contentType, IReadOnlyList<AssertionDefinition> definitions, string runId, string requestId, int? httpStatus = null, bool ignoreEmptyUnconfigured = false, bool assertOnlyResponse = false)
    {
        if (definitions.Count == 0) return [];
        var values = Normalize(body, contentType).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (httpStatus.HasValue) values["statusCode"] = httpStatus.Value.ToString(CultureInfo.InvariantCulture);
        // A blank equality cell represents an optional field: if the API omits
        // that path entirely, there is no value to validate and the assertion
        // should not fail. A non-blank expectation remains strict.
        var activeDefinitions = definitions.Where(d => !IsOptionalMissing(d, values)).ToList();
        var results = activeDefinitions.Select(d => EvaluateOne(values, d, runId, requestId)).ToList();
        var expectedPaths = activeDefinitions.Select(d => d.ResponsePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var missing in expectedPaths.Where(path => !values.ContainsKey(path)))
            if (!results.Any(r => r.ResponsePath.Equals(missing, StringComparison.OrdinalIgnoreCase))) results.Add(Mismatch(runId, requestId, definitions[0], missing, "<missing>", $"Missing response path '{missing}' in payload."));
        // statusCode is runner metadata, not a JSON/XML payload field. It remains
        // available for an explicit response-sheet assertion, but is not an
        // unexpected payload field when no such assertion was configured.
        foreach (var unexpected in values.Keys.Where(path => !assertOnlyResponse
            && !path.Equals("statusCode", StringComparison.OrdinalIgnoreCase)
            && !expectedPaths.Contains(path)
            && (!ignoreEmptyUnconfigured || !IsEmptyUnconfiguredField(values, path))))
            results.Add(Mismatch(runId, requestId, definitions[0], unexpected, values[unexpected], $"Unexpected response path '{unexpected}' returned by payload."));
        return results;
    }

    private static bool IsOptionalMissing(AssertionDefinition definition, IReadOnlyDictionary<string, string> values)
    {
        var operation = string.IsNullOrWhiteSpace(definition.AssertionVerb) ? "eq" : definition.AssertionVerb.Trim();
        return operation.Equals("eq", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(definition.ExpectedValue)
            && !values.ContainsKey(definition.ResponsePath);
    }

    public IReadOnlyDictionary<string, string> Normalize(string body, string contentType)
    {
        if (string.IsNullOrWhiteSpace(body)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (contentType.Equals("xml", StringComparison.OrdinalIgnoreCase) || body.TrimStart().StartsWith("<")) return FlattenXml(XDocument.Parse(body));
        using var doc = JsonDocument.Parse(body);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FlattenJson(doc.RootElement, "", result);
        return result;
    }

    private static AssertionResult EvaluateOne(IReadOnlyDictionary<string, string> values, AssertionDefinition d, string runId, string requestId)
    {
        values.TryGetValue(d.ResponsePath, out var actual);
        var op = string.IsNullOrWhiteSpace(d.AssertionVerb) ? "eq" : d.AssertionVerb.ToLowerInvariant();
        var passed = op switch
        {
            "eq" => string.Equals(actual ?? "", d.ExpectedValue, StringComparison.OrdinalIgnoreCase),
            "ne" => !string.Equals(actual ?? "", d.ExpectedValue, StringComparison.OrdinalIgnoreCase),
            "lt" => CompareNumber(actual, d.ExpectedValue, out var lt) && lt < 0,
            "lte" => CompareNumber(actual, d.ExpectedValue, out var lte) && lte <= 0,
            "gt" => CompareNumber(actual, d.ExpectedValue, out var gt) && gt > 0,
            "gte" => CompareNumber(actual, d.ExpectedValue, out var gte) && gte >= 0,
            "contains" => actual?.Contains(d.ExpectedValue, StringComparison.OrdinalIgnoreCase) == true,
            "notcontains" => actual?.Contains(d.ExpectedValue, StringComparison.OrdinalIgnoreCase) != true,
            "startswith" => actual?.StartsWith(d.ExpectedValue, StringComparison.OrdinalIgnoreCase) == true,
            "endswith" => actual?.EndsWith(d.ExpectedValue, StringComparison.OrdinalIgnoreCase) == true,
            "regex" => actual is not null && System.Text.RegularExpressions.Regex.IsMatch(actual, d.ExpectedValue),
            "exists" => values.ContainsKey(d.ResponsePath),
            "notexists" => !values.ContainsKey(d.ResponsePath),
            "empty" => string.IsNullOrEmpty(actual),
            "notempty" => !string.IsNullOrEmpty(actual),
            "in" => d.ExpectedValue.Split('|').Contains(actual ?? "", StringComparer.OrdinalIgnoreCase),
            "size" => int.TryParse(d.ExpectedValue, out var size) && actual is not null && actual.Length == size,
            _ => throw new InvalidDataException($"Unsupported assertion verb '{d.AssertionVerb}'.")
        };
        var actualValue = actual ?? "<missing>";
        var failureMessage = passed ? "" : $"Assertion failed: {d.ResponsePath} {op} '{d.ExpectedValue}', actual '{actualValue}'.";
        return new AssertionResult { RunId = runId, RequestId = requestId, TestcaseIndex = d.TestcaseIndex, Testcase = d.Testcase, DataId = d.DataId, StepName = d.StepName, ResponsePath = d.ResponsePath, AssertionVerb = op, AssertionType = AssertionSemantics.Type(op, actualValue, failureMessage), AssertionMeaning = AssertionSemantics.Meaning(op, actualValue, failureMessage), ExpectedValue = d.ExpectedValue, ActualValue = actualValue, Result = passed ? "PASS" : "FAIL", FailureMessage = failureMessage, ExtractVariable = d.ExtractVariable, ExcelRowNumber = d.ExcelRowNumber, ExpectedValueColumn = d.ExpectedValueColumn };
    }

    private static AssertionResult Mismatch(string runId, string requestId, AssertionDefinition source, string path, string actualValue, string message) => new() { RunId = runId, RequestId = requestId, TestcaseIndex = source.TestcaseIndex, Testcase = source.Testcase, DataId = source.DataId, StepName = source.StepName, ResponsePath = path, AssertionVerb = "contract", AssertionType = AssertionSemantics.Type("contract", actualValue, message), AssertionMeaning = AssertionSemantics.Meaning("contract", actualValue, message), ExpectedValue = "<response-sheet-path>", ActualValue = actualValue, Result = "FAIL", FailureMessage = message, ExcelRowNumber = source.ExcelRowNumber, ExpectedValueColumn = source.ExpectedValueColumn };

    private static bool CompareNumber(string? actual, string expected, out int result)
    {
        result = 0;
        if (!decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) || !decimal.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var e)) return false;
        result = decimal.Compare(a, e); return true;
    }

    private static bool IsEmptyResponseValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim();
        return normalized.Equals("null", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("[]", StringComparison.Ordinal)
            || normalized.Equals("{}", StringComparison.Ordinal);
    }

    private static bool IsEmptyUnconfiguredField(IReadOnlyDictionary<string, string> values, string path)
    {
        if (IsEmptyResponseValue(values[path])) return true;
        if (path.EndsWith(".name", StringComparison.OrdinalIgnoreCase))
        {
            var valuePath = path[..^5] + ".value";
            if (values.TryGetValue(valuePath, out var fieldValue) && IsEmptyResponseValue(fieldValue)) return true;
        }
        return false;
    }

    private static void FlattenJson(JsonElement element, string path, Dictionary<string, string> output)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject()) FlattenJson(p.Value, string.IsNullOrEmpty(path) ? p.Name : $"{path}.{p.Name}", output);
                break;
            case JsonValueKind.Array:
                var i = 0; foreach (var item in element.EnumerateArray()) FlattenJson(item, $"{path}[{i++}]", output); break;
            case JsonValueKind.Null: output[path] = ""; break;
            default: output[path] = element.ToString(); break;
        }
    }

    private static Dictionary<string, string> FlattenXml(XDocument document)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.Root is not null) FlattenXmlElement(document.Root, document.Root.Name.LocalName, output);
        return output;
    }

    private static void FlattenXmlElement(XElement element, string path, Dictionary<string, string> output)
    {
        foreach (var attribute in element.Attributes()) output[$"{path}.@{attribute.Name.LocalName}"] = attribute.Value;
        var children = element.Elements().ToList();
        if (children.Count == 0) { output[path] = element.Value; return; }
        foreach (var group in children.GroupBy(x => x.Name.LocalName))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++) FlattenXmlElement(list[i], list.Count == 1 ? $"{path}.{group.Key}" : $"{path}.{group.Key}[{i}]", output);
        }
    }
}
