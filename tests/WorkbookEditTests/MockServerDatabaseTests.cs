using System.Net;
using System.Text;
using System.Text.Json;
using LoadTestingTool.Domain;
using LoadTestingTool.Execution;
using LoadTestingTool.MockServer;
using Xunit;
using RunnerExecutionContext = LoadTestingTool.Domain.ExecutionContext;

namespace WorkbookEditTests;

public class MockConditionTests
{
    private static MockRequest Req(string method = "POST", string path = "/orders/42", string body = "", Dictionary<string, string>? headers = null, params (string Key, string Value)[] query) =>
        new(method, path, query.ToDictionary(q => q.Key, q => q.Value, StringComparer.OrdinalIgnoreCase), headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), body);

    private const string Body = "{\"status\":\"NEW\",\"qty\":12,\"total\":59.9,\"when\":\"2026-11-20T10:00:00Z\",\"tier\":\"Gold\"}";

    [Theory]
    [InlineData("json", "status", "notequals", "PAID", false, true)]
    [InlineData("json", "status", "notequals", "NEW", false, false)]
    [InlineData("json", "status", "equals", "new", false, false)]
    [InlineData("json", "status", "equals", "new", true, true)]
    [InlineData("json", "status", "notcontains", "EW", false, false)]
    [InlineData("json", "tier", "startswith", "go", true, true)]
    [InlineData("json", "tier", "endswith", "LD", false, false)]
    [InlineData("json", "qty", "gt", "10", false, true)]
    [InlineData("json", "qty", "gt", "12", false, false)]
    [InlineData("json", "qty", "ge", "12", false, true)]
    [InlineData("json", "total", "lt", "60", false, true)]
    [InlineData("json", "total", "le", "59.8", false, false)]
    [InlineData("json", "qty", "between", "10..20", false, true)]
    [InlineData("json", "qty", "between", "13,20", false, false)]
    [InlineData("json", "when", "gt", "2026-11-01", false, true)]
    [InlineData("json", "status", "gt", "1", false, false)]
    [InlineData("json", "tier", "in", "Silver, Gold", false, true)]
    [InlineData("json", "tier", "in", "silver,gold", true, true)]
    [InlineData("json", "tier", "notin", "Silver,Bronze", false, true)]
    [InlineData("json", "missing", "notequals", "x", false, true)]
    [InlineData("json", "missing", "gt", "1", false, false)]
    [InlineData("path", "id", "equals", "42", false, true)]
    [InlineData("path", "id", "between", "1..9", false, false)]
    [InlineData("method", "", "in", "POST,PUT", false, true)]
    [InlineData("cookie", "session", "equals", "abc 1", false, true)]
    [InlineData("cookie", "other", "exists", "", false, false)]
    [InlineData("state", "cart", "equals", "Started", false, true)]
    public void NewOperatorsAndSources(string source, string name, string op, string value, bool ignoreCase, bool expected)
    {
        var request = Req(body: Body, headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Cookie"] = "theme=dark; session=abc%201" });
        var context = new MockMatchContext(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["id"] = "42" });
        Assert.Equal(expected, MockRouteMatcher.Holds(new MockCondition { Source = source, Name = name, Op = op, Value = value, IgnoreCase = ignoreCase }, request, context));
    }

    [Fact]
    public void ConditionsInAGroupAreAlternativesAndUngroupedOnesAreAllRequired()
    {
        var conditions = new List<MockCondition>
        {
            new() { Source = "method", Op = "equals", Value = "POST" },
            new() { Source = "json", Name = "tier", Op = "equals", Value = "Silver", Group = "vip" },
            new() { Source = "json", Name = "qty", Op = "gt", Value = "10", Group = "vip" }
        };
        var context = MockMatchContext.Empty;
        Assert.Null(MockRouteMatcher.FirstFailure(conditions, Req(body: Body), context));
        Assert.Contains("none of group 'vip' held", MockRouteMatcher.FirstFailure(conditions, Req(body: "{\"tier\":\"Gold\",\"qty\":1}"), context));
        Assert.StartsWith("condition 1 failed", MockRouteMatcher.FirstFailure(conditions, Req("GET", body: Body), context));
    }

    [Fact]
    public void TheTraceSaysWhyEachRouteDidNotFit()
    {
        var config = new MockServerConfig { Routes =
        [
            new MockRoute { Name = "Paid", Method = "POST", Path = "/orders/{id}", Conditions = [new MockCondition { Source = "json", Name = "status", Op = "equals", Value = "PAID" }] },
            new MockRoute { Name = "Off", Method = "POST", Path = "/orders/{id}", Enabled = false },
            new MockRoute { Name = "Big", Method = "POST", Path = "/orders/{id}", Conditions = [new MockCondition { Source = "path", Name = "id", Op = "gt", Value = "100" }] },
            new MockRoute { Name = "Get", Method = "GET", Path = "/orders/{id}" }
        ]};
        var trace = new List<string>();
        Assert.Null(MockRouteMatcher.Find(config, Req(body: Body), null, trace));
        Assert.Equal(3, trace.Count);
        Assert.Equal("Route 1 (Paid): condition 1 failed: json status equals 'PAID', but it was 'NEW'.", trace[0]);
        Assert.Contains("disabled", trace[1]);
        Assert.Equal("Route 3 (Big): condition 1 failed: path {id} > '100', but it was '42'.", trace[2]);

        trace.Clear();
        Assert.Null(MockRouteMatcher.Find(config, Req("DELETE", body: Body), null, trace));
        Assert.Equal("No route has DELETE /orders/42; this path has POST, GET.", Assert.Single(trace));
        trace.Clear();
        Assert.Null(MockRouteMatcher.Find(config, Req("GET", "/nothing"), null, trace));
        Assert.Equal("No route has the path /nothing.", Assert.Single(trace));
    }

    [Fact]
    public void ValidationKnowsTheNewSourcesOperatorsAndActions()
    {
        var config = new MockServerConfig { Routes =
        [
            new MockRoute { Path = "/a/{id}", Conditions =
            [
                new MockCondition { Source = "path", Name = "nope", Op = "equals", Value = "1" },
                new MockCondition { Source = "json", Name = "qty", Op = "gt", Value = "lots" },
                new MockCondition { Source = "json", Name = "qty", Op = "between", Value = "1-5" },
                new MockCondition { Source = "json", Name = "tier", Op = "in", Value = " , " },
                new MockCondition { Source = "cookie", Name = "", Op = "exists" },
                new MockCondition { Source = "json", Name = "qty", Op = "between", Value = "1..5" }
            ],
            Sequence = [new MockResponse { Status = 42 }], SetState = [new MockStateChange { Scenario = " " }], Data = new MockDataAction() }
        ]};
        var problems = config.Validate();
        Assert.Contains(problems, p => p.Contains("has no {nope} value"));
        Assert.Contains(problems, p => p.Contains("'lots' is neither"));
        Assert.Contains(problems, p => p.Contains("'between' needs two numbers"));
        Assert.Contains(problems, p => p.Contains("needs a list"));
        Assert.Contains(problems, p => p.Contains("a cookie condition needs a name"));
        Assert.Contains(problems, p => p.Contains("answer 1 of the sequence: status 42"));
        Assert.Contains(problems, p => p.Contains("state change has no scenario"));
        Assert.Contains(problems, p => p.Contains("no database file"));
        Assert.Contains(problems, p => p.Contains("has no SQL"));
        Assert.Equal(9, problems.Count);
    }

    [Fact]
    public void NewFieldsSurviveSaveAndLoad()
    {
        var config = new MockServerConfig { Routes =
        [
            new MockRoute
            {
                Conditions = [new MockCondition { Source = "cookie", Name = "s", Op = "in", Value = "a,b", IgnoreCase = true, Group = "g" }],
                Sequence = [new MockResponse { Status = 503 }], SequenceLoop = true, SetState = [new MockStateChange { Scenario = "cart", State = "full" }],
                Data = new MockDataAction { Database = "Databases/shop.db", Sql = "select 1", Parameters = { ["id"] = "{{path.id}}" }, NotFoundStatus = 404, NotFoundBody = "{}" }
            }
        ]};
        var back = MockServerConfig.FromJson(config.ToJson()).Routes[0];
        Assert.True(back.Conditions[0].IgnoreCase); Assert.Equal("g", back.Conditions[0].Group);
        Assert.Equal(503, back.Sequence[0].Status); Assert.True(back.SequenceLoop);
        Assert.Equal("full", back.SetState[0].State);
        Assert.Equal("{{path.id}}", back.Data!.Parameters["id"]); Assert.Equal(404, back.Data.NotFoundStatus);
        Assert.Null(MockServerConfig.FromJson("{\"Routes\":[{\"Path\":\"/x\"}]}").Routes[0].Data);
    }

    [Fact]
    public void TemplatesFillCookiesStatesCountsAndRows()
    {
        var request = Req(headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Cookie"] = "u=eva" });
        var data = new MockTemplateData
        {
            Count = 3, State = s => s == "cart" ? "full" : MockStates.Initial, Affected = 1, LastId = 7,
            Rows = [new(StringComparer.OrdinalIgnoreCase) { ["id"] = "O1", ["total"] = 59.9, ["note"] = null }, new(StringComparer.OrdinalIgnoreCase) { ["id"] = "O2", ["total"] = 1L, ["note"] = "a\"b" }]
        };
        var text = MockTemplate.Render("{{cookie.u}}|{{state.cart}}|{{state.x}}|{{count}}|{{row.id}}|{{row.total}}|{{row.note}}|{{rowcount}}|{{affected}}|{{lastid}}", request, new Dictionary<string, string>(), data);
        Assert.Equal("eva|full|Started|3|O1|59.9||2|1|7", text);
        Assert.Equal("{\"id\":\"O1\",\"total\":59.9,\"note\":null}", MockTemplate.Render("{{row}}", request, new Dictionary<string, string>(), data));
        using var rows = JsonDocument.Parse(MockTemplate.Render("{{rows}}", request, new Dictionary<string, string>(), data));
        Assert.Equal("a\"b", rows.RootElement[1].GetProperty("note").GetString());
        Assert.Equal("null|[]|0", MockTemplate.Render("{{row}}|{{rows}}|{{rowcount}}", request, new Dictionary<string, string>()));
    }
}

public class MockDatabaseManagerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mockdb-{Guid.NewGuid():N}");
    public MockDatabaseManagerTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private static MockTable Orders() => new()
    {
        Name = "orders", Columns =
        [
            new MockColumn { Name = "id", Type = "INTEGER", PrimaryKey = true, AutoIncrement = true },
            new MockColumn { Name = "customer", Type = "TEXT", NotNull = true },
            new MockColumn { Name = "status", Type = "TEXT", NotNull = true, Default = "NEW" },
            new MockColumn { Name = "total", Type = "REAL", Default = "0" }
        ]
    };

    [Fact]
    public void CreateListAndRefuseBadOrDoubleNames()
    {
        var path = MockDatabase.Create(_folder, "shop");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(MockDatabase.SchemaFile(path)));
        Assert.True(File.Exists(MockDatabase.SeedFile(path)));
        Assert.Equal(["shop"], MockDatabase.List(_folder));
        Assert.Throws<InvalidOperationException>(() => MockDatabase.Create(_folder, "shop"));
        Assert.Throws<InvalidOperationException>(() => MockDatabase.Create(_folder, "../evil"));
        Assert.Equal("Databases/shop.db", MockDatabase.RouteReference("shop"));
        MockDatabase.Delete(path);
        Assert.Empty(MockDatabase.List(_folder));
    }

    [Fact]
    public void TablesAreCreatedDescribedAndWrittenToTheSchemaScript()
    {
        var path = MockDatabase.Create(_folder, "shop");
        Assert.Equal("CREATE TABLE \"orders\" (\n  \"id\" INTEGER PRIMARY KEY AUTOINCREMENT,\n  \"customer\" TEXT NOT NULL,\n  \"status\" TEXT NOT NULL DEFAULT 'NEW',\n  \"total\" REAL DEFAULT 0\n);", MockDatabase.CreateTableSql(Orders()));
        MockDatabase.CreateTable(path, Orders());
        var table = Assert.Single(MockDatabase.Tables(path));
        Assert.Equal(["id", "customer", "status", "total"], table.Columns.Select(c => c.Name));
        Assert.True(table.Columns[0].PrimaryKey);
        Assert.Equal("'NEW'", table.Columns[2].Default);
        Assert.Contains("CREATE TABLE \"orders\"", File.ReadAllText(MockDatabase.SchemaFile(path)));
        MockDatabase.AddColumn(path, "orders", new MockColumn { Name = "note", Type = "TEXT" });
        Assert.Contains("\"note\"", File.ReadAllText(MockDatabase.SchemaFile(path)));
        Assert.Throws<InvalidOperationException>(() => MockDatabase.AddColumn(path, "orders", new MockColumn { Name = "x", NotNull = true }));
        Assert.Contains(MockDatabase.Check(new MockTable { Name = "t", Columns = [new MockColumn { Name = "a" }, new MockColumn { Name = "A" }] }), p => p.Contains("twice"));
        Assert.Contains(MockDatabase.Check(new MockTable { Name = "t", Columns = [new MockColumn { Name = "a", Type = "TEXT", PrimaryKey = true, AutoIncrement = true }] }), p => p.Contains("Auto-increment"));
        MockDatabase.DropTable(path, "orders");
        Assert.Empty(MockDatabase.Tables(path));
    }

    [Fact]
    public void RowsAreAddedEditedDeletedSavedAsSeedAndResetBringsThemBack()
    {
        var path = MockDatabase.Create(_folder, "shop");
        MockDatabase.CreateTable(path, Orders());
        var first = MockDatabase.InsertRow(path, "orders", new Dictionary<string, object?> { ["customer"] = "C1", ["total"] = 59.9 });
        MockDatabase.InsertRow(path, "orders", new Dictionary<string, object?> { ["customer"] = "O'Neil", ["status"] = "PAID" });
        MockDatabase.UpdateCell(path, "orders", first, "status", MockDatabase.FromText("SHIPPED"));
        var (columns, rows) = MockDatabase.Rows(path, "orders");
        Assert.Equal(["id", "customer", "status", "total"], columns);
        Assert.Equal("SHIPPED", rows[0].Values[2]);
        Assert.Equal("NEW", MockDatabase.Rows(path, "orders").Rows.Count == 2 ? "NEW" : "");
        Assert.Equal(2, MockDatabase.SaveSeed(path));
        Assert.Contains("'O''Neil'", File.ReadAllText(MockDatabase.SeedFile(path)));
        MockDatabase.DeleteRows(path, "orders", [first]);
        MockDatabase.InsertRow(path, "orders", new Dictionary<string, object?> { ["customer"] = "temp" });
        Assert.Equal(2, MockDatabase.Rows(path, "orders").Rows.Count);
        Assert.Null(MockDatabase.Reset(path));
        var back = MockDatabase.Rows(path, "orders").Rows;
        Assert.Equal(2, back.Count);
        Assert.Equal("SHIPPED", back[0].Values[2]);
        Assert.Equal(59.9, back[0].Values[3]);
        Assert.Equal("O'Neil", back[1].Values[1]);
    }

    [Fact]
    public void ResetNamesTheScriptThatFailed()
    {
        var path = MockDatabase.Create(_folder, "shop");
        File.WriteAllText(MockDatabase.SchemaFile(path), "CREATE TABLE a (x TEXT);");
        File.WriteAllText(MockDatabase.SeedFile(path), "INSERT INTO nope VALUES (1);");
        Assert.Contains("The seed script (shop.seed.sql) stopped", MockDatabase.Reset(path));
        Assert.Single(MockDatabase.Tables(path));
    }

    [Theory]
    [InlineData("", null)] [InlineData("NULL", null)] [InlineData("12", 12L)] [InlineData("-3", -3L)] [InlineData("007", "007")]
    [InlineData("12.5", 12.5)] [InlineData("0.25", 0.25)] [InlineData("1e5", "1e5")] [InlineData("abc", "abc")]
    public void GridTextBecomesAValue(string text, object? expected) => Assert.Equal(expected, MockDatabase.FromText(text));

    [Fact]
    public void QueryReadsEveryResultSetTheChangedRowsAndTheLastId()
    {
        var path = MockDatabase.Create(_folder, "shop");
        MockDatabase.CreateTable(path, Orders());
        var insert = MockDatabase.Query(path, "insert into orders (customer) values (@c)", new Dictionary<string, object?> { ["c"] = "C9" });
        Assert.Equal(1, insert.Affected); Assert.Equal(1, insert.LastId); Assert.Empty(insert.ResultSets);
        var two = MockDatabase.Query(path, "select id, customer from orders; select count(*) as n from orders");
        Assert.Equal(2, two.ResultSets.Count);
        Assert.Equal(["id", "customer"], two.Columns);
        Assert.Equal("C9", two.Rows[0]["customer"]);
        Assert.Equal(1L, two.ResultSets[1][0]["n"]);
    }

    [Fact]
    public void CsvIsImportedIntoANewOrAnExistingTable()
    {
        var path = MockDatabase.Create(_folder, "shop");
        Assert.Equal(2, MockDatabase.ImportCsv(path, "people", "name;city\nEva;\"Stock;holm\"\n\"Jon \"\"J\"\"\";Oslo\n", createTable: true));
        var rows = MockDatabase.Rows(path, "people").Rows;
        Assert.Equal("Stock;holm", rows[0].Values[1]);
        Assert.Equal("Jon \"J\"", rows[1].Values[0]);
        MockDatabase.CreateTable(path, Orders());
        Assert.Equal(1, MockDatabase.ImportCsv(path, "orders", "customer,total\r\nC1,10.5\r\n", createTable: false));
        Assert.Equal(10.5, MockDatabase.Rows(path, "orders").Rows[0].Values[3]);
        Assert.Contains("no column city", Assert.Throws<InvalidOperationException>(() => MockDatabase.ImportCsv(path, "orders", "city\nX", false)).Message);
        Assert.Throws<InvalidOperationException>(() => MockDatabase.ImportCsv(path, "nope", "a\n1", false));
    }

    [Fact]
    public void TheSampleBecomesAManagedDatabaseWithScripts()
    {
        var path = MockDatabase.CreateSample(_folder);
        Assert.Equal(["customers", "order_lines", "orders"], MockDatabase.Tables(path).Select(t => t.Name));
        Assert.Equal(4, MockDatabase.Tables(path).First(t => t.Name == "orders").RowCount);
        MockDatabase.Execute(path, "delete from order_lines; delete from orders");
        Assert.Null(MockDatabase.Reset(path));
        Assert.Equal(4, MockDatabase.Tables(path).First(t => t.Name == "orders").RowCount);
    }
}

public class MockServerStateAndDataTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mockstate-{Guid.NewGuid():N}");
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(5) };
    private MockServerHost? _host;

    public MockServerStateAndDataTests() => Directory.CreateDirectory(_folder);
    public void Dispose() { _host?.Dispose(); _client.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_folder, true); } catch (IOException) { } }

    private string Start(MockServerConfig config)
    {
        config.Port = MockServerHost.FreePort();
        _host = new MockServerHost(config, _folder);
        _host.Start();
        return $"http://127.0.0.1:{config.Port}";
    }

    private async Task<(int Status, string Body)> Send(string method, string url, string? json = null)
    {
        using var message = new HttpRequestMessage(new HttpMethod(method), url);
        if (json is not null) message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.SendAsync(message);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ASequenceAnswersInTurnThenFallsBackOrLoops()
    {
        var url = Start(new MockServerConfig { Routes =
        [
            new MockRoute { Method = "GET", Path = "/flaky", Sequence = [new MockResponse { Status = 503, Body = "busy {{count}}" }, new MockResponse { Status = 500 }], Response = new MockResponse { Body = "ok {{count}}" } },
            new MockRoute { Method = "GET", Path = "/loop", SequenceLoop = true, Sequence = [new MockResponse { Body = "a" }, new MockResponse { Body = "b" }] }
        ]});
        Assert.Equal((503, "busy 1"), await Send("GET", url + "/flaky"));
        Assert.Equal(500, (await Send("GET", url + "/flaky")).Status);
        Assert.Equal((200, "ok 3"), await Send("GET", url + "/flaky"));
        Assert.Equal(["a", "b", "a"], new[] { (await Send("GET", url + "/loop")).Body, (await Send("GET", url + "/loop")).Body, (await Send("GET", url + "/loop")).Body });
        Assert.Contains("sequence answer 1 of 2", _host!.Snapshot()[0].Note);
        _host.ResetState();
        Assert.Equal(503, (await Send("GET", url + "/flaky")).Status);
    }

    [Fact]
    public async Task ScenarioStatesChangeWhichRouteAnswers()
    {
        var url = Start(new MockServerConfig { Routes =
        [
            new MockRoute { Name = "Logged in", Method = "GET", Path = "/me", Conditions = [new MockCondition { Source = "state", Name = "auth", Op = "equals", Value = "in" }], Response = new MockResponse { Body = "{\"user\":\"eva\"}" } },
            new MockRoute { Name = "Anonymous", Method = "GET", Path = "/me", Response = new MockResponse { Status = 401, Body = "state={{state.auth}}" } },
            new MockRoute { Name = "Login", Method = "POST", Path = "/login", SetState = [new MockStateChange { Scenario = "auth", State = "in" }] },
            new MockRoute { Name = "Logout", Method = "POST", Path = "/logout", SetState = [new MockStateChange { Scenario = "auth", State = "" }] }
        ]});
        Assert.Equal((401, "state=Started"), await Send("GET", url + "/me"));
        await Send("POST", url + "/login");
        Assert.Equal(200, (await Send("GET", url + "/me")).Status);
        Assert.Equal("in", _host!.States["auth"]);
        await Send("POST", url + "/logout");
        Assert.Equal(401, (await Send("GET", url + "/me")).Status);
        var log = _host.Snapshot();
        Assert.Contains("state: auth → in", log.First(e => e.Route == "Login").Note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(log.First(e => e.Route == "Anonymous").Explanation, line => line.Contains("state of 'auth' equals 'in', but it was 'Started'"));
        using var control = new MockServerClient(_host.Port);
        Assert.Equal("Started", (await control.StatesAsync())["auth"]);
        await Send("POST", url + "/login");
        await control.ResetStateAsync();
        Assert.Empty(await control.StatesAsync());
    }

    [Fact]
    public async Task RoutesReadAndWriteAMockDatabase()
    {
        var db = MockDatabase.Create(_folder, "shop");
        MockDatabase.CreateTable(db, new MockTable { Name = "orders", Columns =
        [
            new MockColumn { Name = "id", Type = "INTEGER", PrimaryKey = true, AutoIncrement = true },
            new MockColumn { Name = "customer", Type = "TEXT", NotNull = true },
            new MockColumn { Name = "qty", Type = "INTEGER", NotNull = true, Default = "1" }
        ]});
        var reference = MockDatabase.RouteReference("shop");
        var url = Start(new MockServerConfig { Routes =
        [
            new MockRoute { Name = "List", Method = "GET", Path = "/orders", Data = new MockDataAction { Database = reference, Sql = "select * from orders order by id" }, Response = new MockResponse { Body = "{{rows}}" } },
            new MockRoute { Name = "Get", Method = "GET", Path = "/orders/{id}", Data = new MockDataAction { Database = reference, Sql = "select * from orders where id = @id", NotFoundStatus = 404, NotFoundBody = "{\"error\":\"order {{path.id}} not found\"}" }, Response = new MockResponse { Body = "{{row}}" } },
            new MockRoute { Name = "Create", Method = "POST", Path = "/orders", Data = new MockDataAction { Database = reference, Sql = "insert into orders (customer, qty) values (@customer, @qty)", Parameters = { ["customer"] = "{{json.customer}}", ["qty"] = "{{json.qty}}" } }, Response = new MockResponse { Status = 201, Body = "{\"id\":{{lastid}}}" } },
            new MockRoute { Name = "Delete", Method = "DELETE", Path = "/orders/{id}", Data = new MockDataAction { Database = reference, Sql = "delete from orders where id = @id", NotFoundStatus = 404 }, Response = new MockResponse { Status = 204 } },
            new MockRoute { Name = "Broken", Method = "GET", Path = "/broken", Data = new MockDataAction { Database = reference, Sql = "select * from nope" } }
        ]});
        Assert.Equal((201, "{\"id\":1}"), await Send("POST", url + "/orders", "{\"customer\":\"C1\",\"qty\":3}"));
        Assert.Equal((201, "{\"id\":2}"), await Send("POST", url + "/orders", "{\"customer\":\"C2\",\"qty\":5}"));
        var one = await Send("GET", url + "/orders/2");
        Assert.Equal(200, one.Status);
        using (var row = JsonDocument.Parse(one.Body)) { Assert.Equal("C2", row.RootElement.GetProperty("customer").GetString()); Assert.Equal(5, row.RootElement.GetProperty("qty").GetInt32()); }
        using (var all = JsonDocument.Parse((await Send("GET", url + "/orders")).Body)) Assert.Equal(2, all.RootElement.GetArrayLength());
        Assert.Equal((404, "{\"error\":\"order 9 not found\"}"), await Send("GET", url + "/orders/9"));
        Assert.Equal(204, (await Send("DELETE", url + "/orders/1")).Status);
        Assert.Equal(404, (await Send("DELETE", url + "/orders/1")).Status);
        var broken = await Send("GET", url + "/broken");
        Assert.Equal(500, broken.Status);
        Assert.Contains("no such table", broken.Body);
        var log = _host!.Snapshot();
        Assert.Contains("1 changed", log.First(e => e.Route == "Create").Data);
        Assert.Contains("not-found", log.First(e => e.Url == "/orders/9").Note);
        Assert.StartsWith("Database action failed", log.First(e => e.Route == "Broken").Note);
    }

    [Fact]
    public async Task ADatabaseOutsideTheServerFolderIsRefused()
    {
        var url = Start(new MockServerConfig { Routes = [new MockRoute { Method = "GET", Path = "/x", Data = new MockDataAction { Database = "../other.db", Sql = "select 1" } }] });
        var answer = await Send("GET", url + "/x");
        Assert.Equal(500, answer.Status);
        Assert.Contains("outside the server", answer.Body);
    }
}

public class SqlStepResultTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sqlres-{Guid.NewGuid():N}");
    public SqlStepResultTests() { Directory.CreateDirectory(_root); SampleDatabase.Create(Path.Combine(_root, "s.db")); }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_root, true); } catch (IOException) { } }

    private async Task<StepResult> Run(string sql, string outputs = "{}")
    {
        var features = new FeatureConfig { WorkspaceRoot = _root, Connections = { ["Db"] = new SqlConnectionProfile { Provider = "Sqlite", ConnectionString = "Data Source=s.db" } } };
        var config = JsonSerializer.Serialize(new { ConnectionProfile = "Db", CommandText = sql, Outputs = JsonDocument.Parse(outputs).RootElement });
        var step = new RequestStep { StepName = "Q", StepType = StepType.Sql, StepConfig = config };
        return await new SqlStepExecutor(features).ExecuteAsync(new StepExecutionRequest { Step = step, Context = new RunnerExecutionContext(), WorkspaceRoot = _root, CancellationToken = CancellationToken.None });
    }

    [Fact]
    public async Task OneResultSetStaysAPlainArray()
    {
        var result = await Run("select id from customers order by id");
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(result.ResponseText).RootElement.ValueKind);
        Assert.Equal(0, result.Outputs["rowsAffected"]);
    }

    [Fact]
    public async Task AnUpdateReportsTheRowsItChanged()
    {
        var result = await Run("update orders set status = 'X' where customer_id = 'C1'", "{\"changed\":\"rowsAffected\"}");
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("{\"rowsAffected\":2}", result.ResponseText);
        Assert.Equal(2, result.Outputs["rowsAffected"]);
        Assert.Equal(2, result.Outputs["changed"]);
    }

    [Fact]
    public async Task SeveralResultSetsAreAllReturned()
    {
        var result = await Run("select id from customers where id = 'C1'; select count(*) as n from orders", "{\"first\":\"id\"}");
        Assert.True(result.Succeeded, result.ErrorMessage);
        using var doc = JsonDocument.Parse(result.ResponseText);
        Assert.Equal(2, doc.RootElement.GetProperty("resultSets").GetArrayLength());
        Assert.Equal(4, doc.RootElement.GetProperty("resultSets")[1][0].GetProperty("n").GetInt32());
        Assert.Equal("C1", result.Outputs["first"]);
    }
}
