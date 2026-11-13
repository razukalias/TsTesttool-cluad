using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;

namespace LoadTestingTool.Execution;

public static class TemplateRenderer
{
    private const string Name = @"[\p{L}\p{N}._\-\[\]]+";
    private static readonly JsonSerializerOptions ReadableJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static string Render(string raw, string contentType, IReadOnlyDictionary<string, string> variables)
    {
        var value = raw ?? string.Empty;
        foreach (var pair in variables.Where(x => string.IsNullOrEmpty(x.Value)))
        {
            var escaped = Regex.Escape(pair.Key);
            if (IsJsonContentType(contentType))
                value = RemoveJsonProperty(value, escaped);
            else if (IsXmlContentType(contentType))
                value = Regex.Replace(value, $@"\s*<(?<tag>[\w:.-]+)>\s*_{escaped}_\s*</\k<tag>>", string.Empty, RegexOptions.IgnoreCase);
        }

        var rendered = Regex.Replace(value, $@"_(?:\*({Name})\*|({Name}))_", match =>
            variables.TryGetValue(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, out var replacement)
                ? replacement
                : throw new InvalidDataException($"Missing request variable '{(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)}'."));
        return IsJsonContentType(contentType)
            ? RemoveEmptyJsonObjects(rendered)
            : rendered;
    }

    private static bool IsJsonContentType(string? contentType)
    {
        var value = contentType?.Trim() ?? string.Empty;
        return value.Equals("json", StringComparison.OrdinalIgnoreCase)
            || value.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsXmlContentType(string? contentType)
    {
        var value = contentType?.Trim() ?? string.Empty;
        return value.Equals("xml", StringComparison.OrdinalIgnoreCase)
            || value.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveJsonProperty(string json, string escapedVariable)
    {
        var valueToken = "(?:\"(?:_" + escapedVariable + "_|_\\*" + escapedVariable + "\\*_)\"|(?:_" + escapedVariable + "_|_\\*" + escapedVariable + "\\*_))";
        // Prefer removing the property together with its following comma. This handles
        // first and middle properties without leaving a trailing comma.
        var withFollowingComma = "\"[^\"\\r\\n]+\"\\s*:\\s*" + valueToken + ",\\s*";
        json = Regex.Replace(json, withFollowingComma, string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // If it was the final property, remove its preceding comma instead.
        var finalProperty = ",\\s*\"[^\"\\r\\n]+\"\\s*:\\s*" + valueToken + "(?=\\s*[}])";
        json = Regex.Replace(json, finalProperty, string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Handle a single-property object or a final property that has no preceding comma.
        var onlyProperty = "\"[^\"\\r\\n]+\"\\s*:\\s*" + valueToken + "(?=\\s*[}])";
        json = Regex.Replace(json, onlyProperty, string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return json;
    }

    private static string RemoveEmptyJsonObjects(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root is null) return json;
            PruneEmptyObjects(root, isRoot: true);
            return root.ToJsonString(ReadableJsonOptions);
        }
        catch (JsonException)
        {
            // Preserve the original renderer error path for malformed JSON.
            // HTTP execution will report the malformed payload as it did before.
            return json;
        }
    }

    private static bool PruneEmptyObjects(JsonNode node, bool isRoot)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (property.Value is null) continue;
                if (PruneEmptyObjects(property.Value, isRoot: false)) jsonObject.Remove(property.Key);
            }
            return !isRoot && jsonObject.Count == 0;
        }

        if (node is JsonArray jsonArray)
        {
            for (var index = jsonArray.Count - 1; index >= 0; index--)
            {
                var item = jsonArray[index];
                if (item is not null && PruneEmptyObjects(item, isRoot: false)) jsonArray.RemoveAt(index);
            }
        }
        return false;
    }
}
