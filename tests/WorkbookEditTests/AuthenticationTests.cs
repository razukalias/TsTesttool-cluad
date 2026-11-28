using LoadTestingTool.Domain;
using System.Net;
using System.Text.Json;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

public class AuthenticationTests : IDisposable
{
    public AuthenticationTests() => Secrets.ResetForTests();
    public void Dispose() => Secrets.ResetForTests();

    private sealed class Fake(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(string Url, string? Auth, string? Body, string? Query)> Seen { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Seen.Add((request.RequestUri!.GetLeftPart(UriPartial.Path), request.Headers.Authorization?.ToString() ?? (request.Headers.TryGetValues("X-Key", out var v) ? v.First() : null), body, request.RequestUri.Query));
            return answer(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static (AuthManager Manager, Fake Login, Fake Api, HttpClient Client) Setup(AuthSettings settings, Func<HttpRequestMessage, string?, HttpResponseMessage>? login = null, Func<HttpRequestMessage, string?, HttpResponseMessage>? api = null)
    {
        var loginFake = new Fake(login ?? ((_, _) => Json("{\"access_token\":\"tok-abcdef\",\"expires_in\":3600}")));
        var apiFake = new Fake(api ?? ((r, _) => r.Headers.Authorization?.Parameter == "tok-abcdef" || r.Headers.Authorization?.Scheme == "Basic" || r.Headers.Contains("X-Key") ? new HttpResponseMessage(HttpStatusCode.OK) : new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var manager = new AuthManager(settings, new HttpClient(loginFake));
        return (manager, loginFake, apiFake, new HttpClient(new AuthHandler(apiFake, manager)));
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, AuthManager manager, string profile, IReadOnlyDictionary<string, string>? vars = null, string url = "http://api.test/x", string? body = null, bool explicitHeader = false)
    {
        var (name, p) = manager.Select(JsonDocument.Parse($"{{\"auth\":\"{profile}\"}}").RootElement)!.Value;
        using var _ = AuthScope.Enter(new AuthScope { ProfileName = name, Profile = p, Variables = vars ?? new Dictionary<string, string>() });
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url);
        if (body is not null) request.Content = new StringContent(body);
        if (explicitHeader) request.Headers.TryAddWithoutValidation("Authorization", "Bearer mine");
        return await client.SendAsync(request);
    }

    private static AuthSettings Bearer() => new() { Profiles = { ["api"] = new AuthProfile { Type = "bearer", LoginUrl = "http://auth.test/login", LoginBody = "{\"user\":\"_user_\"}" } } };

    [Fact]
    public async Task A_bearer_profile_logs_in_once_and_sends_the_token_every_time()
    {
        var (manager, login, api, client) = Setup(Bearer());
        Assert.Equal(HttpStatusCode.OK, (await Send(client, manager, "api", new Dictionary<string, string> { ["user"] = "ann" })).StatusCode);
        await Send(client, manager, "api", new Dictionary<string, string> { ["user"] = "ann" });
        Assert.Single(login.Seen);
        Assert.Equal("{\"user\":\"ann\"}", login.Seen[0].Body);
        Assert.All(api.Seen, s => Assert.Equal("Bearer tok-abcdef", s.Auth));
    }

    [Fact]
    public async Task Each_data_row_user_gets_its_own_token()
    {
        var (manager, login, _, client) = Setup(Bearer());
        await Send(client, manager, "api", new Dictionary<string, string> { ["user"] = "ann" });
        await Send(client, manager, "api", new Dictionary<string, string> { ["user"] = "bob" });
        Assert.Equal(2, login.Seen.Count);
    }

    [Fact]
    public async Task A_401_logs_in_again_and_sends_the_request_once_more_with_its_body()
    {
        var logins = 0;
        var (manager, login, api, client) = Setup(Bearer(),
            login: (_, _) => Json($"{{\"access_token\":\"{(++logins == 1 ? "old-token-1" : "tok-abcdef")}\"}}"));
        var response = await Send(client, manager, "api", body: "{\"a\":1}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, login.Seen.Count);
        Assert.Equal(2, api.Seen.Count);
        Assert.Equal("{\"a\":1}", api.Seen[1].Body);
    }

    [Fact]
    public async Task A_header_written_in_the_step_wins_over_the_profile()
    {
        var (manager, _, api, client) = Setup(Bearer());
        await Send(client, manager, "api", explicitHeader: true);
        Assert.Equal("Bearer mine", api.Seen[0].Auth);
    }

    [Fact]
    public async Task Oauth2_client_credentials_asks_for_a_token_with_the_form_and_uses_the_lifetime()
    {
        var settings = new AuthSettings { Profiles = { ["o"] = new AuthProfile { Type = "oauth2", TokenUrl = "http://auth.test/token", ClientId = "id1", ClientSecret = "secret-xyz", Scope = "read" } } };
        var (manager, login, api, client) = Setup(settings, login: (_, _) => Json("{\"access_token\":\"tok-abcdef\",\"expires_in\":10}"));
        await Send(client, manager, "o");
        Assert.Contains("grant_type=client_credentials", login.Seen[0].Body);
        Assert.Contains("client_id=id1", login.Seen[0].Body);
        Assert.Contains("scope=read", login.Seen[0].Body);
        await Send(client, manager, "o");   // 10 seconds is inside the 30 second renewal margin: a new token is asked for
        Assert.Equal(2, login.Seen.Count);
        Assert.Equal("Bearer tok-abcdef", api.Seen[0].Auth);
    }

    [Fact]
    public async Task Oauth2_can_send_the_client_in_a_basic_header()
    {
        var settings = new AuthSettings { Profiles = { ["o"] = new AuthProfile { Type = "oauth2", TokenUrl = "http://auth.test/token", ClientId = "id1", ClientSecret = "secret-xyz", ClientAuth = "basic" } } };
        var (manager, login, _, client) = Setup(settings);
        await Send(client, manager, "o");
        Assert.StartsWith("Basic ", login.Seen[0].Auth);
        Assert.DoesNotContain("client_secret", login.Seen[0].Body);
    }

    [Fact]
    public async Task Basic_apikey_and_fixed_headers_need_no_login()
    {
        var settings = new AuthSettings
        {
            Profiles =
            {
                ["b"] = new AuthProfile { Type = "basic", User = "ann", Password = "pw-1234" },
                ["k"] = new AuthProfile { Type = "apikey", Name = "X-Key", Value = "key-9999" },
                ["q"] = new AuthProfile { Type = "apikey", Name = "api_key", Value = "key-9999", In = "query" },
            }
        };
        var (manager, login, api, client) = Setup(settings);
        await Send(client, manager, "b"); await Send(client, manager, "k"); await Send(client, manager, "q", url: "http://api.test/x?a=1");
        Assert.Empty(login.Seen);
        Assert.Equal("Basic " + Convert.ToBase64String("ann:pw-1234"u8.ToArray()), api.Seen[0].Auth);
        Assert.Equal("key-9999", api.Seen[1].Auth);
        Assert.Equal("?a=1&api_key=key-9999", api.Seen[2].Query);
    }

    [Fact]
    public async Task A_cookie_profile_calls_the_login_once()
    {
        var settings = new AuthSettings { Profiles = { ["c"] = new AuthProfile { Type = "cookie", LoginUrl = "http://auth.test/login", LoginBody = "u=ann", LoginContentType = "application/x-www-form-urlencoded" } } };
        var (manager, login, api, client) = Setup(settings, login: (_, _) => new HttpResponseMessage(HttpStatusCode.OK), api: (_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        await Send(client, manager, "c"); await Send(client, manager, "c");
        Assert.Single(login.Seen);
        Assert.Equal(2, api.Seen.Count);
    }

    [Fact]
    public async Task A_failed_login_says_which_profile_and_what_the_server_answered_without_secrets()
    {
        var (manager, _, _, client) = Setup(Bearer(), login: (_, _) => Json("{\"error\":\"bad password\"}", HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Send(client, manager, "api"));
        Assert.Contains("Authentication 'api' failed", ex.Message);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task A_token_is_masked_after_it_was_used()
    {
        var (manager, _, _, client) = Setup(Bearer());
        await Send(client, manager, "api");
        Assert.Equal("header: ***", Secrets.Mask("header: tok-abcdef"));
    }

    [Fact]
    public async Task Oauth2_password_grant_sends_the_user_and_a_refresh_token_is_used_for_the_renewal()
    {
        var calls = new List<string>();
        var settings = new AuthSettings { Profiles = { ["o"] = new AuthProfile { Type = "oauth2", GrantType = "password", TokenUrl = "http://auth.test/token", ClientId = "c", ClientSecret = "secret-xyz", User = "ann", Password = "pw-1234" } } };
        var (manager, login, api, client) = Setup(settings, login: (_, body) => { calls.Add(body!); return Json("{\"access_token\":\"tok-abcdef\",\"expires_in\":10,\"refresh_token\":\"refresh-777\"}"); });
        await Send(client, manager, "o");
        Assert.Contains("grant_type=password", calls[0]); Assert.Contains("username=ann", calls[0]); Assert.Contains("password=pw-1234", calls[0]);
        await Send(client, manager, "o");   // 10 seconds is inside the renewal margin: the refresh token is used, not the password
        Assert.Contains("grant_type=refresh_token", calls[1]); Assert.Contains("refresh_token=refresh-777", calls[1]); Assert.DoesNotContain("password=", calls[1]);
        Assert.Equal("***", Secrets.Mask("refresh-777"));
    }

    [Fact]
    public async Task A_refused_refresh_token_falls_back_to_the_configured_grant()
    {
        var calls = new List<string>();
        var settings = new AuthSettings { Profiles = { ["o"] = new AuthProfile { Type = "oauth2", GrantType = "password", TokenUrl = "http://auth.test/token", ClientId = "c", ClientSecret = "secret-xyz", User = "ann", Password = "pw-1234" } } };
        var (manager, _, _, client) = Setup(settings, login: (_, body) => { calls.Add(body!); return body!.Contains("grant_type=refresh_token") ? Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest) : Json("{\"access_token\":\"tok-abcdef\",\"expires_in\":10,\"refresh_token\":\"refresh-777\"}"); });
        await Send(client, manager, "o"); await Send(client, manager, "o");
        Assert.Equal(3, calls.Count);
        Assert.Contains("grant_type=password", calls[2]);
    }

    [Fact]
    public async Task A_refresh_token_grant_uses_the_given_token()
    {
        var calls = new List<string>();
        var settings = new AuthSettings { Profiles = { ["o"] = new AuthProfile { Type = "oauth2", GrantType = "refresh_token", RefreshToken = "from-pipeline-1", TokenUrl = "http://auth.test/token", ClientId = "c", ClientSecret = "secret-xyz" } } };
        var (manager, _, _, client) = Setup(settings, login: (_, body) => { calls.Add(body!); return Json("{\"access_token\":\"tok-abcdef\"}"); });
        await Send(client, manager, "o");
        Assert.Contains("refresh_token=from-pipeline-1", calls[0]);
    }

    [Fact]
    public void A_profile_for_an_environment_is_used_instead_of_the_plain_one()
    {
        var settings = new AuthSettings { Default = "api", Profiles = { ["api"] = new AuthProfile { User = "plain" }, ["api@qa"] = new AuthProfile { User = "qa" } } };
        var manager = new AuthManager(settings, new HttpClient());
        JsonElement Config(string json) => JsonDocument.Parse(json).RootElement;
        Assert.Equal("api@qa", manager.Select(Config("{}"), "qa")!.Value.Name);
        Assert.Equal("api@qa", manager.Select(Config("{\"auth\":\"api\"}"), "QA")!.Value.Name, ignoreCase: true);
        Assert.Equal("api", manager.Select(Config("{}"), "prod")!.Value.Name);
        Assert.Equal("api", manager.Select(Config("{}"))!.Value.Name);
    }

    [Fact]
    public async Task A_profile_with_a_certificate_sends_the_login_and_the_requests_through_the_certificate_client()
    {
        var viaCertificate = new Fake((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var asked = new List<string>();
        var settings = new AuthSettings { Profiles = { ["m"] = new AuthProfile { Type = "certificate", CertificatePath = "c.pfx", CertificatePassword = "cert-pw-1" } } };
        var (manager, _, api, client) = Setup(settings);
        manager.CertificateFactory = (path, password, key) => { asked.Add($"{path}|{password}"); return new HttpMessageInvoker(viaCertificate); };
        var response = await Send(client, manager, "m"); await Send(client, manager, "m");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(api.Seen);
        Assert.Equal(2, viaCertificate.Seen.Count);
        Assert.Single(asked);   // the client is built once
        Assert.Equal("***", Secrets.Mask("cert-pw-1"));
    }

    [Fact]
    public void A_pfx_file_is_loaded_and_a_wrong_path_says_so()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cert_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = Path.Combine(dir, "c.pfx");
        File.WriteAllBytes(pfx, certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "pw-12345"));
        var manager = new AuthManager(new AuthSettings(), new HttpClient());
        Assert.NotNull(manager.CertificateInvoker(new AuthProfile { CertificatePath = pfx, CertificatePassword = "pw-12345" }));
        var ex = Assert.Throws<InvalidDataException>(() => manager.CertificateInvoker(new AuthProfile { CertificatePath = Path.Combine(dir, "missing.pfx") }));
        Assert.Contains("could not be loaded", ex.Message);
        Assert.Null(manager.CertificateInvoker(new AuthProfile()));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Select_follows_none_a_name_the_default_and_an_inline_profile()
    {
        var settings = new AuthSettings { Default = "api", Profiles = { ["api"] = new AuthProfile(), ["other"] = new AuthProfile { Type = "basic" } } };
        var manager = new AuthManager(settings, new HttpClient());
        JsonElement Config(string json) => JsonDocument.Parse(json).RootElement;
        Assert.Equal("api", manager.Select(Config("{}"))!.Value.Name);
        Assert.Equal("api", manager.Select(Config("{\"auth\":\"default\"}"))!.Value.Name);
        Assert.Equal("other", manager.Select(Config("{\"auth\":\"other\"}"))!.Value.Name);
        Assert.Null(manager.Select(Config("{\"auth\":\"none\"}")));
        var inline = manager.Select(Config("{\"auth\":{\"type\":\"basic\",\"user\":\"u\",\"password\":\"p\"}}"))!.Value;
        Assert.Equal("inline", inline.Name); Assert.Equal("u", inline.Profile.User);
        var ex = Assert.Throws<InvalidDataException>(() => manager.Select(Config("{\"auth\":\"missing\"}")));
        Assert.Contains("'missing' is not defined", ex.Message); Assert.Contains("api, other", ex.Message);
        Assert.Null(new AuthManager(new AuthSettings(), new HttpClient()).Select(Config("{}")));
    }

    [Fact]
    public void The_instance_settings_win_over_the_global_ones()
    {
        var global = new AuthSettings { Default = "a", Profiles = { ["a"] = new AuthProfile { User = "global" }, ["g"] = new AuthProfile() } };
        var instance = new AuthSettings { Default = "i", Profiles = { ["a"] = new AuthProfile { User = "instance" }, ["i"] = new AuthProfile() } };
        var merged = AuthSettings.Merge(global, instance);
        Assert.Equal("i", merged.Default);
        Assert.Equal("instance", merged.Profiles["a"].User);
        Assert.Equal(3, merged.Profiles.Count);
        Assert.Equal("a", AuthSettings.Merge(global, new AuthSettings()).Default);
    }
}

public class AuthenticationRunnerTests
{
    private sealed class Api : HttpMessageHandler
    {
        public List<(string Path, string? Auth)> Seen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var auth = request.Headers.Authorization?.ToString();
            lock (Seen) Seen.Add((request.RequestUri!.AbsolutePath, auth));
            if (request.RequestUri.AbsolutePath == "/login")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"tok-run-12345\"}", System.Text.Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(auth == "Bearer tok-run-12345" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized) { Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static async Task<(RunResult Run, Api Api)> RunAsync(string stepConfig, AuthSettings settings)
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Templates"));
        File.WriteAllText(Path.Combine(dir.Combine("Templates"), "t.json"), "{}");
        var path = Workbooks.CreateSmall(dir, (config, request, response) =>
        {
            config.Cell(1, 9).Value = "stepconfig";
            config.Cell(2, 1).Value = 1; config.Cell(2, 2).Value = "Flow"; config.Cell(2, 3).Value = "Call"; config.Cell(2, 4).Value = "HTTP";
            config.Cell(2, 5).Value = "t.json"; config.Cell(2, 6).Value = "http://127.0.0.1:1/data"; config.Cell(2, 7).Value = "POST"; config.Cell(2, 8).Value = true;
            config.Cell(2, 9).Value = stepConfig;
        });
        var model = new LoadTestingTool.Excel.WorkbookReader().Read(path);
        var api = new Api();
        var manager = new AuthManager(settings, new HttpClient(api));
        var runner = new TestRunner(new HttpClient(new AuthHandler(api, manager)), new AssertionEngine(), Path.Combine(dir.Path, "Templates"), 30, features: new FeatureConfig { Auth = manager });
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        await runner.RunAsync(model.Testcases[0], model, run, CancellationToken.None, selectedDataIds: new HashSet<string> { "D1" }, executionMode: "sequential");
        return (run, api);
    }

    private static AuthSettings Settings(string defaultProfile = "") => new() { Default = defaultProfile, Profiles = { ["api"] = new AuthProfile { Type = "bearer", LoginUrl = "http://auth.test/login", LoginBody = "{}" } } };

    [Fact]
    public async Task A_step_that_names_a_profile_is_sent_with_its_token_and_the_log_shows_no_token()
    {
        var (run, api) = await RunAsync("{\"auth\":\"api\"}", Settings());
        var step = run.Testcases.SelectMany(t => t.Requests).Single();
        Assert.Equal("PASS", step.Result);
        Assert.Contains("Bearer tok-run-12345", api.Seen.Last().Auth);
        Assert.Contains("authentication=api (Authorization added", step.RequestHeaders);
        Assert.DoesNotContain("tok-run-12345", step.RequestHeaders);
    }

    [Fact]
    public async Task The_default_profile_applies_and_none_opts_out()
    {
        var withDefault = await RunAsync("{}", Settings("api"));
        Assert.Equal("PASS", withDefault.Run.Testcases.SelectMany(t => t.Requests).Single().Result);
        var optedOut = await RunAsync("{\"auth\":\"none\"}", Settings("api"));
        var step = optedOut.Run.Testcases.SelectMany(t => t.Requests).Single();
        Assert.Equal("FAIL", step.Result);   // the fake API refuses a request without a token
        Assert.Equal(401, step.HttpStatus);
        Assert.DoesNotContain(optedOut.Api.Seen, s => s.Path == "/login");
    }

    [Fact]
    public async Task An_unknown_profile_fails_that_step_with_the_defined_names()
    {
        var (run, _) = await RunAsync("{\"auth\":\"nope\"}", Settings());
        var step = run.Testcases.SelectMany(t => t.Requests).Single();
        Assert.Contains("'nope' is not defined", step.ErrorMessage);
        Assert.Equal("Authentication", step.FailureCategory);
    }
}
