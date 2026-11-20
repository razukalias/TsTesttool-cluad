using LoadTestingTool.Execution;

namespace LoadTestingTool.UI;

/// <summary>The settings of the control steps (If, While, For, ForEach) as named fields, and how they are written into the step's StepConfig (<c>key=value;key=value</c>).</summary>
internal static class ControlStepSettings
{
    internal sealed record Setting(string Key, string Label, string Placeholder, string Help);

    public static bool IsControl(string type) => type is "If" or "While" or "For" or "ForEach";

    public static IReadOnlyList<Setting> For(string type) => type switch
    {
        "While" =>
        [
            new("max", "Most rounds", "100", "Stops the loop after this many rounds."),
            new("onlimit", "When the limit is reached", "fail", "fail (default) makes the step fail; continue goes on with the next step."),
            new("delayms", "Wait between rounds (ms)", "0", "Pause before the next round, for example while polling."),
            new("timeoutms", "Whole loop timeout (ms)", "", "The loop fails when it runs longer than this (checked between rounds)."),
            new("onfailure", "When a step inside fails", "stop", "stop (default) ends the data row; break leaves the loop; continue goes on with the next round."),
        ],
        "For" =>
        [
            new("from", "From", "1", "First number."),
            new("to", "To", "5", "Last number. Use either To or Count."),
            new("count", "Count", "", "How many rounds (instead of To)."),
            new("step", "Step", "1", "Added every round; may be negative."),
            new("var", "Variable name", "i", "Use it as _i_ in the steps inside."),
            new("delayms", "Wait between rounds (ms)", "0", ""),
            new("timeoutms", "Whole loop timeout (ms)", "", "The loop fails when it runs longer than this (checked between rounds)."),
            new("onfailure", "When a step inside fails", "stop", "stop (default), break or continue."),
        ],
        "ForEach" =>
        [
            new("items", "Items", "<From_response_List.ids_r>", "A JSON array, a reference to a response list, or a comma list."),
            new("separator", "Separator", ",", "Splits a plain list."),
            new("var", "Variable name", "item", "Use it as _item_ in the steps inside."),
            new("index", "Index name", "index", "Counts from 0."),
            new("delayms", "Wait between rounds (ms)", "0", ""),
            new("timeoutms", "Whole loop timeout (ms)", "", "The loop fails when it runs longer than this (checked between rounds)."),
            new("onfailure", "When a step inside fails", "stop", "stop (default), break or continue."),
        ],
        _ => [],
    };

    /// <summary>The values already in a StepConfig (JSON or key=value text), by key.</summary>
    public static Dictionary<string, string> Read(string? stepConfig) => ControlConfig.Parse(stepConfig);

    /// <summary>The StepConfig for the given values: only filled settings, in the order of the type's fields; settings of other keys in the old text are kept.</summary>
    public static string Write(string type, IReadOnlyDictionary<string, string> values, string? oldStepConfig)
    {
        var known = For(type).Select(s => s.Key).ToList();
        var parts = known.Where(k => values.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)).Select(k => $"{k}={values[k].Trim()}").ToList();
        foreach (var other in Read(oldStepConfig)) if (!known.Contains(other.Key, StringComparer.OrdinalIgnoreCase)) parts.Add($"{other.Key}={other.Value}");
        return string.Join(";", parts);
    }
}
