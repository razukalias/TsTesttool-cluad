using System.IO.Compression;
using System.Text;

namespace LoadTestingTool.Reporting;

/// <summary>One step execution as the history keeps it: what ran, how it ended and (in the body fields) what was sent and received. Values are masked the way the workbook history masks them.</summary>
public sealed record RequestDetail(int Seq, string RequestId, string RowKey, int TestcaseIndex, string Testcase, string DataId, int Iteration, string Environment, string Step, string StepType, string Result,
    int HttpStatus, long DurationMs, DateTimeOffset StartedAt, string Error, string FailureCategory, string Verb, string TargetUrl, string StepConfig, string RequestHeaders, string RequestBody, string ResponseHeaders, string ResponseBody);

/// <summary>One assertion of a request (<see cref="RequestSeq"/> is the <see cref="RequestDetail.Seq"/> of the request it belongs to).</summary>
public sealed record AssertionDetail(int Seq, int RequestSeq, string Testcase, string DataId, string Step, string Path, string Verb, string Type, string Meaning, string Expected, string Actual, string Result, string Message);

/// <summary>The requests and assertions of one run, to be stored next to its rows.</summary>
public sealed record HistoryDetails(IReadOnlyList<RequestDetail> Requests, IReadOnlyList<AssertionDetail> Assertions)
{
    public static HistoryDetails Empty { get; } = new([], []);
}

/// <summary>Bodies are kept compressed: a response is mostly repeated JSON, which shrinks to a fraction.</summary>
internal static class StoredText
{
    public static byte[]? Pack(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }

    public static string Unpack(object? value)
    {
        if (value is not byte[] bytes || bytes.Length == 0) return string.Empty;
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
