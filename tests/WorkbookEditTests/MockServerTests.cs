using System.Net;
using System.Text;
using LoadTestingTool.MockServer;
using Xunit;

namespace WorkbookEditTests;

public class MockMatchingTests
{
    private static MockRequest Req(string method, string path, string body = "", params (string Key, string Value)[] query) =>
        new(method, path, query.ToDictionary(q => q.Key, q => q.Value, StringComparer.OrdinalIgnoreCase), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Tenant"] = "acme" }, body);

    [Theory]
    [InlineData("/orders/{id}", "/orders/42", true)]
    [InlineData("/orders/{id}", "/orders/42/", true)]
    [InlineData("/orders/{id}", "/orders", false)]
    [InlineData("/orders/{id}", "/orders/42/lines", false)]
    [InlineData("/orders/*/lines", "/orders/7/lines", true)]
    [InlineData("/files/**", "/files/a/b/c.txt", true)]
    [InlineData("/files/**", "/files", true)]
    [InlineData("/", "/", true)]
    [InlineData("/**", "/anything/at/all", true)]
    [InlineData("/Orders", "/orders", true)]
    public void PathPatterns(string pattern, string path, bool expected) => Assert.Equal(expected, MockPath.Match(pattern, path) is not null);

    [Fact]
    public void PathValuesAreDecodedAndNamed()
    {
        var values = MockPath.Match("/orders/{id}/lines/{line}", "/orders/A%20B/lines/3");
        Assert.Equal("A B", values!["id"]);
        Assert.Equal("3", values["line"]);
    }

    [Theory]
    [InlineData("/a/**/b")] [InlineData("/a/{bad name}")] [InlineData("/a//b")] [InlineData("/a/{x")]
    public void BadPatternsAreRefused(string pattern) => Assert.Null(MockPath.Compile(pattern));

    [Fact]
    public void FirstEnabledRouteWinsAndMethodAnyMatchesCustomVerbs()
    {
        var config = new MockServerConfig { Routes =
        [
            new MockRoute { Name = "off", Enabled = false, Method = "ANY", Path = "/x" },
            new MockRoute { Name = "get", Method = "GET", Path = "/x" },
            new MockRoute { Name = "any", Method = "ANY", Path = "/x" }
        ]};
        Assert.Equal("get", MockRouteMatcher.Find(config, Req("GET", "/x"))!.Route.Name);
        Assert.Equal("any", MockRouteMatcher.Find(config, Req("PURGE", "/x"))!.Route.Name);
        Assert.Null(MockRouteMatcher.Find(config, Req("GET", "/y")));
    }

    [Fact]
    public void BasePathIsStrippedBeforeMatching()
    {
        var config = new MockServerConfig { BasePath = "/api/v1", Routes = [new MockRoute { Name = "o", Path = "/orders/{id}" }, new MockRoute { Name = "root", Path = "/" }] };
        Assert.Equal("42", MockRouteMatcher.Find(config, Req("GET", "/api/v1/orders/42"))!.PathParams["id"]);
        Assert.Equal("root", MockRouteMatcher.Find(config, Req("GET", "/api/v1"))!.Route.Name);
        Assert.Null(MockRouteMatcher.Find(config, Req("GET", "/orders/42")));
    }

    [Theory]
    [InlineData("query", "mode", "equals", "fast", true)]
    [InlineData("query", "mode", "equals", "slow", false)]
    [InlineData("query", "mode", "exists", "", true)]
    [InlineData("query", "nope", "notexists", "", true)]
    [InlineData("header", "x-tenant", "equals", "acme", true)]
    [InlineData("header", "X-Tenant", "contains", "CM", true)]
    [InlineData("body", "", "contains", "\"sku\"", true)]
    [InlineData("json", "order.lines[1].sku", "equals", "B2", true)]
    [InlineData("json", "order.lines[5].sku", "exists", "", false)]
    [InlineData("json", "order.total", "regex", "^1\\d\\.5$", true)]
    public void Conditions(string source, string name, string op, string value, bool expected)
    {
        var body = "{\"order\":{\"total\":12.5,\"lines\":[{\"sku\":\"A1\"},{\"sku\":\"B2\"}]}}";
        var condition = new MockCondition { Source = source, Name = name, Op = op, Value = value };
        Assert.Equal(expected, MockRouteMatcher.Holds(condition, Req("POST", "/x", body, ("mode", "fast"))));
    }

    [Fact]
    public void SamePathAnswersByCondition()
    {
        var config = new MockServerConfig { Routes =
        [
            new MockRoute { Name = "vip", Method = "GET", Path = "/p", Conditions = [new MockCondition { Source = "header", Name = "X-Tenant", Op = "equals", Value = "acme" }] },
            new MockRoute { Name = "other", Method = "GET", Path = "/p" }
        ]};
        Assert.Equal("vip", MockRouteMatcher.Find(config, Req("GET", "/p"))!.Route.Name);
    }

    [Fact]
    public void TemplatesFillRequestValues()
    {
        var request = Req("POST", "/orders/9", "{\"customer\":{\"name\":\"Eva\"}}", ("q", "1"));
        var text = MockTemplate.Render("{{method}} {{path.id}} {{query.q}} {{header.x-tenant}} {{json.customer.name}} {{unknown}} {{ path }}", request, new Dictionary<string, string> { ["id"] = "9" });
        Assert.Equal("POST 9 1 acme Eva  /orders/9", text);
        Assert.Matches("^[0-9a-f-]{36}$", MockTemplate.Render("{{guid}}", request, new Dictionary<string, string>()));
    }

    [Fact]
    public void ValidationNamesEachProblem()
    {
        var config = new MockServerConfig { Port = 0, BasePath = "api", Routes =
        [
            new MockRoute { Method = "G ET", Path = "orders" },
            new MockRoute { Path = "/a/**/b", FailurePercent = 120, Response = new MockResponse { Status = 42 } },
            new MockRoute { Path = "/ok", Conditions = [new MockCondition { Source = "cookie", Op = "like" }, new MockCondition { Source = "body", Op = "regex", Value = "(" }] }
        ]};
        var text = string.Join("\n", config.Validate());
        Assert.Contains("Port 0", text); Assert.Contains("base path", text); Assert.Contains("not an HTTP method", text);
        Assert.Contains("must start with /", text); Assert.Contains("not valid", text); Assert.Contains("0-100", text); Assert.Contains("status 42", text);
        Assert.Contains("cookie", text); Assert.Contains("like", text); Assert.Contains("regular expression", text);
        Assert.Empty(new MockServerConfig { Routes = [new MockRoute { Path = "/ok" }] }.Validate());
    }

    [Fact]
    public void ConfigSurvivesSaveAndLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mock-{Guid.NewGuid():N}", "server.json");
        var config = new MockServerConfig { Name = "S", Port = 9001, Routes = [new MockRoute { Name = "r", Method = "PUT", Path = "/a/{id}", Conditions = [new MockCondition { Source = "json", Name = "a.b", Op = "equals", Value = "1" }], Response = new MockResponse { Status = 201, Headers = [new MockHeader { Name = "X-A", Value = "1" }], Body = "hi", DelayMs = 5 } }] };
        config.Save(path);
        var back = MockServerConfig.Load(path);
        Assert.Equal(9001, back.Port);
        Assert.Equal("PUT", back.Routes[0].Method);
        Assert.Equal("a.b", back.Routes[0].Conditions[0].Name);
        Assert.Equal("X-A", back.Routes[0].Response.Headers[0].Name);
        Assert.Equal(201, back.Routes[0].Response.Status);
    }
}

public class MockServerHostTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mock-{Guid.NewGuid():N}");
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(5) };
    private MockServerHost? _host;

    public MockServerHostTests() => Directory.CreateDirectory(_folder);

    private string Start(MockServerConfig config)
    {
        config.Port = MockServerHost.FreePort();
        _host = new MockServerHost(config, _folder);
        _host.Start();
        return $"http://127.0.0.1:{config.Port}";
    }

    public void Dispose() { _host?.Dispose(); _client.Dispose(); try { Directory.Delete(_folder, true); } catch (IOException) { } }

    [Fact]
    public async Task EveryVerbIsServed()
    {
        var url = Start(new MockServerConfig { Routes = [new MockRoute { Name = "all", Path = "/v", Response = new MockResponse { Body = "{{method}}" } }] });
        foreach (var verb in new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "PURGE", "LINK" })
        {
            var answer = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(verb), url + "/v"));
            Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            Assert.Equal(verb, await answer.Content.ReadAsStringAsync());
        }
        var head = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url + "/v"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("", await head.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ResponseHasStatusHeadersTypeAndTemplatedBody()
    {
        var url = Start(new MockServerConfig { Routes = [new MockRoute { Name = "create", Method = "POST", Path = "/orders/{id}", Response = new MockResponse
        {
            Status = 201, ContentType = "application/vnd.test+json", Headers = [new MockHeader { Name = "X-Order", Value = "{{path.id}}" }, new MockHeader { Name = "Location", Value = "/orders/{{path.id}}" }],
            Body = "{\"id\":\"{{path.id}}\",\"who\":\"{{json.customer}}\",\"tenant\":\"{{header.x-tenant}}\"}"
        } }] });
        var request = new HttpRequestMessage(HttpMethod.Post, url + "/orders/77?x=1") { Content = new StringContent("{\"customer\":\"Eva\"}", Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Tenant", "acme");
        var answer = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, answer.StatusCode);
        Assert.Equal("application/vnd.test+json", answer.Content.Headers.ContentType!.MediaType);
        Assert.Equal("77", answer.Headers.GetValues("X-Order").Single());
        Assert.Equal("/orders/77", answer.Headers.GetValues("Location").Single());
        Assert.Equal("{\"id\":\"77\",\"who\":\"Eva\",\"tenant\":\"acme\"}", await answer.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnmatchedRequestsGetTheFallbackAndAreLoggedAsUnmatched()
    {
        var url = Start(new MockServerConfig());
        var answer = await _client.GetAsync(url + "/nothing");
        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Contains("GET /nothing", await answer.Content.ReadAsStringAsync());
        var entry = Assert.Single(_host!.Snapshot());
        Assert.False(entry.Matched);
        Assert.Equal(404, entry.Status);
    }

    [Fact]
    public async Task LogKeepsRequestAndResponseDetail()
    {
        var url = Start(new MockServerConfig { Routes = [new MockRoute { Name = "echo", Method = "POST", Path = "/echo", Response = new MockResponse { Headers = [new MockHeader { Name = "X-Seen", Value = "yes" }], Body = "got {{body}}" } }] });
        var request = new HttpRequestMessage(HttpMethod.Post, url + "/echo?a=1") { Content = new StringContent("hello", Encoding.UTF8, "text/plain") };
        request.Headers.Add("X-Trace", "t1");
        await _client.SendAsync(request);
        var entry = Assert.Single(_host!.Snapshot());
        Assert.Equal("POST", entry.Method); Assert.Equal("/echo?a=1", entry.Url); Assert.Equal("echo", entry.Route); Assert.True(entry.Matched);
        Assert.Equal("hello", entry.RequestBody); Assert.Contains("X-Trace: t1", entry.RequestHeaders);
        Assert.Equal("got hello", entry.ResponseBody); Assert.Contains("X-Seen: yes", entry.ResponseHeaders);
        Assert.Equal(9, entry.Size);
    }

    [Fact]
    public async Task DelayIsApplied()
    {
        var url = Start(new MockServerConfig { Routes = [new MockRoute { Path = "/slow", Response = new MockResponse { DelayMs = 300 } }] });
        var started = DateTime.UtcNow;
        await _client.GetAsync(url + "/slow");
        Assert.True((DateTime.UtcNow - started).TotalMilliseconds >= 280);
        Assert.True(_host!.Snapshot().Single().DurationMs >= 280);
    }

    [Fact]
    public async Task InjectedFailureAnswers500OrDropsTheConnection()
    {
        var url = Start(new MockServerConfig { Routes =
        [
            new MockRoute { Name = "boom", Path = "/boom", FailurePercent = 100 },
            new MockRoute { Name = "drop", Path = "/drop", FailurePercent = 100, DropConnection = true },
            new MockRoute { Name = "never", Path = "/never", FailurePercent = 0, DropConnection = true }
        ]});
        Assert.Equal(HttpStatusCode.InternalServerError, (await _client.GetAsync(url + "/boom")).StatusCode);
        await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetAsync(url + "/drop"));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(url + "/never")).StatusCode);
    }

    [Fact]
    public async Task BodyFileIsServedAndCannotEscapeTheFolder()
    {
        File.WriteAllText(Path.Combine(_folder, "data.json"), "{\"from\":\"file\"}");
        var url = Start(new MockServerConfig { Routes =
        [
            new MockRoute { Path = "/f", Response = new MockResponse { BodyFile = "data.json" } },
            new MockRoute { Path = "/escape", Response = new MockResponse { BodyFile = "../outside.txt" } }
        ]});
        Assert.Equal("{\"from\":\"file\"}", await _client.GetStringAsync(url + "/f"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await _client.GetAsync(url + "/escape")).StatusCode);
    }

    [Fact]
    public async Task ControlCommandsReloadClearAndStop()
    {
        var url = Start(new MockServerConfig { Routes = [new MockRoute { Path = "/a", Response = new MockResponse { Body = "one" } }] });
        using var control = new MockServerClient(_host!.Port);
        Assert.Equal("one", await _client.GetStringAsync(url + "/a"));
        var status = await control.StatusAsync();
        Assert.Equal(1, status!.Value.Routes);
        Assert.Equal(1, status.Value.LastSeq);
        Assert.Single(await control.RequestsAsync(0));
        Assert.Empty(await control.RequestsAsync(1));

        var changed = _host.Config;
        changed.Routes[0].Response.Body = "two";
        Assert.Null(await control.ReloadAsync(changed));
        Assert.Equal("two", await _client.GetStringAsync(url + "/a"));

        var wrongPort = MockServerConfig.FromJson(changed.ToJson()); wrongPort.Port++;
        Assert.Contains("restart", await control.ReloadAsync(wrongPort));
        var invalid = MockServerConfig.FromJson(changed.ToJson()); invalid.Routes[0].Path = "nope";
        Assert.Contains("must start with /", await control.ReloadAsync(invalid));

        await control.ClearAsync();
        Assert.Empty(await control.RequestsAsync(0));
        Assert.False(_host.StopRequested.IsCompleted);
        await control.StopAsync();
        await Task.WhenAny(_host.StopRequested, Task.Delay(2000));
        Assert.True(_host.StopRequested.IsCompleted);
    }

    [Fact]
    public async Task LogIsLimited()
    {
        var url = Start(new MockServerConfig { LogLimit = 10, Routes = [new MockRoute { Path = "/n" }] });
        for (var i = 0; i < 15; i++) await _client.GetAsync(url + "/n");
        var log = _host!.Snapshot();
        Assert.Equal(10, log.Count);
        Assert.Equal(15, log[^1].Seq);
    }

    [Fact]
    public void BusyPortIsReportedInPlainWords()
    {
        var config = new MockServerConfig { Port = MockServerHost.FreePort() };
        using var first = new MockServerHost(config);
        first.Start();
        using var second = new MockServerHost(MockServerConfig.FromJson(config.ToJson()));
        var error = Assert.Throws<InvalidOperationException>(second.Start);
        Assert.Contains($"Port {config.Port} could not be opened", error.Message);
    }

    [Fact]
    public async Task ServeCommandRunsUntilStopped()
    {
        var path = Path.Combine(_folder, "server.json");
        var config = new MockServerConfig { Port = MockServerHost.FreePort(), Routes = [new MockRoute { Path = "/hi", Response = new MockResponse { Body = "hello" } }] };
        config.Save(path);
        var output = new StringWriter(); var error = new StringWriter();
        using var cancel = new CancellationTokenSource();
        var run = MockServerCli.RunAsync(path, null, output, error, cancel.Token);
        using var control = new MockServerClient(config.Port);
        for (var i = 0; i < 50 && await control.StatusAsync() is null; i++) await Task.Delay(100);
        Assert.Equal("hello", await _client.GetStringAsync($"http://127.0.0.1:{config.Port}/hi"));
        await control.StopAsync();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("listening", output.ToString());
        Assert.Contains("GET /hi -> 200", output.ToString());

        Assert.Equal(2, await MockServerCli.RunAsync(Path.Combine(_folder, "missing.json"), null, output, error, cancel.Token));
        Assert.Contains("could not be read", error.ToString());
    }
}

public class MockResponseTimeAndUrlTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8099/orders/<From_request_id_r>", "http://127.0.0.1:9000", false, "http://127.0.0.1:9000/orders/<From_request_id_r>")]
    [InlineData("HTTP://localhost:1234/x?y=1", "http://127.0.0.1:9000", false, "http://127.0.0.1:9000/x?y=1")]
    [InlineData("http://[::1]:80", "http://127.0.0.1:9000", false, "http://127.0.0.1:9000")]
    [InlineData("https://api.example.test/z", "http://127.0.0.1:9000", false, null)]
    [InlineData("https://api.example.test/z", "http://127.0.0.1:9000", true, "http://127.0.0.1:9000/z")]
    [InlineData("<From_response_A.url_r>/next", "http://127.0.0.1:9000", true, null)]
    [InlineData("http://127.0.0.1:9000/same", "http://127.0.0.1:9000", false, null)]
    [InlineData("", "http://127.0.0.1:9000", true, null)]
    public void UrlsAreRepointed(string url, string origin, bool remote, string? expected) => Assert.Equal(expected, MockUrls.Repoint(url, origin, remote));

    [Fact]
    public void OriginsAreFoundAndLocalOnesRecognised()
    {
        Assert.Equal("http://127.0.0.1:8099", MockUrls.OriginOf("http://127.0.0.1:8099/a/b"));
        Assert.Null(MockUrls.OriginOf("<From_x_r>/a"));
        Assert.True(MockUrls.IsLocal("http://localhost:5"));
        Assert.False(MockUrls.IsLocal("https://example.com"));
    }

    [Fact]
    public void ValidationChecksTheResponseTimes()
    {
        var config = new MockServerConfig { ExtraDelayMs = -1, Routes = [new MockRoute { Path = "/a", Response = new MockResponse { DelayMs = 500, DelayMaxMs = 100 } }] };
        var text = string.Join("\n", config.Validate());
        Assert.Contains("extra latency", text);
        Assert.Contains("longest delay (100 ms) is shorter", text);
        config.ExtraDelayMs = 0; config.Routes[0].Response.DelayMaxMs = 800;
        Assert.Empty(config.Validate());
    }

    [Fact]
    public async Task ResponseTimeIsARandomRangeAndExtraLatencyIsAdded()
    {
        var config = new MockServerConfig { Port = MockServerHost.FreePort(), ExtraDelayMs = 150, Routes =
        [
            new MockRoute { Name = "range", Path = "/range", Response = new MockResponse { DelayMs = 100, DelayMaxMs = 300 } },
            new MockRoute { Name = "fixed", Path = "/fixed", Response = new MockResponse { DelayMs = 200 } },
            new MockRoute { Name = "plain", Path = "/plain" }
        ]};
        using var host = new MockServerHost(config); host.Start();
        using var http = new HttpClient();
        var url = $"http://127.0.0.1:{config.Port}";
        for (var i = 0; i < 6; i++) await http.GetAsync(url + "/range");
        await http.GetAsync(url + "/fixed"); await http.GetAsync(url + "/plain");
        var log = host.Snapshot();
        var ranges = log.Where(e => e.Route == "range").Select(e => e.DurationMs).ToList();
        Assert.All(ranges, d => Assert.True(d >= 240 && d <= 1500, $"range answer took {d} ms (expected 250..450 plus slack): {string.Join(",", ranges)}"));
        Assert.True(ranges.Distinct().Count() > 1, "a random range gives different times");
        Assert.InRange(log.Single(e => e.Route == "fixed").DurationMs, 340, 700);
        Assert.InRange(log.Single(e => e.Route == "plain").DurationMs, 140, 600);
    }
}

public class MockGraphQlTests : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    public void Dispose() => _http.Dispose();

    private static string Body(string query, string? operationName = null, string? variables = null)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?> { ["query"] = query, ["operationName"] = operationName, ["variables"] = variables is null ? null : System.Text.Json.JsonDocument.Parse(variables).RootElement });

    [Theory]
    [InlineData("query GetOrder($id: ID!) { order(id: $id) { id } }", null, "operation", "GetOrder")]
    [InlineData("query GetOrder { order { id } }", "Other", "operation", "Other")]
    [InlineData("{ customers { id } }", null, "operation", "customers")]
    [InlineData("{ customers { id } }", null, "type", "query")]
    [InlineData("mutation CreateOrder { createOrder(input: {}) { id } }", null, "type", "mutation")]
    [InlineData("mutation CreateOrder { createOrder(input: {}) { id } }", null, "field", "createOrder")]
    [InlineData("query { mine: order(id: 1) { id } }", null, "field", "order")]
    [InlineData("subscription OnPaid { paid { id } }", null, "type", "subscription")]
    public void WhatARequestAsksFor(string query, string? operationName, string part, string expected) => Assert.Equal(expected, MockGraphQl.Part(Body(query, operationName), part));

    [Fact]
    public void NonGraphQlBodiesGiveNothing()
    {
        Assert.Null(MockGraphQl.Part("{\"a\":1}", "operation"));
        Assert.Null(MockGraphQl.Part("", "operation"));
        Assert.Null(MockGraphQl.Part("not json", "type"));
    }

    [Fact]
    public async Task OneGraphQlPathAnswersEachOperationDifferently()
    {
        var config = new MockServerConfig { Port = MockServerHost.FreePort(), Routes =
        [
            new MockRoute { Name = "order", Method = "POST", Path = "/graphql", Conditions = [new MockCondition { Source = "graphql", Name = "operation", Op = "equals", Value = "GetOrder" }], Response = new MockResponse { Body = "{\"data\":{\"order\":{\"id\":\"{{json.variables.id}}\",\"op\":\"{{operation}}\"}}}" } },
            new MockRoute { Name = "mutations", Method = "POST", Path = "/graphql", Conditions = [new MockCondition { Source = "graphql", Name = "type", Op = "equals", Value = "mutation" }], Response = new MockResponse { Status = 200, Body = "{\"errors\":[{\"message\":\"Not allowed\"}]}" } }
        ]};
        using var host = new MockServerHost(config); host.Start();
        var url = $"http://127.0.0.1:{config.Port}/graphql";
        async Task<string> Post(string body) => await (await _http.PostAsync(url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"))).Content.ReadAsStringAsync();
        Assert.Equal("{\"data\":{\"order\":{\"id\":\"O1\",\"op\":\"GetOrder\"}}}", await Post(Body("query GetOrder($id: ID!) { order(id: $id) { id } }", null, "{\"id\":\"O1\"}")));
        Assert.Contains("Not allowed", await Post(Body("mutation Make { make { id } }")));
        Assert.Contains("No route matched", await Post(Body("query Other { other { id } }")));
        Assert.Equal(2, host.Snapshot().Count(e => e.Matched));
    }

    [Fact]
    public void ValidationKnowsTheGraphQlSource()
    {
        var config = new MockServerConfig { Routes = [new MockRoute { Path = "/g", Conditions = [new MockCondition { Source = "graphql", Name = "operaton", Op = "equals" }, new MockCondition { Source = "graphql", Name = "", Op = "equals" }] }] };
        var text = string.Join("\n", config.Validate());
        Assert.Contains("operaton", text);
        Assert.Single(config.Validate());
    }
}
