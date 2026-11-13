using System.Text;
using LoadTestingTool.Editing;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>What one step would send for one data row: the URL, headers and the template with the row's values filled in.</summary>
public sealed record RequestPreview(string StepName, string StepType, string Verb, string Url, IReadOnlyList<KeyValuePair<string, string>> Headers, string Body, string? Warning)
{
    public string Title => $"{StepName} · {(StepType.Equals("HTTP", StringComparison.OrdinalIgnoreCase) ? Verb : StepType)}";
    public string HeadersText => string.Join(Environment.NewLine, Headers.Select(h => $"{h.Key}: {h.Value}"));
    public bool HasWarning => Warning is not null;

    /// <summary>
    /// Builds the preview the way the runner does: variables are the step's own <c>Step.name</c> columns plus the plain ones.
    /// Values that come from formulas, earlier responses or dynamic functions are not known here and are shown as they are.
    /// </summary>
    public static RequestPreview Build(string workbookPath, TableRow requestRow, IReadOnlyList<string> requestHeaders, TableRow stepRow, IReadOnlyList<string> stepHeaders)
    {
        var name = stepRow.Get("stepname");
        var type = Or(stepRow.Get("steptype"), "HTTP");
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var prefix = name + ".";
        foreach (var header in requestHeaders.Where(h => h.Length > 0 && !h.Equals("dataid", StringComparison.OrdinalIgnoreCase) && !h.Equals("testcaseindex", StringComparison.OrdinalIgnoreCase)))
        {
            var value = requestRow.Get(header);
            var key = header.EndsWith("_NoLog", StringComparison.OrdinalIgnoreCase) || header.EndsWith("_Log", StringComparison.OrdinalIgnoreCase) ? header[..header.LastIndexOf('_')] : header;
            if (!key.Contains('.')) variables[key] = value;
        }
        foreach (var header in requestHeaders.Where(h => h.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var key = header[prefix.Length..];
            if (key.EndsWith("_NoLog", StringComparison.OrdinalIgnoreCase) || key.EndsWith("_Log", StringComparison.OrdinalIgnoreCase)) key = key[..key.LastIndexOf('_')];
            variables[key] = requestRow.Get(header);
        }

        var warnings = new List<string>();
        var url = stepRow.Get("targeturl");
        try { url = Substitute(url, variables); }
        catch (InvalidDataException ex) { warnings.Add(ex.Message); }

        var headers = stepHeaders.Where(h => h.StartsWith("header_", StringComparison.OrdinalIgnoreCase))
            .Select(h => new KeyValuePair<string, string>(h["header_".Length..], stepRow.Get(h)))
            .Where(p => p.Value.Length > 0).ToList();

        var body = string.Empty;
        var template = stepRow.Get("templetsource");
        var contentType = stepRow.Get("contenttype");
        if (contentType.Length == 0) contentType = Path.GetExtension(template).Equals(".xml", StringComparison.OrdinalIgnoreCase) ? "xml" : "json";
        if (template.Length > 0)
        {
            var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workbookPath)) ?? ".", "Templates");
            var file = Path.Combine(folder, template);
            if (!File.Exists(file)) warnings.Add($"Template '{template}' was not found in {folder}.");
            else
            {
                try { body = TemplateRenderer.Render(File.ReadAllText(file), contentType, variables); }
                catch (Exception ex) when (ex is InvalidDataException or IOException) { warnings.Add(ex.Message); }
            }
        }
        else if (!type.Equals("HTTP", StringComparison.OrdinalIgnoreCase))
        {
            body = stepRow.Get("stepconfig");
        }

        return new RequestPreview(name, type, Or(stepRow.Get("verb"), "POST").ToUpperInvariant(), url, headers, body,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Substitute(string value, IReadOnlyDictionary<string, string> vars)
        => System.Text.RegularExpressions.Regex.Replace(value, @"_([\p{L}\p{N}._-]+)_", m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : throw new InvalidDataException($"Missing request variable '{m.Groups[1].Value}'."));
}
