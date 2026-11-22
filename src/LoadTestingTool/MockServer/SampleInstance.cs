using ClosedXML.Excel;
using LoadTestingTool.Execution;

namespace LoadTestingTool.MockServer;

/// <summary>
/// Builds the "learn" instance: one small working testcase per step type (HTTP, WCF, GraphQL, SQL on SQLite, File, Script and the
/// control steps If/While/For/ForEach), each step with a note that says what it shows. It runs against the local mock server
/// (<see cref="MockConfigFile"/>, started from the editor's Mock server window) and the sample database, so it needs no internet.
/// </summary>
public static class SampleInstance
{
    public const string TemplateName = "Sample instance (all step types, runs offline)";
    public const string MockConfigFile = "MockServer.json";
    public const string ProfileName = "AllStepsSqlite";
    public const int DefaultPort = 8099;

    private static readonly string[] Headers =
    [
        "testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb", "contenttype", "threads", "iterations", "wait",
        "expectedstatus", "stoponfailure", "enabled", "timeout", "stage", "parent", "condition", "stepconfig", "header_Accept", "header_Content-Type", "assertonlyresponse", "note"
    ];

    private sealed record Step(int Index, string Case, string Name, string Type, string Template = "", string Url = "", string Verb = "GET", string Content = "json",
        string Expected = "", string Parent = "", string Condition = "", string Config = "", string Note = "", bool Enabled = true, bool StopOnFailure = true);

    /// <summary>Writes the instance into <paramref name="folder"/> (created if needed) and returns the workbook path. <paramref name="settingsFile"/>, when given, gets the SQLite connection profile.</summary>
    public static string Build(string folder, string workbookName = "DataEngine.xlsx", int port = DefaultPort, string? settingsFile = null)
    {
        folder = Path.GetFullPath(folder);
        foreach (var sub in new[] { "Templates", "Files", "Queries", "Scripts", "Inputs", "Artifacts", "History", "Logs", "Results" }) Directory.CreateDirectory(Path.Combine(folder, sub));
        var origin = $"http://127.0.0.1:{port}";

        File.WriteAllText(Path.Combine(folder, "Templates", "order.json"), "{\n  \"customer\": \"_customer_\",\n  \"sku\": \"_sku_\",\n  \"qty\": _qty_\n}\n");
        File.WriteAllText(Path.Combine(folder, "Templates", "get-order.xml"), "<GetOrder xmlns=\"http://shop.example/\"><OrderId>_orderid_</OrderId></GetOrder>\n");
        File.WriteAllText(Path.Combine(folder, "Templates", "empty.json"), "{}\n");
        File.WriteAllText(Path.Combine(folder, "Queries", "get-order.graphql"), "query GetOrder($id: ID!) {\n  order(id: $id) { id status }\n}\n");
        File.WriteAllText(Path.Combine(folder, "Files", "input.txt"), "hello from the sample instance\n");
        File.WriteAllText(Path.Combine(folder, "Files", "payload.json"), "{\n  \"customer\": { \"id\": \"C-100\", \"name\": \"Example Customer\" }\n}\n");

        SampleDatabase.Create(Path.Combine(folder, "Files", "sample.db"));
        if (!string.IsNullOrWhiteSpace(settingsFile)) SampleDatabase.RegisterProfile(settingsFile, Path.Combine(folder, "Files", "sample.db"), ProfileName);

        var mock = MockConfig(port);
        mock.Save(Path.Combine(folder, MockConfigFile));

        var steps = Steps(origin);
        using (var workbook = new XLWorkbook())
        {
            WriteConfig(workbook, steps); WriteRequest(workbook); WriteResponse(workbook); WriteReadme(workbook, origin);
            workbook.SaveAs(Path.Combine(folder, workbookName));
        }
        File.WriteAllText(Path.Combine(folder, "README.md"),
            $"# Sample instance (all step types)\n\nEvery testcase shows one step type; the `note` column of the config sheet explains each step.\n\n1. Open the editor, press **Mock server…**, open `{MockConfigFile}` from this folder and press Run (port {port}).\n2. Run the instance. Everything passes without internet.\n\nThe SQL step uses the connection profile `{ProfileName}` (SQLite, `Files/sample.db`), registered in the central settings file when the instance is created.\n" +
            "ActionManual (testcase 10) is disabled: it waits for a person, so enable it when you want to try it.\n");
        return Path.Combine(folder, workbookName);
    }

    private static MockServerConfig MockConfig(int port) => new()
    {
        Name = "SampleInstance", Port = port,
        Routes =
        [
            new() { Name = "Get order", Method = "GET", Path = "/orders/{id}", Response = new MockResponse { Body = "{\"id\":\"{{path.id}}\",\"status\":\"CREATED\",\"total\":59.9,\"customer\":{\"id\":\"C1\",\"city\":\"Berlin\"},\"lines\":[{\"sku\":\"SKU-1\",\"qty\":2},{\"sku\":\"SKU-2\",\"qty\":1}]}" } },
            new() { Name = "Create order", Method = "POST", Path = "/orders", Response = new MockResponse { Status = 201, Body = "{\"id\":\"O-NEW\",\"customer\":\"{{json.customer}}\",\"sku\":\"{{json.sku}}\",\"qty\":{{json.qty}},\"status\":\"CREATED\"}" } },
            new() { Name = "Echo", Method = "GET", Path = "/echo/{x}", Response = new MockResponse { Body = "{\"echo\":\"{{path.x}}\"}" } },
            new() { Name = "Items", Method = "GET", Path = "/items", Response = new MockResponse { Body = "{\"id\":\"A7\",\"count\":3,\"items\":[\"a\",\"b\",\"c\"]}" } },
            new() { Name = "SOAP: missing order", Method = "POST", Path = "/Orders.svc", Conditions = [new MockCondition { Source = "xml", Name = "GetOrder.OrderId", Op = "equals", Value = "NOPE" }],
                Response = new MockResponse { Status = 500, ContentType = "text/xml; charset=utf-8", Body = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>Order {{xml.GetOrder.OrderId}} not found</faultstring></s:Fault></s:Body></s:Envelope>" } },
            new() { Name = "SOAP: GetOrder", Method = "POST", Path = "/Orders.svc", Conditions = [new MockCondition { Source = "soap", Name = "operation", Op = "equals", Value = "GetOrder" }],
                Response = new MockResponse { ContentType = "text/xml; charset=utf-8", Body = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><GetOrderResponse xmlns=\"http://shop.example/\"><Id>{{xml.GetOrder.OrderId}}</Id><Status>SHIPPED</Status></GetOrderResponse></s:Body></s:Envelope>" } },
            new() { Name = "GraphQL: GetOrder", Method = "POST", Path = "/graphql", Conditions = [new MockCondition { Source = "graphql", Name = "operation", Op = "equals", Value = "GetOrder" }],
                Response = new MockResponse { Body = "{\"data\":{\"order\":{\"id\":\"{{json.variables.id}}\",\"status\":\"PAID\"}}}" } },
            new() { Name = "Poll", Method = "GET", Path = "/slow", Response = new MockResponse { Body = "{\"state\":\"DONE\"}", DelayMs = 150 } },
        ]
    };

    private static List<Step> Steps(string o)
    {
        var s = new List<Step>();
        // 1 HTTP
        s.Add(new(1, "Http", "GetOrder", "HTTP", "empty.json", o + "/orders/<From_request_orderid_r>", Expected: "200", Note: "HTTP GET. The URL takes the order id from this row's request data (<From_request_orderid_r>). Asserted by the response sheet."));
        s.Add(new(1, "Http", "CreateOrder", "HTTP", "order.json", o + "/orders", "POST", Expected: "201", Note: "HTTP POST with a template: order.json, where _customer_, _sku_ and _qty_ are filled from the request sheet. The mock server answers 201."));
        // 2 chaining
        s.Add(new(2, "Chaining", "Fetch", "HTTP", "empty.json", o + "/items", Config: "{\"outputs\":\"firstId=id,cnt=count\"}", Note: "Outputs: the stepconfig keeps values of this response as variables (_firstId_, _cnt_) for later steps."));
        s.Add(new(2, "Chaining", "UseOutput", "HTTP", "empty.json", o + "/echo/_firstId_?count=_cnt_", Note: "Uses the variables the previous step kept. Another way: <From_response_Fetch.id_r> reads straight from that step's response."));
        // 3 WCF
        s.Add(new(3, "Wcf", "GetOrderSoap", "WCF", "get-order.xml", o + "/Orders.svc", "POST", "xml", Config: "{\"SoapAction\":\"http://shop.example/IOrders/GetOrder\"}", Note: "WCF/SOAP. The template is the Body content; the runner wraps it in an envelope and sends the SOAPAction. Response fields are asserted as GetOrderResponse.Id and so on."));
        s.Add(new(3, "Wcf", "GetMissingSoap", "WCF", "get-order.xml", o + "/Orders.svc", "POST", "xml", Expected: "500", Config: "{\"SoapAction\":\"http://shop.example/IOrders/GetOrder\"}", Note: "A SOAP Fault is an expected result here (expectedstatus 500). Its own request column (GetMissingSoap.orderid = NOPE) makes the mock server answer with a fault; its text is asserted."));
        // 4 GraphQL
        s.Add(new(4, "GraphQl", "GetOrderGql", "GraphQL", Url: o + "/graphql", Verb: "POST", Config: "{\"Source\":\"Queries/get-order.graphql\",\"OperationName\":\"GetOrder\",\"Variables\":{\"id\":\"_orderid_\"}}", Note: "GraphQL. The query is read from Queries/get-order.graphql, variables come from the request sheet. Asserted as data.order.status."));
        // 5 SQL
        s.Add(new(5, "Sql", "OrdersOfCustomer", "SQL", Config: "{\"ConnectionProfile\":\"" + ProfileName + "\",\"CommandText\":\"select id, status, total from orders where customer_id = @c order by id\",\"Parameters\":{\"c\":\"_customer_\"},\"Outputs\":{\"firstOrder\":\"id\"}}", Note: "SQL on SQLite (Files/sample.db). @c is a parameter filled from the request sheet; Outputs keeps the first order id as _firstOrder_. #count asserts the number of rows."));
        s.Add(new(5, "Sql", "UseDbValue", "HTTP", "empty.json", o + "/orders/_firstOrder_", Note: "Takes the value the SQL step kept and uses it in an HTTP call: the usual way to mix database data and API calls."));
        // 6 File
        s.Add(new(6, "File", "WriteText", "File", Config: "{\"Action\":\"Write\",\"Path\":\"Artifacts/sample-output.txt\",\"Content\":\"first line\\n\"}", Note: "File Write: creates Artifacts/sample-output.txt."));
        s.Add(new(6, "File", "AppendText", "File", Config: "{\"Action\":\"Append\",\"Path\":\"Artifacts/sample-output.txt\",\"Content\":\"second line\\n\"}", Note: "File Append: adds a line."));
        s.Add(new(6, "File", "ReadText", "File", Config: "{\"Action\":\"Read\",\"Path\":\"Artifacts/sample-output.txt\",\"OutputVariable\":\"textData\"}", Note: "File Read: puts the content in the variable _textData_."));
        s.Add(new(6, "File", "ReadJson", "File", Config: "{\"Action\":\"Json\",\"Path\":\"Files/payload.json\",\"OutputVariable\":\"customerId\",\"JsonPath\":\"$.customer.id\"}", Note: "File Json: reads one value of a JSON file with a JsonPath into _customerId_."));
        s.Add(new(6, "File", "UseFileValue", "HTTP", "empty.json", o + "/echo/_customerId_", Note: "Uses the value read from the file."));
        // 7 Script
        s.Add(new(7, "Script", "Calc", "Script", Config: "{\"Inline\":\"var total = 2 * 21; return new { total, label = \\\"answer\\\" };\"}", Note: "C# script. The returned object is the response, so total and label can be asserted like any response field."));
        // 8 If
        s.Add(new(8, "If", "Fetch", "HTTP", "empty.json", o + "/items", Note: "Control steps: first an ordinary call whose response the If looks at."));
        s.Add(new(8, "If", "Check", "If", Condition: "<From_response_Fetch.count_r> gt 2", Note: "If: the steps with parent 'Check' run when the condition is true, the ones with parent 'Check:else' when it is false."));
        s.Add(new(8, "If", "ThenPath", "HTTP", "empty.json", o + "/echo/then", Parent: "Check", Note: "Runs: count is 3."));
        s.Add(new(8, "If", "ElsePath", "HTTP", "empty.json", o + "/echo/else", Parent: "Check:else", Note: "Skipped: this is the else branch."));
        // 9 loops
        s.Add(new(9, "Loops", "Fetch", "HTTP", "empty.json", o + "/items", Note: "An ordinary call first: the ForEach below loops over the array in its response."));
        s.Add(new(9, "Loops", "Count", "For", Config: "from=1;to=5;stopwhen=_i_ eq 3", Note: "For: _i_ runs 1..5 and stops when _i_ is 3, so two child calls are sent."));
        s.Add(new(9, "Loops", "EachCount", "HTTP", "empty.json", o + "/echo/count-_i_", Parent: "Count", Note: "Child of the For loop (parent = Count)."));
        s.Add(new(9, "Loops", "Each", "ForEach", Config: "items=<From_response_Fetch.items_r>;var=item", Note: "ForEach over the array of the Fetch response (a, b, c)."));
        s.Add(new(9, "Loops", "EachItem", "HTTP", "empty.json", o + "/echo/item-_item_", Parent: "Each", Note: "Child of the ForEach: one call per item."));
        s.Add(new(9, "Loops", "Wait", "While", Condition: "<From_response_Poll.state_r> ne DONE", Config: "max=5;delayms=50", Note: "While: repeats its children while the condition holds. The poll answers DONE on the first pass, so the loop ends after one round; max stops runaway loops."));
        s.Add(new(9, "Loops", "Poll", "HTTP", "empty.json", o + "/slow", Parent: "Wait", Note: "Child of the While loop."));
        // 10 manual
        s.Add(new(10, "Manual", "AskPerson", "ActionManual", Config: "", Enabled: false, Note: "ActionManual waits for a person to confirm something. Disabled so unattended runs do not stop; enable it to try."));
        return s;
    }

    private static void WriteConfig(XLWorkbook w, List<Step> steps)
    {
        var sheet = w.Worksheets.Add("config");
        for (var c = 0; c < Headers.Length; c++) sheet.Cell(1, c + 1).Value = Headers[c];
        var row = 2;
        foreach (var st in steps)
        {
            object[] values = [st.Index, st.Case, st.Name, st.Type, st.Template, st.Url, st.Verb, st.Content, 1, 1, 0, st.Expected, st.StopOnFailure, st.Enabled, 30, "Test", st.Parent, st.Condition, st.Config, "application/json", "application/json", true, st.Note];
            for (var c = 0; c < values.Length; c++)
            {
                if (values[c] is string text && text.Length == 0) continue;
                sheet.Cell(row, c + 1).Value = XLCellValue.FromObject(values[c]);
            }
            row++;
        }
        Style(sheet, Headers.Length);
    }

    private static void WriteRequest(XLWorkbook w)
    {
        var s = w.Worksheets.Add("request");
        string[] h = ["testcaseindex", "testcase", "dataid", "orderid", "customer", "sku", "qty", "GetMissingSoap.orderid"];
        for (var c = 0; c < h.Length; c++) s.Cell(1, c + 1).Value = h[c];
        object[][] rows =
        [
            [1, "Http", "H1", "O1", "C1", "SKU-1", 2], [1, "Http", "H2", "O2", "C2", "SKU-9", 5],
            [2, "Chaining", "C1", null!, null!, null!, null!],
            [3, "Wcf", "W1", "O42", null!, null!, null!, "NOPE"],
            [4, "GraphQl", "G1", "O7", null!, null!, null!],
            [5, "Sql", "S1", null!, "C1", null!, null!],
            [6, "File", "F1", null!, null!, null!, null!],
            [7, "Script", "X1", null!, null!, null!, null!],
            [8, "If", "I1", null!, null!, null!, null!],
            [9, "Loops", "L1", null!, null!, null!, null!],
            [10, "Manual", "M1", null!, null!, null!, null!],
        ];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) if (rows[r][c] is { } v) s.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(v);
        Style(s, h.Length);
    }

    private static void WriteResponse(XLWorkbook w)
    {
        var s = w.Worksheets.Add("response");
        string[] h =
        [
            "testcaseindex", "testcase", "dataid",
            "{eq}GetOrder.statusCode", "{eq}GetOrder.status", "{gte}GetOrder.lines.#count", "{eq}GetOrder.customer.city", "{eq}CreateOrder.statusCode", "{eq}CreateOrder.sku",
            "{eq}GetOrderSoap.GetOrderResponse.Status", "{eq}GetOrderSoap.GetOrderResponse.Id", "{eq}GetMissingSoap.statusCode", "{eq}GetMissingSoap.Fault.faultstring",
            "{eq}GetOrderGql.data.order.status", "{eq}GetOrderGql.data.order.id",
            "{eq}OrdersOfCustomer.#count", "{eq}Calc.total", "{eq}Calc.label"
        ];
        for (var c = 0; c < h.Length; c++) s.Cell(1, c + 1).Value = h[c];
        void Put(int row, params (string header, object value)[] cells)
        {
            foreach (var (header, value) in cells) s.Cell(row, Array.IndexOf(h, header) + 1).Value = XLCellValue.FromObject(value);
        }
        s.Cell(2, 1).Value = 1; s.Cell(2, 2).Value = "Http"; s.Cell(2, 3).Value = "H1";
        Put(2, ("{eq}GetOrder.statusCode", 200), ("{eq}GetOrder.status", "CREATED"), ("{gte}GetOrder.lines.#count", 2), ("{eq}GetOrder.customer.city", "Berlin"), ("{eq}CreateOrder.statusCode", 201), ("{eq}CreateOrder.sku", "SKU-1"));
        s.Cell(3, 1).Value = 1; s.Cell(3, 2).Value = "Http"; s.Cell(3, 3).Value = "H2";
        Put(3, ("{eq}GetOrder.statusCode", 200), ("{eq}GetOrder.status", "CREATED"), ("{gte}GetOrder.lines.#count", 2), ("{eq}GetOrder.customer.city", "Berlin"), ("{eq}CreateOrder.statusCode", 201), ("{eq}CreateOrder.sku", "SKU-9"));
        s.Cell(4, 1).Value = 3; s.Cell(4, 2).Value = "Wcf"; s.Cell(4, 3).Value = "W1";
        Put(4, ("{eq}GetOrderSoap.GetOrderResponse.Status", "SHIPPED"), ("{eq}GetOrderSoap.GetOrderResponse.Id", "O42"), ("{eq}GetMissingSoap.statusCode", 500), ("{eq}GetMissingSoap.Fault.faultstring", "Order NOPE not found"));
        s.Cell(5, 1).Value = 4; s.Cell(5, 2).Value = "GraphQl"; s.Cell(5, 3).Value = "G1";
        Put(5, ("{eq}GetOrderGql.data.order.status", "PAID"), ("{eq}GetOrderGql.data.order.id", "O7"));
        s.Cell(6, 1).Value = 5; s.Cell(6, 2).Value = "Sql"; s.Cell(6, 3).Value = "S1";
        Put(6, ("{eq}OrdersOfCustomer.#count", 2));
        s.Cell(7, 1).Value = 7; s.Cell(7, 2).Value = "Script"; s.Cell(7, 3).Value = "X1";
        Put(7, ("{eq}Calc.total", 42), ("{eq}Calc.label", "answer"));
        foreach (var (index, name, id) in new[] { (2, "Chaining", "C1"), (6, "File", "F1"), (8, "If", "I1"), (9, "Loops", "L1"), (10, "Manual", "M1") })
        {
            var row = s.LastRowUsed()!.RowNumber() + 1;
            s.Cell(row, 1).Value = index; s.Cell(row, 2).Value = name; s.Cell(row, 3).Value = id;
        }
        Style(s, h.Length);
    }

    private static void WriteReadme(XLWorkbook w, string origin)
    {
        var s = w.Worksheets.Add("README");
        string[][] rows =
        [
            ["Sample instance", "One working testcase per step type; read the note column of the config sheet."],
            ["Before you run", $"Start the mock server (editor: Mock server… > open {MockConfigFile} > Run). It listens on {origin}."],
            ["1 Http", "GET and POST, request data, templates, expected columns, #count"],
            ["2 Chaining", "Outputs and <From_response_…_r>"],
            ["3 Wcf", "SOAP envelope, SOAPAction, fault as expected result"],
            ["4 GraphQl", "Query file, variables, one /graphql path answering by operation"],
            ["5 Sql", "SQLite sample database, parameters, outputs"],
            ["6 File", "Write, Append, Read, Json"],
            ["7 Script", "C# script returning an object"],
            ["8 If, 9 Loops", "If/else, For, ForEach, While"],
            ["10 Manual", "ActionManual (disabled)"],
        ];
        for (var r = 0; r < rows.Length; r++) { s.Cell(r + 1, 1).Value = rows[r][0]; s.Cell(r + 1, 2).Value = rows[r][1]; }
        s.Column(1).Width = 20; s.Column(2).Width = 110;
    }

    private static void Style(IXLWorksheet sheet, int columns)
    {
        var header = sheet.Range(1, 1, 1, columns);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9EAF7");
        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, columns).AdjustToContents(1, 30);
    }
}
