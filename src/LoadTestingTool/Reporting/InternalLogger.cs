using System.Text;

namespace LoadTestingTool.Reporting;

public sealed class InternalLogger : IDisposable
{
    public static string SafeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((value ?? "testcase").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "testcase" : cleaned;
    }
    private readonly object _sync = new();
    private readonly List<StreamWriter> _writers = [];
    private readonly string _instanceId;
    private readonly string _runId;

    public InternalLogger(string folder, string instanceId, string runId, bool enabled, string? secondaryFolder = null, string fileName = "internal.log")
    {
        Enabled = enabled;
        _instanceId = instanceId;
        _runId = runId;
        if (enabled)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            _writers.Add(new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true });
            FilePath = path;
            if (!string.IsNullOrWhiteSpace(secondaryFolder) && !Path.GetFullPath(secondaryFolder).Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(secondaryFolder);
                _writers.Add(new StreamWriter(Path.Combine(secondaryFolder, fileName), append: false, Encoding.UTF8) { AutoFlush = true });
                SecondaryFilePath = Path.Combine(secondaryFolder, fileName);
            }
        }
    }

    public bool Enabled { get; }
    public string? FilePath { get; }
    public string? SecondaryFilePath { get; }

    public static string MaskSensitiveData(string value, bool enabled = true)
    {
        if (!enabled || string.IsNullOrEmpty(value)) return value ?? string.Empty;
        var masked = System.Text.RegularExpressions.Regex.Replace(
            value,
            "(?i)(\\\"(?:authorization|password|secret|token|cookie|api[-_]?key)\\\"\\s*:\\s*\\\")(?:[^\\\"\\\\]|\\\\.)*(\\\")",
            "$1********$2");
        return System.Text.RegularExpressions.Regex.Replace(
            masked,
            "(?i)\\b(authorization|password|secret|token|cookie|api[-_]?key)(\\s*[:=]\\s*)[^;\\r\\n,}]+",
            "$1$2********");
    }

    public static string RedactVariableValues(string value, IReadOnlySet<string>? variableNames, IReadOnlyDictionary<string, string>? variables)
    {
        if (string.IsNullOrEmpty(value) || variableNames is null || variableNames.Count == 0 || variables is null) return value ?? string.Empty;
        var result = value;
        foreach (var name in variableNames.OrderByDescending(x => x.Length))
        {
            if (!variables.TryGetValue(name, out var replacement) || string.IsNullOrEmpty(replacement)) continue;
            result = result.Replace(replacement, $"[redacted:{name}]", StringComparison.Ordinal);
        }
        return result;
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? exception = null) => Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    public void Section(string title)
    {
        if (!Enabled || _writers.Count == 0) return;
        Write("INFO", $"==================== {title.Trim()} ====================");
    }

    private void Write(string level, string message)
    {
        if (!Enabled || _writers.Count == 0) return;
        lock (_sync)
        {
            var timestamp = DateTimeOffset.Now.ToString("O");
            var lines = (message ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var rawLine = lines[index];
                var line = index == 0
                    ? $"{timestamp} [{level}] [{_instanceId}] [run={_runId}] {MaskSensitiveData(rawLine)}"
                    : MaskSensitiveData(rawLine);
                foreach (var writer in _writers) writer.WriteLine(line);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync) foreach (var writer in _writers) writer.Dispose();
    }
}
