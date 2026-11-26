using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using LoadTestingTool.Domain;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using Xunit;
using Xunit.Abstractions;

namespace WorkbookEditTests;

/// <summary>
/// Is the response that is asserted really the response of THAT row, cell, environment and instance? Six copies of one workbook (rows and columns shuffled differently in each, the
/// request values all unique) run at the same time, with rows and environments in parallel, against a server that answers after random delays so replies come back out of order.
/// Every request body, response body and assertion is then compared with what that row was given.
/// </summary>
public sealed class ParallelIsolationTests : IDisposable
{
    private const int Instances = 6, Rows = 40, Columns = 10;
    private static readonly string[] Environments = ["dev", "test", "prod"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"isolation-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _output;
    private HttpListener? _listener;
    private int _port;
    private int _served;

    public ParallelIsolationTests(ITestOutputHelper output) { _output = output; Directory.CreateDirectory(_root); }
    public void Dispose() { try { _listener?.Stop(); _listener?.Close(); } catch { } try { Directory.Delete(_root, true); } catch (IOException) { } }

    /// <summary>The server echoes the request body, after a random delay (some of them long), so the answers do not come back in the order they were asked for.</summary>
    private void StartServer()
    {
        _port = LoadTestingTool.MockServer.MockServerHost.FreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); } catch { return; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                        var body = await reader.ReadToEndAsync();
                        await Task.Delay(Random.Shared.Next(0, 6) == 0 ? Random.Shared.Next(40, 120) : Random.Shared.Next(0, 25));
                        var env = context.Request.Url!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
                        var echo = JsonNode.Parse(body);
                        var reply = new JsonObject { ["row"] = echo?["row"]?.GetValue<string>(), ["echo"] = echo };
                        context.Response.AddHeader("X-Env", env);
                        var bytes = Encoding.UTF8.GetBytes(reply.ToJsonString());
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes);
                        Interlocked.Increment(ref _served);
                    }
                    catch { }
                    finally { try { context.Response.Close(); } catch { } }
                });
            }
        });
    }

    private sealed record Truth(string Folder, string Workbook, Dictionary<(string DataId, string Path), string> Values);

    /// <summary>One instance: its own folder, workbook, templates; rows and columns of both sheets shuffled with the instance's own seed.</summary>
    private Truth BuildInstance(int instance)
    {
        var random = new Random(1000 + instance);
        var folder = Path.Combine(_root, "Instance" + instance);
        Directory.CreateDirectory(Path.Combine(folder, "Templates"));
        var names = Enumerable.Range(1, Columns).Select(c => "c" + c).ToList();
        File.WriteAllText(Path.Combine(folder, "Templates", "echo.json"), "{ \"row\": \"_row_\", " + string.Join(", ", names.Select(n => $"\"{n}\": \"_{n}_\"")) + " }");
        File.WriteAllText(Path.Combine(folder, "Templates", "echo2.json"), "{ \"row\": \"_row_\", \"prev\": \"<From_response_Echo.echo.c1_r>\", \"prevrow\": \"<From_response_Echo.row_r>\" }");

        var truth = new Dictionary<(string, string), string>();
        var ids = Enumerable.Range(1, Rows).Select(n => $"I{instance}R{n:000}").ToList();
        foreach (var id in ids)
        {
            truth[(id, "row")] = $"i{instance}-{id}-ROW-{random.Next(100000, 999999)}";
            foreach (var n in names) truth[(id, n)] = $"i{instance}-{id}-{n}-{random.Next(100000, 999999)}";
        }

        string path = Path.Combine(folder, "workbook.xlsx");
        using (var book = new XLWorkbook())
        {
            var config = book.AddWorksheet("config");
            string[] header = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb", "contenttype", "expectedstatus"];
            string[][] steps =
            [
                ["1", "Isolation", "Echo", "HTTP", "echo.json", $"http://127.0.0.1:{_port}/echo/_env_", "POST", "json", "200"],
                ["1", "Isolation", "Echo2", "HTTP", "echo2.json", $"http://127.0.0.1:{_port}/echo/_env_", "POST", "json", "200"],
            ];
            for (var c = 0; c < header.Length; c++) config.Cell(1, c + 1).Value = header[c];
            for (var r = 0; r < steps.Length; r++) for (var c = 0; c < header.Length; c++) config.Cell(r + 2, c + 1).Value = steps[r][c];

            // request sheet: shared columns row + c1..cN, in a shuffled column order, rows in a shuffled order
            var request = book.AddWorksheet("request");
            var requestColumns = new[] { "row" }.Concat(names).OrderBy(_ => random.Next()).ToList();
            var fixedHeaders = new[] { "testcaseindex", "testcase", "dataid", "dontrun" };
            for (var c = 0; c < fixedHeaders.Length; c++) request.Cell(1, c + 1).Value = fixedHeaders[c];
            for (var c = 0; c < requestColumns.Count; c++) request.Cell(1, fixedHeaders.Length + c + 1).Value = requestColumns[c];
            var requestRows = ids.OrderBy(_ => random.Next()).ToList();
            for (var r = 0; r < requestRows.Count; r++)
            {
                request.Cell(r + 2, 1).Value = 1; request.Cell(r + 2, 2).Value = "Isolation"; request.Cell(r + 2, 3).Value = requestRows[r];
                for (var c = 0; c < requestColumns.Count; c++) request.Cell(r + 2, fixedHeaders.Length + c + 1).Value = truth[(requestRows[r], requestColumns[c])];
            }

            // response sheet: the expected values of every echoed field of step 1, and of the chained fields of step 2; another shuffle
            var response = book.AddWorksheet("response");
            var expectedColumns = names.Select(n => ($"{{eq}}Echo.echo.{n}", n)).Append(("{eq}Echo.row", "row")).Append(("{eq}Echo.echo.row", "row")).Append(("{eq}Echo2.echo.row", "row"))
                .Append(("{eq}Echo2.row", "row")).Append(("{eq}Echo2.echo.prev", "c1")).Append(("{eq}Echo2.echo.prevrow", "row")).OrderBy(_ => random.Next()).ToList();
            var responseFixed = new[] { "testcaseindex", "testcase", "dataid" };
            for (var c = 0; c < responseFixed.Length; c++) response.Cell(1, c + 1).Value = responseFixed[c];
            for (var c = 0; c < expectedColumns.Count; c++) response.Cell(1, responseFixed.Length + c + 1).Value = expectedColumns[c].Item1;
            var responseRows = ids.OrderBy(_ => random.Next()).ToList();
            for (var r = 0; r < responseRows.Count; r++)
            {
                response.Cell(r + 2, 1).Value = 1; response.Cell(r + 2, 2).Value = "Isolation"; response.Cell(r + 2, 3).Value = responseRows[r];
                for (var c = 0; c < expectedColumns.Count; c++) response.Cell(r + 2, responseFixed.Length + c + 1).Value = truth[(responseRows[r], expectedColumns[c].Item2)];
            }
            book.SaveAs(path);
        }
        return new Truth(folder, path, truth);
    }

    private static string Field(JsonNode? node, string path)
    {
        foreach (var part in path.Split('.')) node = node?[part];
        return node?.ToString() ?? "<missing>";
    }

    /// <summary>Everything that does not match what the row was given: request body, response body, environment, every assertion's actual and expected value.</summary>
    private static List<string> Violations(int instance, string environment, RunResult run, Truth truth)
    {
        var bad = new List<string>();
        var names = Enumerable.Range(1, Columns).Select(c => "c" + c).ToList();
        foreach (var row in run.Testcases)
        {
            var id = row.DataId;
            if (row.Environment != environment) bad.Add($"{id}: row result says environment '{row.Environment}', ran in '{environment}'");
            if (row.Requests.Count != 2) bad.Add($"{id}/{environment}: {row.Requests.Count} steps instead of 2");
            foreach (var r in row.Requests)
            {
                var where = $"i{instance}/{environment}/{id}/{r.StepName}";
                if (r.DataId != id) bad.Add($"{where}: the step belongs to {r.DataId}");
                if (r.Result != "PASS") bad.Add($"{where}: {r.Result} {r.ErrorMessage}");
                JsonNode? sent, got;
                try { sent = JsonNode.Parse(r.RequestBody); got = JsonNode.Parse(r.ResponseBody); } catch (Exception ex) { bad.Add($"{where}: body is not JSON ({ex.Message})"); continue; }
                if (!r.ResponseHeaders.Contains("X-Env=" + environment, StringComparison.OrdinalIgnoreCase)) bad.Add($"{where}: the server saw another environment, headers: {r.ResponseHeaders.Replace('\n', ' ')}");
                if (r.StepName == "Echo")
                {
                    if (Field(sent, "row") != truth.Values[(id, "row")]) bad.Add($"{where}: sent row '{Field(sent, "row")}'");
                    foreach (var n in names)
                    {
                        if (Field(sent, n) != truth.Values[(id, n)]) bad.Add($"{where}: sent {n} = '{Field(sent, n)}' instead of '{truth.Values[(id, n)]}'");
                        if (Field(got, "echo." + n) != truth.Values[(id, n)]) bad.Add($"{where}: response {n} = '{Field(got, "echo." + n)}' instead of '{truth.Values[(id, n)]}'");
                    }
                }
                else
                {
                    if (Field(sent, "prev") != truth.Values[(id, "c1")]) bad.Add($"{where}: chained prev '{Field(sent, "prev")}' is not this row's c1");
                    if (Field(sent, "prevrow") != truth.Values[(id, "row")]) bad.Add($"{where}: chained prevrow '{Field(sent, "prevrow")}' is not this row's row");
                }
                foreach (var a in r.Assertions)
                {
                    var key = a.ResponsePath switch { "row" => "row", "echo.prev" => "c1", "echo.prevrow" => "row", var p when p.StartsWith("echo.") => p[5..], _ => null };
                    if (key is null || !truth.Values.TryGetValue((id, key), out var want)) { bad.Add($"{where}: unexpected assertion on '{a.ResponsePath}'"); continue; }
                    if (a.ActualValue != want) bad.Add($"{where}: assertion {a.ResponsePath} saw '{a.ActualValue}' but this row sent '{want}'");
                    if (a.ExpectedValue != want) bad.Add($"{where}: assertion {a.ResponsePath} expected '{a.ExpectedValue}' but the workbook cell holds '{want}'");
                    if (a.Result != "PASS") bad.Add($"{where}: assertion {a.ResponsePath} {a.Result}");
                    if (a.DataId != id || !string.Equals(a.StepName, r.StepName, StringComparison.OrdinalIgnoreCase)) bad.Add($"{where}: assertion is filed under {a.DataId}/{a.StepName}");
                }
                var expectedAssertions = r.StepName == "Echo" ? Columns + 2 : 4;
                if (r.Assertions.Count != expectedAssertions) bad.Add($"{where}: {r.Assertions.Count} assertions instead of {expectedAssertions}");
            }
        }
        if (run.Testcases.Count != Rows) bad.Add($"i{instance}/{environment}: {run.Testcases.Count} rows ran instead of {Rows}");
        if (run.Testcases.Select(t => t.DataId).Distinct().Count() != run.Testcases.Count) bad.Add($"i{instance}/{environment}: a row ran twice or another did not run");
        return bad;
    }

    [Fact]
    public async Task Six_shuffled_instances_in_parallel_with_parallel_rows_and_environments_assert_only_their_own_responses()
    {
        StartServer();
        var truths = Enumerable.Range(1, Instances).Select(BuildInstance).ToList();
        var outcomes = new List<(int Instance, string Environment, RunResult Run)>();
        var gate = new object();

        // all instances at once, each with all its environments at once, each environment with all its rows at once
        await Task.WhenAll(truths.Select((truth, index) => Task.Run(async () =>
        {
            var model = new WorkbookReader().Read(truth.Workbook);
            var runner = new TestRunner(new HttpClient(), new AssertionEngine(), Path.Combine(truth.Folder, "Templates"), 30, features: new FeatureConfig { WorkspaceRoot = truth.Folder });
            await Task.WhenAll(Environments.Select(async environment =>
            {
                var run = new RunResult { RunId = $"iso{index}{environment}", StartedAt = DateTimeOffset.Now, Environment = environment };
                await runner.RunAsync(model.Testcases.Single(), model, run, CancellationToken.None, environment, "parallel", null, 1, 1, 0, 0, 100);
                lock (gate) outcomes.Add((index + 1, environment, run));
            }));
        })));

        Assert.Equal(Instances * Environments.Length, outcomes.Count);
        var violations = outcomes.SelectMany(o => Violations(o.Instance, o.Environment, o.Run, truths[o.Instance - 1])).ToList();
        var checkedAssertions = outcomes.SelectMany(o => o.Run.Testcases).SelectMany(t => t.Requests).SelectMany(r => r.Assertions).Count();
        _output.WriteLine($"{outcomes.Count} runs, {outcomes.Sum(o => o.Run.Testcases.Count)} rows, {_served} requests served, {checkedAssertions} assertions compared, {violations.Count} violations");
        Assert.True(violations.Count == 0, $"{violations.Count} violations:\n" + string.Join("\n", violations.Take(25)));
        Assert.Equal(Instances * Environments.Length * Rows * 2, _served);
    }

    [Fact]
    public async Task The_check_itself_notices_when_two_rows_swap_their_values()
    {
        StartServer();
        var truth = BuildInstance(9);
        var model = new WorkbookReader().Read(truth.Workbook);
        var runner = new TestRunner(new HttpClient(), new AssertionEngine(), Path.Combine(truth.Folder, "Templates"), 30, features: new FeatureConfig { WorkspaceRoot = truth.Folder });
        var run = new RunResult { RunId = "isocontrol", StartedAt = DateTimeOffset.Now, Environment = "dev" };
        await runner.RunAsync(model.Testcases.Single(), model, run, CancellationToken.None, "dev", "parallel", null, 1, 1, 0, 0, 100);
        var baseline = Violations(9, "dev", run, truth);
        Assert.True(baseline.Count == 0, string.Join("\n", baseline.Take(8)) + "\n" + string.Join("\n", run.Testcases.SelectMany(t => t.Requests).Take(1).SelectMany(r => r.Assertions.Take(3).Select(a => $"[{a.DataId}|{a.StepName}|{a.ResponsePath}|exp={a.ExpectedValue}|act={a.ActualValue}|{a.Result}|{a.FailureMessage}] status={r.HttpStatus} err={r.ErrorMessage}"))));
        // pretend the engine had mixed up two cells of two rows: the same run now has to be reported
        var a = ("I9R001", "c3"); var b = ("I9R002", "c3");
        (truth.Values[a], truth.Values[b]) = (truth.Values[b], truth.Values[a]);
        Assert.NotEmpty(Violations(9, "dev", run, truth));
    }

    [Fact]
    public async Task Rows_by_environment_runs_each_row_only_in_its_own_environment()
    {
        StartServer();
        var truth = BuildInstance(8);
        File.WriteAllText(Path.Combine(truth.Folder, "appsettings.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["ExcelFilePath"] = truth.Workbook, ["TemplatesFolder"] = Path.Combine(truth.Folder, "Templates"), ["LogsFolder"] = Path.Combine(truth.Folder, "Logs"),
            ["HistoryFolder"] = Path.Combine(truth.Folder, "History"), ["ResultsFolder"] = Path.Combine(truth.Folder, "Results"), ["WorkspaceRoot"] = truth.Folder,
            ["Environments"] = Environments, ["RunEnvironmentsParallel"] = true, ["TestcaseSelection"] = "1",
        }));
        var dll = typeof(AppConfig).Assembly.Location;
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { WorkingDirectory = truth.Folder, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { dll, "--config", Path.Combine(truth.Folder, "appsettings.json"), "--testcases", "1", "--dataids", "1:I8R001,I8R002,I8R003",
            "--rows-by-environment", "{\"dev\":[\"I8R001\"],\"prod\":[\"I8R002\",\"I8R003\"]}", "--environments-json", "[\"dev\",\"prod\"]" }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, output);
        Assert.Equal(3 * 2, _served);   // 2 steps per row run: dev x 1 row + prod x 2 rows; nothing else ran
    }

    private async Task<(int ExitCode, string Output, string History)> RunProcess(Truth truth, int instance, string storage, params string[] extra)
    {
        File.WriteAllText(Path.Combine(truth.Folder, "appsettings.json"), System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["ExcelFilePath"] = truth.Workbook, ["TemplatesFolder"] = Path.Combine(truth.Folder, "Templates"), ["LogsFolder"] = Path.Combine(truth.Folder, "Logs"),
            ["HistoryFolder"] = Path.Combine(truth.Folder, "History"), ["ResultsFolder"] = Path.Combine(truth.Folder, "Results"), ["WorkspaceRoot"] = truth.Folder,
            ["Environments"] = Environments, ["HistoryStorage"] = storage, ["TestcaseSelection"] = "1",
        }));
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { WorkingDirectory = truth.Folder, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(AppConfig).Assembly.Location, "--config", Path.Combine(truth.Folder, "appsettings.json"), "--testcases", "1", "--dataids", $"1:I{instance}R001,I{instance}R002,I{instance}R003", "--environments-json", "[\"dev\"]" }.Concat(extra)) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output, Path.Combine(truth.Folder, "History"));
    }

    [Fact]
    public async Task Storage_both_writes_workbooks_and_database_that_agree_and_database_only_writes_no_workbooks()
    {
        StartServer();
        var truth = BuildInstance(5);
        var both = await RunProcess(truth, 5, "Both");
        Assert.True(both.ExitCode == 0, both.Output);
        var store = new LoadTestingTool.Reporting.SqliteHistoryStore(both.History);
        Assert.True(store.Exists, both.Output + "\n" + string.Join("\n", Directory.GetFileSystemEntries(both.History, "*", SearchOption.AllDirectories)));
        Assert.Equal(3, store.ScanPage(0, 10).Rows.Count);
        Assert.NotEmpty(Directory.GetFiles(both.History, "*.xlsx", SearchOption.AllDirectories));
        Assert.Empty(store.CompareWithWorkbooks(both.History));

        var onlyDatabase = BuildInstance(4);
        var database = await RunProcess(onlyDatabase, 4, "Database");
        Assert.True(database.ExitCode == 0, database.Output);
        Assert.Empty(Directory.GetFiles(database.History, "*.xlsx", SearchOption.AllDirectories));
        var rows = new LoadTestingTool.Reporting.SqliteHistoryStore(database.History).ScanPage(0, 10).Rows;
        Assert.Equal(3, rows.Count);
        // a re-run with the old data of that run reads it from the database, not from workbooks
        var runId = rows[0].RunId;
        var rerun = await RunProcess(onlyDatabase, 4, "Database", "--rerun-of", runId, "--rerun", "true");
        Assert.True(rerun.ExitCode == 0, rerun.Output);
        Assert.Equal(2, new LoadTestingTool.Reporting.SqliteHistoryStore(database.History).ScanPage(0, 10).Files);
    }

    [Fact]
    public async Task Both_mode_over_several_runs_and_a_rerun_keeps_workbooks_and_database_in_agreement()
    {
        StartServer();
        var truth = BuildInstance(3);
        var store = new LoadTestingTool.Reporting.SqliteHistoryStore(Path.Combine(truth.Folder, "History"));
        var first = await RunProcess(truth, 3, "Both");
        Assert.True(first.ExitCode == 0, first.Output);
        Assert.Empty(store.CompareWithWorkbooks(first.History));
        var runId = store.ScanPage(0, 10).Rows[0].RunId;

        var second = await RunProcess(truth, 3, "Both", "--rerun-of", runId, "--rerun", "true");    // a re-run with the old data of the first run
        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Empty(store.CompareWithWorkbooks(second.History));

        var third = await RunProcess(truth, 3, "Both");
        Assert.True(third.ExitCode == 0, third.Output);
        var differences = store.CompareWithWorkbooks(third.History);
        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(10)));
        Assert.Equal(3, store.ScanPage(0, 10).Files);
        Assert.Equal(9, store.ScanPage(0, 10).Rows.Count);          // 3 runs x 3 rows
        Assert.Equal(runId, store.ScanPage(0, 10).Rows.Single(r => r.RerunOf is not null && r.DataId == "I3R001").RerunOf);
    }
}
