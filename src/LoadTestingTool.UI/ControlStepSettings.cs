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
            new("rows", "Result rows", "yes", "yes (default): every round writes a small result row (output, index) into the results and reports. no: no rows unless the response sheet checks output or index; the values stay available to later steps."),
        ],
        "For" =>
        [
            new("from", "From", "1", "First number."),
            new("to", "To", "5", "Last number. Use either To or Count."),
            new("count", "Count", "", "How many rounds (instead of To)."),
            new("step", "Step", "1", "Added every round; may be negative."),
            new("var", "Variable name", "i", "Use it as _i_ in the steps inside."),
            new("stopwhen", "Stop when", "_i_ gte 3", "A condition checked after every round; when it holds the loop ends (like break). Build it with the Stop when… button."),
            new("skipwhen", "Skip a round when", "_i_ eq 2", "A condition checked before every round; when it holds that round is skipped (like continue)."),
            new("delayms", "Wait between rounds (ms)", "0", ""),
            new("timeoutms", "Whole loop timeout (ms)", "", "The loop fails when it runs longer than this (checked between rounds)."),
            new("onfailure", "When a step inside fails", "stop", "stop (default), break or continue."),
            new("rows", "Result rows", "yes", "yes (default): every round writes a small result row (output, index) into the results and reports. no: no rows unless the response sheet checks output or index; the values stay available to later steps."),
        ],
        "ForEach" =>
        [
            new("items", "Items", "<From_response_List.ids_r>", "A JSON array, a reference to a response list, or a comma list."),
            new("separator", "Separator", ",", "Splits a plain list."),
            new("file", "Data file", "users.csv", "Instead of Items: read the rows of a CSV, Excel (.xlsx) or JSON file. A bare name is looked up in the instance's Files folder; a full path works too (read only). Each round gets _item.<column>_ for every column."),
            new("sheet", "Sheet (Excel)", "People", "Excel only: the sheet name or its number. Empty = the first sheet."),
            new("column", "Only this column", "email", "Loop over one column: _item_ is then its plain value. Empty = all columns as _item.<column>_."),
            new("path", "Array path (JSON)", "$.data.users", "JSON only: where the list is in the file, for example $.users or $.data.items. Empty = the whole file is the list."),
            new("header", "First row has names", "yes", "yes (default) or no. With no the columns are called col1, col2, …"),
            new("delimiter", "Delimiter (CSV)", ";", "Empty = found by itself (comma, semicolon or tab). Use tab for a tab."),
            new("skip", "Skip rows", "0", "Leave out this many data rows at the start."),
            new("take", "Take rows", "10", "Only this many rows. Empty = all."),
            new("var", "Variable name", "item", "Use it as _item_ in the steps inside; with columns or fields also _item.<column>_."),
            new("index", "Index name", "index", "Counts from 0."),
            new("stopwhen", "Stop when", "_item_ eq STOP", "A condition checked after every round; when it holds the loop ends (like break). Build it with the Stop when… button."),
            new("skipwhen", "Skip a round when", "_item_ empty", "A condition checked before every round; when it holds that round is skipped (like continue)."),
            new("delayms", "Wait between rounds (ms)", "0", ""),
            new("timeoutms", "Whole loop timeout (ms)", "", "The loop fails when it runs longer than this (checked between rounds)."),
            new("onfailure", "When a step inside fails", "stop", "stop (default), break or continue."),
            new("rows", "Result rows", "yes", "yes (default): every round writes a small result row (output, index) into the results and reports. no: no rows unless the response sheet checks output or index; the values stay available to later steps."),
        ],
        _ => [],
    };

    /// <summary>The StepConfig with one key set (or taken out when the value is empty), the other keys kept. A JSON config stays JSON; a key=value config becomes JSON only when a value contains ';'.</summary>
    public static string WithKey(string? stepConfig, string key, string value)
    {
        var values = Read(stepConfig);
        var existing = values.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) values.Remove(existing);
        if (!string.IsNullOrWhiteSpace(value)) values[key] = value.Trim();   // a key that was there keeps its place only when it is set again: it moves to the end
        var json = (stepConfig ?? string.Empty).TrimStart().StartsWith('{') || values.Values.Any(v => v.Contains(';'));
        return json ? System.Text.Json.JsonSerializer.Serialize(values) : string.Join(";", values.Select(p => $"{p.Key}={p.Value}"));
    }

    /// <summary>The values already in a StepConfig (JSON or key=value text), by key.</summary>
    public static Dictionary<string, string> Read(string? stepConfig) => ControlConfig.Parse(stepConfig);

    /// <summary>The StepConfig for the given values: only filled settings, in the order of the type's fields; settings of other keys in the old text are kept.</summary>
    public static string Write(string type, IReadOnlyDictionary<string, string> values, string? oldStepConfig)
    {
        var known = For(type).Select(s => s.Key).ToList();
        var pairs = known.Where(k => values.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)).Select(k => new KeyValuePair<string, string>(k, values[k].Trim())).ToList();
        foreach (var other in Read(oldStepConfig)) if (!known.Contains(other.Key, StringComparer.OrdinalIgnoreCase)) pairs.Add(other);
        // a condition may contain ';' which would cut a key=value text in two: then the settings are written as JSON
        if (pairs.Any(p => p.Value.Contains(';'))) return System.Text.Json.JsonSerializer.Serialize(pairs.ToDictionary(p => p.Key, p => p.Value));
        return string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"));
    }
}
