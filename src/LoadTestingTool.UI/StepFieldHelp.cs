using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

/// <summary>One list of step field names and what each means, reused by every step form (and anywhere else a column needs explaining).</summary>
public static class StepFieldHelp
{
    private static readonly IReadOnlyDictionary<string, (string Label, string Text)> Fields = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["stepname"] = ("Name", "The name of the step. Data and response columns refer to it as StepName.column, so renaming it renames those references too."),
        ["steptype"] = ("Type", "What the step does: HTTP, GraphQL, CaseQL, SQL, File, Script, ActionManual (pauses the row until a person continues), or a control step that holds other steps: If (condition, optional else), While (repeats while a condition holds; max, delayms, onlimit), For (from, to or count, step, var) and ForEach (items, var, index, separator). Control settings go in Step config as JSON or key=value;key=value; onfailure=stop|break|continue decides what a failed step inside a loop does."),
        ["stage"] = ("Stage", "Setup runs first, Test is the main part, Teardown runs last (also after a failure) to clean up."),
        ["enabled"] = ("Enabled", "Untick to skip the step without deleting it."),
        ["stoponfailure"] = ("Stop on failure", "When ticked, a failing step stops the rest of the testcase for that data row."),
        ["verb"] = ("Verb", "The HTTP method: GET, POST, PUT, PATCH, DELETE, HEAD or OPTIONS."),
        ["targeturl"] = ("Target URL", "Where the request is sent. May contain _variable_ placeholders and <From_...> references, e.g. https://host/orders/_orderId_."),
        ["contenttype"] = ("Content type", "The Content-Type header of the body, e.g. application/json. Choose multipart/form-data to build a multipart body in Step config."),
        ["templetsource"] = ("Template", "The file in the instance's Templates folder that holds the request body (or the SQL, script or file text). Its _name_ placeholders are filled from the data row."),
        ["expectedstatus"] = ("Expected status", "The HTTP status the step must return, e.g. 201. Leave empty to accept any 2xx."),
        ["timeout"] = ("Timeout (s)", "How many seconds the step may take before it is stopped and counted as failed."),
        ["responsetimeout"] = ("Response timeout (ms)", "How many milliseconds to wait for the response (http, graphql, caseql). When it passes, the app stops waiting, marks the step failed with \"timed out after N ms\" and runs the next step; the server may keep processing the request. Overrides Timeout (s) for the response wait; empty = use Timeout (s)."),
        ["wait"] = ("Wait before (ms or message)", "A pause before the step: a number of milliseconds (500), a row variable (_waitMs_), or any other text, which pauses the row in the ActionManual panel and shows the text."),
        ["assert"] = ("Assertions", "Check the response against the expected cells of the response sheet. Untick to run the step without checking."),
        ["ignoreempty"] = ("Ignore empty", "Skip expected cells that are empty instead of treating them as 'must be empty'."),
        ["assertonlyresponse"] = ("Only response", "Check only the response fields that have an expected column; unexpected fields are not reported."),
        ["manualtimeoutminutes"] = ("Manual timeout (min)", "How many minutes an ActionManual step waits for a person to continue before it is stopped and counted as failed. Default 15."),
        ["parent"] = ("Inside (parent)", "The name of an If, While, For or ForEach step this step runs inside; leave empty for a top-level step. For the else branch of an If write IfName:else."),
        ["condition"] = ("Condition", "left verb right, with the same verbs as assertions: _status_ eq 200, <From_response_Poll.state_r> ne DONE, _name_ notempty. Quote values with spaces. A single word is true when it is true, 1 or yes."),
        ["stepconfig"] = ("Step config", "Extra settings of the step type as JSON, e.g. the SQL connection, the script source or the multipart parts. Use the guided window when unsure."),
    };

    /// <summary>The label to show for a column header, or null when it is not a step field.</summary>
    public static string? LabelFor(string header) => Fields.TryGetValue(header, out var field) ? field.Label : null;

    /// <summary>The explanation of a step field, or null.</summary>
    public static string? TextFor(string header) => Fields.TryGetValue(header, out var field) ? field.Text : null;

    // The older wizard words its labels differently; this maps them to the same explanations.
    private static readonly IReadOnlyDictionary<string, string> WizardLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Step name"] = "stepname", ["Step type"] = "steptype", ["Stage"] = "stage", ["Timeout (seconds)"] = "timeout", ["Wait (ms, _variable_, or manual text)"] = "wait",
        ["Target URL"] = "targeturl", ["HTTP verb"] = "verb", ["Template source"] = "templetsource", ["Content type"] = "contenttype", ["Expected status"] = "expectedstatus",
        ["Manual timeout (minutes)"] = "manualtimeoutminutes", ["Advanced StepConfig JSON"] = "stepconfig",
    };

    public static string? TextForLabel(string label) => WizardLabels.TryGetValue(label, out var key) ? TextFor(key) : null;

    /// <summary>A small (i) button: the explanation shows as a tooltip on hover and in a flyout on click, Enter or Space.</summary>
    public static Control? Icon(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var button = new Button
        {
            Content = "ⓘ", Padding = new Thickness(3, 0), Margin = new Thickness(4, 0, 0, 0), MinHeight = 0, MinWidth = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Brush.Parse("#6FA8DC"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Focusable = true, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        ToolTip.SetTip(button, text);
        button.Flyout = new Flyout { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 } };
        Avalonia.Automation.AutomationProperties.SetName(button, "About this field");
        return button;
    }

    /// <summary>A label with its info icon after it.</summary>
    public static Control Labelled(Control label, string? text)
        => Icon(text) is { } icon ? new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { label, icon } } : label;
}
