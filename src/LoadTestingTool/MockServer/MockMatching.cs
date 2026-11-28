using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace LoadTestingTool.MockServer;

/// <summary>What the matcher and templates see of one incoming request.</summary>
public sealed record MockRequest(string Method, string Path, IReadOnlyDictionary<string, string> Query, IReadOnlyDictionary<string, string> Headers, string Body);

public sealed record MockMatch(MockRoute Route, IReadOnlyDictionary<string, string> PathParams);

/// <summary>Turns a path pattern into a matcher.</summary>
public static class MockPath
{
    private static readonly Regex Name = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    /// <summary>The pattern as a regular expression, or null when it is not valid.</summary>
    public static Regex? Compile(string pattern)
    {
        var trimmed = pattern.Trim('/');
        var parts = trimmed.Length == 0 ? [] : trimmed.Split('/');
        var text = new StringBuilder("^");
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part == "**")
            {
                if (i != parts.Length - 1) return null;
                text.Append("(?:/.*)?");
            }
            else if (part == "*") text.Append("/[^/]+");
            else if (part.StartsWith('{') && part.EndsWith('}'))
            {
                var name = part[1..^1];
                if (!Name.IsMatch(name)) return null;
                text.Append("/(?<").Append(name).Append(">[^/]+)");
            }
            else if (part.Length == 0 || part.Contains('{') || part.Contains('}')) return null;
            else text.Append('/').Append(Regex.Escape(part));
        }
        text.Append("/?$");
        return new Regex(text.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static Dictionary<string, string>? Match(string pattern, string path)
    {
        var regex = Compile(pattern);
        if (regex is null) return null;
        var m = regex.Match(path);
        if (!m.Success) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Group group in m.Groups) if (group.Success && !int.TryParse(group.Name, out _)) values[group.Name] = Uri.UnescapeDataString(group.Value);
        return values;
    }
}

/// <summary>What a condition can see besides the request: the values taken from the path and the scenario states.</summary>
public sealed record MockMatchContext(IReadOnlyDictionary<string, string> PathParams, Func<string, string>? State = null)
{
    public static readonly MockMatchContext Empty = new(new Dictionary<string, string>());
}

public static class MockRouteMatcher
{
    /// <summary>The first enabled route that fits, or null.</summary>
    public static MockMatch? Find(MockServerConfig config, MockRequest request, Func<string, string>? state = null) => Find(config, request, state, null);

    /// <summary>
    /// The first enabled route that fits, or null. <paramref name="trace"/>, when given, gets one plain line for every route whose method and
    /// path fit but whose conditions did not (and why), or a line saying that no route has the method and path at all.
    /// </summary>
    public static MockMatch? Find(MockServerConfig config, MockRequest request, Func<string, string>? state, List<string>? trace)
    {
        var path = request.Path;
        if (config.BasePath.Length > 0)
        {
            var baseTrim = config.BasePath.TrimEnd('/');
            if (!path.Equals(baseTrim, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(baseTrim + "/", StringComparison.OrdinalIgnoreCase))
            {
                trace?.Add($"The path {request.Path} is outside the base path {config.BasePath}.");
                return null;
            }
            path = path.Length == baseTrim.Length ? "/" : path[baseTrim.Length..];
        }
        var pathFitted = false;
        var methodsForPath = new List<string>();
        for (var i = 0; i < config.Routes.Count; i++)
        {
            var route = config.Routes[i];
            var values = MockPath.Match(route.Path, path);
            if (values is null) continue;
            if (!route.Method.Equals("ANY", StringComparison.OrdinalIgnoreCase) && !route.Method.Equals(request.Method, StringComparison.OrdinalIgnoreCase))
            {
                if (route.Enabled) methodsForPath.Add(route.Method.ToUpperInvariant());
                continue;
            }
            if (!route.Enabled) { trace?.Add($"Route {i + 1} ({route.Title}) would fit but is disabled."); continue; }
            pathFitted = true;
            var context = new MockMatchContext(values, state);
            var failed = FirstFailure(route.Conditions, request, context);
            if (failed is null) return new MockMatch(route, values);
            trace?.Add($"Route {i + 1} ({route.Title}): {failed}");
        }
        if (trace is not null && !pathFitted)
            trace.Add(methodsForPath.Count > 0
                ? $"No route has {request.Method} {path}; this path has {string.Join(", ", methodsForPath.Distinct())}."
                : $"No route has the path {path}.");
        return null;
    }

    /// <summary>
    /// Null when the conditions hold; else why not. Conditions without a group must all hold; within each named group at least one must.
    /// </summary>
    public static string? FirstFailure(IReadOnlyList<MockCondition> conditions, MockRequest request, MockMatchContext context)
    {
        for (var i = 0; i < conditions.Count; i++)
        {
            var condition = conditions[i];
            if (condition.Group.Trim().Length > 0) continue;
            var (holds, actual) = Evaluate(condition, request, context);
            if (!holds) return $"condition {i + 1} failed: {Describe(condition)}, but {ActualText(condition, actual)}.";
        }
        foreach (var group in conditions.Where(c => c.Group.Trim().Length > 0).GroupBy(c => c.Group.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var results = group.Select(c => (Condition: c, Result: Evaluate(c, request, context))).ToList();
            if (results.Any(r => r.Result.Holds)) continue;
            return $"none of group '{group.Key}' held: " + string.Join("; ", results.Select(r => $"{Describe(r.Condition)} ({ActualText(r.Condition, r.Result.Actual)})")) + ".";
        }
        return null;
    }

    /// <summary>Whether the condition holds for the request (no path values or states).</summary>
    public static bool Holds(MockCondition condition, MockRequest request) => Evaluate(condition, request, MockMatchContext.Empty).Holds;

    public static bool Holds(MockCondition condition, MockRequest request, MockMatchContext context) => Evaluate(condition, request, context).Holds;

    /// <summary>The value the condition reads from the request (null: not there).</summary>
    public static string? Actual(MockCondition condition, MockRequest request, MockMatchContext context) => condition.Source.Trim().ToLowerInvariant() switch
    {
        "query" => request.Query.TryGetValue(condition.Name, out var q) ? q : null,
        "header" => request.Headers.TryGetValue(condition.Name, out var h) ? h : null,
        "cookie" => MockCookies.Get(request, condition.Name),
        "path" => context.PathParams.TryGetValue(condition.Name.Trim(), out var p) ? p : null,
        "method" => request.Method,
        "body" => request.Body,
        "json" => MockJson.Get(request.Body, condition.Name),
        "graphql" => MockGraphQl.Part(request.Body, condition.Name),
        "soap" => MockSoap.Part(request, condition.Name),
        "xml" => MockXml.Get(request.Body, condition.Name),
        "state" => context.State?.Invoke(condition.Name.Trim()) ?? MockStates.Initial,
        _ => null
    };

    public static (bool Holds, string? Actual) Evaluate(MockCondition condition, MockRequest request, MockMatchContext context)
    {
        var actual = Actual(condition, request, context);
        var comparison = condition.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var expected = condition.Value;
        var holds = condition.Op.Trim().ToLowerInvariant() switch
        {
            "exists" => actual is not null,
            "notexists" => actual is null,
            "equals" => actual is not null && string.Equals(actual, expected, comparison),
            "notequals" => actual is null || !string.Equals(actual, expected, comparison),
            "contains" => actual is not null && actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "notcontains" => actual is null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "startswith" => actual is not null && actual.StartsWith(expected, comparison),
            "endswith" => actual is not null && actual.EndsWith(expected, comparison),
            "regex" => actual is not null && SafeRegex(actual, expected, condition.IgnoreCase),
            "gt" => MockCompare.Compare(actual, expected) is > 0,
            "ge" => MockCompare.Compare(actual, expected) is >= 0,
            "lt" => MockCompare.Compare(actual, expected) is < 0,
            "le" => MockCompare.Compare(actual, expected) is <= 0,
            "between" => MockCompare.Range(expected) is { } range && MockCompare.Compare(actual, range.Low) is >= 0 && MockCompare.Compare(actual, range.High) is <= 0,
            "in" => actual is not null && MockCompare.List(expected).Contains(actual, condition.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            "notin" => actual is null || !MockCompare.List(expected).Contains(actual, condition.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            _ => false
        };
        return (holds, actual);
    }

    private static bool SafeRegex(string actual, string pattern, bool ignoreCase)
    {
        try { return Regex.IsMatch(actual, pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromSeconds(1)); }
        catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException) { return false; }
    }

    /// <summary>The condition in words, for example: json order.status equals 'NEW'.</summary>
    public static string Describe(MockCondition condition)
    {
        var source = condition.Source.Trim().ToLowerInvariant();
        var what = source switch
        {
            "body" => "the body",
            "method" => "the method",
            "state" => $"state of '{condition.Name.Trim()}'",
            "path" => $"path {{{condition.Name.Trim()}}}",
            _ => $"{source} {condition.Name.Trim()}"
        };
        var op = condition.Op.Trim().ToLowerInvariant();
        var words = op switch
        {
            "notequals" => "is not", "notcontains" => "does not contain", "startswith" => "starts with", "endswith" => "ends with",
            "regex" => "matches", "gt" => ">", "ge" => ">=", "lt" => "<", "le" => "<=", "in" => "is one of", "notin" => "is none of",
            "exists" => "exists", "notexists" => "does not exist", _ => op
        };
        var value = op is "exists" or "notexists" ? "" : $" '{condition.Value}'";
        return $"{what} {words}{value}{(condition.IgnoreCase && op is not ("contains" or "notcontains") ? " (any case)" : "")}";
    }

    private static string ActualText(MockCondition condition, string? actual)
    {
        if (actual is null) return "it is not in the request";
        var shown = actual.Length > 80 ? actual[..80] + "…" : actual;
        return $"it was '{shown}'";
    }
}

/// <summary>Number, date and list comparisons for conditions.</summary>
public static class MockCompare
{
    public static bool IsComparable(string? text) => Number(text) is not null || Date(text) is not null;

    /// <summary>Negative, zero or positive when both are numbers (or both dates); null when they cannot be compared.</summary>
    public static int? Compare(string? actual, string expected)
    {
        if (actual is null) return null;
        if (Number(actual) is { } a && Number(expected) is { } b) return a.CompareTo(b);
        if (Date(actual) is { } da && Date(expected) is { } db) return da.CompareTo(db);
        return null;
    }

    /// <summary>"10..20" (or "10,20") as its two ends.</summary>
    public static (string Low, string High)? Range(string text)
    {
        var parts = text.Contains("..", StringComparison.Ordinal) ? text.Split("..", 2) : text.Split(',', 2);
        if (parts.Length != 2) return null;
        var low = parts[0].Trim(); var high = parts[1].Trim();
        return IsComparable(low) && IsComparable(high) ? (low, high) : null;
    }

    public static List<string> List(string text) => text.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();

    private static double? Number(string? text) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset? Date(string? text) =>
        text is not null && text.Trim().Length >= 8 && DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;
}

/// <summary>Reads a cookie from the request's Cookie header.</summary>
public static class MockCookies
{
    public static string? Get(MockRequest request, string name)
    {
        if (!request.Headers.TryGetValue("Cookie", out var header)) return null;
        foreach (var part in header.Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            if (part[..eq].Trim().Equals(name.Trim(), StringComparison.Ordinal)) return Uri.UnescapeDataString(part[(eq + 1)..].Trim());
        }
        return null;
    }
}

/// <summary>Scenario states: every scenario starts in "Started".</summary>
public static class MockStates
{
    public const string Initial = "Started";
}

public static class MockJson
{
    private static readonly Regex Part = new(@"([^.\[\]]+)|\[(\d+)\]", RegexOptions.Compiled);

    public static string? Get(string json, string path)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var current = document.RootElement;
            foreach (Match part in Part.Matches(path))
            {
                if (part.Groups[2].Success)
                {
                    if (current.ValueKind != JsonValueKind.Array || !int.TryParse(part.Groups[2].Value, out var index) || index >= current.GetArrayLength()) return null;
                    current = current[index];
                }
                else
                {
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part.Groups[1].Value, out var next)) return null;
                    current = next;
                }
            }
            return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>What a route's database action and the server's memory add to the templates.</summary>
public sealed class MockTemplateData
{
    /// <summary>The rows the database action returned (null: the route has no database action).</summary>
    public List<Dictionary<string, object?>>? Rows { get; init; }
    public int Affected { get; init; }
    public long LastId { get; init; }
    /// <summary>Which request this is for the route: 1 for the first.</summary>
    public long Count { get; init; }
    public Func<string, string>? State { get; init; }
}

/// <summary>
/// {{method}} {{path}} {{url}} {{now}} {{guid}} {{random}} {{path.id}} {{query.name}} {{header.name}} {{cookie.name}} {{body}} {{json.order.id}}
/// {{xml.a.b}} {{soap.action}} {{state.scenario}} {{count}}, and from a database action {{row}} {{row.column}} {{rows}} {{rowcount}} {{affected}} {{lastid}}.
/// Unknown names give an empty text.
/// </summary>
public static class MockTemplate
{
    private static readonly Regex Token = new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions RowJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Render(string text, MockRequest request, IReadOnlyDictionary<string, string> pathParams, MockTemplateData? data = null)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal)) return text;
        return Token.Replace(text, m => Value(m.Groups[1].Value, request, pathParams, data));
    }

    public static string RowsJson(List<Dictionary<string, object?>> rows) => JsonSerializer.Serialize(rows, RowJson);

    private static string Value(string key, MockRequest request, IReadOnlyDictionary<string, string> pathParams, MockTemplateData? data)
    {
        switch (key.ToLowerInvariant())
        {
            case "method": return request.Method;
            case "path": return request.Path;
            case "url": return request.Path + (request.Query.Count == 0 ? "" : "?" + string.Join("&", request.Query.Select(p => $"{p.Key}={p.Value}")));
            case "operation": return MockGraphQl.Part(request.Body, "operation") ?? MockSoap.Part(request, "operation") ?? "";
            case "now": return DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            case "guid": return Guid.NewGuid().ToString();
            case "random": return Random.Shared.Next(0, 1_000_000).ToString(CultureInfo.InvariantCulture);
            case "body": return request.Body;
            case "count": return (data?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
            case "rows": return data?.Rows is { } all ? RowsJson(all) : "[]";
            case "row": return data?.Rows is { Count: > 0 } some ? JsonSerializer.Serialize(some[0], RowJson) : "null";
            case "rowcount": return (data?.Rows?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
            case "affected": return (data?.Affected ?? 0).ToString(CultureInfo.InvariantCulture);
            case "lastid": return (data?.LastId ?? 0).ToString(CultureInfo.InvariantCulture);
        }
        var dot = key.IndexOf('.');
        if (dot < 0) return "";
        var kind = key[..dot].ToLowerInvariant();
        var name = key[(dot + 1)..];
        return kind switch
        {
            "path" => pathParams.TryGetValue(name, out var p) ? p : "",
            "query" => request.Query.TryGetValue(name, out var q) ? q : "",
            "header" => request.Headers.TryGetValue(name, out var h) ? h : "",
            "cookie" => MockCookies.Get(request, name) ?? "",
            "json" => MockJson.Get(request.Body, name) ?? "",
            "xml" => MockXml.Get(request.Body, name) ?? "",
            "soap" => MockSoap.Part(request, name) ?? "",
            "state" => data?.State?.Invoke(name) ?? MockStates.Initial,
            "row" => data?.Rows is { Count: > 0 } rows && rows[0].TryGetValue(name, out var cell) ? CellText(cell) : "",
            _ => ""
        };
    }

    /// <summary>A database value as text: numbers in invariant form, null as an empty text.</summary>
    public static string CellText(object? value) => value switch
    {
        null => "",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value.ToString() ?? ""
    };
}

public static class MockGraphQl
{
    public static readonly string[] Parts = ["operation", "type", "field"];
    private static readonly Regex Header = new(@"^\s*(?<type>query|mutation|subscription)\b\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FirstField = new(@"\{\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*:\s*)?(?<field>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    /// <summary>
    /// "operation": the operationName, else the name in "query GetOrder", else the first field; "type": query, mutation or subscription
    /// (a bare { … } is a query); "field": the first field asked for. Null when the body is not a GraphQL request.
    /// </summary>
    public static string? Part(string body, string part)
    {
        var query = MockJson.Get(body, "query");
        if (query is null) return null;
        var type = "query"; string? name = null;
        var header = Header.Match(query);
        if (header.Success) { type = header.Groups["type"].Value.ToLowerInvariant(); if (header.Groups["name"].Success) name = header.Groups["name"].Value; }
        var field = FirstField.Match(query) is { Success: true } f ? f.Groups["field"].Value : null;
        var operationName = MockJson.Get(body, "operationName");
        return (string.IsNullOrWhiteSpace(part) ? "operation" : part.Trim().ToLowerInvariant()) switch
        {
            "type" => type,
            "field" => field,
            "operation" => !string.IsNullOrWhiteSpace(operationName) && operationName != "null" ? operationName : name ?? field,
            _ => null
        };
    }
}

/// <summary>Reads one value out of an XML text by local element names, for example GetOrder.OrderId or Items.Item[1].@sku. A SOAP envelope is read from its body.</summary>
public static class MockXml
{
    private static readonly Regex Part = new(@"([^.\[\]]+)|\[(\d+)\]", RegexOptions.Compiled);

    public static string? Get(string xml, string path)
    {
        if (string.IsNullOrWhiteSpace(xml) || !xml.TrimStart().StartsWith('<')) return null;
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return null;
            IEnumerable<XElement> current = MockSoap.IsEnvelope(root) ? MockSoap.BodyOf(root)?.Elements() ?? [] : [root];
            XElement? element = null;
            var first = true;
            foreach (Match part in Part.Matches(path))
            {
                if (part.Groups[2].Success)
                {
                    var list = current.ToList();
                    if (!int.TryParse(part.Groups[2].Value, out var index) || index >= list.Count) return null;
                    element = list[index]; current = [element];
                    continue;
                }
                var name = part.Groups[1].Value;
                if (name.StartsWith('@'))
                    return element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name[1..])?.Value;
                var matches = (first ? current : current.SelectMany(e => e.Elements())).Where(e => e.Name.LocalName == name).ToList();
                first = false;
                if (matches.Count == 0) return null;
                current = matches; element = matches[0];
            }
            return element?.Value;
        }
        catch (System.Xml.XmlException) { return null; }
    }
}

/// <summary>What a SOAP request asks for: the operation (the first element in the body), the action and the version.</summary>
public static class MockSoap
{
    public static readonly string[] Parts = ["operation", "action", "version"];
    private const string Ns11 = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string Ns12 = "http://www.w3.org/2003/05/soap-envelope";

    public static bool IsEnvelope(XElement root) => root.Name.LocalName == "Envelope" && root.Name.NamespaceName is Ns11 or Ns12;
    public static XElement? BodyOf(XElement envelope) => envelope.Elements().FirstOrDefault(e => e.Name.LocalName == "Body");

    /// <summary>operation, action (SOAPAction header, or action= of the content type) or version (1.1 / 1.2). Null when the request is not SOAP.</summary>
    public static string? Part(MockRequest request, string part)
    {
        XElement? root;
        try { root = request.Body.TrimStart().StartsWith('<') ? XDocument.Parse(request.Body).Root : null; }
        catch (System.Xml.XmlException) { return null; }
        if (root is null || !IsEnvelope(root)) return null;
        switch ((string.IsNullOrWhiteSpace(part) ? "operation" : part.Trim()).ToLowerInvariant())
        {
            case "operation": return BodyOf(root)?.Elements().FirstOrDefault()?.Name.LocalName;
            case "version": return root.Name.NamespaceName == Ns12 ? "1.2" : "1.1";
            case "action":
                if (request.Headers.TryGetValue("SOAPAction", out var header)) return header.Trim().Trim('"');
                if (request.Headers.TryGetValue("Content-Type", out var type) && Regex.Match(type, "action=\"?([^\";]+)\"?", RegexOptions.IgnoreCase) is { Success: true } m) return m.Groups[1].Value;
                return "";
            default: return null;
        }
    }
}
