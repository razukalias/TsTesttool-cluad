using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

public sealed class AssertionEngine
{
    public const string ResponseHeaderPrefix = "responseHeader.";

    public IReadOnlyList<AssertionResult> Evaluate(string body, string contentType, IReadOnlyList<AssertionDefinition> definitions, string runId, string requestId, int? httpStatus = null, bool ignoreEmptyUnconfigured = false, bool assertOnlyResponse = false, IReadOnlyDictionary<string, string>? responseHeaders = null)
    {
        if (definitions.Count == 0) return [];
        var values = Normalize(body, contentType).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        AddCounts(body, contentType, definitions, values);
        if (httpStatus.HasValue) values["statusCode"] = httpStatus.Value.ToString(CultureInfo.InvariantCulture);
        // HTTP response headers are addressed as responseHeader.<Name> (Step.responseHeader.Content-Type); like statusCode they are metadata, never an "unexpected payload field".
        if (responseHeaders is not null) foreach (var header in responseHeaders) values[ResponseHeaderPrefix + header.Key] = header.Value;
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
            && !path.StartsWith(ResponseHeaderPrefix, StringComparison.OrdinalIgnoreCase)
            && !expectedPaths.Contains(path)
            && (!ignoreEmptyUnconfigured || !IsEmptyUnconfiguredField(values, path))))
            results.Add(Mismatch(runId, requestId, definitions[0], unexpected, values[unexpected], $"Unexpected response path '{unexpected}' returned by payload."));
        return results;
    }

    /// <summary>
    /// A path ending in <c>#count</c> (<c>items.#count</c>, <c>customer.#count</c>) is the number of elements of that array or properties of that object. It is added only when
    /// a header asks for it, so no other path of the response is reported as unexpected because of it.
    /// </summary>
    private static void AddCounts(string body, string contentType, IReadOnlyList<AssertionDefinition> definitions, Dictionary<string, string> values)
    {
        var wanted = definitions.Select(d => d.ResponsePath).Where(p => p.Equals(CountSegment, StringComparison.OrdinalIgnoreCase) || p.EndsWith("." + CountSegment, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0 || string.IsNullOrWhiteSpace(body) || contentType.Equals("xml", StringComparison.OrdinalIgnoreCase) || body.TrimStart().StartsWith('<')) return;
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var path in wanted)
            {
                var basePath = path.Equals(CountSegment, StringComparison.OrdinalIgnoreCase) ? string.Empty : path[..^(CountSegment.Length + 1)];
                if (TryFind(document.RootElement, basePath, out var element))
                {
                    if (element.ValueKind == JsonValueKind.Array) values[path] = element.GetArrayLength().ToString(CultureInfo.InvariantCulture);
                    else if (element.ValueKind == JsonValueKind.Object) values[path] = element.EnumerateObject().Count().ToString(CultureInfo.InvariantCulture);
                }
            }
        }
        catch (JsonException) { /* not JSON: the count paths stay missing */ }
    }

    public const string CountSegment = "#count";

    private static bool TryFind(JsonElement root, string path, out JsonElement element)
    {
        element = root;
        if (path.Length == 0) return true;
        foreach (var segment in path.Split('.'))
        {
            var name = segment;
            var indexes = new List<int>();
            var bracket = segment.IndexOf('[');
            if (bracket >= 0)
            {
                name = segment[..bracket];
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(segment[bracket..], @"\[(\d+)\]")) indexes.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
            if (name.Length > 0)
            {
                if (element.ValueKind != JsonValueKind.Object) return false;
                var found = false;
                foreach (var property in element.EnumerateObject())
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { element = property.Value; found = true; break; }
                if (!found) return false;
            }
            foreach (var index in indexes)
            {
                if (element.ValueKind != JsonValueKind.Array || index >= element.GetArrayLength()) return false;
                element = element[index];
            }
        }
        return true;
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
        var exists = values.ContainsKey(d.ResponsePath);
        bool passed;
        string described;
        if (AssertionAlternatives.HasAny(d.ExpectedValue))
        {
            var (primary, alternatives) = AssertionAlternatives.Parse(d.ExpectedValue, op);
            foreach (var verb in alternatives.Select(a => a.Verb).Prepend(op).SelectMany(AssertionAlternatives.Split))
                if (!AssertionAlternatives.CanCombine(verb)) throw new InvalidDataException($"The verb '{verb}' cannot be combined with <or:> / <and:>; use one of: {string.Join(", ", AssertionAlternatives.Combinable)}.");
            passed = AssertionAlternatives.Evaluate(op, primary, alternatives, (verb, value) => AssertionAlternatives.TestAny(verb, v => Test(v, actual, value, exists, v)));
            described = AssertionAlternatives.Describe(op, primary, alternatives);
        }
        else
        {
            passed = AssertionAlternatives.TestAny(op, v => Test(v, actual, d.ExpectedValue, exists, v));
            described = $"{op.Replace("|", " or ")} '{d.ExpectedValue}'";
        }
        var actualValue = actual ?? "<missing>";
        var failureMessage = passed ? "" : $"Assertion failed: {d.ResponsePath} {described}, actual '{actualValue}'.";
        return new AssertionResult { RunId = runId, RequestId = requestId, TestcaseIndex = d.TestcaseIndex, Testcase = d.Testcase, DataId = d.DataId, StepName = d.StepName, ResponsePath = d.ResponsePath, AssertionVerb = op, AssertionType = AssertionSemantics.Type(op, actualValue, failureMessage), AssertionMeaning = AssertionSemantics.Meaning(op, actualValue, failureMessage), ExpectedValue = d.ExpectedValue, ActualValue = actualValue, Result = passed ? "PASS" : "FAIL", FailureMessage = failureMessage, ExtractVariable = d.ExtractVariable, ExcelRowNumber = d.ExcelRowNumber, ExpectedValueColumn = d.ExpectedValueColumn };
    }

    /// <summary>The comparison behind every {verb}: used by assertions and by the conditions of If and While steps.</summary>
    public static bool Test(string op, string? actual, string expected, bool exists, string? original = null) => op switch
    {
        "eq" => string.Equals(actual ?? "", expected, StringComparison.OrdinalIgnoreCase),
        "ne" => !string.Equals(actual ?? "", expected, StringComparison.OrdinalIgnoreCase),
        "lt" => CompareNumber(actual, expected, out var lt) && lt < 0,
        "lte" => CompareNumber(actual, expected, out var lte) && lte <= 0,
        "gt" => CompareNumber(actual, expected, out var gt) && gt > 0,
        "gte" => CompareNumber(actual, expected, out var gte) && gte >= 0,
        "contains" => actual?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true,
        "notcontains" => actual?.Contains(expected, StringComparison.OrdinalIgnoreCase) != true,
        "startswith" => actual?.StartsWith(expected, StringComparison.OrdinalIgnoreCase) == true,
        "endswith" => actual?.EndsWith(expected, StringComparison.OrdinalIgnoreCase) == true,
        "regex" => actual is not null && System.Text.RegularExpressions.Regex.IsMatch(actual, expected),
        "exists" => exists,
        "notexists" => !exists,
        "empty" => string.IsNullOrEmpty(actual),
        "notempty" => !string.IsNullOrEmpty(actual),
        "in" => expected.Split('|').Contains(actual ?? "", StringComparer.OrdinalIgnoreCase),
        "size" => int.TryParse(expected, out var size) && actual is not null && actual.Length == size,
        _ => throw new InvalidDataException($"Unsupported assertion verb '{original ?? op}'.")
    };

    public static readonly IReadOnlySet<string> Verbs = new HashSet<string>(["eq", "ne", "lt", "lte", "gt", "gte", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "in", "size"], StringComparer.OrdinalIgnoreCase);

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
        if (document.Root is null) return output;
        // A SOAP envelope is read from its body: GetOrderResponse.Status, Fault.faultstring, and Header.<element> for header elements.
        if (document.Root.Name.LocalName == "Envelope" && document.Root.Name.NamespaceName is SoapEnvelope.Ns11 or SoapEnvelope.Ns12
            && document.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "Body") is { } soapBody)
        {
            foreach (var header in document.Root.Elements().Where(e => e.Name.LocalName == "Header").SelectMany(e => e.Elements())) FlattenXmlElement(header, "Header." + header.Name.LocalName, output);
            foreach (var child in soapBody.Elements()) FlattenXmlElement(child, child.Name.LocalName, output);
            return output;
        }
        FlattenXmlElement(document.Root, document.Root.Name.LocalName, output);
        return output;
    }

    private static void FlattenXmlElement(XElement element, string path, Dictionary<string, string> output)
    {
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration)) output[$"{path}.@{attribute.Name.LocalName}"] = attribute.Value;
        var children = element.Elements().ToList();
        if (children.Count == 0) { output[path] = element.Value; return; }
        foreach (var group in children.GroupBy(x => x.Name.LocalName))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++) FlattenXmlElement(list[i], list.Count == 1 ? $"{path}.{group.Key}" : $"{path}.{group.Key}[{i}]", output);
        }
    }
}
