using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class StepLogViewTests
{
    private const string P = "2026-11-17T19:58:24.3364605+01:00 [INFO] [FileHttpScenarios] [run=20261117_195803_3d2dd134c] ";
    private const string W = "2026-11-17T19:58:24.5000000+01:00 [WARN] [FileHttpScenarios] [run=20261117_195803_3d2dd134c] ";
    private const string E = "2026-11-17T19:58:24.6000000+01:00 [ERROR] [FileHttpScenarios] [run=20261117_195803_3d2dd134c] ";

    private static readonly string FileStep = string.Join('\n',
        P + "---- STEP 1: ReadBinary | type=File | dataid=B1 | iteration=1 ----",
        P + "STEP TEMPLATE LOAD [STEP-00000001] testcase=1; testcaseName=BinaryCopyFlow; dataid=B1; stepName=ReadBinary; stepType=file; environment=staging; template=C:\\x\\Templates\\read.json; templateExists=True; configuredTargetUrl=; resolvedTargetUrl=; ignoreempty=False; stepConfig={\"action\":\"read\"}",
        P + "STEP RESOLUTION [STEP-00000001]",
        "STEP TYPE: File",
        "ORIGINAL TARGET: ",
        "RESOLVED TARGET: ",
        "ORIGINAL TEMPLATE:",
        "{\"path\":\"_filename_\"}",
        "RESOLVED TEMPLATE:",
        "{\"path\":\"upload.bin\"}",
        "VARIABLES:",
        "env=staging",
        "filename=upload.bin",
        P + "STEP REQUEST [STEP-00000001]",
        "TYPE: File",
        "STEP: ReadBinary",
        "ENVIRONMENT: staging",
        "TARGET: ",
        "METHOD: readbinary",
        "TEMPLATE: read.json",
        "HEADERS:",
        "",
        "DETAILS:",
        "path=C:\\x\\upload.bin",
        "stepConfig={\"action\":\"read\"}",
        "BODY:",
        "[binary content omitted]",
        P + "STEP RESPONSE [STEP-00000001]",
        "STATUS: 200",
        "HEADERS:",
        "",
        "BODY:",
        "{\"size\":5,\"sha256\":\"abc\"}",
        P + "STEP COMPLETED [STEP-00000001] testcase=1; testcaseName=BinaryCopyFlow; dataid=B1; stepName=ReadBinary; stepType=file; result=PASS; status=200; durationMs=2");

    private static readonly string HttpStep = string.Join('\n',
        P + "---- STEP 1: Create | type=HTTP | dataid=D1 | iteration=1 ----",
        P + "Request starting. RequestId=REQ-00000007; testcase=1; dataid=D1; stepName=Create; thread=1; iteration=1; verb=POST; url=http://h/<x>; template=C:\\t\\c.json; ignoreempty=False",
        P + "STEP RESOLUTION [REQ-00000007]",
        "STEP TYPE: HTTP",
        "ORIGINAL TARGET: http://h/_id_",
        "RESOLVED TARGET: http://h/42",
        "ORIGINAL TEMPLATE:",
        "{\"title\":\"_title_\"}",
        "RESOLVED TEMPLATE:",
        "{\"title\":\"hello\"}",
        "VARIABLES:",
        "id=42",
        "title=hello",
        P + "STEP REQUEST [REQ-00000007]",
        "TYPE: HTTP",
        "STEP: Create",
        "ENVIRONMENT: staging",
        "TARGET: http://h/42",
        "METHOD: POST",
        "TEMPLATE: c.json",
        "HEADERS:",
        "Accept=application/json; X-Trace=a; b",
        "DETAILS:",
        "contentType=json",
        "BODY:",
        "{\"title\":\"hello\"}",
        P + "STEP RESPONSE [REQ-00000007]",
        "STATUS: 500",
        "HEADERS:",
        "Content-Type=application/json",
        "BODY:",
        "{\"error\":\"boom\"}",
        P + "ASSERTIONS COMPLETED [REQ-00000007]",
        "PASSED: 1",
        "FAILED: 1",
        W + "ASSERTION FAILED [REQ-00000007] testcase=1; testcaseName=Flow; dataid=D1; stepName=Create; responsePath=id; operator=notempty; expected=; actual=; message=Value is empty",
        E + "HTTP STATUS FAILURE [REQ-00000007] | testcase=1 | dataid=D1 | step=Create | actual=500 | expected=2xx/3xx",
        P + "REQUEST COMPLETED [REQ-00000007] testcase=1; testcaseName=Flow; dataid=D1; stepName=Create; result=FAIL; status=500; durationMs=31; error=Unexpected HTTP status 500; retry later");

    private static string Text(IReadOnlyList<StepSection> sections, string title) => sections.Single(s => s.Title == title).Text;

    [Fact]
    public void A_file_step_gets_operation_and_result_tabs_with_their_own_data()
    {
        var sections = StepLogView.Build([FileStep], "File", "staging");

        Assert.Equal(["Overview", "Operation", "Result", "Template", "Variables", "Assertions", "Errors", "Complete log", "Raw log"], sections.Select(s => s.Title));
        var overview = Text(sections, "Overview");
        Assert.Contains("ReadBinary  (File)", overview);
        Assert.Contains("BinaryCopyFlow  (#1)", overview);
        Assert.Contains("B1", overview);
        Assert.Contains("staging", overview);
        Assert.Contains("PASS   (status 200, 2 ms)", overview);
        Assert.Contains("STEP-00000001", overview);

        var operation = Text(sections, "Operation");
        Assert.Contains("readbinary", operation);
        Assert.Contains("Template    read.json", operation);
        Assert.Contains("path        C:\\x\\upload.bin", operation);
        Assert.Contains("── CONTENT ──", operation);
        Assert.Contains("── STEP SETTINGS ──", operation);
        Assert.Contains("\"action\": \"read\"", operation);                 // JSON is indented

        var result = Text(sections, "Result");
        Assert.Contains("Status      200", result);
        Assert.Contains("\"sha256\": \"abc\"", result);
        Assert.DoesNotContain("[binary content omitted]", result);       // the input is not mixed into the output

        var variables = Text(sections, "Variables");
        Assert.Contains("env       ", variables);
        Assert.Contains("staging", variables);
        Assert.DoesNotContain("TYPE:", variables);

        var template = Text(sections, "Template");
        Assert.Contains("── TEMPLATE AS WRITTEN ──", template);
        Assert.Contains("_filename_", template);
        Assert.Contains("── TEMPLATE WITH VALUES FILLED IN ──", template);

        Assert.Equal("No errors or warnings were recorded for this step.", Text(sections, "Errors"));
        Assert.Equal("No assertions were evaluated for this step.", Text(sections, "Assertions"));
    }

    [Fact]
    public void An_http_step_separates_request_headers_body_response_and_assertions()
    {
        var sections = StepLogView.Build([HttpStep], "HTTP", "staging");

        Assert.Equal(["Overview", "Request", "Response"], sections.Take(3).Select(s => s.Title));
        var request = Text(sections, "Request");
        Assert.StartsWith("POST  http://h/42", request);
        Assert.Contains("Accept: application/json", request);
        Assert.Contains("X-Trace: a; b", request);                    // a ';' inside a header value stays in that header
        Assert.Contains("\"title\": \"hello\"", request);
        Assert.Contains("contentType", request);
        Assert.DoesNotContain("boom", request);

        var response = Text(sections, "Response");
        Assert.Contains("Status      500", response);
        Assert.Contains("\"error\": \"boom\"", response);
        Assert.DoesNotContain("hello", response);

        var assertions = Text(sections, "Assertions");
        Assert.Contains("✓ 1 passed", assertions);
        Assert.Contains("✗ 1 failed", assertions);
        Assert.Contains("✗ id   notempty", assertions);
        Assert.Contains("  message   Value is empty", assertions);

        var errors = Text(sections, "Errors");
        Assert.Contains("WARN  ASSERTION FAILED", errors);
        Assert.Contains("ERROR HTTP STATUS FAILURE", errors);
        Assert.DoesNotContain("REQUEST SENT", errors);

        var overview = Text(sections, "Overview");
        Assert.Contains("FAIL   (status 500, 31 ms)", overview);
        Assert.Contains("Unexpected HTTP status 500; retry later", overview);   // a ';' inside the error text is kept
        Assert.Contains("1 passed, 1 failed", overview);
    }

    private static string Step(string type, string request, string response, string extra = "") => string.Join('\n',
        P + $"---- STEP 1: S | type={type} | dataid=R1 | iteration=1 ----",
        P + "STEP REQUEST [STEP-00000002]",
        $"TYPE: {type}", "STEP: S", "ENVIRONMENT: staging", request,
        P + "STEP RESPONSE [STEP-00000002]", response,
        P + $"STEP COMPLETED [STEP-00000002] testcase=1; testcaseName=T; dataid=R1; stepName=S; stepType={type}; result=PASS; status=200; durationMs=5" + extra);

    [Fact]
    public void A_graphql_step_shows_the_query_as_text_with_its_variables_and_lists_response_errors()
    {
        var log = Step("GraphQL",
            "TARGET: http://h/graphql\nMETHOD: POST\nTEMPLATE: \nHEADERS:\nAuthorization=Bearer x\nDETAILS:\nvariables={\"id\":7}\noperationName=GetUser\nBODY:\nquery GetUser($id: ID!) {\n  user(id: $id) { name }\n}",
            "STATUS: 200\nHEADERS:\n\nBODY:\n{\"data\":null,\"errors\":[{\"message\":\"User not found\"}]}");
        var sections = StepLogView.Build([log], "GraphQL", "staging");

        var query = Text(sections, "Query");
        Assert.Contains("POST  http://h/graphql", query);
        Assert.Contains("operationName", query);
        Assert.Contains("── VARIABLES ──", query);
        Assert.Contains("\"id\": 7", query);
        Assert.Contains("── QUERY ──\nquery GetUser($id: ID!) {\n  user(id: $id) { name }\n}", query.Replace("\r\n", "\n"));
        Assert.Contains("── GRAPHQL ERRORS ──", Text(sections, "Response"));
        Assert.Contains("✗ User not found", Text(sections, "Errors"));
    }

    [Fact]
    public void A_sql_step_shows_command_and_parameters_and_the_rows_as_a_table()
    {
        var log = Step("SQL",
            "TARGET: \nMETHOD: Text\nTEMPLATE: \nHEADERS:\n\nDETAILS:\nconnection=main\nparameters={\"id\":7}\nBODY:\nselect id, name from users where id = @id",
            "STATUS: 200\nHEADERS:\n\nBODY:\n[{\"id\":7,\"name\":\"Ann\"},{\"id\":8,\"name\":null}]");
        var sections = StepLogView.Build([log], "SQL", "staging");

        var query = Text(sections, "Query");
        Assert.Contains("connection  main", query);
        Assert.Contains("── PARAMETERS ──", query);
        Assert.Contains("── COMMAND ──\nselect id, name from users where id = @id", query.Replace("\r\n", "\n"));

        var rows = Text(sections, "Result").Replace("\r\n", "\n");
        Assert.Contains("── ROWS (2) ──", rows);
        Assert.Contains("id  name", rows);
        Assert.Contains("7   Ann", rows);
        Assert.Contains("8   NULL", rows);
    }

    [Fact]
    public void A_script_step_shows_its_code_and_return_value()
    {
        var log = Step("Script", "TARGET: \nMETHOD: C# script\nTEMPLATE: \nHEADERS:\n\nDETAILS:\n(none)\nBODY:\nreturn 1 + 1;", "STATUS: 200\nHEADERS:\n\nBODY:\n2");
        var sections = StepLogView.Build([log], "Script", "staging");

        Assert.Contains("── CODE ──\nreturn 1 + 1;", Text(sections, "Script").Replace("\r\n", "\n"));
        Assert.Contains("── RESULT ──\n2", Text(sections, "Output").Replace("\r\n", "\n"));
    }

    [Fact]
    public void The_complete_log_is_one_clean_line_per_entry_and_the_raw_log_is_untouched()
    {
        var sections = StepLogView.Build([FileStep], "File", "staging");

        var clean = Text(sections, "Complete log");
        Assert.Contains("19:58:24.336 INFO  ---- STEP 1: ReadBinary", clean);
        Assert.DoesNotContain("[run=", clean);
        Assert.DoesNotContain("[FileHttpScenarios]", clean);
        Assert.Contains("    ORIGINAL TEMPLATE:", clean);               // continuation lines are indented under their entry

        Assert.Contains("[run=20261117_195803_3d2dd134c]", Text(sections, "Raw log"));
    }

    [Fact]
    public void A_step_that_stopped_early_says_what_is_missing_instead_of_showing_other_data()
    {
        var stopped = string.Join('\n',
            P + "---- STEP 1: Create | type=HTTP | dataid=D1 | iteration=1 ----",
            P + "Request starting. RequestId=REQ-00000007; testcase=1; dataid=D1; stepName=Create; thread=1; iteration=1; verb=POST; url=http://h; template=; ignoreempty=False",
            E + "REQUEST FAILED [REQ-00000007] testcase=1; stepName=Create | HttpRequestException: connection refused\n   at System.Net.Http.Foo()");
        var sections = StepLogView.Build([stopped], "HTTP", "staging");

        Assert.Contains("was not recorded", Text(sections, "Response"));
        Assert.Contains("not finished", Text(sections, "Overview"));
        Assert.Contains("connection refused", Text(sections, "Errors"));
        Assert.Contains("   at System.Net.Http.Foo()", Text(sections, "Errors"));
    }

    [Fact]
    public void A_step_that_ran_twice_shows_both_runs()
    {
        var sections = StepLogView.Build([FileStep, FileStep], "File", "staging");

        Assert.Contains("── Run 1 of 2 ──", Text(sections, "Overview"));
        Assert.Contains("── Run 2 of 2 ──", Text(sections, "Overview"));
    }

    [Theory]
    [InlineData("SQL", "Query", "Result")]
    [InlineData("Script", "Script", "Output")]
    [InlineData("GraphQL", "Query", "Response")]
    [InlineData("Whatever", "Input", "Output")]
    public void The_input_and_output_tabs_are_named_for_the_step_type(string type, string input, string output)
        => Assert.Equal((input, output), StepLogView.TabNames(type));

    [AvaloniaFact]
    public void The_window_shows_the_tabs_for_the_step_and_renders()
    {
        using var dir = new TempDir();
        var log = dir.Combine("BinaryCopyFlow_staging_passed.log");
        File.WriteAllText(log, FileStep);
        var window = new StepDetailsWindow(log, "BinaryCopyFlow", "B1", "ReadBinary", "staging", "File");
        window.Width = 1000; window.Height = 700;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TabControl>().Single();
        Assert.Equal(["Overview", "Operation", "Result", "Template", "Variables", "Assertions", "Errors", "Complete log", "Raw log"], tabs.Items.OfType<TabItem>().Select(t => t.Header as string));
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(Path.Combine(shots, "step-details-overview.png"));
        tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(shots, "step-details-operation.png"));
        tabs.SelectedIndex = 7; Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(shots, "step-details-log.png"));
        window.Close();
    }

    private static string Row(string id, string dataId, int status, string body) => string.Join('\n',
        P + $"STEP REQUEST [{id}]", "TYPE: HTTP", "STEP: FetchUser", "ENVIRONMENT: staging", $"TARGET: http://h/users/{dataId}", "METHOD: GET", "TEMPLATE: g.json", "HEADERS:", "", "DETAILS:", "contentType=json", "BODY:", "",
        P + $"STEP RESPONSE [{id}]", $"STATUS: {status}", "HEADERS:", "", "BODY:", body);

    [Fact]
    public void Rows_that_ran_in_parallel_do_not_mix_their_entries_the_step_shows_its_own_status()
    {
        // three rows of the same step run at once: their entries are interleaved in the file
        var log = string.Join('\n',
            P + "---- STEP 1: FetchUser | type=HTTP | dataid=HTTP001 | iteration=1 ----",
            P + "---- STEP 1: FetchUser | type=HTTP | dataid=HTTP002 | iteration=1 ----",
            P + "---- STEP 1: FetchUser | type=HTTP | dataid=HTTP003 | iteration=1 ----",
            P + "Request starting. RequestId=REQ-00000001; testcase=6; dataid=HTTP001; stepName=FetchUser; thread=1; iteration=1; verb=GET; url=http://h/users/HTTP001; template=g.json; ignoreempty=False",
            P + "Request starting. RequestId=REQ-00000002; testcase=6; dataid=HTTP002; stepName=FetchUser; thread=2; iteration=1; verb=GET; url=http://h/users/HTTP002; template=g.json; ignoreempty=False",
            P + "Request starting. RequestId=REQ-00000003; testcase=6; dataid=HTTP003; stepName=FetchUser; thread=3; iteration=1; verb=GET; url=http://h/users/HTTP003; template=g.json; ignoreempty=False",
            Row("REQ-00000001", "HTTP001", 200, "{\"name\":\"one\"}"),
            Row("REQ-00000002", "HTTP002", 200, "{\"name\":\"two\"}"),
            Row("REQ-00000003", "HTTP003", 404, "{\"message\":\"not found\"}"),
            E + "HTTP STATUS FAILURE [REQ-00000003] | testcase=6 | dataid=HTTP003 | step=FetchUser | actual=404 | expected=200",
            P + "REQUEST COMPLETED [REQ-00000001] testcase=6; testcaseName=HttpFlow; dataid=HTTP001; stepName=FetchUser; result=PASS; status=200; durationMs=5",
            P + "REQUEST COMPLETED [REQ-00000002] testcase=6; testcaseName=HttpFlow; dataid=HTTP002; stepName=FetchUser; result=PASS; status=200; durationMs=6",
            P + "REQUEST COMPLETED [REQ-00000003] testcase=6; testcaseName=HttpFlow; dataid=HTTP003; stepName=FetchUser; result=FAIL; status=404; durationMs=7");

        var blocks = StepLogView.SelectBlocks(log, "HTTP003", "FetchUser");
        var sections = StepLogView.Build(blocks, "HTTP", "staging");

        Assert.Single(blocks);
        var response = Text(sections, "Response");
        Assert.Contains("Status      404", response);
        Assert.Contains("not found", response);
        Assert.DoesNotContain("200", response);
        Assert.DoesNotContain("two", response);
        Assert.DoesNotContain("HTTP002", string.Join("\n", sections.Select(x => x.Text)));
        Assert.Contains("HTTP003", Text(sections, "Request"));
        Assert.Contains("actual=404", Text(sections, "Errors"));

        // and the first row gets its own 200
        var one = StepLogView.Build(StepLogView.SelectBlocks(log, "HTTP001", "FetchUser"), "HTTP", "staging");
        Assert.Contains("Status      200", Text(one, "Response"));
        Assert.DoesNotContain("actual=404", Text(one, "Errors"));
    }
}
