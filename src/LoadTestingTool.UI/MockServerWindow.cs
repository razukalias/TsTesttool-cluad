using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI;

/// <summary>Configure a mock web server (routes, requests, responses), run it as its own process, and watch its traffic.</summary>
public sealed partial class MockServerWindow : Window
{
    private static readonly string[] Verbs = ["ANY", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];
    private static readonly string[] Sources = ["query", "header", "body", "json", "graphql"];
    private static readonly string[] Ops = ["equals", "contains", "regex", "exists", "notexists"];

    private readonly string _serversFolder;
    private readonly MockServerProcess _process;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private MockServerConfig _config = Starter();
    private string? _path;
    private bool _loading;
    private bool _dirty;
    private bool _running;
    private bool _polling;
    private MockServerClient? _client;
    private int _clientPort;

    private readonly TextBox _name = new() { MinWidth = 150 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Increment = 1, FormatString = "0", Width = 110 };
    private readonly CheckBox _allInterfaces = new() { Content = "All interfaces" };
    private readonly TextBox _basePath = new() { Width = 130, Watermark = "/api/v1" };
    private readonly NumericUpDown _extraDelay = new() { Minimum = 0, Maximum = 600000, Increment = 50, FormatString = "0", Width = 120 };
    private readonly CheckBox _includeRemote = new() { Content = "also non-local URLs" };
    private readonly Button _runButton = new() { MinWidth = 90 };
    private readonly Ellipse _light = new() { Width = 12, Height = 12, Fill = DashboardStyle.Brush("#5B6B7D"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = DashboardStyle.Text("Stopped", 12, false, DashboardStyle.Muted);
    private readonly TextBlock _problems = DashboardStyle.Text("", 12, false, DashboardStyle.Red);
    /// <summary>Set by the editor: points the selected instance's step URLs at the given address (and whether remote ones too); returns what happened.</summary>
    public Func<string, bool, string>? UseInInstance { get; set; }
    internal TextBlock StatusText => _status;
    internal TextBlock ProblemText => _problems;

    public MockServerWindow(string runner, string serversFolder)
    {
        _serversFolder = Path.GetFullPath(serversFolder);
        _process = new MockServerProcess(runner);
        Title = "Mock server";
        Width = 1180; Height = 780; MinWidth = 900; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = Build();
        LoadIntoControls();
        _timer.Tick += async (_, _) => await PollAsync();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private static MockServerConfig Starter() => new()
    {
        Name = "Mock server", Port = 8099,
        Routes =
        [
            new MockRoute { Name = "Health", Method = "GET", Path = "/health", Response = new MockResponse { Body = "{\"status\":\"ok\"}" } },
            new MockRoute { Name = "Get order", Method = "GET", Path = "/orders/{id}", Response = new MockResponse { Body = "{\"id\":\"{{path.id}}\",\"status\":\"NEW\"}" } },
            new MockRoute { Name = "Create order", Method = "POST", Path = "/orders", Response = new MockResponse { Status = 201, Headers = [new MockHeader { Name = "Location", Value = "/orders/{{guid}}" }], Body = "{\"created\":true,\"echo\":{{body}}}" } }
        ]
    };

    // ----- layout -----

    private Control Build()
    {
        var help = DashboardStyle.Action("Help", () => ContextHelpWindow.Show(this, MockServerHelp.Title, MockServerHelp.Text));
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Children =
        {
            Labeled("Name", _name), Labeled("Port", _port), Labeled("Base path", _basePath), Labeled("Extra latency (ms)", _extraDelay), Pad(_allInterfaces),
            DashboardStyle.Action("New", NewServer), DashboardStyle.Action("Open…", async () => await OpenAsync()), DashboardStyle.Action("Save", () => Save()), help
        }};
        _runButton.Click += async (_, _) => await ToggleRunAsync();
        _runButton.Classes.Add("primary");
        var copy = DashboardStyle.Action("Copy URL", async () => { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(BaseUrl()); });
        var use = DashboardStyle.Action("Use in instance", UseInSelectedInstance);
        ToolTip.SetTip(use, "Point the step URLs (targetUrl) of the instance selected in the editor at this server. 127.0.0.1 and localhost URLs only, unless 'also non-local URLs' is ticked. One undo step; save the instance to keep it.");
        var runBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _runButton, _light, _status, copy, use, _includeRemote } };
        _name.TextChanged += (_, _) => Edit(() => _config.Name = _name.Text ?? "");
        _basePath.TextChanged += (_, _) => Edit(() => _config.BasePath = _basePath.Text?.Trim() ?? "");
        _allInterfaces.IsCheckedChanged += (_, _) => Edit(() => _config.AllInterfaces = _allInterfaces.IsChecked == true);
        _extraDelay.ValueChanged += (_, _) => Edit(() => _config.ExtraDelayMs = (int)(_extraDelay.Value ?? 0));
        _port.ValueChanged += (_, _) => Edit(() => _config.Port = (int)(_port.Value ?? 8099));

        var tabs = new TabControl { Items = { new TabItem { Header = "Routes", Content = BuildRoutes() }, new TabItem { Header = "Monitor", Content = BuildMonitor() } } };
        _tabs = tabs;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(16) };
        root.Children.Add(DashboardStyle.Text("Mock server", 22, true));
        Add(root, bar, 1); Add(root, runBar, 2); Add(root, tabs, 3);
        Add(root, _problems, 4);
        return root;
    }

    private static void Add(Grid grid, Control control, int row) { Grid.SetRow(control, row); grid.Children.Add(control); }
    private static Control Pad(Control c) { c.Margin = new Thickness(0, 0, 10, 6); c.VerticalAlignment = VerticalAlignment.Bottom; return c; }
    private static Control Labeled(string label, Control control) => new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 10, 6), Children = { DashboardStyle.Text(label, 11, false, DashboardStyle.Muted), control } };

    // ----- edit tracking, files -----

    private void Edit(Action change)
    {
        if (_loading) return;
        change();
        _dirty = true;
        Revalidate();
        Title = "Mock server *";
    }

    private void Revalidate()
    {
        var problems = _config.Validate();
        _problems.Text = problems.Count == 0 ? "" : string.Join("  •  ", problems.Take(3)) + (problems.Count > 3 ? $"  (+{problems.Count - 3} more)" : "");
    }

    private void LoadIntoControls()
    {
        _loading = true;
        _name.Text = _config.Name; _port.Value = _config.Port; _basePath.Text = _config.BasePath; _extraDelay.Value = _config.ExtraDelayMs; _allInterfaces.IsChecked = _config.AllInterfaces;
        _loading = false;
        _dirty = false;
        Title = _path is null ? "Mock server" : "Mock server – " + Path.GetFileName(_path);
        _selected = 0;
        RefreshRouteList();
        Revalidate();
        UpdateRunUi();
    }

    internal MockServerConfig Config => _config;
    internal string? FilePath => _path;

    internal void NewServer()
    {
        _config = Starter(); _path = null;
        LoadIntoControls();
    }

    internal void Load(string path)
    {
        _config = MockServerConfig.Load(path); _path = path;
        LoadIntoControls();
    }

    private async Task OpenAsync()
    {
        Directory.CreateDirectory(_serversFolder);
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a mock server", AllowMultiple = false, SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_serversFolder),
            FileTypeFilter = [new FilePickerFileType("Mock server") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        try { Load(path); } catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException) { _problems.Text = "That file could not be read: " + exception.Message; }
    }

    private static string FileNameOf(string name)
    {
        var clean = string.Concat((string.IsNullOrWhiteSpace(name) ? "Mock server" : name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return clean + ".json";
    }

    /// <summary>Writes the file (a new one gets its name from the server name) and, when the server runs, gives it the new routes.</summary>
    internal async Task<string?> SaveAsync()
    {
        var problems = _config.Validate();
        if (problems.Count > 0) { Revalidate(); return "Fix the problems first: " + problems[0]; }
        _path ??= Path.Combine(_serversFolder, FileNameOf(_config.Name));
        _config.Save(_path);
        _dirty = false;
        Title = "Mock server – " + Path.GetFileName(_path);
        if (_running && _client is not null)
        {
            var refused = await _client.ReloadAsync(_config);
            if (refused is not null) return refused.Contains("restart", StringComparison.OrdinalIgnoreCase) ? "Saved. The new port needs a restart: Stop, then Run." : "Saved, but the running server refused it: " + refused;
        }
        return null;
    }

    private async void Save()
    {
        var error = await SaveAsync();
        _status.Text = error ?? (_running ? $"Saved and applied to the running server on port {_config.Port}" : "Saved " + Path.GetFileName(_path));
    }

    internal void UseInSelectedInstance()
    {
        var problems = _config.Validate();
        if (problems.Count > 0) { _status.Text = "Fix the problems first: " + problems[0]; return; }
        _status.Text = UseInInstance?.Invoke(BaseUrl(), _includeRemote.IsChecked == true) ?? "Open this window from the editor to use it in an instance.";
    }

    // ----- run / stop / poll -----

    private string BaseUrl() => $"http://127.0.0.1:{_config.Port}";

    internal async Task ToggleRunAsync()
    {
        _runButton.IsEnabled = false;
        try
        {
            if (_running)
            {
                _status.Text = "Stopping…";
                await _process.StopAsync(_clientPort);
                await PollAsync();
                return;
            }
            var error = await SaveAsync();
            if (error is not null && !error.StartsWith("Saved", StringComparison.Ordinal)) { _status.Text = error; return; }
            _status.Text = "Starting…";
            var failed = await _process.StartAsync(_path!, _config.Port);
            if (failed is not null) { _status.Text = failed; return; }
            await PollAsync();
        }
        finally { _runButton.IsEnabled = true; UpdateRunUi(); }
    }

    private void UpdateRunUi()
    {
        _runButton.Content = _running ? "Stop" : "Run";
        _light.Fill = DashboardStyle.Brush(_running ? "#39D39B" : "#5B6B7D");
        _port.IsEnabled = !_running; _allInterfaces.IsEnabled = !_running;
    }

    /// <summary>Asks the server for its state and for requests it has not shown yet.</summary>
    internal async Task PollAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            var port = _config.Port;
            if (_client is null || _clientPort != port) { _client?.Dispose(); _client = new MockServerClient(port); _clientPort = port; _lastSeq = 0; }
            var status = await _client.StatusAsync();
            var wasRunning = _running;
            _running = status is not null;
            if (_running)
            {
                _status.Text = $"Running on {BaseUrl()}  ·  {status!.Value.Routes} route(s)  ·  {status.Value.LastSeq} request(s)";
                if (status.Value.LastSeq < _lastSeq) { _lastSeq = 0; _entries.Clear(); }
                if (status.Value.LastSeq > _lastSeq) AddEntries(await _client.RequestsAsync(_lastSeq));
            }
            else if (wasRunning) _status.Text = "Stopped";
            else if (_status.Text?.StartsWith("Running", StringComparison.Ordinal) == true) _status.Text = "Stopped";
            else if (_status.Text is null or "") _status.Text = "Stopped";
            UpdateRunUi();
        }
        finally { _polling = false; }
    }
}
