using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>What a run did to a step, grouped by where the fix is: the data, the workbook, the references, or neither.</summary>
public enum RunState
{
    NotRun,
    Running,
    Passed,
    Cancelled,
    AssertionFailed,
    VariableError,
    ValidationError,
    UnexpectedError
}

/// <summary>One executed step (one data row in one environment) and how it ended.</summary>
public sealed record StepOutcome(string Testcase, string Step, string DataId, string Environment, RunState State, string Message);

/// <summary>What a host knows about the latest run of one instance.</summary>
public sealed record RunStatusSnapshot(bool Running, IReadOnlyList<StepOutcome> Outcomes)
{
    public static RunStatusSnapshot Empty { get; } = new(false, []);
}

/// <summary>Where a click on a status icon should take the person.</summary>
public sealed record RunFocus(string Testcase, string? Step, RunState State);

public static class RunClassifier
{
    /// <summary>Maps a runner result and its failure category to a state; null when the request was not executed (skipped).</summary>
    public static RunState? Classify(string? result, string? failureCategory, string? error)
    {
        result = result?.Trim() ?? string.Empty;
        if (result.Equals("PASS", StringComparison.OrdinalIgnoreCase)) return RunState.Passed;
        if (!result.Equals("FAIL", StringComparison.OrdinalIgnoreCase) && !result.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) return null;
        return FromCategory(failureCategory, error);
    }

    public static RunState FromCategory(string? category, string? error = null)
    {
        switch (category?.Trim())
        {
            case "Configuration" or "WorkbookValidation" or "Template" or "Validation": return RunState.ValidationError;
            case "VariableResolution" or "RuntimeReference": return RunState.VariableError;
            case "Assertion" or "HttpStatus" or "ActionManual": return RunState.AssertionFailed;
            case "Cancelled": return RunState.Cancelled;
            case null or "" or "None": return string.IsNullOrWhiteSpace(error) ? RunState.AssertionFailed : RunState.UnexpectedError;
            default: return RunState.UnexpectedError;
        }
    }

    /// <summary>Higher is worse; a run's colour follows the worst state below it.</summary>
    public static int Severity(RunState state) => state switch
    {
        RunState.UnexpectedError => 6,
        RunState.ValidationError => 5,
        RunState.VariableError => 4,
        RunState.AssertionFailed => 3,
        RunState.Cancelled => 2,
        RunState.Passed => 1,
        _ => 0
    };
}

/// <summary>The status icon, colour, counts and tooltip of one tree node (instance, testcase or step).</summary>
public sealed partial class RunBadge : ObservableObject
{
    private static readonly IBrush Green = Brush("#39D39B"), Red = Brush("#FF6B6B"), Orange = Brush("#FFA94D"), Purple = Brush("#B58CFF"), Magenta = Brush("#E0446A"), Blue = Brush("#5AA9FF"), Grey = Brush("#8EA4BC"), Dim = Brush("#55697F");
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private int? _signature;

    [ObservableProperty] private RunState _state = RunState.NotRun;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _stale;
    [ObservableProperty] private string _icon = string.Empty;
    [ObservableProperty] private IBrush _tint = Dim;
    [ObservableProperty] private string _counts = string.Empty;
    [ObservableProperty] private string _tooltip = string.Empty;

    public bool HasStatus => Icon.Length > 0;
    public bool HasCounts => Counts.Length > 0;
    public bool NotRunning => !IsRunning;

    partial void OnIconChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnCountsChanged(string value) => OnPropertyChanged(nameof(HasCounts));
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(NotRunning));

    /// <summary>Recomputes everything from the outcomes that belong to this node.</summary>
    public void Apply(IReadOnlyList<StepOutcome> outcomes, bool running)
    {
        var signature = new HashCode();
        signature.Add(running);
        foreach (var outcome in outcomes) { signature.Add(outcome.State); signature.Add(outcome.Message); }
        var hash = signature.ToHashCode();
        if (_signature == hash) return;
        _signature = hash;
        IsRunning = running;
        Stale = false;
        var worst = outcomes.Count == 0 ? RunState.NotRun : outcomes.MaxBy(o => RunClassifier.Severity(o.State))!.State;
        State = running ? RunState.Running : worst;
        var counts = outcomes.GroupBy(o => o.State).ToDictionary(g => g.Key, g => g.Count());
        int N(RunState s) => counts.GetValueOrDefault(s);
        if (outcomes.Count == 0 && !running) { Icon = string.Empty; Counts = string.Empty; Tooltip = string.Empty; Tint = Dim; return; }

        var shown = running ? RunState.Running : worst;
        (Icon, Tint) = shown switch
        {
            RunState.Running => ("●", Blue),
            RunState.Passed => ("✓", Green),
            RunState.AssertionFailed => ("✗", Red),
            RunState.ValidationError => ("⚠", Orange),
            RunState.VariableError => ("◆", Purple),
            RunState.UnexpectedError => ("‼", Magenta),
            RunState.Cancelled => ("⊘", Grey),
            _ => (string.Empty, Dim)
        };

        var parts = new List<string>();
        void Add(RunState s, string label) { if (N(s) > 0) parts.Add($"{N(s)} {label}"); }
        Add(RunState.Passed, "passed");
        Add(RunState.AssertionFailed, "assertion failed");
        Add(RunState.ValidationError, "validation error");
        Add(RunState.VariableError, "variable error");
        Add(RunState.UnexpectedError, "unexpected error");
        Add(RunState.Cancelled, "cancelled");

        var badge = new List<string>();
        void Mark(RunState s, string symbol) { if (N(s) > 0) badge.Add($"{N(s)}{symbol}"); }
        Mark(RunState.Passed, "✓"); Mark(RunState.AssertionFailed, "✗"); Mark(RunState.ValidationError, "⚠"); Mark(RunState.VariableError, "◆"); Mark(RunState.UnexpectedError, "‼");
        Counts = outcomes.Count > 1 ? string.Join(' ', badge) : string.Empty;

        var lead = shown switch
        {
            RunState.Running => "Running…",
            RunState.Passed => "Passed",
            RunState.AssertionFailed => "Assertion failed — the request worked, a value did not match",
            RunState.ValidationError => "Validation error — fix the step or its template in the workbook",
            RunState.VariableError => "Variable error — a reference could not be resolved",
            RunState.UnexpectedError => "Unexpected error — not caused by the test definition, often worth a retry",
            RunState.Cancelled => "Cancelled",
            _ => string.Empty
        };
        var lines = new List<string> { lead };
        if (parts.Count > 0) lines.Add(string.Join(" · ", parts));
        var environments = outcomes.Where(o => o.Environment.Length > 0).GroupBy(o => o.Environment, StringComparer.OrdinalIgnoreCase).ToList();
        if (environments.Count > 1)
            lines.Add(string.Join(" · ", environments.Select(g => $"{g.Key} {SymbolOf(g.MaxBy(o => RunClassifier.Severity(o.State))!.State)}")));
        var first = outcomes.Where(o => RunClassifier.Severity(o.State) > RunClassifier.Severity(RunState.Passed) && o.Message.Length > 0).OrderByDescending(o => RunClassifier.Severity(o.State)).FirstOrDefault();
        if (first is not null) lines.Add(first.Message.Length > 160 ? first.Message[..160] + "…" : first.Message);
        lines.Add("Click for details");
        Tooltip = string.Join(Environment.NewLine, lines.Where(l => l.Length > 0));
    }

    /// <summary>Greys the icon after the step was edited, so a colour never describes an older version of it.</summary>
    public void MarkStale()
    {
        if (!HasStatus || Stale) return;
        Stale = true;
        Tint = Dim;
        Tooltip = "Edited since the last run — run it again to refresh this status." + Environment.NewLine + Tooltip;
    }

    private static string SymbolOf(RunState state) => state switch
    {
        RunState.Passed => "✓", RunState.AssertionFailed => "✗", RunState.ValidationError => "⚠", RunState.VariableError => "◆", RunState.UnexpectedError => "‼", RunState.Cancelled => "⊘", _ => "•"
    };
}
