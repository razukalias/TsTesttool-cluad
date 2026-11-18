using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

/// <summary>How to fill in a step of each type: the fields that matter, what goes where, and a working example of the Step config.</summary>
public static class StepTypeGuide
{
    public sealed record Guide(string Title, string[] Steps, string? ExampleConfig, string? ExampleNote = null);

    private static readonly IReadOnlyDictionary<string, Guide> Guides = new Dictionary<string, Guide>(StringComparer.OrdinalIgnoreCase)
    {
        ["http"] = new("HTTP step",
        [
            "Verb: GET, POST, PUT, PATCH or DELETE. Target URL: the full address; it may hold _variable_ placeholders (a column of this step in the request sheet) and <From_response_Step.path> references to an earlier step.",
            "Template: the file in the Templates folder with the request body, e.g. create.json. Leave it empty for GET and DELETE. Its _name_ placeholders are filled from the data row.",
            "Body format (content type): application/json for JSON. Choose multipart/form-data to define form parts below.",
            "Expected status: the status the step must return, e.g. 201. Empty accepts any 2xx.",
            "Step config is optional. Use it for extra request headers.",
        ], "{\n  \"Headers\": {\n    \"Authorization\": \"Bearer _token_\"\n  }\n}", "Adds an Authorization header; _token_ is read from the data row."),

        ["graphql"] = new("GraphQL step",
        [
            "Target URL: the GraphQL endpoint. Verb is normally POST.",
            "Query: use the Template, a Query file, or type it in the Query box. If several are set, the Query file wins over the Template, which wins over the Query box.",
            "Variables: a JSON object sent with the query. Use _*column*_ to read a request column as a typed value (numbers stay numbers).",
            "Operation name is only needed when the query holds several operations.",
        ], "{\n  \"OperationName\": \"GetUser\",\n  \"Inline\": \"query GetUser($id: ID!) { user(id: $id) { id name } }\",\n  \"Variables\": { \"id\": \"_*userId*_\" }\n}"),

        ["caseql"] = new("CaseQL step",
        [
            "Works like a GraphQL step: Target URL is the endpoint, the query comes from the Template, a Query file or the Query box.",
            "Variables is a JSON object and may use _*column*_ to read a request column.",
        ], "{\n  \"Inline\": \"query { cases { id status } }\",\n  \"Variables\": {}\n}"),

        ["sql"] = new("SQL step",
        [
            "Connection: the name of a connection profile defined in appsettings.json (not the connection string itself).",
            "Command type: Text for a normal statement, StoredProcedure to call a procedure.",
            "Command: type it in the Command box, or use the Template / a Command file (the file overrides the template). Write parameters as @name.",
            "Parameters: a JSON object with a value per @name; values may use _column_ placeholders.",
            "Outputs: copies result values into variables for later steps, as output name → variable name.",
        ], "{\n  \"ConnectionProfile\": \"MainDb\",\n  \"CommandType\": \"Text\",\n  \"CommandText\": \"select Id, Email from Users where Id = @id\",\n  \"Parameters\": { \"id\": \"_userId_\" }\n}", "MainDb must exist under Connections in appsettings.json."),

        ["file"] = new("File step",
        [
            "Action: Read, Json (read one value from a JSON file), ReadBinary, Write, Append, WriteBinary or AppendBinary.",
            "File: a path inside the workspace, e.g. Files/data.json. A path outside it is refused.",
            "Content: only for Write and Append. It may hold _variable_ placeholders.",
            "Output variable: the name later steps use to read the result, e.g. <From_response_...> or _fileContent_.",
            "Output mode: Reference keeps big binary files out of memory; Value or Base64 puts the content itself in the variable.",
        ], "{\n  \"Action\": \"Read\",\n  \"Path\": \"Files/payload.json\",\n  \"OutputVariable\": \"payload\"\n}"),

        ["script"] = new("Script step",
        [
            "Runs a C# script. Trusted scripts must be enabled in the instance settings, otherwise the step fails.",
            "Script: use the Template, a Script file in the workspace, or type the code in the Inline box. The file wins over the template, which wins over the inline code.",
            "The script can read the row's data as _name_ placeholders and return a value for later steps.",
        ], "{\n  \"Inline\": \"return _data_;\"\n}"),

        ["actionmanual"] = new("Action manual step",
        [
            "Pauses the row until a person confirms the manual action in the Action Manual tab of the Overview.",
            "Wait before (ms or message): the text a person sees. Use a message such as \"Approve the payment in the bank portal\".",
            "Manual timeout (min): how long to wait before the step fails. Default 15.",
            "There is no Step config for this type.",
        ], null),
    };

    public static Guide? For(string type) => Guides.TryGetValue((type ?? string.Empty).Trim(), out var guide) ? guide : null;

    /// <summary>
    /// A small ⓘ button for a step type's settings header: a flyout with the steps to fill it in and, where the type has a Step config, a working example.
    /// "Use this example" fills the Step config, but only while it is empty (it never overwrites what is there).
    /// </summary>
    public static Control? Icon(string type, Func<string> currentConfig, Action<string> useExample)
    {
        if (For(type) is not { } guide) return null;
        var body = new StackPanel { Spacing = 6, MaxWidth = 430 };
        body.Children.Add(new TextBlock { Text = $"How to fill a {guide.Title.ToLowerInvariant()}", FontWeight = FontWeight.Bold });
        for (var i = 0; i < guide.Steps.Length; i++)
            body.Children.Add(new TextBlock { Text = $"{i + 1}.  {guide.Steps[i]}", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        if (guide.ExampleConfig is { } example)
        {
            body.Children.Add(new TextBlock { Text = "Example Step config", FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) });
            body.Children.Add(new Border { Background = Brush.Parse("#0E1B2B"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8),
                Child = new TextBlock { Text = example, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap } });
            if (guide.ExampleNote is { } note) body.Children.Add(new TextBlock { Text = note, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            var use = new Button { Content = "Use this example", Tag = "use-example", HorizontalAlignment = HorizontalAlignment.Left };
            var status = new TextBlock { FontSize = 11, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
            use.Click += (_, _) =>
            {
                if (currentConfig().Trim().Length > 0) { status.Text = "Step config already has content, so it was left alone. Clear it first to use the example."; return; }
                useExample(example);
                status.Text = "Step config filled in. Change the values to match your step.";
            };
            body.Children.Add(use);
            body.Children.Add(status);
        }
        var button = new Button
        {
            Content = "ⓘ", Tag = "type-guide", Padding = new Thickness(4, 0), Margin = new Thickness(6, 0, 0, 0), MinHeight = 0, MinWidth = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Brush.Parse("#6FA8DC"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        ToolTip.SetTip(button, $"How to fill a {guide.Title.ToLowerInvariant()}");
        button.Flyout = new Flyout { Content = new ScrollViewer { Content = body, MaxHeight = 520 } };
        Avalonia.Automation.AutomationProperties.SetName(button, "How to fill this step type");
        return button;
    }
}
