using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>The five row-execution settings. Null means "not set" (an instance inherits the global value).</summary>
public sealed record ExecutionValues(string? Mode, int? MaxParallelRows, int? Iterations, int? StartIntervalMs, int? IterationIntervalMs)
{
    public static ExecutionValues Defaults { get; } = new("parallel", 100, 1, 0, 0);
    public static ExecutionValues None { get; } = new(null, null, null, null, null);

    public string? Get(string key) => key switch
    {
        ExecutionKeys.Mode => Mode,
        ExecutionKeys.MaxRows => MaxParallelRows?.ToString(),
        ExecutionKeys.Iterations => Iterations?.ToString(),
        ExecutionKeys.StartInterval => StartIntervalMs?.ToString(),
        ExecutionKeys.IterationInterval => IterationIntervalMs?.ToString(),
        _ => null
    };

    public bool HasAny => Mode is not null || MaxParallelRows is not null || Iterations is not null || StartIntervalMs is not null || IterationIntervalMs is not null;
}

public static class ExecutionKeys
{
    public const string Mode = "RowsExecutionMode";
    public const string MaxRows = "MaxParallelRows";
    public const string Iterations = "Iterations";
    public const string StartInterval = "ParallelRowStartIntervalMs";
    public const string IterationInterval = "IterationIntervalMs";

    public static readonly string[] All = [Mode, MaxRows, Iterations, StartInterval, IterationInterval];

    /// <summary>Checks and normalizes one value. Returns null and the value to store, or the reason it was refused.</summary>
    public static string? Validate(string key, string? text, out string? normalized)
    {
        normalized = null;
        text = text?.Trim();
        switch (key)
        {
            case Mode:
                if (string.Equals(text, "parallel", StringComparison.OrdinalIgnoreCase)) { normalized = "parallel"; return null; }
                if (string.Equals(text, "sequential", StringComparison.OrdinalIgnoreCase)) { normalized = "sequential"; return null; }
                return "Row execution mode must be parallel or sequential.";
            case MaxRows: return Number(text, 1, 100, "Max parallel rows", out normalized);
            case Iterations: return Number(text, 1, 100000, "Iterations", out normalized);
            case StartInterval: return Number(text, 0, 3600000, "Row start interval", out normalized);
            case IterationInterval: return Number(text, 0, 3600000, "Iteration interval", out normalized);
            default: return $"Unknown execution setting {key}.";
        }
    }

    private static string? Number(string? text, int min, int max, string label, out string? normalized)
    {
        normalized = null;
        if (!int.TryParse(text, out var number) || number < min || number > max) return $"{label} must be a whole number from {min} to {max}.";
        normalized = number.ToString();
        return null;
    }
}

/// <summary>
/// The boxes of one scope: the global row-execution settings (instance null) or one instance's overrides.
/// Text is committed per field (on Enter / leaving the box); an empty instance box removes the override.
/// </summary>
public sealed partial class ExecutionSettings : ObservableObject
{
    private readonly IEnvironmentSettings _settings;
    private readonly ExecutionSettings? _global;
    private bool _loading;

    public ExecutionSettings(IEnvironmentSettings settings, string? instance, ExecutionSettings? global)
    {
        _settings = settings;
        Instance = instance;
        _global = global;
        if (global is not null) global.Changed += (_, _) => { Reload(); };
        Reload();
    }

    public string? Instance { get; }
    public bool IsGlobal => Instance is null;

    /// <summary>Raised after a value was saved (or reloaded), so summaries and the instances' watermarks refresh.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised with a message when a value was refused or could not be saved.</summary>
    public event EventHandler<string>? Problem;

    [ObservableProperty] private string _mode = string.Empty;
    [ObservableProperty] private string _maxRows = string.Empty;
    [ObservableProperty] private string _iterations = string.Empty;
    [ObservableProperty] private string _startInterval = string.Empty;
    [ObservableProperty] private string _iterationInterval = string.Empty;

    /// <summary>What an empty instance box inherits (shown as its watermark); empty for the global scope.</summary>
    public string InheritedMode => _global?.EffectiveMode ?? string.Empty;
    public string InheritedMaxRows => _global?.EffectiveMaxRows ?? string.Empty;
    public string InheritedIterations => _global?.EffectiveIterations ?? string.Empty;
    public string InheritedStartInterval => _global?.EffectiveStartInterval ?? string.Empty;
    public string InheritedIterationInterval => _global?.EffectiveIterationInterval ?? string.Empty;

    public string EffectiveMode => Mode.Length > 0 ? Mode : InheritedMode;
    public string EffectiveMaxRows => MaxRows.Length > 0 ? MaxRows : InheritedMaxRows;
    public string EffectiveIterations => Iterations.Length > 0 ? Iterations : InheritedIterations;
    public string EffectiveStartInterval => StartInterval.Length > 0 ? StartInterval : InheritedStartInterval;
    public string EffectiveIterationInterval => IterationInterval.Length > 0 ? IterationInterval : InheritedIterationInterval;

    public bool IsSequential => string.Equals(EffectiveMode, "sequential", StringComparison.OrdinalIgnoreCase);

    /// <summary>Max parallel rows and the row start interval only matter in parallel mode.</summary>
    public bool ParallelFieldsEnabled => !IsSequential;

    /// <summary>The iteration interval only spaces iterations in sequential mode.</summary>
    public bool IterationIntervalEnabled => IsSequential;

    /// <summary>True when an instance overrides at least one value.</summary>
    public bool HasOverrides => !IsGlobal && (Mode.Length + MaxRows.Length + Iterations.Length + StartInterval.Length + IterationInterval.Length) > 0;

    /// <summary>One line for the button: "parallel · 3 rows · ×2".</summary>
    public string Summary
    {
        get
        {
            if (IsSequential) return $"sequential · ×{EffectiveIterations}";
            return $"parallel · {EffectiveMaxRows} rows · ×{EffectiveIterations}";
        }
    }

    public string Hint => IsGlobal
        ? "Applies to every instance. Setting a value here also clears that value from each instance's own override."
        : "Leave a box empty to use the global value.";

    public void Reload()
    {
        var values = _settings.GetExecution(Instance);
        _loading = true;
        try
        {
            Mode = values.Mode ?? string.Empty;
            MaxRows = values.MaxParallelRows?.ToString() ?? string.Empty;
            Iterations = values.Iterations?.ToString() ?? string.Empty;
            StartInterval = values.StartIntervalMs?.ToString() ?? string.Empty;
            IterationInterval = values.IterationIntervalMs?.ToString() ?? string.Empty;
        }
        finally { _loading = false; }
        Refresh();
    }

    /// <summary>Validates and saves the current text of one field; on a refusal the field reverts and the problem is reported.</summary>
    public bool Commit(string key)
    {
        if (_loading) return true;
        var text = key switch
        {
            ExecutionKeys.Mode => Mode,
            ExecutionKeys.MaxRows => MaxRows,
            ExecutionKeys.Iterations => Iterations,
            ExecutionKeys.StartInterval => StartInterval,
            _ => IterationInterval
        };
        string? stored;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (IsGlobal) { Revert(key, "A global value cannot be empty."); return false; }
            stored = null;
        }
        else
        {
            var invalid = ExecutionKeys.Validate(key, text, out stored);
            if (invalid is not null) { Revert(key, invalid); return false; }
        }
        var current = _settings.GetExecution(Instance).Get(key);
        if (string.Equals(current ?? string.Empty, stored ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { Reload(); return true; }
        var problem = _settings.SaveExecution(Instance, key, stored);
        if (problem is not null) { Revert(key, problem); return false; }
        Reload();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Revert(string key, string message)
    {
        Reload();
        Problem?.Invoke(this, message);
    }

    private void Refresh()
    {
        foreach (var name in new[] { nameof(InheritedMode), nameof(InheritedMaxRows), nameof(InheritedIterations), nameof(InheritedStartInterval), nameof(InheritedIterationInterval),
                     nameof(EffectiveMode), nameof(EffectiveMaxRows), nameof(EffectiveIterations), nameof(EffectiveStartInterval), nameof(EffectiveIterationInterval),
                     nameof(IsSequential), nameof(ParallelFieldsEnabled), nameof(IterationIntervalEnabled), nameof(HasOverrides), nameof(Summary) })
            OnPropertyChanged(name);
    }
}
