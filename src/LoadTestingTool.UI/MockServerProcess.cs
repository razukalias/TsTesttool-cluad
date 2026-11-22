using System.Diagnostics;
using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI;

/// <summary>Starts and stops the mock server as its own process (the runner with --serve) and finds out whether one answers on a port.</summary>
internal sealed class MockServerProcess
{
    private readonly string _runner;
    private readonly List<string> _output = [];
    private Process? _process;

    public MockServerProcess(string runner) => _runner = runner;

    /// <summary>What the process printed (its startup line, then one line per request).</summary>
    public string Output { get { lock (_output) return string.Join(Environment.NewLine, _output.TakeLast(200)); } }
    public bool Owns => _process is { HasExited: false };

    public static ProcessStartInfo StartInfo(string runner, string configPath, string workingFolder)
    {
        var isDll = runner.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(isDll ? "dotnet" : runner)
        {
            WorkingDirectory = workingFolder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (isDll) start.ArgumentList.Add(runner);
        start.ArgumentList.Add("--serve"); start.ArgumentList.Add(configPath);
        return start;
    }

    /// <summary>Starts the server for a saved config and waits until it answers. Returns an error in plain words, or null.</summary>
    public async Task<string?> StartAsync(string configPath, int port)
    {
        using (var probe = new MockServerClient(port))
            if (await probe.StatusAsync() is { } running) return $"A mock server ('{running.Name}') already answers on port {port}. Stop it first, or use another port.";
        if (!File.Exists(_runner)) return $"The runner was not found at {_runner}. Set the runner path in the dashboard settings.";
        var start = StartInfo(_runner, configPath, Path.GetDirectoryName(Path.GetFullPath(configPath))!);
        try { _process = Process.Start(start); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { return "The server process could not be started: " + exception.Message; }
        if (_process is null) return "The server process could not be started.";
        lock (_output) _output.Clear();
        void Add(string? line) { if (line is null) return; lock (_output) _output.Add(line); }
        _process.OutputDataReceived += (_, e) => Add(e.Data);
        _process.ErrorDataReceived += (_, e) => Add(e.Data);
        _process.BeginOutputReadLine(); _process.BeginErrorReadLine();
        using var client = new MockServerClient(port);
        for (var i = 0; i < 60; i++)
        {
            if (_process.HasExited)
            {
                await Task.Delay(100);
                return "The server stopped right after it started: " + (Output.Length == 0 ? $"exit code {_process.ExitCode}." : Output.Trim());
            }
            if (await client.StatusAsync() is not null) return null;
            await Task.Delay(100);
        }
        return "The server did not answer on port " + port + " within 6 seconds. " + Output.Trim();
    }

    public async Task StopAsync(int port)
    {
        using (var client = new MockServerClient(port)) await client.StopAsync();
        var process = _process;
        if (process is null) return;
        try
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(wait.Token);
        }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } }
        _process = null;
    }
}
