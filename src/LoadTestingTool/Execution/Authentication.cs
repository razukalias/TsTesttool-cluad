using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

/// <summary>
/// One way of logging in. Every text may hold <c>${NAME}</c> (filled when the settings are read), and <c>_column_</c> placeholders that are filled from the data row of the step, so
/// every row can log in as another user. Types: <c>bearer</c> (a login call returns a token), <c>oauth2</c> (client credentials), <c>basic</c>, <c>apikey</c> (header or query),
/// <c>headers</c> (fixed headers) and <c>cookie</c> (a login call sets the session cookie).
/// </summary>
public sealed class AuthProfile
{
    public string Type { get; set; } = "bearer";
    // bearer and cookie: the login call
    public string LoginUrl { get; set; } = string.Empty;
    public string LoginMethod { get; set; } = "POST";
    public string LoginBody { get; set; } = string.Empty;
    public string LoginContentType { get; set; } = "application/json";
    public Dictionary<string, string> LoginHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Where the token is in the answer of the login call (bearer) or the token call (oauth2).</summary>
    public string TokenPath { get; set; } = "$.access_token";
    /// <summary>Optional: where the lifetime in seconds is in the answer. Without it <see cref="TtlSeconds"/> is used.</summary>
    public string ExpiresInPath { get; set; } = string.Empty;
    /// <summary>How long a token is used when the answer does not say. 0 = until a request answers 401.</summary>
    public int TtlSeconds { get; set; }
    // oauth2
    public string TokenUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    /// <summary>"body" sends client_id and client_secret in the form, "basic" sends them in an Authorization header.</summary>
    public string ClientAuth { get; set; } = "body";
    // basic
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    // apikey
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    /// <summary>header (default) or query.</summary>
    public string In { get; set; } = "header";
    // headers
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // where a token goes
    public string Header { get; set; } = "Authorization";
    public string Prefix { get; set; } = "Bearer ";
}

/// <summary>The profiles and the one used by every HTTP, GraphQL and WCF step that does not choose another (<c>"auth"</c> in its settings).</summary>
public sealed class AuthSettings
{
    public string Default { get; set; } = string.Empty;
    public Dictionary<string, AuthProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The instance's profiles and default over the global ones (an instance profile hides a global one of the same name).</summary>
    public static AuthSettings Merge(AuthSettings? global, AuthSettings? instance)
    {
        var merged = new AuthSettings { Default = !string.IsNullOrWhiteSpace(instance?.Default) ? instance!.Default : global?.Default ?? string.Empty };
        foreach (var source in new[] { global, instance })
            if (source is not null) foreach (var (name, profile) in source.Profiles) merged.Profiles[name] = profile;
        return merged;
    }
}


/// <summary>What the step being sent uses: set by the runner around a send, read by <see cref="AuthHandler"/>.</summary>
public sealed class AuthScope
{
    private static readonly AsyncLocal<AuthScope?> Current = new();
    public static AuthScope? Active => Current.Value;
    public string ProfileName { get; init; } = string.Empty;
    public AuthProfile Profile { get; init; } = new();
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    /// <summary>The names of the headers and query values the handler added, for the request log (never their values).</summary>
    public List<string> Applied { get; } = [];

    public static IDisposable Enter(AuthScope? scope)
    {
        var previous = Current.Value;
        Current.Value = scope;
        return new Restore(() => Current.Value = previous);
    }

    private sealed class Restore(Action action) : IDisposable { public void Dispose() => action(); }
}

public sealed class AuthApplied
{
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Query { get; } = new(StringComparer.Ordinal);
}

/// <summary>Logs in when needed, keeps the token until it expires and logs in again after a 401.</summary>
public sealed class AuthManager(AuthSettings settings, HttpClient loginClient, Action<string>? log = null)
{
    private sealed record Entry(string Token, DateTimeOffset? Expires);
    private readonly ConcurrentDictionary<string, Entry> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    /// <summary>The manager of this run (set at start), handed to the runner.</summary>
    public static AuthManager? Current { get; set; }
    public AuthSettings Settings { get; } = settings;
    public int Logins => _logins;
    private int _logins;

    /// <summary>The profile a step uses: its own <c>"auth"</c> (a profile name, <c>none</c>, or a whole profile written in the settings) or the default. Null = no authentication.</summary>
    public (string Name, AuthProfile Profile)? Select(JsonElement stepSettings)
    {
        if (stepSettings.ValueKind == JsonValueKind.Object && stepSettings.TryGetProperty("auth", out var choice))
        {
            if (choice.ValueKind == JsonValueKind.Object)
                return ("inline", JsonSerializer.Deserialize<AuthProfile>(choice.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AuthProfile());
            var name = choice.ValueKind == JsonValueKind.String ? choice.GetString()?.Trim() ?? string.Empty : string.Empty;
            if (name.Equals("none", StringComparison.OrdinalIgnoreCase) || name.Equals("off", StringComparison.OrdinalIgnoreCase)) return null;
            if (name.Length > 0 && !name.Equals("default", StringComparison.OrdinalIgnoreCase)) return Find(name);
        }
        return string.IsNullOrWhiteSpace(Settings.Default) ? null : Find(Settings.Default);
    }

    private (string, AuthProfile) Find(string name)
        => Settings.Profiles.TryGetValue(name, out var profile) ? (name, profile)
            : throw new InvalidDataException($"Authentication profile '{name}' is not defined. Defined profiles: {(Settings.Profiles.Count == 0 ? "none" : string.Join(", ", Settings.Profiles.Keys))}.");

    /// <summary>The headers and query values to add to a request.</summary>
    public async Task<AuthApplied> ApplyAsync(string name, AuthProfile profile, IReadOnlyDictionary<string, string> variables, bool refresh, CancellationToken cancellationToken)
    {
        var p = Fill(profile, variables);
        var applied = new AuthApplied();
        switch (p.Type.Trim().ToLowerInvariant())
        {
            case "basic":
                if (p.User.Length == 0) throw new InvalidDataException($"Authentication '{name}' (basic) has no User.");
                applied.Headers[p.Header] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{p.User}:{p.Password}"));
                Secrets.Register(p.Password, "basic password"); Secrets.Register(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{p.User}:{p.Password}")), "basic credentials");
                break;
            case "apikey":
                if (p.Name.Length == 0) throw new InvalidDataException($"Authentication '{name}' (apikey) has no Name for the header or query value.");
                Secrets.Register(p.Value, "api key");
                if (p.In.Equals("query", StringComparison.OrdinalIgnoreCase)) applied.Query[p.Name] = p.Value; else applied.Headers[p.Name] = p.Value;
                break;
            case "headers":
                foreach (var (key, value) in p.Headers) { applied.Headers[key] = value; Secrets.Register(value, "auth header"); }
                break;
            case "bearer":
            case "oauth2":
                var token = await TokenAsync(name, p, refresh, cancellationToken);
                applied.Headers[p.Header] = p.Prefix + token;
                break;
            case "cookie":
                await TokenAsync(name, p, refresh, cancellationToken);   // the cookie jar of the client keeps the session
                break;
            default:
                throw new InvalidDataException($"Authentication '{name}' has an unknown type '{p.Type}'. Use bearer, oauth2, basic, apikey, headers or cookie.");
        }
        return applied;
    }

    /// <summary>Forgets the token a request was refused with, so the next call logs in again.</summary>
    public void Invalidate(string name, AuthProfile profile, IReadOnlyDictionary<string, string> variables) => _tokens.TryRemove(KeyOf(name, Fill(profile, variables)), out _);

    private async Task<string> TokenAsync(string name, AuthProfile p, bool refresh, CancellationToken cancellationToken)
    {
        var key = KeyOf(name, p);
        if (!refresh && _tokens.TryGetValue(key, out var cached) && (cached.Expires is null || cached.Expires > DateTimeOffset.UtcNow.AddSeconds(30))) return cached.Token;
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _tokens.TryGetValue(key, out cached) && (cached.Expires is null || cached.Expires > DateTimeOffset.UtcNow.AddSeconds(30))) return cached.Token;
            var entry = p.Type.Equals("oauth2", StringComparison.OrdinalIgnoreCase) ? await OAuthAsync(name, p, cancellationToken) : await LoginAsync(name, p, cancellationToken);
            Interlocked.Increment(ref _logins);
            _tokens[key] = entry;
            Secrets.Register(entry.Token, $"token of {name}");
            return entry.Token;
        }
        finally { gate.Release(); }
    }

    private async Task<Entry> LoginAsync(string name, AuthProfile p, CancellationToken cancellationToken)
    {
        if (p.LoginUrl.Length == 0) throw new InvalidDataException($"Authentication '{name}' has no LoginUrl.");
        using var request = new HttpRequestMessage(new HttpMethod(p.LoginMethod.Length == 0 ? "POST" : p.LoginMethod), p.LoginUrl);
        if (p.LoginBody.Length > 0) request.Content = new StringContent(p.LoginBody, Encoding.UTF8, p.LoginContentType);
        foreach (var (key, value) in p.LoginHeaders) request.Headers.TryAddWithoutValidation(key, value);
        var (status, body) = await SendAsync(name, request, cancellationToken);
        if (p.Type.Equals("cookie", StringComparison.OrdinalIgnoreCase)) return new Entry("cookie", p.TtlSeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(p.TtlSeconds) : null);
        return ReadToken(name, p, status, body);
    }

    private async Task<Entry> OAuthAsync(string name, AuthProfile p, CancellationToken cancellationToken)
    {
        var url = p.TokenUrl.Length > 0 ? p.TokenUrl : p.LoginUrl;
        if (url.Length == 0) throw new InvalidDataException($"Authentication '{name}' (oauth2) has no TokenUrl.");
        var form = new List<KeyValuePair<string, string>> { new("grant_type", "client_credentials") };
        if (p.Scope.Length > 0) form.Add(new("scope", p.Scope));
        if (p.Audience.Length > 0) form.Add(new("audience", p.Audience));
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (p.ClientAuth.Equals("basic", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{p.ClientId}:{p.ClientSecret}")));
        else { form.Add(new("client_id", p.ClientId)); form.Add(new("client_secret", p.ClientSecret)); }
        Secrets.Register(p.ClientSecret, "client secret");
        request.Content = new FormUrlEncodedContent(form);
        var (status, body) = await SendAsync(name, request, cancellationToken);
        return ReadToken(name, p, status, body, defaultExpiresIn: "$.expires_in");
    }

    private async Task<(int Status, string Body)> SendAsync(string name, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        log?.Invoke($"AUTH login | profile={name} | {request.Method} {request.RequestUri}");
        try
        {
            using var response = await loginClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidDataException($"Authentication '{name}' failed: the login call to {request.RequestUri} answered {(int)response.StatusCode} {response.ReasonPhrase}. {Secrets.Mask(Short(body))}".TrimEnd());
            return ((int)response.StatusCode, body);
        }
        catch (HttpRequestException ex) { throw new InvalidDataException($"Authentication '{name}' failed: the login call to {request.RequestUri} could not be sent: {ex.Message}"); }
    }

    private static Entry ReadToken(string name, AuthProfile p, int status, string body, string defaultExpiresIn = "")
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { throw new InvalidDataException($"Authentication '{name}' failed: the answer of the login call is not JSON, so the token at {p.TokenPath} cannot be read. Answer: {Short(body)}"); }
        using (doc)
        {
            var token = JsonPath.Select(doc.RootElement, p.TokenPath)?.ToString();
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException($"Authentication '{name}' failed: no token at {p.TokenPath} in the answer of the login call. Answer: {Secrets.Mask(Short(body))}");
            var expiresPath = p.ExpiresInPath.Length > 0 ? p.ExpiresInPath : defaultExpiresIn;
            DateTimeOffset? expires = null;
            if (expiresPath.Length > 0 && JsonPath.Select(doc.RootElement, expiresPath) is { } seconds && double.TryParse(Convert.ToString(seconds, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0)
                expires = DateTimeOffset.UtcNow.AddSeconds(s);
            else if (p.TtlSeconds > 0) expires = DateTimeOffset.UtcNow.AddSeconds(p.TtlSeconds);
            return new Entry(token, expires);
        }
    }

    private static string Short(string text) => text.Length > 300 ? text[..300] + "…" : text;

    private static string KeyOf(string name, AuthProfile p) => name + "|" + System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(p))).Aggregate("", (a, b) => a + b.ToString("x2"));

    /// <summary>A copy with the <c>_column_</c> placeholders of every text filled from the row.</summary>
    public static AuthProfile Fill(AuthProfile profile, IReadOnlyDictionary<string, string> variables)
    {
        string F(string text) => StepConfig.Fill(text, variables);
        return new AuthProfile
        {
            Type = F(profile.Type), LoginUrl = F(profile.LoginUrl), LoginMethod = F(profile.LoginMethod), LoginBody = F(profile.LoginBody), LoginContentType = F(profile.LoginContentType),
            LoginHeaders = profile.LoginHeaders.ToDictionary(x => x.Key, x => F(x.Value), StringComparer.OrdinalIgnoreCase),
            TokenPath = F(profile.TokenPath), ExpiresInPath = F(profile.ExpiresInPath), TtlSeconds = profile.TtlSeconds,
            TokenUrl = F(profile.TokenUrl), ClientId = F(profile.ClientId), ClientSecret = F(profile.ClientSecret), Scope = F(profile.Scope), Audience = F(profile.Audience), ClientAuth = F(profile.ClientAuth),
            User = F(profile.User), Password = F(profile.Password), Name = F(profile.Name), Value = F(profile.Value), In = F(profile.In),
            Headers = profile.Headers.ToDictionary(x => x.Key, x => F(x.Value), StringComparer.OrdinalIgnoreCase), Header = F(profile.Header), Prefix = profile.Prefix
        };
    }
}

/// <summary>Adds the credentials of the step being sent (<see cref="AuthScope"/>) to the request and, when the answer is 401, logs in again and sends it once more.</summary>
public sealed class AuthHandler(HttpMessageHandler inner, AuthManager manager) : DelegatingHandler(inner)
{
    private const long RetryBodyLimit = 5 * 1024 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = AuthScope.Active;
        if (scope is null) return await base.SendAsync(request, cancellationToken);
        byte[]? body = null;
        if (request.Content is not null && (request.Content.Headers.ContentLength ?? 0) <= RetryBodyLimit && request.Content is not StreamContent)
        {
            await request.Content.LoadIntoBufferAsync();
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        var applied = await manager.ApplyAsync(scope.ProfileName, scope.Profile, scope.Variables, refresh: false, cancellationToken);
        var retry = body is not null || request.Content is null ? Clone(request, body) : null;
        Add(request, applied, scope);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || retry is null || scope.Profile.Type.Equals("basic", StringComparison.OrdinalIgnoreCase)
            || scope.Profile.Type.Equals("apikey", StringComparison.OrdinalIgnoreCase) || scope.Profile.Type.Equals("headers", StringComparison.OrdinalIgnoreCase)) return response;
        response.Dispose();
        manager.Invalidate(scope.ProfileName, scope.Profile, scope.Variables);
        var again = await manager.ApplyAsync(scope.ProfileName, scope.Profile, scope.Variables, refresh: true, cancellationToken);
        Add(retry, again, scope);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static void Add(HttpRequestMessage request, AuthApplied applied, AuthScope scope)
    {
        foreach (var (name, value) in applied.Headers)
        {
            if (request.Headers.Contains(name) && !scope.Applied.Contains(name)) continue;   // a header written in the step wins
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
            if (!scope.Applied.Contains(name)) scope.Applied.Add(name);
        }
        if (applied.Query.Count > 0)
        {
            var builder = new UriBuilder(request.RequestUri!);
            var query = string.Join("&", applied.Query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
            builder.Query = string.IsNullOrEmpty(builder.Query) ? query : builder.Query.TrimStart('?') + "&" + query;
            request.RequestUri = builder.Uri;
            foreach (var key in applied.Query.Keys) if (!scope.Applied.Contains("?" + key)) scope.Applied.Add("?" + key);
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original, byte[]? body)
    {
        var copy = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (body is not null)
        {
            copy.Content = new ByteArrayContent(body);
            foreach (var header in original.Content!.Headers) copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return copy;
    }
}

/// <summary>Reads and writes the <c>Auth</c> section of appsettings.json without the settings that hold their default value.</summary>
public static class AuthJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static AuthSettings Read(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is null) return new AuthSettings();
        var settings = node.Deserialize<AuthSettings>(Options) ?? new AuthSettings();
        settings.Profiles = new Dictionary<string, AuthProfile>(settings.Profiles ?? [], StringComparer.OrdinalIgnoreCase);
        return settings;
    }

    public static System.Text.Json.Nodes.JsonNode? Write(AuthSettings settings)
    {
        if (settings.Profiles.Count == 0 && string.IsNullOrWhiteSpace(settings.Default)) return null;
        var root = new System.Text.Json.Nodes.JsonObject();
        if (!string.IsNullOrWhiteSpace(settings.Default)) root["Default"] = settings.Default;
        var profiles = new System.Text.Json.Nodes.JsonObject();
        var defaults = new AuthProfile();
        foreach (var (name, profile) in settings.Profiles)
        {
            var own = new System.Text.Json.Nodes.JsonObject { ["Type"] = profile.Type };
            foreach (var property in typeof(AuthProfile).GetProperties())
            {
                if (property.Name == "Type") continue;
                var value = property.GetValue(profile); var standard = property.GetValue(defaults);
                switch (value)
                {
                    case string text when text.Length > 0 && text != (standard as string): own[property.Name] = text; break;
                    case int number when number != 0: own[property.Name] = number; break;
                    case Dictionary<string, string> map when map.Count > 0: own[property.Name] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(map)); break;
                }
            }
            profiles[name] = own;
        }
        root["Profiles"] = profiles;
        return root;
    }
}
