using System.Text.Json;

namespace LoadTestingTool.Reporting;

public sealed record ManualActionResponse(string RequestId, string Result, string Comment, DateTimeOffset RespondedAt);

public static class ManualActionControl
{
    public static string ResponsePath(string folder, string requestId) => Path.Combine(folder, $"manual_{Safe(requestId)}.response.json");

    public static void WriteResponse(string folder, string requestId, string result, string comment)
    {
        Directory.CreateDirectory(folder);
        var path = ResponsePath(folder, requestId);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new ManualActionResponse(requestId, result, comment ?? string.Empty, DateTimeOffset.Now)));
        File.Move(temp, path, true);
    }

    public static bool TryReadResponse(string folder, string requestId, out ManualActionResponse response)
    {
        response = new ManualActionResponse(requestId, string.Empty, string.Empty, DateTimeOffset.MinValue);
        var path = ResponsePath(folder, requestId);
        if (!File.Exists(path)) return false;
        try
        {
            response = JsonSerializer.Deserialize<ManualActionResponse>(File.ReadAllText(path)) ?? response;
            File.Delete(path);
            return response.RequestId.Equals(requestId, StringComparison.OrdinalIgnoreCase) && (response.Result.Equals("pass", StringComparison.OrdinalIgnoreCase) || response.Result.Equals("fail", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static string Safe(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
}
