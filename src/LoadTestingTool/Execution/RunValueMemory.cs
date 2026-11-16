using System.Text.Json;

namespace LoadTestingTool.Execution;

/// <summary>
/// Remembers the values a run generated for itself, so a re-run of one failed step can use the same ones:
/// the &lt;randomnumber&gt; / &lt;guid&gt; values written into the data cells, and the values earlier steps
/// extracted from their responses (per data row and iteration). One file per workbook, beside it.
/// </summary>
public sealed class RunValueMemory
{
    /// <summary>The memory of the run in progress, or null (the workbook reader and the runner both look here).</summary>
    public static RunValueMemory? Active { get; set; }

    private sealed class Snapshot
    {
        public Dictionary<string, string> Generated { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> Rows { get; set; } = new(StringComparer.Ordinal);
        /// <summary>What a step's dynamic text (URL, headers, body template, expected values, variables) turned into when the step ran.</summary>
        public Dictionary<string, string> Steps { get; set; } = new(StringComparer.Ordinal);
    }

    private readonly string _path;
    private readonly object _gate = new();
    private Snapshot _previous = new();
    private Snapshot _current = new();
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    public RunValueMemory(string workbookPath)
    {
        _path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(workbookPath))!, ".last-run-state", "values.json");
        try { if (File.Exists(_path)) _previous = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path)) ?? new Snapshot(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { _previous = new Snapshot(); }
    }

    /// <summary>True while generated values should come from the last run instead of being made new.</summary>
    public bool Reuse { get; private set; }

    /// <summary>True when the last run left anything to reuse.</summary>
    public bool HasPrevious => _previous.Generated.Count > 0 || _previous.Rows.Count > 0 || _previous.Steps.Count > 0;

    /// <summary>Which data row, iteration and step the running code belongs to; set by the runner around each step.</summary>
    public static AsyncLocal<string?> StepScope { get; } = new();

    /// <summary>The step whose cell the workbook reader is compiling (null for a cell no step owns); lets a re-run reuse values for chosen steps only.</summary>
    [ThreadStatic] private static string? _cellOwner;
    public static string? CellOwner { get => _cellOwner; set => _cellOwner = value; }

    private bool _reuseAll = true;
    private HashSet<string> _reuseSteps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Limits reuse to the named steps (null = every step). Cells no step owns follow whenever any step reuses.</summary>
    public void ReuseOnly(IEnumerable<string>? steps)
    {
        lock (_gate) { _reuseAll = steps is null; _reuseSteps = new HashSet<string>(steps ?? [], StringComparer.OrdinalIgnoreCase); }
    }

    private bool Applies(string? step) => _reuseAll || (step is null ? _reuseSteps.Count > 0 : _reuseSteps.Contains(step));

    /// <summary>The name of the step the running code belongs to; set by the runner around each step.</summary>
    public static AsyncLocal<string?> StepName { get; } = new();

    private const int LargestRemembered = 512 * 1024;
    private readonly Dictionary<string, int> _stepOccurrences = new(StringComparer.Ordinal);

    /// <summary>Starts a (re)read of the workbook; with reuse, unchanged cells get their old generated values back.</summary>
    public void BeginRead(bool reuse)
    {
        lock (_gate) { Reuse = reuse; _current.Generated.Clear(); _occurrences.Clear(); _stepOccurrences.Clear(); }
    }

    /// <summary>
    /// The value for one generated token of one data row and iteration: the last run's value when reusing (and the owning step is chosen),
    /// else the one just made. Either way it is remembered for the next run.
    /// </summary>
    public string GeneratedFor(string rowKey, string tokenId, string? owner, Func<string> make)
    {
        var key = $"{rowKey}|{tokenId}";
        lock (_gate)
        {
            var value = Reuse && Applies(owner) && _previous.Generated.TryGetValue(key, out var old) ? old : make();
            _current.Generated[key] = value;
            return value;
        }
    }

    /// <summary>The value for one generated token: the old one when reusing and the cell is unchanged, else a new one (remembered).</summary>
    public string Generated(string sheet, int? row, string rawCellText, string token, Func<string> make)
    {
        lock (_gate)
        {
            var baseKey = $"{sheet}|{row}|{rawCellText}|{token}";
            var key = $"{baseKey}|{_occurrences.GetValueOrDefault(baseKey)}";
            _occurrences[baseKey] = _occurrences.GetValueOrDefault(baseKey) + 1;
            var value = Reuse && Applies(CellOwner) && _previous.Generated.TryGetValue(key, out var old) ? old : make();
            _current.Generated[key] = value;
            return value;
        }
    }

    /// <summary>
    /// Runs <paramref name="resolve"/> for a piece of step text, or — when reusing and this exact text was resolved in the last run — returns what it became then.
    /// Only text that actually changed is remembered, and only up to 512 KB.
    /// </summary>
    public async Task<string> StepTextAsync(string text, Func<Task<string>> resolve)
    {
        var scope = StepScope.Value;
        if (scope is null || string.IsNullOrEmpty(text)) return await resolve();
        string key;
        lock (_gate)
        {
            var baseKey = $"{scope}|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))}";
            var n = _stepOccurrences.GetValueOrDefault(baseKey);
            _stepOccurrences[baseKey] = n + 1;
            key = $"{baseKey}|{n}";
            if (Reuse && Applies(StepName.Value) && _previous.Steps.TryGetValue(key, out var old)) { _current.Steps[key] = old; return old; }
        }
        var resolved = await resolve();
        if (!string.Equals(resolved, text, StringComparison.Ordinal) && resolved.Length <= LargestRemembered) lock (_gate) _current.Steps[key] = resolved;
        return resolved;
    }

    public static string RowKey(int testcaseIndex, string dataId, int iteration, string environment)
        => $"{testcaseIndex}|{dataId}|{iteration}|{(string.IsNullOrWhiteSpace(environment) ? "default" : environment)}";

    /// <summary>What earlier steps extracted for this row in the last run.</summary>
    public IReadOnlyDictionary<string, string> PreviousRow(string rowKey)
    {
        lock (_gate) return _previous.Rows.TryGetValue(rowKey, out var row) ? new Dictionary<string, string>(row) : new Dictionary<string, string>();
    }

    public void SetRow(string rowKey, IReadOnlyDictionary<string, string> values)
    {
        lock (_gate) _current.Rows[rowKey] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the memory. A reusing (partial) run keeps what it did not touch from the last run; a normal run replaces everything it generated.
    /// </summary>
    public void Save()
    {
        lock (_gate)
        {
            var merged = new Snapshot();
            if (Reuse) { foreach (var pair in _previous.Generated) merged.Generated[pair.Key] = pair.Value; foreach (var pair in _previous.Rows) merged.Rows[pair.Key] = pair.Value; foreach (var pair in _previous.Steps) merged.Steps[pair.Key] = pair.Value; }
            foreach (var pair in _current.Generated) merged.Generated[pair.Key] = pair.Value;
            foreach (var pair in _current.Rows) merged.Rows[pair.Key] = pair.Value;
            foreach (var pair in _current.Steps) merged.Steps[pair.Key] = pair.Value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(merged));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the memory is a convenience; a run must not fail over it */ }
        }
    }
}
