using System.Globalization;

namespace LoadTestingTool.Editing;

/// <summary>How a config-sheet field reads to the runner, so the editor and the validator give the same advice.</summary>
public enum FieldState
{
    /// <summary>Nothing to say.</summary>
    Ok,
    /// <summary>Fine, but worth telling the author what it will do (a text Wait pauses for a person).</summary>
    Info,
    /// <summary>The runner accepts it but will not do what the author probably meant.</summary>
    Warning
}

/// <param name="State">How serious it is.</param>
/// <param name="Message">What the runner will do with the value; empty for <see cref="FieldState.Ok"/>.</param>
public readonly record struct FieldCheck(FieldState State, string Message)
{
    public static readonly FieldCheck Fine = new(FieldState.Ok, string.Empty);
}

/// <summary>The rules for the numeric and status fields of a step: wait, timeout, manualtimeoutminutes and expectedstatus.</summary>
public static class StepFieldRules
{
    public static bool Covers(string header) => header.Trim().ToLowerInvariant() is "wait" or "timeout" or "timeoutms" or "manualtimeoutminutes" or "expectedstatus";

    public static FieldCheck Check(string header, string? rawValue)
    {
        var name = header.Trim().ToLowerInvariant();
        var value = (rawValue ?? string.Empty).Trim();
        if (value.Length == 0) return FieldCheck.Fine;
        return name switch
        {
            "wait" => CheckWait(value),
            "timeout" => CheckWhole(name, value, "the runner will use 0 instead (the default timeout)"),
            "timeoutms" => CheckWhole(name, value, "the runner will use 0 instead (the timeout in seconds, or the default)"),
            "manualtimeoutminutes" => CheckWhole(name, value, "the runner will use 0 instead"),
            "expectedstatus" => CheckStatus(value),
            _ => FieldCheck.Fine
        };
    }

    private static FieldCheck CheckWait(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            return new FieldCheck(FieldState.Info, $"Pauses the run for a person to confirm: \"{value}\".");
        if (ms < 0) return new FieldCheck(FieldState.Warning, $"wait '{value}' is negative; the runner will not wait.");
        return ms == 0 ? FieldCheck.Fine : new FieldCheck(FieldState.Info, $"Waits {ms:N0} ms before the step.");
    }

    private static FieldCheck CheckWhole(string name, string value, string consequence)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return new FieldCheck(FieldState.Warning, $"{name} '{value}' is not a whole number; {consequence}.");
        return number < 0 ? new FieldCheck(FieldState.Warning, $"{name} '{value}' is negative; the runner will use 0 instead.") : FieldCheck.Fine;
    }

    private static FieldCheck CheckStatus(string value)
    {
        // The runner accepts 200, a range 200-299 and a family 2xx; a variable or expression is resolved at run time.
        if (value.IndexOfAny(['{', '$', '@', '=']) >= 0) return FieldCheck.Fine;
        if (Valid(value)) return FieldCheck.Fine;
        return new FieldCheck(FieldState.Warning, $"expectedstatus '{value}' is not a status like 200, a range like 200-299 or a family like 2xx; every response will count as failed.");
    }

    private static bool Valid(string value)
    {
        static bool Code(string text) => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && code is >= 100 and <= 599;
        if (value.Contains('-')) { var parts = value.Split('-'); return parts.Length == 2 && Code(parts[0]) && Code(parts[1]) && int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture) <= int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture); }
        if (value.EndsWith("xx", StringComparison.OrdinalIgnoreCase)) return value.Length == 3 && value[0] is >= '1' and <= '5';
        return Code(value);
    }
}
