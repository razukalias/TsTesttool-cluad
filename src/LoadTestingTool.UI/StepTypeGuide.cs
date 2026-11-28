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

        ["wcf"] = new("WCF step (SOAP)",
        [
            "Target URL: the service endpoint, e.g. http://host/Orders.svc. The request is always POST.",
            "Template: an XML file in the Templates folder with the operation element, e.g. <GetOrder xmlns=\"http://tempuri.org/\"><OrderId>_orderId_</OrderId></GetOrder>. It is wrapped in a SOAP envelope for you; a template that already is an <Envelope> is sent as it is. _name_ placeholders are filled from the data row and escaped for XML.",
            "SOAP action: the operation's action (sent as the SOAPAction header for SOAP 1.1, as action= in the content type for 1.2). SOAP version: 1.1 (basicHttpBinding) or 1.2.",
            "User name / Password: optional, adds a WS-Security UsernameToken with a plain-text password to the header. Extra header XML goes in the SOAP header too.",
            "Expected response values: paths start at the body's first element, e.g. GetOrderResponse.Status or GetOrderResponse.Lines.Line[1].@sku; header elements are Header.<name>; a fault is Fault.faultstring (Fault.Reason.Text in SOAP 1.2). statusCode and response headers work as for HTTP.",
            "A SOAP fault fails the step with the fault text. For a negative test set Expected status to 500 and assert on Fault.faultstring.",
            "Only SOAP over HTTP(S) with basicHttp or wsHttp without message security is supported (no net.tcp, no WS-Security signing).",
        ], "{\n  \"SoapAction\": \"http://tempuri.org/IOrders/GetOrder\",\n  \"SoapVersion\": \"1.1\"\n}", "Use the Mock server window's SOAP button to get a service to call."),

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
        ], "{\n  \"ConnectionProfile\": \"MainDb\",\n  \"CommandType\": \"Text\",\n  \"CommandText\": \"select Id, Email from Users where Id = @id\",\n  \"Parameters\": { \"id\": \"_userId_\" }\n}", "MainDb must exist under Connections in appsettings.json. For SQL Server leave Provider out; for a SQLite file give the profile \"Provider\": \"Sqlite\" and a connection string like Data Source=Files/my.db (the Mock server window's \"Sample SQL database\" button creates a ready profile SampleSqlite)."),

        ["file"] = new("File step",
        [
            "Action: Read (text), Json (one value of a JSON file), ReadBinary (a file reference that streams, nothing is loaded into memory), Write, Append, WriteBinary or AppendBinary.",
            "File comes from: Fixed path = one file for every row. The data row = every row names its own file in the request column <Step>.file. A bare name (data.json) is read from the instance's Files folder; reading also works with a full path anywhere (C:\\Data\\a.pdf). Writing stays inside the workspace.",
            "Content: only for Write/Append (text) and WriteBinary/AppendBinary (Base64 or a file reference such as _payload_ where the column payload holds <From_response_ReadPdf.output_r>).",
            "Output: every File step answers to the constant name <Step>.output. Example: step ReadPdf (ReadBinary) -> later steps use <From_response_ReadPdf.output_r>; to check it, add the expected-value column {eq}ReadPdf.output in the response sheet (it is added when the step is created).",
            "Sending a file with an HTTP step: give that step a request column, e.g. payload = <From_response_ReadPdf.output_r>, and write \"data\": \"_payload_\" in the template (JSON body: the file is streamed as Base64) or use the column as the Path of a multipart file part. No stars needed. The file must come from a File step of the same run.",
            "Example: ReadPdf (ReadBinary, File = invoice.pdf) -> Upload (HTTP POST, column payload = <From_response_ReadPdf.output_r>, template {\"name\":\"invoice\",\"data\":\"_payload_\"}) -> Archive (WriteBinary, File = Files/out/invoice.pdf, Content = _payload_).",
            "In any request cell you can also write <from_filepath>name (text), <from_filepath_base64>name, <from_filepath_binary>name (streams) or <from_filepath_json>data.json|$.user.id (one value of a JSON file) without a File step; the wizard's File menu inserts them.",
        ], "{\n  \"Action\": \"ReadBinary\",\n  \"Path\": \"invoice.pdf\"\n}", "Then use <From_response_<StepName>.output_r> in later steps. The older OutputVariable setting still works."),

        ["script"] = new("Script step",
        [
            "Runs a C# script. Trusted scripts must be enabled in the instance settings, otherwise the step fails.",
            "Script: use the Template, a Script file in the workspace, or type the code in the Inline box. The file wins over the template, which wins over the inline code.",
            "Add variable…: adds a line such as  var orderId = \"_orderId_\";  at the top of the script. Pick a request column or a value of an earlier step and choose the C# name; the value always arrives as a string (quotes and line breaks are escaped for you). Use int.Parse(orderId) when you need a number.",
            "Output: the returned value is <Step>.output for later steps (<From_response_Calc.output_r>) and can be checked with the response column {eq}Calc.output. A returned object or list can be read by path, e.g. Calc.total.",
            "Example: var qty = \"_qty_\"; return int.Parse(qty) * 2;  with a request column Calc.qty = 21 gives Calc.output = 42.",
        ], "{\n  \"Inline\": \"var qty = \\\"_qty_\\\";\\nreturn int.Parse(qty) * 2;\"\n}"),

        ["if"] = new("If step",
        [
            "Runs the steps inside it only when the condition holds; the Else side runs when it does not.",
            "Condition: one or more comparisons, e.g. _status_ eq 200, \"<From_response_Login.role_r>\" ne admin, _country_ eq SE and not _skip_ eq yes. Build it row by row with Build condition…",
            "A value can come from the data row: the column Gate.who is written as _who_ in the condition.",
            "Output: <From_response_Gate.output_r> is true or false for later steps; the optional column {eq}Gate.output checks it (button Add output column).",
        ], null),

        ["for"] = new("For step",
        [
            "Repeats the steps inside it with a counter. From and To (or Count) say how many rounds; Step may be negative to count down; Variable name is the counter (default i, used as _i_).",
            "Examples: From 1, To 5 -> 1 2 3 4 5.   From 10, To 1, Step -3 -> 10 7 4 1.   Count 3 -> three rounds.   To = _rounds_ -> the column Loop.rounds of each data row decides.",
            "Stop when / Skip a round when: conditions such as _i_ gte 3 (like break / continue). Most rounds is capped by Max (default 10,000).",
            "Output: <From_response_Loop.output_r> is the counter value and <From_response_Loop.index_r> the round counted from 0; the columns {eq}Loop.output / {eq}Loop.index check them every round (button Add output and index columns). Result rows can be switched off with Result rows = no.",
        ], "from=1;to=3;var=i"),

        ["foreach"] = new("ForEach step",
        [
            "Repeats the steps inside it once per item. Items can be a comma list (a,b,c), a JSON array, a reference to a response list (<From_response_List.ids_r>) or a request column (_ids_).",
            "The item is _item_ (change it with Variable name) and its position _index_ counted from 0.",
            "Examples: Items a,b,c -> three rounds.   Items <From_response_Search.ids_r> -> one round per id the search returned.   Skip a round when: _item_ empty.",
            "Output: <From_response_Each.output_r> is the item and <From_response_Each.index_r> its position.",
        ], "items=a,b,c;var=item"),

        ["while"] = new("While step",
        [
            "Repeats the steps inside it while the Condition holds, for example polling until a job is done. The condition is checked before every round.",
            "Most rounds (default 100) protects against endless loops; When the limit is reached = fail (default) or continue. Wait between rounds (ms) pauses, e.g. 2000 while polling. Most rounds can come from a column: _tries_.",
            "Example: Condition \"<From_response_Poll.state_r>\" ne DONE, Most rounds 10, Wait 2000 -> polls every 2 s, at most 10 times.",
            "Output: <From_response_Wait.output_r> is the round number and <From_response_Wait.index_r> the round from 0.",
        ], "max=10;delayms=2000"),

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
