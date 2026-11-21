using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Threading;

namespace LoadTestingTool.UI;

/// <summary>
/// Keeps one failing handler from taking the whole app down. Unhandled exceptions on the UI thread are written to <c>Logs/ui-errors.log</c>,
/// the action that failed is dropped and the user gets a message; a short trail of what the user did last is kept for the log.
/// </summary>
public static class CrashGuard
{
    private static readonly ConcurrentQueue<string> Trail = new();
    private static readonly object LogLock = new();
    private static bool _installed;
    private static long _lastNotice;

    /// <summary>Where the logs go (Logs next to the app); tests point it elsewhere.</summary>
    public static string LogFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Logs");

    /// <summary>Raised on the UI thread with a message for the user whenever something was caught.</summary>
    public static event Action<string>? Reported;

    /// <summary>How many failures were caught so far.</summary>
    public static int CaughtCount { get; private set; }

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (e.Exception is OutOfMemoryException or InsufficientExecutionStackException) return;   // not safe to carry on
            Report(e.Exception, "the window");
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log("unobserved task", e.Exception); e.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("fatal", e.ExceptionObject as Exception);
    }

    /// <summary>Remembers what the user did (opened a wizard, moved to a cell ...), so a crash or hang log says what led to it.</summary>
    public static void Trace(string what)
    {
        Trail.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {what}");
        while (Trail.Count > 60 && Trail.TryDequeue(out _)) { }
    }

    public static IReadOnlyList<string> RecentTrail() => Trail.ToArray();

    /// <summary>Runs the action; a failure is logged and reported instead of thrown. False when it failed.</summary>
    public static bool Guard(Action action, string what)
    {
        try { action(); return true; }
        catch (Exception ex) when (ex is not (OutOfMemoryException or InsufficientExecutionStackException)) { Report(ex, what); return false; }
    }

    public static void Report(Exception ex, string what)
    {
        CaughtCount++;
        Log(what, ex);
        // one message per two seconds, so a failing loop does not bury the window in notices
        var now = Environment.TickCount64;
        if (now - _lastNotice < 2000) return;
        _lastNotice = now;
        var first = ex.Message.Split('\n')[0].Trim();
        Notify($"Something went wrong in {what}: {first} Your work is kept; details are in Logs/ui-errors.log.");
    }

    /// <summary>Gives the user a message (from any thread).</summary>
    public static void Notify(string message)
    {
        if (Dispatcher.UIThread.CheckAccess()) Reported?.Invoke(message);
        else Dispatcher.UIThread.Post(() => Reported?.Invoke(message));
    }

    internal static void Log(string what, Exception? ex)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogFolder);
                var trail = string.Join(Environment.NewLine + "    ", Trail.TakeLast(15));
                File.AppendAllText(Path.Combine(LogFolder, "ui-errors.log"),
                    $"{DateTimeOffset.Now:O} [{what}]{Environment.NewLine}{ex}{Environment.NewLine}  last actions:{Environment.NewLine}    {trail}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { /* logging must never fail the app again */ }
    }

    internal static void WriteHangLog(TimeSpan frozen, string threads)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogFolder);
                var trail = string.Join(Environment.NewLine + "    ", Trail.TakeLast(40));
                File.AppendAllText(Path.Combine(LogFolder, "ui-hang.log"),
                    $"{DateTimeOffset.Now:O} The window has not responded for {frozen.TotalSeconds:0} s.{Environment.NewLine}  {threads}{Environment.NewLine}  last actions before it froze:{Environment.NewLine}    {trail}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

/// <summary>
/// Notices when the UI thread stops answering. The UI thread stamps a heartbeat every second; this class is checked from another thread.
/// After the limit it writes <c>Logs/ui-hang.log</c> (with the last actions) once per freeze and tells the user when the window answers again.
/// </summary>
public sealed class HangMonitor
{
    private readonly TimeSpan _limit;
    private long _lastBeat;
    private bool _reported;
    private TimeSpan _reportedFor;

    public HangMonitor(TimeSpan limit, DateTime? now = null) { _limit = limit; _lastBeat = (now ?? DateTime.UtcNow).Ticks; }

    public bool Frozen => _reported;

    /// <summary>Called on the UI thread (a timer).</summary>
    public void Beat(DateTime? now = null)
    {
        var time = (now ?? DateTime.UtcNow).Ticks;
        var wasReported = _reported;
        var frozenFor = _reportedFor;
        Interlocked.Exchange(ref _lastBeat, time);
        if (!wasReported) return;
        _reported = false;
        CrashGuard.Notify($"The window was frozen for about {frozenFor.TotalSeconds:0} seconds and is responding again. What led to it is in Logs/ui-hang.log; please send that file.");
    }

    /// <summary>Called from the watchdog thread. True the moment a freeze is reported.</summary>
    public bool Check(DateTime? now = null)
    {
        var frozen = TimeSpan.FromTicks((now ?? DateTime.UtcNow).Ticks - Interlocked.Read(ref _lastBeat));
        if (frozen < _limit || _reported) { if (_reported) _reportedFor = frozen; return false; }
        _reported = true;
        _reportedFor = frozen;
        ThreadPool.GetAvailableThreads(out var workers, out _);
        var process = Process.GetCurrentProcess();
        CrashGuard.WriteHangLog(frozen, $"memory {process.WorkingSet64 / 1048576} MB, {process.Threads.Count} threads, {workers} free pool threads");
        return true;
    }

    /// <summary>Starts the heartbeat on the UI thread and the checking thread.</summary>
    public static HangMonitor Start(TimeSpan? limit = null)
    {
        var monitor = new HangMonitor(limit ?? TimeSpan.FromSeconds(10));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => monitor.Beat();
        timer.Start();
        var thread = new Thread(() => { while (true) { Thread.Sleep(1000); monitor.Check(); } }) { IsBackground = true, Name = "UI watchdog" };
        thread.Start();
        return monitor;
    }
}
