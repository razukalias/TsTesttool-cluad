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

public static class MockRouteMatcher
{
    /// <summary>The first enabled route that fits, or null.</summary>
    public static MockMatch? Find(MockServerConfig config, MockRequest request)
    {
        var path = request.Path;
        if (config.BasePath.Length > 0)
        {
            var baseTrim = config.BasePath.TrimEnd('/');
            if (!path.Equals(baseTrim, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(baseTrim + "/", StringComparison.OrdinalIgnoreCase)) return null;
            path = path.Length == baseTrim.Length ? "/" : path[baseTrim.Length..];
        }
        foreach (var route in config.Routes)
        {
            if (!route.Enabled) continue;
            if (!route.Method.Equals("ANY", StringComparison.OrdinalIgnoreCase) && !route.Method.Equals(request.Method, StringComparison.OrdinalIgnoreCase)) continue;
            var values = MockPath.Match(route.Path, path);
            if (values is null) continue;
            if (route.Conditions.All(c => Holds(c, request))) return new MockMatch(route, values);
        }
        return null;
    }

    public static bool Holds(MockCondition condition, MockRequest request)
    {
        string? actual = condition.Source.ToLowerInvariant() switch
        {
            "query" => request.Query.TryGetValue(condition.Name, out var q) ? q : null,
            "header" => request.Headers.TryGetValue(condition.Name, out var h) ? h : null,
            "body" => request.Body,
            "json" => MockJson.Get(request.Body, condition.Name),
            "graphql" => MockGraphQl.Part(request.Body, condition.Name),
            "soap" => MockSoap.Part(request, condition.Name),
            "xml" => MockXml.Get(request.Body, condition.Name),
            _ => null
        };
        switch (condition.Op.ToLowerInvariant())
        {
            case "exists": return actual is not null;
            case "notexists": return actual is null;
            case "equals": return actual is not null && actual == condition.Value;
            case "contains": return actual is not null && actual.Contains(condition.Value, StringComparison.OrdinalIgnoreCase);
            case "regex":
                try { return actual is not null && Regex.IsMatch(actual, condition.Value); } catch (ArgumentException) { return false; }
            default: return false;
        }
    }
}

/// <summary>Reads one value out of a JSON text by a path like order.lines[0].sku.</summary>
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

/// <summary>
/// {{method}} {{path}} {{url}} {{now}} {{guid}} {{random}} {{path.id}} {{query.name}} {{header.name}} {{body}} {{json.order.id}}.
/// Unknown names give an empty text.
/// </summary>
public static class MockTemplate
{
    private static readonly Regex Token = new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);

    public static string Render(string text, MockRequest request, IReadOnlyDictionary<string, string> pathParams)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal)) return text;
        return Token.Replace(text, m => Value(m.Groups[1].Value, request, pathParams));
    }

    private static string Value(string key, MockRequest request, IReadOnlyDictionary<string, string> pathParams)
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
            "json" => MockJson.Get(request.Body, name) ?? "",
            "xml" => MockXml.Get(request.Body, name) ?? "",
            "soap" => MockSoap.Part(request, name) ?? "",
            _ => ""
        };
    }
}

/// <summary>Reads what a GraphQL request asks for, so one /graphql path can answer different operations differently.</summary>
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
