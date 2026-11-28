using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Execution;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The authentication profiles of an instance (or of all instances): a list of profiles on the left, the fields of the chosen profile on the right, which login type it is,
/// which one is used by every step that does not choose another, and a Test button that logs in once and says whether it worked (the token itself is never shown).
/// </summary>
public sealed class AuthenticationWindow : Window
{
    private static readonly string[] Types = ["bearer", "oauth2", "basic", "apikey", "headers", "cookie", "certificate"];
    private static readonly IBrush Muted = Brush.Parse("#8EA4BC");

    private readonly IEnvironmentSettings _settings;
    private readonly string _instance;
    private readonly ListBox _list = new() { Tag = "auth-list", MinWidth = 170 };
    private readonly StackPanel _form = new() { Spacing = 8 };
    private readonly ComboBox _scope = new() { Tag = "auth-scope", ItemsSource = new[] { "This instance only", "All instances" }, SelectedIndex = 0 };
    private readonly ComboBox _default = new() { Tag = "auth-default", MinWidth = 170 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Tag = "auth-status" };
    private readonly List<Action> _flush = [];
    private AuthSettings _working;
    private string? _selected;
    private bool _loading;

    public AuthenticationWindow(IEnvironmentSettings settings, string instance)
    {
        _settings = settings; _instance = instance;
        Title = "Authentication – " + instance; Width = 900; Height = 680; MinWidth = 700; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _working = Load();
        _scope.SelectionChanged += (_, _) => { if (_loading) return; _working = Load(); _selected = null; Refresh(); };
        _list.SelectionChanged += (_, _) => { if (!_loading && _list.SelectedItem is string name) { _selected = name; BuildForm(); } };
        _default.SelectionChanged += (_, _) => { if (_loading) return; _working.Default = _default.SelectedItem is string d && d != "(none)" ? d : string.Empty; };

        var add = new Button { Content = "Add profile", Tag = "auth-add" }; add.Click += (_, _) => AddProfile();
        var remove = new Button { Content = "Remove", Tag = "auth-remove" }; remove.Click += (_, _) => RemoveSelected();
        var test = new Button { Content = "Test login", Tag = "auth-test" }; test.Click += async (_, _) => await TestAsync();
        var save = new Button { Content = "Save", Classes = { "accent" }, Tag = "auth-save" }; save.Click += (_, _) => Save();
        var close = new Button { Content = "Close" }; close.Click += (_, _) => Close();
        var help = new Button { Content = "Help", Tag = "auth-help" }; help.Click += (_, _) => ContextHelpText.Show(this, "Authentication – help", HelpText);

        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0), Children = { add, remove } };
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons); left.Children.Add(_list);

        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 10), Children =
        {
            new TextBlock { Text = "Profiles are used by HTTP, GraphQL and WCF steps. A step picks one in its settings (Authentication), or uses the default below; \"none\" turns it off for a step.", TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new TextBlock { Text = "Save for", VerticalAlignment = VerticalAlignment.Center }, _scope, new TextBlock { Text = "Default profile", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) }, _default } },
        } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { help, test, close, save } };
        var bottom = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0), Children = { _status, buttons } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(16) };
        Grid.SetColumn(left, 0);
        var scroll = new ScrollViewer { Content = _form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        grid.Children.Add(left); Grid.SetColumn(scroll, 1); grid.Children.Add(scroll);
        var root = new DockPanel { Margin = new Thickness(0) };
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top); root.Children.Add(bottom); root.Children.Add(grid);
        Content = new Border { Padding = new Thickness(16), Child = root };
        Refresh();
    }

    public AuthSettings Working => _working;
    public string? Selected => _selected;

    private bool Global => _scope.SelectedIndex == 1;
    private AuthSettings Load() => Copy(_settings.GetAuth(Global ? null : _instance));

    private static AuthSettings Copy(AuthSettings source)
    {
        var copy = new AuthSettings { Default = source.Default };
        foreach (var (name, profile) in source.Profiles) copy.Profiles[name] = Clone(profile);
        return copy;
    }

    private static AuthProfile Clone(AuthProfile p) => System.Text.Json.JsonSerializer.Deserialize<AuthProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;

    private void Refresh()
    {
        _loading = true;
        try
        {
            _list.ItemsSource = _working.Profiles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            _list.SelectedItem = _selected is not null && _working.Profiles.ContainsKey(_selected) ? _selected : null;
            var names = new List<string> { "(none)" }; names.AddRange(_working.Profiles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            _default.ItemsSource = names;
            _default.SelectedItem = names.FirstOrDefault(n => string.Equals(n, _working.Default, StringComparison.OrdinalIgnoreCase)) ?? "(none)";
        }
        finally { _loading = false; }
        BuildForm();
    }

    public void AddProfile(string? name = null)
    {
        name ??= Enumerable.Range(1, 99).Select(i => "profile" + i).First(n => !_working.Profiles.ContainsKey(n));
        _working.Profiles[name] = new AuthProfile { Type = "bearer", TokenPath = "$.access_token" };
        _selected = name;
        Refresh();
    }

    private void RemoveSelected()
    {
        if (_selected is null) return;
        _working.Profiles.Remove(_selected);
        if (string.Equals(_working.Default, _selected, StringComparison.OrdinalIgnoreCase)) _working.Default = string.Empty;
        _selected = null;
        Refresh();
    }

    private void BuildForm()
    {
        _form.Children.Clear();
        _flush.Clear();
        if (_selected is null || !_working.Profiles.TryGetValue(_selected, out var p))
        {
            _form.Children.Add(new TextBlock { Text = _working.Profiles.Count == 0 ? "No profiles yet. Click \"Add profile\"." : "Choose a profile on the left.", Foreground = Muted });
            return;
        }
        var name = new TextBox { Text = _selected, Tag = "auth-name" };
        name.LostFocus += (_, _) => Rename(name.Text ?? string.Empty);
        var type = new ComboBox { ItemsSource = Types, SelectedItem = Types.FirstOrDefault(t => t.Equals(p.Type, StringComparison.OrdinalIgnoreCase)) ?? "bearer", Tag = "auth-type", HorizontalAlignment = HorizontalAlignment.Stretch };
        type.SelectionChanged += (_, _) => { if (type.SelectedItem is string t && !t.Equals(p.Type, StringComparison.OrdinalIgnoreCase)) { p.Type = t; BuildForm(); } };
        _form.Children.Add(Labeled("Name (what a step writes in its Authentication setting)", name));
        _form.Children.Add(Labeled("Type", type));
        _form.Children.Add(new TextBlock { Text = Explain(p.Type), TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12 });
        switch (p.Type.ToLowerInvariant())
        {
            case "bearer":
                Login(p); Token(p);
                break;
            case "cookie":
                Login(p);
                _form.Children.Add(Text("TtlSeconds", "Log in again after (seconds, 0 = never)", () => p.TtlSeconds.ToString(), v => p.TtlSeconds = int.TryParse(v, out var n) && n >= 0 ? n : 0));
                break;
            case "oauth2":
                _form.Children.Add(Combo("GrantType", "Grant", ["client_credentials", "password", "refresh_token"], () => p.GrantType, v => { if (!v.Equals(p.GrantType, StringComparison.OrdinalIgnoreCase)) { p.GrantType = v; BuildForm(); } }));
                _form.Children.Add(Text("TokenUrl", "Token URL", () => p.TokenUrl, v => p.TokenUrl = v, "https://auth.example.com/oauth/token"));
                _form.Children.Add(Text("ClientId", "Client id", () => p.ClientId, v => p.ClientId = v, "${CLIENT_ID}"));
                _form.Children.Add(Text("ClientSecret", "Client secret", () => p.ClientSecret, v => p.ClientSecret = v, "${CLIENT_SECRET}"));
                _form.Children.Add(Text("Scope", "Scope", () => p.Scope, v => p.Scope = v, "read write"));
                _form.Children.Add(Text("Audience", "Audience", () => p.Audience, v => p.Audience = v));
                if (p.GrantType.Equals("password", StringComparison.OrdinalIgnoreCase))
                {
                    _form.Children.Add(Text("User", "User", () => p.User, v => p.User = v, "${API_USER}"));
                    _form.Children.Add(Text("Password", "Password", () => p.Password, v => p.Password = v, "${API_PASSWORD}"));
                }
                if (p.GrantType.Equals("refresh_token", StringComparison.OrdinalIgnoreCase))
                    _form.Children.Add(Text("RefreshToken", "Refresh token (for example from a one-time browser sign-in, kept in a pipeline secret)", () => p.RefreshToken, v => p.RefreshToken = v, "${REFRESH_TOKEN}"));
                _form.Children.Add(Combo("ClientAuth", "Send the client in", ["body", "basic"], () => p.ClientAuth, v => p.ClientAuth = v));
                Token(p, withPath: false);
                break;
            case "basic":
                _form.Children.Add(Text("User", "User", () => p.User, v => p.User = v, "${API_USER}"));
                _form.Children.Add(Text("Password", "Password", () => p.Password, v => p.Password = v, "${API_PASSWORD}"));
                break;
            case "apikey":
                _form.Children.Add(Text("Name", "Header or query name", () => p.Name, v => p.Name = v, "X-Api-Key"));
                _form.Children.Add(Text("Value", "Value", () => p.Value, v => p.Value = v, "${API_KEY}"));
                _form.Children.Add(Combo("In", "Send it in", ["header", "query"], () => p.In, v => p.In = v));
                break;
            case "headers":
                _form.Children.Add(Text("Headers", "Headers (one per line: Name: value)", () => string.Join("\n", p.Headers.Select(h => $"{h.Key}: {h.Value}")), v =>
                    p.Headers = v.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(':', 2)).Where(x => x.Length == 2 && x[0].Trim().Length > 0).ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.OrdinalIgnoreCase), "X-Tenant: acme", multi: true));
                break;
            case "certificate":
                break;
        }
        _form.Children.Add(new TextBlock { Text = p.Type.Equals("certificate", StringComparison.OrdinalIgnoreCase) ? "Client certificate (mutual TLS)" : "Client certificate (optional, mutual TLS)", FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 8, 0, 0) });
        _form.Children.Add(Text("CertificatePath", "Certificate file (.pfx / .p12, or .pem with a key file)", () => p.CertificatePath, v => p.CertificatePath = v, "C:\\certs\\client.pfx"));
        _form.Children.Add(Text("CertificatePassword", "Certificate password", () => p.CertificatePassword, v => p.CertificatePassword = v, "${CERT_PASSWORD}"));
        _form.Children.Add(Text("CertificateKeyPath", "Key file (only for .pem)", () => p.CertificateKeyPath, v => p.CertificateKeyPath = v));
    }

    private void Login(AuthProfile p)
    {
        _form.Children.Add(Text("LoginUrl", "Login URL", () => p.LoginUrl, v => p.LoginUrl = v, "https://api.example.com/login"));
        _form.Children.Add(Combo("LoginMethod", "Method", ["POST", "GET", "PUT"], () => p.LoginMethod, v => p.LoginMethod = v));
        _form.Children.Add(Combo("LoginContentType", "Body format", ["application/json", "application/x-www-form-urlencoded", "text/plain", "application/xml"], () => p.LoginContentType, v => p.LoginContentType = v));
        _form.Children.Add(Text("LoginBody", "Body", () => p.LoginBody, v => p.LoginBody = v, "{\"user\":\"${API_USER}\",\"password\":\"${API_PASSWORD}\"}", multi: true));
    }

    private void Token(AuthProfile p, bool withPath = true)
    {
        if (withPath) _form.Children.Add(Text("TokenPath", "Token in the answer", () => p.TokenPath, v => p.TokenPath = v.Length == 0 ? "$.access_token" : v, "$.access_token"));
        _form.Children.Add(Text("ExpiresInPath", "Lifetime (seconds) in the answer (optional)", () => p.ExpiresInPath, v => p.ExpiresInPath = v, "$.expires_in"));
        _form.Children.Add(Text("TtlSeconds", "Or use the token for (seconds, 0 = until a request answers 401)", () => p.TtlSeconds.ToString(), v => p.TtlSeconds = int.TryParse(v, out var n) && n >= 0 ? n : 0));
        _form.Children.Add(Text("Header", "Send it in the header", () => p.Header, v => p.Header = v.Length == 0 ? "Authorization" : v));
        _form.Children.Add(Text("Prefix", "Before the token", () => p.Prefix, v => p.Prefix = v, "Bearer "));
    }

    private static string Explain(string type) => type.ToLowerInvariant() switch
    {
        "bearer" => "Logs in once with the call below, takes the token out of the answer and sends it in every request; logs in again when it expires or a request answers 401.",
        "oauth2" => "OAuth2 client credentials: asks the token URL for a token with the client id and secret, renews it before it expires.",
        "basic" => "Sends Authorization: Basic with the user and password.",
        "apikey" => "Sends a fixed key in a header or in the query string.",
        "headers" => "Sends fixed headers with every request.",
        "cookie" => "Logs in with the call below; the session cookie it sets is sent with the following requests.",
        "certificate" => "Only a client certificate: every request of the step is sent with it (mutual TLS). The certificate can also be added to any other type below.",
        _ => string.Empty
    };

    private static Control Labeled(string label, Control control) => new StackPanel { Spacing = 3, Children = { new TextBlock { Text = label, FontSize = 12, Foreground = Muted }, control } };

    private Control Text(string key, string label, Func<string> get, Action<string> set, string? hint = null, bool multi = false)
    {
        var box = new TextBox { Text = get(), Watermark = hint, Tag = "auth-" + key, AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multi ? 70 : 0, FontFamily = multi ? new FontFamily("Consolas,Menlo,monospace") : FontFamily.Default };
        box.LostFocus += (_, _) => set(box.Text ?? string.Empty);
        box.TextChanged += (_, _) => set(box.Text ?? string.Empty);
        _flush.Add(() => set(box.Text ?? string.Empty));   // Save and Test take what is in the boxes even when no event arrived
        return Labeled(label, box);
    }

    private Control Combo(string key, string label, string[] options, Func<string> get, Action<string> set)
    {
        var list = options.ToList();
        if (get().Length > 0 && !list.Contains(get(), StringComparer.OrdinalIgnoreCase)) list.Add(get());
        var combo = new ComboBox { ItemsSource = list, SelectedItem = list.FirstOrDefault(o => o.Equals(get(), StringComparison.OrdinalIgnoreCase)) ?? list[0], Tag = "auth-" + key, HorizontalAlignment = HorizontalAlignment.Stretch };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string chosen) set(chosen); };
        return Labeled(label, combo);
    }

    private void Rename(string name)
    {
        name = name.Trim();
        if (_selected is null || name.Length == 0 || name == _selected) return;
        if (_working.Profiles.ContainsKey(name)) { Say($"A profile called {name} already exists.", true); return; }
        _working.Profiles[name] = _working.Profiles[_selected]; _working.Profiles.Remove(_selected);
        if (string.Equals(_working.Default, _selected, StringComparison.OrdinalIgnoreCase)) _working.Default = name;
        _selected = name;
        Refresh();
    }

    public void Say(string text, bool error) { _status.Text = text; _status.Foreground = error ? Brush.Parse("#FF7A7A") : Brush.Parse("#39D39B"); }

    public bool Save()
    {
        foreach (var flush in _flush) flush();
        var problem = _settings.SaveAuth(Global ? null : _instance, _working);
        Say(problem ?? $"Saved {_working.Profiles.Count} {(_working.Profiles.Count == 1 ? "profile" : "profiles")} for {(Global ? "all instances" : "this instance")}. Steps pick one in their settings (Authentication).", problem is not null);
        return problem is null;
    }

    /// <summary>Logs in with the chosen profile once (values like ${NAME} are read from the environment variables) and says whether it worked. The token is never shown.</summary>
    public async Task TestAsync()
    {
        foreach (var flush in _flush) flush();
        if (_selected is null || !_working.Profiles.TryGetValue(_selected, out var profile)) { Say("Choose a profile first.", true); return; }
        try
        {
            var text = Secrets.Expand(System.Text.Json.JsonSerializer.Serialize(profile), "the profile");
            var expanded = System.Text.Json.JsonSerializer.Deserialize<AuthProfile>(text)!;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var manager = new AuthManager(new AuthSettings(), client);
            var applied = await manager.ApplyAsync(_selected, expanded, new Dictionary<string, string>(), refresh: true, CancellationToken.None);
            var names = applied.Headers.Keys.Concat(applied.Query.Keys.Select(k => "?" + k)).ToList();
            Say(names.Count > 0 ? $"It works: {string.Join(", ", names)} would be sent (value hidden)." : "It works: the login call was accepted.", false);
        }
        catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or TaskCanceledException) { Say(Secrets.Mask(ex.Message), true); }
    }

    public const string HelpText =
@"AUTHENTICATION

A profile says how to log in. Steps use it without any login steps in the testcase: the runner logs in when the first step needs it, keeps the token, and logs in again when it expires or a request answers 401.

WHERE A PROFILE LIVES
  This instance only  – saved under this instance in appsettings.json.
  All instances       – saved at the top of appsettings.json. An instance profile with the same name hides the global one.
The Default profile is used by every HTTP, GraphQL and WCF step that does not choose another.

WHICH PROFILE A STEP USES
  Step settings > Authentication:  (empty / default) = the default profile, none = no authentication, or the name of a profile.
  A header written in the step itself (for example Authorization) always wins over the profile.
  A step can also carry a whole profile in its StepConfig:  {""auth"":{""type"":""basic"",""user"":""ann"",""password"":""${ANN_PASSWORD}""}}

TYPES
  bearer   Login URL + body, e.g. POST https://api.example.com/login  {""user"":""${API_USER}"",""password"":""${API_PASSWORD}""}.
           The token is read at Token in the answer ($.access_token), sent as  Authorization: Bearer <token>.
  oauth2   Token URL + client id + secret (+ scope). Renewed before it expires.
  basic    User + password.
  apikey   Name + value, in a header (X-Api-Key) or in the query string (?api_key=…).
  headers  Fixed headers, one per line.
  cookie   Login call; the session cookie is kept for the following requests.
  certificate  A client certificate (mutual TLS); it can also be added to any other type. .pfx/.p12 with a password, or .pem with a key file.

OAUTH2 GRANTS
  client_credentials (default), password (User + Password), refresh_token (a refresh token you got once by signing in with a browser and keep as a pipeline secret: ${REFRESH_TOKEN}). When the server answers with a new refresh token it is used for the renewals.

PER ENVIRONMENT
  A profile called api@qa is used instead of api when the run is for the environment qa (api@prod for prod …). Steps and the default keep writing the name api.

SECRETS
  Write ${NAME} for passwords, secrets and keys: the value comes from --set NAME=value or the environment variable NAME when the run starts, and is never saved in the workbook.
  Tokens and passwords are shown as *** in logs, reports and history; the history shows which header was added (value hidden).

DIFFERENT USER PER DATA ROW
  Use a request column in the profile, e.g. Body {""user"":""_user_"",""password"":""_password_""}. Every row logs in with its own values and gets its own token.

TEST LOGIN
  Logs in once with the chosen profile and says whether it worked. ${NAME} values are read from the environment variables.";
}

internal static class ContextHelpText
{
    public static void Show(Window owner, string title, string text)
    {
        var window = new Window { Title = title, Width = 720, Height = 620, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        close.Click += (_, _) => window.Close();
        var dock = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(close);
        dock.Children.Add(new ScrollViewer { Content = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 } });
        window.Content = dock;
        window.Show(owner);
    }
}
