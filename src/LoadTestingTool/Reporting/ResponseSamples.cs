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
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            Walk(document.RootElement, string.Empty, fields);
        }
        catch (JsonException) { /* not JSON (XML, plain text, a truncated body): no fields */ }
        return fields;
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
}
