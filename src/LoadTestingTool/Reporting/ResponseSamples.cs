using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;

namespace LoadTestingTool.Reporting;

/// <summary>A value found in a JSON response, with the path the runner uses to address it (<c>data.cases[0].id</c>).</summary>
public sealed record ResponseField(string Path, string Value);

public sealed record ResponseSample(string Body, string RunId, string DataId, string Source);

/// <summary>Finds what a step answered in earlier runs, so the editor can offer the response's fields instead of asking for paths to be typed.</summary>
public static class ResponseSamples
{
    public const int MaxFields = 500;
    private const int MaxArrayItems = 5;

    /// <summary>Every leaf of a JSON document as a flat list with dotted paths; arrays are indexed ([0]). Returns nothing for text that is not JSON.</summary>
    public static List<ResponseField> Flatten(string json)
    {
        var fields = new List<ResponseField>();
        if (string.IsNullOrWhiteSpace(json)) return fields;
        if (json.TrimStart().StartsWith('<'))
        {
            // XML (SOAP): the same paths the runner reads, from the tree
            if (ResponseTree.ParseXml(json, out _) is { } xml) WalkXml(xml, fields);
            return fields;
        }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            Walk(document.RootElement, string.Empty, fields);
        }
        catch (JsonException) { /* not JSON (XML, plain text, a truncated body): no fields */ }
        return fields;
    }

    private static void WalkXml(ResponseNode node, List<ResponseField> fields)
    {
        if (fields.Count >= MaxFields) return;
        if (node.IsLeaf && node.Path.Length > 0) fields.Add(new ResponseField(node.Path, node.Value));
        foreach (var child in node.Kind == ResponseNodeKind.Array ? node.Children.Take(MaxArrayItems) : node.Children) WalkXml(child, fields);
    }

    private static void Walk(JsonElement element, string path, List<ResponseField> fields)
    {
        if (fields.Count >= MaxFields) return;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Walk(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, fields);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index >= MaxArrayItems) break;
                    Walk(item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", fields);
                    index++;
                }
                break;
            case JsonValueKind.String: if (path.Length > 0) fields.Add(new ResponseField(path, element.GetString() ?? string.Empty)); break;
            case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False: if (path.Length > 0) fields.Add(new ResponseField(path, element.GetRawText())); break;
            case JsonValueKind.Null: if (path.Length > 0) fields.Add(new ResponseField(path, string.Empty)); break;
        }
    }

    /// <summary>
    /// The newest stored response body of a step. History files are searched newest first; within a file the row of
    /// <paramref name="preferredDataId"/> wins, else any row of the step. Bodies the history did not keep ("[omitted]") are skipped.
    /// </summary>
    public static ResponseSample? Latest(string historyFolder, string stepName, string? preferredDataId = null, int maxFiles = 60)
    {
        if (!Directory.Exists(historyFolder) || stepName.Length == 0) return null;
        foreach (var file in new DirectoryInfo(historyFolder).EnumerateFiles("*.xlsx", SearchOption.AllDirectories)
                     .Where(f => !f.Name.StartsWith("~$", StringComparison.Ordinal)).OrderByDescending(f => f.LastWriteTimeUtc).Take(maxFiles))
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var book = new XLWorkbook(stream);
                if (!book.TryGetWorksheet("RequestHistory", out var sheet)) continue;
                var headers = sheet.FirstRowUsed()?.CellsUsed(c => !string.IsNullOrWhiteSpace(c.GetString())).ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber);
                if (headers is null || !headers.TryGetValue("stepname", out var stepColumn) || !headers.TryGetValue("responsebody", out var bodyColumn)) continue;
                headers.TryGetValue("dataid", out var dataColumn);
                headers.TryGetValue("runid", out var runColumn);
                ResponseSample? any = null;
                foreach (var row in sheet.RowsUsed().Skip(1).Reverse())
                {
                    if (!row.Cell(stepColumn).GetString().Equals(stepName, StringComparison.OrdinalIgnoreCase)) continue;
                    var body = row.Cell(bodyColumn).GetString();
                    if (string.IsNullOrWhiteSpace(body) || body == "[omitted]") continue;
                    var dataId = dataColumn > 0 ? row.Cell(dataColumn).GetString() : string.Empty;
                    var sample = new ResponseSample(body, runColumn > 0 ? row.Cell(runColumn).GetString() : string.Empty, dataId, file.Name);
                    if (preferredDataId is not null && dataId.Equals(preferredDataId, StringComparison.OrdinalIgnoreCase)) return sample;
                    any ??= sample;
                }
                if (any is not null) return any;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { /* a file that is open or damaged is skipped */ }
        }
        return null;
    }

    /// <summary>
    /// The newest stored response body of a step for each data ID (history files newest first; the first body found for an ID wins), so every row of a testcase
    /// can be filled from its own response. Bodies the history did not keep ("[omitted]") are skipped.
    /// </summary>
    public static Dictionary<string, ResponseSample> LatestPerDataId(string historyFolder, string stepName, int maxFiles = 60)
    {
        var found = new Dictionary<string, ResponseSample>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(historyFolder) || stepName.Length == 0) return found;
        foreach (var file in new DirectoryInfo(historyFolder).EnumerateFiles("*.xlsx", SearchOption.AllDirectories)
                     .Where(f => !f.Name.StartsWith("~$", StringComparison.Ordinal)).OrderByDescending(f => f.LastWriteTimeUtc).Take(maxFiles))
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var book = new XLWorkbook(stream);
                if (!book.TryGetWorksheet("RequestHistory", out var sheet)) continue;
                var headers = sheet.FirstRowUsed()?.CellsUsed(c => !string.IsNullOrWhiteSpace(c.GetString())).ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber);
                if (headers is null || !headers.TryGetValue("stepname", out var stepColumn) || !headers.TryGetValue("responsebody", out var bodyColumn) || !headers.TryGetValue("dataid", out var dataColumn)) continue;
                headers.TryGetValue("runid", out var runColumn);
                foreach (var row in sheet.RowsUsed().Skip(1).Reverse())
                {
                    if (!row.Cell(stepColumn).GetString().Equals(stepName, StringComparison.OrdinalIgnoreCase)) continue;
                    var body = row.Cell(bodyColumn).GetString();
                    var dataId = row.Cell(dataColumn).GetString();
                    if (string.IsNullOrWhiteSpace(body) || body == "[omitted]" || dataId.Length == 0 || found.ContainsKey(dataId)) continue;
                    found[dataId] = new ResponseSample(body, runColumn > 0 ? row.Cell(runColumn).GetString() : string.Empty, dataId, file.Name);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { /* a file that is open or damaged is skipped */ }
        }
        return found;
    }

    /// <summary>A header the server sent, as stored in the history (<c>Name=value</c>, one per line; older files used "; ").</summary>
    public sealed record HeaderSample(string Name, string Value);

    /// <summary>Splits the stored response-header text. Older history files joined the headers with "; ", which also appears inside values (<c>text/html; charset=utf-8</c>), so there a part only starts a new header when its name begins with an upper-case letter.</summary>
    public static List<HeaderSample> ParseHeaders(string text)
    {
        var headers = new List<HeaderSample>();
        if (string.IsNullOrWhiteSpace(text)) return headers;
        var parts = text.Contains('\n') ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : text.Split("; ", StringSplitOptions.RemoveEmptyEntries);
        var legacy = !text.Contains('\n');
        foreach (var part in parts)
        {
            var equals = part.IndexOf('=');
            var name = equals > 0 ? part[..equals].Trim() : string.Empty;
            var startsHeader = name.Length > 0 && !name.Contains(' ') && (!legacy || char.IsUpper(name[0]) || headers.Count == 0);
            if (startsHeader) headers.Add(new HeaderSample(name, part[(equals + 1)..].Trim()));
            else if (headers.Count > 0) headers[^1] = headers[^1] with { Value = headers[^1].Value + "; " + part };
        }
        return headers;
    }

    /// <summary>Headers pasted or saved as text, one per line: <c>Name: value</c> (browser/curl -i style) or <c>Name=value</c>. A status line (HTTP/1.1 200 OK) and blank lines are ignored.</summary>
    public static List<HeaderSample> ParseHeaderLines(string text)
    {
        var headers = new List<HeaderSample>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase)) continue;
            var colon = line.IndexOf(':');
            var equals = line.IndexOf('=');
            var at = colon > 0 && (equals < 0 || colon < equals) ? colon : equals;
            if (at <= 0) continue;
            var name = line[..at].Trim();
            if (name.Length == 0 || name.Contains(' ')) continue;
            headers.Add(new HeaderSample(name, line[(at + 1)..].Trim()));
        }
        return headers;
    }

    /// <summary>The response headers of the newest stored run of a step (the row of <paramref name="preferredDataId"/> first).</summary>
    public static (List<HeaderSample> Headers, string Source)? LatestHeaders(string historyFolder, string stepName, string? preferredDataId = null, int maxFiles = 60)
    {
        if (!Directory.Exists(historyFolder) || stepName.Length == 0) return null;
        foreach (var file in new DirectoryInfo(historyFolder).EnumerateFiles("*.xlsx", SearchOption.AllDirectories)
                     .Where(f => !f.Name.StartsWith("~$", StringComparison.Ordinal)).OrderByDescending(f => f.LastWriteTimeUtc).Take(maxFiles))
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var book = new XLWorkbook(stream);
                if (!book.TryGetWorksheet("RequestHistory", out var sheet)) continue;
                var columns = sheet.FirstRowUsed()?.CellsUsed(c => !string.IsNullOrWhiteSpace(c.GetString())).ToDictionary(c => c.GetString().Trim().ToLowerInvariant(), c => c.Address.ColumnNumber);
                if (columns is null || !columns.TryGetValue("stepname", out var stepColumn) || !columns.TryGetValue("responseheaders", out var headerColumn)) continue;
                columns.TryGetValue("dataid", out var dataColumn);
                (List<HeaderSample>, string)? any = null;
                foreach (var row in sheet.RowsUsed().Skip(1).Reverse())
                {
                    if (!row.Cell(stepColumn).GetString().Equals(stepName, StringComparison.OrdinalIgnoreCase)) continue;
                    var headers = ParseHeaders(row.Cell(headerColumn).GetString());
                    if (headers.Count == 0) continue;
                    var dataId = dataColumn > 0 ? row.Cell(dataColumn).GetString() : string.Empty;
                    var found = (headers, $"{file.Name}, data ID {dataId}");
                    if (preferredDataId is not null && dataId.Equals(preferredDataId, StringComparison.OrdinalIgnoreCase)) return found;
                    any ??= found;
                }
                if (any is not null) return any;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { /* a file that is open or damaged is skipped */ }
        }
        return null;
    }
}
