using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LoadTestingTool.MockServer;

/// <summary>One handled request, kept for the monitor.</summary>
public sealed class MockLogEntry
{
    public long Seq { get; set; }
    public DateTimeOffset Time { get; set; }
    public string Method { get; set; } = "";
    /// <summary>The path and query as the client sent it.</summary>
    public string Url { get; set; } = "";
    public int Status { get; set; }
    public long DurationMs { get; set; }
    public long Size { get; set; }
    /// <summary>The route that answered; empty when none fitted.</summary>
    public string Route { get; set; } = "";
    public bool Matched { get; set; }
    public string Note { get; set; } = "";
    public string Remote { get; set; } = "";
    public List<string> RequestHeaders { get; set; } = [];
    public string RequestBody { get; set; } = "";
    public List<string> ResponseHeaders { get; set; } = [];
    public string ResponseBody { get; set; } = "";
    /// <summary>Why routes above the one that answered (or every route, when none did) did not fit, one line each.</summary>
    public List<string> Explanation { get; set; } = [];
    /// <summary>The SQL the route ran and what it did, when it has a database action.</summary>
    public string Data { get; set; } = "";
}

/// <summary>
/// The mock server itself. It answers every request by the routes of its config and keeps a log. Requests to /__mock/ from this computer
/// control it: status, requests?after=N, clear, reload (POST a config) and stop.
/// </summary>
public sealed class MockServerHost : IDisposable
{
    public const string ControlPrefix = "/__mock/";
    private const int BodyLimit = 10 * 1024 * 1024;
    private const int LogTextLimit = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    /// <summary>Error answers keep quotes and apostrophes as they are (no \u0027).</summary>
    private static readonly JsonSerializerOptions ReadableJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly List<MockLogEntry> _log = [];
    private readonly string _baseFolder;
    private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MockServerConfig _config;
    private HttpListener? _listener;
    private CancellationTokenSource? _cancel;
    private Task? _loop;
    private long _seq;
    /// <summary>Scenario → current state; a scenario not in here is in "Started".</summary>
    private readonly Dictionary<string, string> _states = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Route (by position and title) → how many requests it has answered, for sequences and {{count}}.</summary>
    private readonly Dictionary<string, long> _calls = new(StringComparer.Ordinal);

    public MockServerHost(MockServerConfig config, string? baseFolder = null)
    {
        _config = config;
        _baseFolder = Path.GetFullPath(baseFolder ?? Directory.GetCurrentDirectory());
    }

    public MockServerConfig Config { get { lock (_gate) return _config; } }
    public int Port => Config.Port;
    public bool IsRunning => _listener?.IsListening == true;
    /// <summary>Completes when /__mock/stop is called.</summary>
    public Task StopRequested => _stopRequested.Task;
    public event Action<MockLogEntry>? Handled;

    /// <summary>A free port on this computer, for tests and for "pick one for me".</summary>
    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    public void Start()
    {
        if (IsRunning) return;
        var problems = Config.Validate();
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        var listener = new HttpListener();
        var port = Config.Port;
        if (Config.AllInterfaces) listener.Prefixes.Add($"http://+:{port}/");
        else { listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Prefixes.Add($"http://localhost:{port}/"); }
        try { listener.Start(); }
        catch (HttpListenerException exception)
        {
            listener.Close();
            throw new InvalidOperationException($"Port {port} could not be opened ({exception.Message.Trim()}). Another program may be using it, or all-interfaces mode needs administrator rights.", exception);
        }
        _listener = listener;
        _cancel = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoop(listener, _cancel.Token));
    }

    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        _cancel?.Cancel();
        try { listener?.Stop(); listener?.Close(); } catch (ObjectDisposedException) { }
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }

    public void Reload(MockServerConfig config)
    {
        var problems = config.Validate();
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        if (config.Port != Config.Port || config.AllInterfaces != Config.AllInterfaces) throw new InvalidOperationException("The port and the all-interfaces setting need a restart of the server.");
        lock (_gate) _config = config;
    }

    public List<MockLogEntry> Snapshot(long after = 0)
    {
        lock (_gate) return _log.Where(e => e.Seq > after).ToList();
    }

    public void ClearLog() { lock (_gate) _log.Clear(); }

    /// <summary>Every scenario back to "Started" and every sequence back to its first answer.</summary>
    public void ResetState() { lock (_gate) { _states.Clear(); _calls.Clear(); } }

    public Dictionary<string, string> States { get { lock (_gate) return new Dictionary<string, string>(_states, StringComparer.OrdinalIgnoreCase); } }

    private string StateOf(string scenario) { lock (_gate) return _states.TryGetValue(scenario, out var state) ? state : MockStates.Initial; }
    public long LastSeq { get { lock (_gate) return _seq; } }

    public void Dispose() => Stop();

    private async Task AcceptLoop(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception) when (token.IsCancellationRequested || !listener.IsListening) { return; }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }
            _ = Task.Run(() => HandleSafely(context, token));
        }
    }

    private async Task HandleSafely(HttpListenerContext context, CancellationToken token)
    {
        try { await Handle(context, token); }
        catch (Exception exception)
        {
            try { context.Response.Abort(); } catch { /* the client is gone */ }
            Record(new MockLogEntry { Time = DateTimeOffset.Now, Method = context.Request.HttpMethod, Url = context.Request.RawUrl ?? "", Status = 0, Note = "Server error: " + exception.Message, Remote = context.Request.RemoteEndPoint?.ToString() ?? "" });
        }
    }

    private async Task Handle(HttpListenerContext context, CancellationToken token)
    {
        var started = Stopwatch.StartNew();
        var http = context.Request;
        var path = http.Url?.AbsolutePath ?? "/";
        if (path.StartsWith(ControlPrefix, StringComparison.OrdinalIgnoreCase) && http.RemoteEndPoint is { } remote && IPAddress.IsLoopback(remote.Address))
        {
            await Control(context, path[ControlPrefix.Length..]);
            return;
        }
        var body = await ReadBody(http);
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in http.QueryString.AllKeys)
        {
            if (key is not null) query[key] = http.QueryString[key] ?? "";
            else foreach (var flag in (http.QueryString[null] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) query[flag] = "";   // ?wsdl has no "="
        }
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in http.Headers.AllKeys) if (key is not null) headers[key] = http.Headers[key] ?? "";
        var request = new MockRequest(http.HttpMethod, path, query, headers, body);
        MockServerConfig config;
        lock (_gate) config = _config;
        var trace = new List<string>();
        var match = MockRouteMatcher.Find(config, request, StateOf, trace);
        var pathParams = match?.PathParams ?? new Dictionary<string, string>();
        long count = 0;
        if (match is not null)
        {
            var key = config.Routes.IndexOf(match.Route) + "|" + match.Route.Title;
            lock (_gate) { _calls.TryGetValue(key, out count); _calls[key] = ++count; }
        }
        var response = match is null ? config.Fallback : Pick(match.Route, count);
        var entry = new MockLogEntry
        {
            Time = DateTimeOffset.Now, Method = http.HttpMethod, Url = http.RawUrl ?? path, Route = match?.Route.Title ?? "", Matched = match is not null,
            Remote = http.RemoteEndPoint?.ToString() ?? "",
            RequestHeaders = headers.Select(p => $"{p.Key}: {p.Value}").ToList(), RequestBody = Clip(body), Explanation = trace
        };
        if (match is not null && match.Route.Sequence.Count > 0) entry.Note = $"Answer {count} ({(ReferenceEquals(response, match.Route.Response) ? "after the sequence: the normal response" : $"sequence answer {SequenceIndex(match.Route, count) + 1} of {match.Route.Sequence.Count}")})";
        var templateData = new MockTemplateData { Count = count, State = StateOf };
        var status = response.Status;
        var dataFailed = false;
        if (match?.Route.Data is { } action)
        {
            try
            {
                var result = MockDatabase.RunAction(action, _baseFolder, request, pathParams, templateData);
                templateData = new MockTemplateData { Count = count, State = StateOf, Rows = result.Rows, Affected = result.Affected, LastId = result.LastId };
                entry.Data = $"{action.Database}: {result.Rows.Count} row(s), {result.Affected} changed" + (result.LastId > 0 ? $", last id {result.LastId}" : "") + $"\n{action.Sql}";
                if (action.NotFoundStatus > 0 && result.Rows.Count == 0 && result.Affected == 0)
                {
                    response = new MockResponse { Status = action.NotFoundStatus, ContentType = response.ContentType, Body = action.NotFoundBody, Headers = response.Headers };
                    status = response.Status;
                    entry.Note = "The database found nothing: not-found answer";
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
            {
                dataFailed = true;
                entry.Data = $"{action.Database}: failed – {exception.Message}\n{action.Sql}";
                entry.Note = "Database action failed: " + exception.Message;
            }
        }
        var wait = (response.DelayMaxMs > response.DelayMs ? Random.Shared.Next(response.DelayMs, response.DelayMaxMs + 1) : response.DelayMs) + Math.Max(0, config.ExtraDelayMs);
        if (wait > 0) await Task.Delay(wait, token);
        var failure = match is not null && match.Route.FailurePercent > 0 && Random.Shared.Next(100) < match.Route.FailurePercent;
        if (failure && match!.Route.DropConnection)
        {
            // Some listeners send an empty 200 when a response is aborted untouched, so the client is promised a body that never arrives:
            // the connection then ends mid-response and every client reports an error.
            try
            {
                context.Response.ContentLength64 = 1024;
                await context.Response.OutputStream.WriteAsync(new byte[] { (byte)'{' }, token);
                await context.Response.OutputStream.FlushAsync(token);
            }
            catch (Exception exception) when (exception is HttpListenerException or IOException or InvalidOperationException) { }
            context.Response.Abort();
            entry.Status = 0; entry.Note = "Connection dropped (injected failure)"; entry.DurationMs = started.ElapsedMilliseconds;
            Record(entry);
            return;
        }
        byte[] payload;
        var contentType = response.ContentType;
        var responseText = "";
        if (failure)
        {
            status = 500; contentType = "application/json"; responseText = "{\"error\":\"Injected failure\"}"; payload = Encoding.UTF8.GetBytes(responseText);
            entry.Note = "Injected failure";
        }
        else if (dataFailed)
        {
            status = 500; contentType = "application/json";
            responseText = System.Text.Json.JsonSerializer.Serialize(new { error = "Mock database action failed", detail = entry.Note }, ReadableJson);
            payload = Encoding.UTF8.GetBytes(responseText);
        }
        else if (!string.IsNullOrWhiteSpace(response.BodyFile))
        {
            var file = Path.GetFullPath(Path.Combine(_baseFolder, response.BodyFile));
            if (!file.StartsWith(_baseFolder, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            {
                status = 500; contentType = "application/json"; responseText = $"{{\"error\":\"Body file not found: {response.BodyFile.Replace("\"", "'")}\"}}"; payload = Encoding.UTF8.GetBytes(responseText);
                entry.Note = "Body file not found or outside the server folder";
            }
            else { payload = await File.ReadAllBytesAsync(file, token); responseText = $"[file {response.BodyFile}, {payload.Length} bytes]"; }
        }
        else
        {
            responseText = MockTemplate.Render(response.Body, request, pathParams, templateData);
            payload = Encoding.UTF8.GetBytes(responseText);
        }
        var output = context.Response;
        output.StatusCode = status;
        if (!string.IsNullOrWhiteSpace(contentType)) output.ContentType = contentType;
        var sent = new List<string>();
        if (!failure && !dataFailed)
            foreach (var header in response.Headers)
            {
                if (string.IsNullOrWhiteSpace(header.Name) || header.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                var value = MockTemplate.Render(header.Value, request, pathParams, templateData);
                try { output.Headers.Set(header.Name, value); sent.Add($"{header.Name}: {value}"); }
                catch (ArgumentException) { entry.Note = $"Header '{header.Name}' cannot be set by the server."; }
            }
        var noBody = status is 204 or 304 || http.HttpMethod.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
        output.ContentLength64 = noBody ? 0 : payload.Length;
        entry.Status = status; entry.Size = noBody ? 0 : payload.Length; entry.DurationMs = started.ElapsedMilliseconds;
        entry.ResponseHeaders = (string.IsNullOrWhiteSpace(contentType) ? sent : sent.Prepend($"Content-Type: {contentType}")).ToList();
        entry.ResponseBody = Clip(noBody ? "" : responseText);
        if (match is not null && !failure && !dataFailed && match.Route.SetState.Count > 0)
        {
            lock (_gate) foreach (var change in match.Route.SetState) if (change.Scenario.Trim().Length > 0) _states[change.Scenario.Trim()] = change.State.Trim().Length == 0 ? MockStates.Initial : change.State.Trim();
            var moved = string.Join(", ", match.Route.SetState.Where(c => c.Scenario.Trim().Length > 0).Select(c => $"{c.Scenario.Trim()} → {(c.State.Trim().Length == 0 ? MockStates.Initial : c.State.Trim())}"));
            entry.Note = entry.Note.Length == 0 ? "State: " + moved : entry.Note + "; state: " + moved;
        }
        // Logged before the answer goes out, so a client that has its answer can always find the request in the log.
        Record(entry);
        if (!noBody && payload.Length > 0) await output.OutputStream.WriteAsync(payload, token);
        output.Close();
    }

    /// <summary>The answer for the route's <paramref name="count"/>th request: from its sequence while there is one, else its normal response.</summary>
    internal static MockResponse Pick(MockRoute route, long count)
    {
        if (route.Sequence.Count == 0) return route.Response;
        var index = SequenceIndex(route, count);
        return index < route.Sequence.Count ? route.Sequence[index] : route.Response;
    }

    private static int SequenceIndex(MockRoute route, long count) =>
        route.SequenceLoop ? (int)((count - 1) % route.Sequence.Count) : (int)Math.Min(count - 1, int.MaxValue);

    private static async Task<string> ReadBody(HttpListenerRequest http)
    {
        if (!http.HasEntityBody) return "";
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await http.InputStream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > BodyLimit) break;
        }
        return (http.ContentEncoding ?? Encoding.UTF8).GetString(buffer.ToArray());
    }

    private static string Clip(string text) => text.Length <= LogTextLimit ? text : text[..LogTextLimit] + "… (cut)";

    private void Record(MockLogEntry entry)
    {
        lock (_gate)
        {
            entry.Seq = ++_seq;
            _log.Add(entry);
            var limit = Math.Max(10, _config.LogLimit);
            if (_log.Count > limit) _log.RemoveRange(0, _log.Count - limit);
        }
        try { Handled?.Invoke(entry); } catch { /* a monitor must never break the server */ }
    }

    private async Task Control(HttpListenerContext context, string command)
    {
        var http = context.Request;
        object? result;
        var status = 200;
        try
        {
            switch (command.Trim('/').ToLowerInvariant())
            {
                case "status": result = new { name = Config.Name, port = Config.Port, routes = Config.Routes.Count, lastSeq = LastSeq, running = true }; break;
                case "requests":
                    long.TryParse(http.QueryString["after"], out var after);
                    result = Snapshot(after); break;
                case "clear": ClearLog(); result = new { cleared = true }; break;
                case "reset-state": ResetState(); result = new { reset = true }; break;
                case "state": result = States; break;
                case "reload":
                    Reload(MockServerConfig.FromJson(await ReadBody(http)));
                    result = new { reloaded = true, routes = Config.Routes.Count }; break;
                case "stop": _stopRequested.TrySetResult(); result = new { stopping = true }; break;
                default: status = 404; result = new { error = "Unknown control command" }; break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            status = 400; result = new { error = exception.Message };
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = payload.Length;
        await context.Response.OutputStream.WriteAsync(payload);
        context.Response.Close();
    }
}

/// <summary>Talks to a running mock server (this process or another) through its /__mock/ commands.</summary>
public sealed class MockServerClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public MockServerClient(int port) => _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>The server's status line, or null when nothing answers on the port.</summary>
    public async Task<(string Name, int Routes, long LastSeq)?> StatusAsync()
    {
        try
        {
            using var response = await _http.GetAsync("/__mock/status");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            return (root.GetProperty("name").GetString() ?? "", root.GetProperty("routes").GetInt32(), root.GetProperty("lastSeq").GetInt64());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { return null; }
    }

    public async Task<List<MockLogEntry>> RequestsAsync(long after)
    {
        try
        {
            var text = await _http.GetStringAsync($"/__mock/requests?after={after}");
            return JsonSerializer.Deserialize<List<MockLogEntry>>(text, Json) ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) { return []; }
    }

    public async Task<string?> ReloadAsync(MockServerConfig config)
    {
        using var response = await _http.PostAsync("/__mock/reload", new StringContent(config.ToJson(), Encoding.UTF8, "application/json"));
        return response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync();
    }

    /// <summary>Scenarios back to "Started", sequences back to their first answer.</summary>
    public async Task ResetStateAsync() { try { (await _http.PostAsync("/__mock/reset-state", null)).Dispose(); } catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { } }

    public async Task<Dictionary<string, string>> StatesAsync()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(await _http.GetStringAsync("/__mock/state"), Json) ?? []; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException) { return []; }
    }

    public async Task ClearAsync() { try { (await _http.PostAsync("/__mock/clear", null)).Dispose(); } catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { } }
    public async Task StopAsync() { try { (await _http.PostAsync("/__mock/stop", null)).Dispose(); } catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { } }

    public void Dispose() => _http.Dispose();
}

/// <summary>The <c>--serve config.json</c> command: runs a mock server until it is stopped.</summary>
public static class MockServerCli
{
    public static async Task<int> RunAsync(string configPath, int? portOverride, TextWriter output, TextWriter error, CancellationToken cancel)
    {
        MockServerConfig config;
        try { config = MockServerConfig.Load(configPath); }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            await error.WriteLineAsync($"The mock server configuration '{configPath}' could not be read: {exception.Message}");
            return 2;
        }
        if (portOverride is { } port) config.Port = port;
        using var host = new MockServerHost(config, Path.GetDirectoryName(Path.GetFullPath(configPath)));
        try { host.Start(); }
        catch (InvalidOperationException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 3;
        }
        host.Handled += e => output.WriteLine($"{e.Time:HH:mm:ss} {e.Method} {e.Url} -> {e.Status} ({e.DurationMs} ms){(e.Matched ? "" : " [no route]")}");
        await output.WriteLineAsync($"Mock server '{config.Name}' listening on http://{(config.AllInterfaces ? "0.0.0.0" : "127.0.0.1")}:{config.Port}/ with {config.Routes.Count} route(s). Press Ctrl+C to stop.");
        await output.FlushAsync();
        try { await Task.WhenAny(host.StopRequested, Task.Delay(Timeout.Infinite, cancel)); }
        catch (OperationCanceledException) { }
        host.Stop();
        return 0;
    }
}
