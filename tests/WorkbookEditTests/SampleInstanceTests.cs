using LoadTestingTool.Domain;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using LoadTestingTool.MockServer;
using Xunit;

namespace WorkbookEditTests;

public class SampleInstanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sample-{Guid.NewGuid():N}");
    private MockServerHost? _host;

    public SampleInstanceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { _host?.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_root, true); } catch (IOException) { } }

    private string Build(out string settings, int port)
    {
        settings = Path.Combine(_root, "appsettings.json");
        return SampleInstance.Build(Path.Combine(_root, "Sample"), "DataEngine.xlsx", port, settings);
    }

    [Fact]
    public void BuildWritesWorkbookFilesMockConfigAndProfile()
    {
        var workbook = Build(out var settings, 8099);
        var folder = Path.GetDirectoryName(workbook)!;
        Assert.True(File.Exists(Path.Combine(folder, "Files", "sample.db")));
        Assert.True(File.Exists(Path.Combine(folder, SampleInstance.MockConfigFile)));
        Assert.Contains(SampleInstance.ProfileName, File.ReadAllText(settings));
        var config = MockServerConfig.Load(Path.Combine(folder, SampleInstance.MockConfigFile));
        Assert.Empty(config.Validate());
        var model = new WorkbookReader().Read(workbook);
        var steps = model.Testcases.SelectMany(t => t.Steps).Select(r => r.StepType).Distinct().ToList();
        foreach (var type in new[] { StepType.Http, StepType.Wcf, StepType.GraphQL, StepType.Sql, StepType.File, StepType.Script, StepType.If, StepType.While, StepType.For, StepType.ForEach, StepType.ActionManual })
            Assert.Contains(type, steps);
        using var book = new ClosedXML.Excel.XLWorkbook(workbook);
        var sheet = book.Worksheet("config");
        var noteColumn = sheet.Row(1).CellsUsed().Single(c => c.GetString() == "note").Address.ColumnNumber;
        for (var row = 2; row <= sheet.LastRowUsed()!.RowNumber(); row++) Assert.False(string.IsNullOrWhiteSpace(sheet.Cell(row, noteColumn).GetString()), $"row {row}");
    }

    [Fact]
    public async Task EveryEnabledTestcaseRunsAndPassesAgainstTheMockServer()
    {
        var port = MockServerHost.FreePort();
        var workbook = Build(out var settings, port);
        var folder = Path.GetDirectoryName(workbook)!;
        _host = new MockServerHost(MockServerConfig.Load(Path.Combine(folder, SampleInstance.MockConfigFile))); _host.Start();
        var model = new WorkbookReader().Read(workbook);
        var features = new FeatureConfig { WorkspaceRoot = folder };
        var profiles = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings)).RootElement.GetProperty("Connections");
        foreach (var p in profiles.EnumerateObject()) features.Connections[p.Name] = new SqlConnectionProfile { Provider = p.Value.GetProperty("Provider").GetString()!, ConnectionString = p.Value.GetProperty("ConnectionString").GetString()! };
        var runner = new TestRunner(new HttpClient(), new AssertionEngine(), Path.Combine(folder, "Templates"), 30, features: features);
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(folder);
        try
        {
            foreach (var testcase in model.Testcases)
                await runner.RunAsync(testcase, model, run, CancellationToken.None, executionMode: "sequential");
        }
        finally { Directory.SetCurrentDirectory(previous); }
        var failures = run.Testcases.SelectMany(t => t.Requests).Where(r => r.Result != "PASS" && r.Result != "SKIPPED")
            .Select(r => $"{r.Testcase}/{r.StepName}: {r.ErrorMessage} {string.Join("; ", r.Assertions.Where(a => a.Result == "FAIL").Select(a => $"{a.ResponsePath} expected '{a.ExpectedValue}' got '{a.ActualValue}'"))}").ToList();
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal(["Calc", "GetOrderGql", "GetOrderSoap"], run.Testcases.SelectMany(t => t.Requests).Select(r => r.StepName).Where(n => n is "Calc" or "GetOrderGql" or "GetOrderSoap").Distinct().Order().ToArray());
        Assert.True(File.Exists(Path.Combine(folder, "Artifacts", "sample-output.txt")));
    }
}
