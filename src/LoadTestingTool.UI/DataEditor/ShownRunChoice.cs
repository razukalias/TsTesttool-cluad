using System.Globalization;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One run in a run drop-down: its id and the date and time it started.</summary>
public sealed record RunChoice(string RunId, DateTime When)
{
    public string Label => RunLabel.Of(RunId, When);
    public override string ToString() => Label;
}

/// <summary>How a run is named in the drop-downs of the dashboard and the table, so both show the same text.</summary>
public static class RunLabel
{
    /// <summary>A run id starts with its start time (yyyyMMdd_HHmmss); the file time is used when it does not.</summary>
    public static DateTime WhenOf(string runId, DateTime fallback)
        => runId.Length >= 15 && DateTime.TryParseExact(runId[..15], "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : fallback;

    public static string Of(string runId, DateTime when) => $"{when:yyyy-MM-dd HH:mm:ss}  ·  {(runId.Length <= 8 ? runId : runId[^8..])}";
}

/// <summary>
/// The one earlier-run choice per instance. The dashboard's Run drop-down and the data editor's table both read and set it, so picking an
/// earlier run shows it in both. No choice (null) means the newest run, which also follows a run that is going on.
/// </summary>
public static class ShownRunChoice
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, string> Choices = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(WeakReference Owner, Action<object, string, string?> Handler)> Listeners = [];

    private static string Key(string resultsFolder)
    {
        try { return Path.GetFullPath(resultsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return resultsFolder; }
    }

    /// <summary>The run the instance's Results folder is shown as, or null for the newest run.</summary>
    public static string? Get(string resultsFolder)
    {
        lock (Gate) return Choices.TryGetValue(Key(resultsFolder), out var runId) ? runId : null;
    }

    /// <summary>Choose a run (null: the newest). Listeners hear about it only when the choice really changed.</summary>
    public static void Set(string resultsFolder, string? runId)
    {
        var key = Key(resultsFolder);
        List<(WeakReference Owner, Action<object, string, string?> Handler)> toTell;
        lock (Gate)
        {
            var before = Choices.TryGetValue(key, out var old) ? old : null;
            if (string.Equals(before, runId, StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrWhiteSpace(runId)) Choices.Remove(key); else Choices[key] = runId;
            Listeners.RemoveAll(l => !l.Owner.IsAlive);
            toTell = [.. Listeners];
        }
        foreach (var (owner, handler) in toTell) if (owner.Target is { } target) handler(target, key, string.IsNullOrWhiteSpace(runId) ? null : runId);
    }

    /// <summary>
    /// Listen for changes. The owner is held weakly and handed back to the handler, which therefore must not capture it
    /// (a closed window must not stay alive because of the subscription).
    /// </summary>
    public static void Subscribe<T>(T owner, Action<T, string, string?> handler) where T : class
    {
        lock (Gate) Listeners.Add((new WeakReference(owner), (o, key, runId) => handler((T)o, key, runId)));
    }

    /// <summary>True when <paramref name="folderA"/> and <paramref name="folderB"/> are the same Results folder.</summary>
    public static bool Same(string folderA, string folderB) => string.Equals(Key(folderA), Key(folderB), StringComparison.OrdinalIgnoreCase);

    /// <summary>The normalised key of a folder, as the listeners receive it.</summary>
    public static string KeyOf(string resultsFolder) => Key(resultsFolder);
}
