using System.Text.RegularExpressions;
using LoadTestingTool.Editing;

namespace LoadTestingTool.MockServer;

/// <summary>Mock server routes made from a WSDL: one POST route per operation (answered with a sample of the operation's output) and a ?wsdl route that serves the WSDL itself.</summary>
public static class WsdlMock
{
    private const string Ns11 = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string Ns12 = "http://www.w3.org/2003/05/soap-envelope";

    /// <param name="info">The parsed WSDL.</param>
    /// <param name="wsdlText">The WSDL text; its service addresses are rewritten to the host the request came to.</param>
    /// <param name="operations">The operations to answer; null means all of them.</param>
    public static List<MockRoute> Routes(WsdlInfo info, string wsdlText, IReadOnlyCollection<string>? operations = null)
    {
        var routes = new List<MockRoute>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackPath = "/" + SafeName(info.ServiceName.Length > 0 ? info.ServiceName : "Service") + ".svc";
        var paths = new List<string>();
        foreach (var operation in info.Operations)
        {
            if (operations is not null && !operations.Contains(operation.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var path = PathOf(operation.Endpoint) ?? fallbackPath;
            if (!seen.Add(path + "|" + operation.Name)) continue;
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
            var ns = operation.SoapVersion == "1.2" ? Ns12 : Ns11;
            routes.Add(new MockRoute
            {
                Name = "SOAP " + operation.Name, Method = "POST", Path = path,
                Conditions = [new MockCondition { Source = "soap", Name = "operation", Op = "equals", Value = operation.Name }],
                Response = new MockResponse
                {
                    ContentType = operation.SoapVersion == "1.2" ? "application/soap+xml; charset=utf-8" : "text/xml; charset=utf-8",
                    Body = $"<s:Envelope xmlns:s=\"{ns}\"><s:Body>{operation.ResponseSample}</s:Body></s:Envelope>"
                }
            });
        }
        if (routes.Count == 0) return routes;
        var wsdl = Regex.Replace(wsdlText, "(<(?:\\w+:)?address\\b[^>]*?\\blocation=\")([^\"]*)(\")", m => m.Groups[1].Value + "http://{{header.Host}}" + (PathOf(m.Groups[2].Value) ?? paths[0]) + m.Groups[3].Value);
        foreach (var path in paths)
            routes.Add(new MockRoute
            {
                Name = "WSDL " + path, Method = "GET", Path = path,
                Conditions = [new MockCondition { Source = "query", Name = "wsdl", Op = "exists" }],
                Response = new MockResponse { ContentType = "text/xml; charset=utf-8", Body = wsdl }
            });
        return routes;
    }

    private static string? PathOf(string endpoint)
        => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.AbsolutePath.Length > 0 ? uri.AbsolutePath : null;

    private static string SafeName(string name) => Regex.Replace(name, "[^\\w.-]", "");
}
