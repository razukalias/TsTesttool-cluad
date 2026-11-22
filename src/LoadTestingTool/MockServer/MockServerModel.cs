using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LoadTestingTool.MockServer;

/// <summary>A response header, as a name and a value (the value may contain {{templates}}).</summary>
public sealed class MockHeader
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// An extra rule a request must meet for a route to apply: Source is query, header, body (the whole body) or json (a path such as
/// order.lines[0].sku in a JSON body); Op is equals, contains, regex, exists or notexists.
/// </summary>
public sealed class MockCondition
{
    public string Source { get; set; } = "query";
    public string Name { get; set; } = "";
    public string Op { get; set; } = "equals";
    public string Value { get; set; } = "";
}

/// <summary>What the server sends back. Body and header values may use {{templates}}; BodyFile (inside the config folder) replaces Body.</summary>
public sealed class MockResponse
{
    public int Status { get; set; } = 200;
    public string ContentType { get; set; } = "application/json";
    public List<MockHeader> Headers { get; set; } = [];
    public string Body { get; set; } = "";
    public string BodyFile { get; set; } = "";
    /// <summary>Response time: wait this long (ms) before answering.</summary>
    public int DelayMs { get; set; }
    /// <summary>When above DelayMs, each answer waits a random time between DelayMs and this (ms), like a real service.</summary>
    public int DelayMaxMs { get; set; }
}

/// <summary>One route: the first enabled route whose method, path and conditions all fit the request answers it.</summary>
public sealed class MockRoute
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>A verb (GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS or any token) or ANY.</summary>
    public string Method { get; set; } = "ANY";
    /// <summary>/orders/{id} takes a segment as a named value, * one segment, and a trailing /** the rest of the path.</summary>
    public string Path { get; set; } = "/";
    public List<MockCondition> Conditions { get; set; } = [];
    public MockResponse Response { get; set; } = new();
    /// <summary>Chance (0-100) that a request fails instead of getting the response.</summary>
    public int FailurePercent { get; set; }
    /// <summary>A failure closes the connection instead of answering 500.</summary>
    public bool DropConnection { get; set; }

    public string Title => string.IsNullOrWhiteSpace(Name) ? $"{Method} {Path}" : Name;
}

public sealed class MockServerConfig
{
    public string Name { get; set; } = "Mock server";
    public int Port { get; set; } = 8099;
    /// <summary>False: only this computer (127.0.0.1) can connect. True: every network address (Windows may need administrator rights).</summary>
    public bool AllInterfaces { get; set; }
    /// <summary>Put in front of every route path, for example /api/v1.</summary>
    public string BasePath { get; set; } = "";
    public List<MockRoute> Routes { get; set; } = [];
    /// <summary>The answer when no route fits.</summary>
    public MockResponse Fallback { get; set; } = new() { Status = 404, Body = "{\"error\":\"No route matched {{method}} {{path}}\"}" };
    public int LogLimit { get; set; } = 2000;
    /// <summary>Added to every answer's own delay (ms): a slow network for the whole server.</summary>
    public int ExtraDelayMs { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static MockServerConfig FromJson(string json) => JsonSerializer.Deserialize<MockServerConfig>(json, Options) ?? new MockServerConfig();
    public static MockServerConfig Load(string path) => FromJson(File.ReadAllText(path));
    public void Save(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        File.WriteAllText(path, ToJson());
    }

    private static readonly Regex Token = new("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$", RegexOptions.Compiled);
    private static readonly string[] Sources = ["query", "header", "body", "json"];
    private static readonly string[] Ops = ["equals", "contains", "regex", "exists", "notexists"];

    /// <summary>Everything wrong with the setup, in plain words; empty when the server can start.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        if (Port is < 1 or > 65535) problems.Add($"Port {Port} must be between 1 and 65535.");
        if (BasePath.Length > 0 && !BasePath.StartsWith('/')) problems.Add("The base path must start with /.");
        if (ExtraDelayMs < 0) problems.Add("The extra latency cannot be negative.");
        CheckResponse(Fallback, "The no-match response", problems);
        for (var i = 0; i < Routes.Count; i++)
        {
            var route = Routes[i];
            var label = $"Route {i + 1} ({route.Title})";
            if (string.IsNullOrWhiteSpace(route.Method) || (!route.Method.Equals("ANY", StringComparison.OrdinalIgnoreCase) && !Token.IsMatch(route.Method))) problems.Add($"{label}: '{route.Method}' is not an HTTP method.");
            if (!route.Path.StartsWith('/')) problems.Add($"{label}: the path must start with /.");
            else if (MockPath.Compile(route.Path) is null) problems.Add($"{label}: the path '{route.Path}' is not valid ({{name}} names may use letters, digits and _; ** only at the end).");
            if (route.FailurePercent is < 0 or > 100) problems.Add($"{label}: the failure chance must be 0-100.");
            foreach (var condition in route.Conditions)
            {
                if (!Sources.Contains(condition.Source, StringComparer.OrdinalIgnoreCase)) problems.Add($"{label}: condition source '{condition.Source}' must be query, header, body or json.");
                if (!Ops.Contains(condition.Op, StringComparer.OrdinalIgnoreCase)) problems.Add($"{label}: condition '{condition.Op}' must be equals, contains, regex, exists or notexists.");
                else if (condition.Op.Equals("regex", StringComparison.OrdinalIgnoreCase))
                {
                    try { _ = new Regex(condition.Value); } catch (ArgumentException) { problems.Add($"{label}: '{condition.Value}' is not a valid regular expression."); }
                }
            }
            CheckResponse(route.Response, label, problems);
        }
        return problems;
    }

    private static void CheckResponse(MockResponse response, string label, List<string> problems)
    {
        if (response.Status is < 100 or > 599) problems.Add($"{label}: status {response.Status} must be 100-599.");
        if (response.DelayMs < 0 || response.DelayMaxMs < 0) problems.Add($"{label}: the delay cannot be negative.");
        else if (response.DelayMaxMs > 0 && response.DelayMaxMs < response.DelayMs) problems.Add($"{label}: the longest delay ({response.DelayMaxMs} ms) is shorter than the shortest ({response.DelayMs} ms).");
        foreach (var header in response.Headers) if (string.IsNullOrWhiteSpace(header.Name)) problems.Add($"{label}: a response header has no name.");
    }
}

/// <summary>Points the URLs of an instance at a mock server.</summary>
public static class MockUrls
{
    private static readonly Regex Absolute = new(@"^(?<scheme>https?://)(?<host>[^/?#<>\s]+)(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly string[] Local = ["127.0.0.1", "localhost", "[::1]", "0.0.0.0"];

    /// <summary>The origin (scheme://host:port) of an absolute http(s) URL, or null (placeholders such as &lt;From_…&gt; in the host part give null).</summary>
    public static string? OriginOf(string url)
    {
        var m = Absolute.Match(url.Trim());
        return m.Success ? (m.Groups["scheme"].Value + m.Groups["host"].Value).ToLowerInvariant() : null;
    }

    public static bool IsLocal(string origin)
    {
        var host = origin[(origin.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var colon = host.StartsWith('[') ? host.IndexOf("]:", StringComparison.Ordinal) + 1 : host.IndexOf(':');
        if (colon > 0) host = host[..colon];
        return Local.Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The URL with its origin replaced by <paramref name="origin"/>, or null when it stays as it is (not absolute, or another host and remote ones are not wanted).</summary>
    public static string? Repoint(string url, string origin, bool includeRemote)
    {
        var m = Absolute.Match(url.Trim());
        if (!m.Success) return null;
        var current = (m.Groups["scheme"].Value + m.Groups["host"].Value);
        if (!includeRemote && !IsLocal(current)) return null;
        var fresh = origin.TrimEnd('/') + m.Groups["rest"].Value;
        return fresh == url.Trim() ? null : fresh;
    }
}
