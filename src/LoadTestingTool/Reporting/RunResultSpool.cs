using System.Text.Json;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Reporting;

/// <summary>
/// Persists completed row results incrementally so a large run has a durable,
/// append-only record even while the compatibility history model is still built.
/// </summary>
public sealed class RunResultSpool : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public RunResultSpool(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        Path = path;
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
    }

    public string Path { get; }

    public void Append(TestcaseResult result)
    {
        var record = new
        {
            recordType = "row-completed",
            runId = result.RunId,
            testcaseIndex = result.TestcaseIndex,
            testcaseRowNumber = result.TestcaseRowNumber,
            excelRowNumber = result.ExcelRowNumber,
            testcase = result.Testcase,
            dataId = result.DataId,
            thread = result.Thread,
            iteration = result.Iteration,
            environment = result.Environment,
            startedAt = result.StartedAt,
            endedAt = result.EndedAt,
            result = result.Result,
            requests = result.Requests
        };
        var json = JsonSerializer.Serialize(record, _options);
        lock (_sync) _writer.WriteLine(json);
    }

    public void Dispose() => _writer.Dispose();
}
