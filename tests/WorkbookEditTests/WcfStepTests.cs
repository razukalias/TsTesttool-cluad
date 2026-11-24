using System.Net;
using LoadTestingTool.Domain;
using LoadTestingTool.Excel;
using LoadTestingTool.Execution;
using LoadTestingTool.MockServer;
using Xunit;

namespace WorkbookEditTests;

public class SoapEnvelopeTests
{
    [Fact]
    public void ABodyIsWrappedWithTheVersionHeaderAndUsernameToken()
    {
        var wrapped = SoapEnvelope.Wrap("<?xml version=\"1.0\"?>\n<GetOrder xmlns=\"http://t/\"><Id>7</Id></GetOrder>", new WcfStepOptions { Username = "a&b", Password = "p<w", HeaderXml = "<Trace>1</Trace>" });
        Assert.StartsWith("<soap:Envelope xmlns:soap=\"" + SoapEnvelope.Ns11 + "\"><soap:Header>", wrapped);
        Assert.Contains("<wsse:Username>a&amp;b</wsse:Username>", wrapped);
        Assert.Contains("<wsse:Password Type=", wrapped);
        Assert.Contains(">p&lt;w</wsse:Password>", wrapped);
        Assert.Contains("<Trace>1</Trace>", wrapped);
        Assert.EndsWith("<soap:Body><GetOrder xmlns=\"http://t/\"><Id>7</Id></GetOrder></soap:Body></soap:Envelope>", wrapped);
        Assert.Contains(SoapEnvelope.Ns12, SoapEnvelope.Wrap("<A/>", new WcfStepOptions { SoapVersion = "1.2" }));
        Assert.DoesNotContain("<soap:Header>", SoapEnvelope.Wrap("<A/>", new WcfStepOptions()));
    }

    [Fact]
    public void ThePasswordIsMaskedForLogsButNotInTheSentEnvelope()
    {
        var sent = SoapEnvelope.Wrap("<A/>", new WcfStepOptions { Username = "u", Password = "secret" });
        Assert.Contains(">secret</wsse:Password>", sent);
        var logged = SoapEnvelope.MaskSecrets(sent);
        Assert.DoesNotContain("secret", logged);
        Assert.Contains(">***</wsse:Password>", logged);
        Assert.Contains("<wsse:Username>u</wsse:Username>", logged);
    }

    [Fact]
    public void AReadyEnvelopeIsSentAsItIs()
    {
        var envelope = "<s:Envelope xmlns:s=\"" + SoapEnvelope.Ns11 + "\"><s:Body><A/></s:Body></s:Envelope>";
        Assert.Equal(envelope, SoapEnvelope.Wrap("<?xml version=\"1.0\"?>" + envelope, new WcfStepOptions { Username = "ignored" }));
        Assert.True(SoapEnvelope.IsEnvelope(envelope));
        Assert.False(SoapEnvelope.IsEnvelope("<Envelope><Body/></Envelope>"));
        Assert.False(SoapEnvelope.IsEnvelope("not xml"));
    }

    [Fact]
    public void ContentTypeFollowsTheVersion()
    {
        Assert.Equal("text/xml; charset=utf-8", SoapEnvelope.ContentType(new WcfStepOptions { SoapAction = "x" }));
        Assert.Equal("application/soap+xml; charset=utf-8; action=\"urn:go\"", SoapEnvelope.ContentType(new WcfStepOptions { SoapVersion = "1.2", SoapAction = "urn:go" }));
        Assert.Equal("application/soap+xml; charset=utf-8", SoapEnvelope.ContentType(new WcfStepOptions { SoapVersion = "1.2" }));
    }

    [Theory]
    [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>Bad id</faultstring></s:Fault></s:Body></s:Envelope>", "s:Client: Bad id")]
    [InlineData("<e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\"><e:Body><e:Fault><e:Code><e:Value>e:Sender</e:Value></e:Code><e:Reason><e:Text xml:lang=\"en\">No such order</e:Text></e:Reason></e:Fault></e:Body></e:Envelope>", "e:Sender: No such order")]
    [InlineData("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><R/></s:Body></s:Envelope>", null)]
    [InlineData("{\"json\":true}", null)]
    [InlineData("", null)]
    public void FaultsAreFound(string xml, string? expected) => Assert.Equal(expected, SoapEnvelope.FaultOf(xml));

    [Fact]
    public void ResponsePathsAreReadFromTheSoapBody()
    {
        var paths = new AssertionEngine().Normalize("<s:Envelope xmlns:s=\"" + SoapEnvelope.Ns11 + "\"><s:Header><Trace>t1</Trace></s:Header><s:Body><GetOrderResponse xmlns=\"http://t/\"><Id>O1</Id><Lines><Line sku=\"A\">1</Line><Line sku=\"B\">2</Line></Lines></GetOrderResponse></s:Body></s:Envelope>", "xml");
        Assert.Equal("O1", paths["GetOrderResponse.Id"]);
        Assert.Equal("B", paths["GetOrderResponse.Lines.Line[1].@sku"]);
        Assert.Equal("t1", paths["Header.Trace"]);
        Assert.False(paths.ContainsKey("Envelope.Body.GetOrderResponse.Id"));
    }

    [Fact]
    public void WcfIsAStepTypeLikeHttp()
    {
        Assert.Equal(StepType.Wcf, StepTypeParser.Parse("WCF"));
        Assert.Equal(StepType.Wcf, StepTypeParser.Parse("soap"));
        Assert.Equal("WCF", StepType.Wcf.WireName());
        Assert.True(StepType.Wcf.IsHttpLike());
        Assert.False(StepType.GraphQL.IsHttpLike());
    }
}

public class MockSoapTests
{
    private static MockRequest Soap(string body, string? action = null, string contentType = "text/xml")
        => new("POST", "/svc", new Dictionary<string, string>(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = contentType }.WithAction(action), body);

    private const string Order = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><GetOrder xmlns=\"http://t/\"><OrderId>O9</OrderId><Opt id=\"x1\">v</Opt></GetOrder></s:Body></s:Envelope>";

    [Fact]
    public void WhatARequestAsksFor()
    {
        Assert.Equal("GetOrder", MockSoap.Part(Soap(Order), "operation"));
        Assert.Equal("GetOrder", MockSoap.Part(Soap(Order), ""));
        Assert.Equal("1.1", MockSoap.Part(Soap(Order), "version"));
        Assert.Equal("urn:get", MockSoap.Part(Soap(Order, "\"urn:get\""), "action"));
        Assert.Equal("urn:get2", MockSoap.Part(Soap(Order, null, "application/soap+xml; charset=utf-8; action=\"urn:get2\""), "action"));
        Assert.Equal("1.2", MockSoap.Part(Soap(Order.Replace("http://schemas.xmlsoap.org/soap/envelope/", "http://www.w3.org/2003/05/soap-envelope")), "version"));
        Assert.Null(MockSoap.Part(Soap("{\"a\":1}"), "operation"));
        Assert.Null(MockSoap.Part(Soap("<plain/>"), "operation"));
    }

    [Theory]
    [InlineData("GetOrder.OrderId", "O9")]
    [InlineData("GetOrder.Opt.@id", "x1")]
    [InlineData("GetOrder.Missing", null)]
    [InlineData("Other.OrderId", null)]
    public void XmlPaths(string path, string? expected) => Assert.Equal(expected, MockXml.Get(Order, path));

    [Fact]
    public void XmlPathsHandleIndexesAndPlainXml()
    {
        Assert.Equal("b", MockXml.Get("<R><I>a</I><I>b</I></R>", "R.I[1]"));
        Assert.Equal("a", MockXml.Get("<R><I>a</I><I>b</I></R>", "R.I"));
        Assert.Null(MockXml.Get("not xml", "R"));
    }

    [Fact]
    public void ConditionsAndTemplatesUseSoapAndXml()
    {
        var request = Soap(Order, "urn:get");
        Assert.True(MockRouteMatcher.Holds(new MockCondition { Source = "soap", Name = "operation", Op = "equals", Value = "GetOrder" }, request));
        Assert.True(MockRouteMatcher.Holds(new MockCondition { Source = "xml", Name = "GetOrder.OrderId", Op = "equals", Value = "O9" }, request));
        Assert.Equal("GetOrder/O9/urn:get", MockTemplate.Render("{{operation}}/{{xml.GetOrder.OrderId}}/{{soap.action}}", request, new Dictionary<string, string>()));
    }

    [Fact]
    public void ValidationKnowsTheSoapSource()
    {
        var config = new MockServerConfig { Routes = [new MockRoute { Path = "/s", Conditions = [new MockCondition { Source = "soap", Name = "operaton", Op = "equals" }, new MockCondition { Source = "xml", Name = "A.B", Op = "exists" }] }] };
        var text = string.Join("\n", config.Validate());
        Assert.Contains("operaton", text);
        Assert.Single(config.Validate());
    }
}

internal static class MockSoapTestExtensions
{
    public static Dictionary<string, string> WithAction(this Dictionary<string, string> headers, string? action) { if (action is not null) headers["SOAPAction"] = action; return headers; }
}

public class WcfStepEndToEndTests : IDisposable
{
    private readonly TempDir _dir = new();
    private MockServerHost? _host;

    public void Dispose() { _host?.Dispose(); _dir.Dispose(); }

    private const string Envelope11 = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><GetOrderResponse xmlns=\"http://t/\"><Id>{{xml.GetOrder.OrderId}}</Id><Status>SHIPPED</Status><Seen>{{header.SOAPAction}}</Seen></GetOrderResponse></s:Body></s:Envelope>";
    private const string Fault11 = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>Order {{xml.GetOrder.OrderId}} not found</faultstring></s:Fault></s:Body></s:Envelope>";

    private string StartServer()
    {
        var config = new MockServerConfig { Port = MockServerHost.FreePort(), Routes =
        [
            new MockRoute { Name = "missing", Method = "POST", Path = "/Orders.svc", Conditions = [new MockCondition { Source = "xml", Name = "GetOrder.OrderId", Op = "equals", Value = "NOPE" }], Response = new MockResponse { Status = 500, ContentType = "text/xml; charset=utf-8", Body = Fault11 } },
            new MockRoute { Name = "get", Method = "POST", Path = "/Orders.svc", Conditions = [new MockCondition { Source = "soap", Name = "operation", Op = "equals", Value = "GetOrder" }], Response = new MockResponse { ContentType = "text/xml; charset=utf-8", Body = Envelope11 } }
        ]};
        _host = new MockServerHost(config); _host.Start();
        return $"http://127.0.0.1:{config.Port}/Orders.svc";
    }

    private async Task<RunResult> Run(string url, string orderId, string expected, string stepConfig = "{\"SoapAction\":\"http://t/IOrders/GetOrder\"}", string? expectedStatus = null)
    {
        Directory.CreateDirectory(_dir.Combine("Templates"));
        File.WriteAllText(Path.Combine(_dir.Combine("Templates"), "get.xml"), "<GetOrder xmlns=\"http://t/\"><OrderId>_orderid_</OrderId></GetOrder>");
        var path = Workbooks.CreateSmall(_dir, (config, request, response) =>
        {
            config.Cell(1, 9).Value = "stepconfig";
            config.Cell(2, 3).Value = "GetOrder"; config.Cell(2, 4).Value = "WCF"; config.Cell(2, 5).Value = "get.xml"; config.Cell(2, 6).Value = url; config.Cell(2, 9).Value = stepConfig;
            if (expectedStatus is not null) { config.Cell(1, 10).Value = "expectedstatus"; config.Cell(2, 10).Value = expectedStatus; }
            request.Cell(1, 6).Value = "GetOrder.orderid";
            response.Cell(1, 4).Value = expected.Split('|')[0]; response.Cell(1, 5).Value = "{eq}GetOrder.GetOrderResponse.Status"; response.Cell(1, 6).Value = "{eq}GetOrder.GetOrderResponse.Id";
            for (var n = 2; n <= 4; n++) { request.Cell(n, 6).Value = n == 2 ? orderId : "x"; response.Cell(n, 4).Value = expected.Split('|')[1]; response.Cell(n, 5).Value = "SHIPPED"; response.Cell(n, 6).Value = orderId; }
        });
        var model = new WorkbookReader().Read(path);
        var runner = new TestRunner(new HttpClient(), new AssertionEngine(), Path.Combine(Path.GetDirectoryName(path)!, "Templates"), 30);
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        await runner.RunAsync(model.Testcases[0], model, run, CancellationToken.None, selectedDataIds: new HashSet<string> { "D1" }, executionMode: "sequential");
        return run;
    }

    [Fact]
    public async Task AWcfStepSendsAnEnvelopeAndAssertsOnTheSoapBody()
    {
        var url = StartServer();
        var run = await Run(url, "O42", "{eq}GetOrder.statusCode|200");
        var result = run.Testcases.SelectMany(t => t.Requests).Single();
        Assert.Equal("WCF", result.StepType);
        Assert.True(string.IsNullOrEmpty(result.ErrorMessage), result.ErrorMessage);
        Assert.All(result.Assertions.Where(a => a.AssertionType == "Value assertion"), a => Assert.Equal("PASS", a.Result));
        Assert.DoesNotContain(result.Assertions, a => a.ResponsePath.Contains("@xmlns"));
        Assert.Contains(result.Assertions, a => a.ResponsePath.EndsWith("GetOrderResponse.Id", StringComparison.OrdinalIgnoreCase) && a.ActualValue == "O42" && a.Result == "PASS");

        var seen = _host!.Snapshot().Single();
        Assert.Equal("POST", seen.Method);
        Assert.Contains("Content-Type: text/xml; charset=utf-8", seen.RequestHeaders);
        Assert.Contains("SOAPAction: \"http://t/IOrders/GetOrder\"", seen.RequestHeaders);
        Assert.StartsWith("<soap:Envelope", seen.RequestBody);
        Assert.Contains("<OrderId>O42</OrderId>", seen.RequestBody);
        Assert.Contains("http://t/IOrders/GetOrder", seen.ResponseBody);   // {{header.SOAPAction}} came back
    }

    [Fact]
    public async Task ASoapFaultFailsTheStepWithTheFaultText()
    {
        var url = StartServer();
        var run = await Run(url, "NOPE", "{eq}GetOrder.statusCode|200");
        var result = run.Testcases.SelectMany(t => t.Requests).Single();
        Assert.Equal(500, result.HttpStatus);
        Assert.Contains("SOAP fault: s:Client: Order NOPE not found (HTTP 500)", result.ErrorMessage);
    }

    [Fact]
    public async Task ExpectingTheFaultStatusMakesAFaultAPassingNegativeTest()
    {
        var url = StartServer();
        var run = await Run(url, "NOPE", "{eq}GetOrder.statusCode|500", expectedStatus: "500");
        var result = run.Testcases.SelectMany(t => t.Requests).Single();
        Assert.Contains(result.Assertions, a => a.ResponsePath.EndsWith("statusCode", StringComparison.OrdinalIgnoreCase) && a.ActualValue == "500" && a.Result == "PASS");
        Assert.DoesNotContain("SOAP fault", result.ErrorMessage ?? "");
        Assert.Contains(result.Assertions, a => a.ResponsePath.EndsWith("Fault.faultstring", StringComparison.OrdinalIgnoreCase) && a.ActualValue == "Order NOPE not found");
    }

    [Fact]
    public async Task Soap12UsesItsContentTypeAndNoSoapActionHeader()
    {
        var url = StartServer();
        await Run(url, "O1", "{eq}GetOrder.statusCode|404", "{\"SoapAction\":\"urn:go\",\"SoapVersion\":\"1.2\"}");
        var seen = _host!.Snapshot().Single();
        Assert.Contains("Content-Type: application/soap+xml; charset=utf-8; action=\"urn:go\"", seen.RequestHeaders);
        Assert.DoesNotContain(seen.RequestHeaders, h => h.StartsWith("SOAPAction", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("http://www.w3.org/2003/05/soap-envelope", seen.RequestBody);
        Assert.True(seen.Matched);   // the mock route asks for the operation, so a 1.2 request matches too
    }
}

public class WcfPasswordLoggingTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task TheStoredRequestBodyHasNoPasswordButTheServerGetsIt()
    {
        var config = new MockServerConfig { Port = MockServerHost.FreePort(), Routes = [new MockRoute { Method = "POST", Path = "/s", Response = new MockResponse { ContentType = "text/xml", Body = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><R><Id>1</Id></R></s:Body></s:Envelope>" } }] };
        using var host = new MockServerHost(config); host.Start();
        Directory.CreateDirectory(_dir.Combine("Templates"));
        File.WriteAllText(Path.Combine(_dir.Combine("Templates"), "a.xml"), "<Go/>");
        var path = Workbooks.CreateSmall(_dir, (c, request, response) =>
        {
            c.Cell(1, 9).Value = "stepconfig";
            c.Cell(2, 4).Value = "WCF"; c.Cell(2, 5).Value = "a.xml"; c.Cell(2, 6).Value = $"http://127.0.0.1:{config.Port}/s"; c.Cell(2, 9).Value = "{\"Username\":\"svc\",\"Password\":\"hunter2\"}";
        });
        var model = new WorkbookReader().Read(path);
        var runner = new TestRunner(new HttpClient(), new AssertionEngine(), Path.Combine(Path.GetDirectoryName(path)!, "Templates"), 30);
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        await runner.RunAsync(model.Testcases[0], model, run, CancellationToken.None, selectedDataIds: new HashSet<string> { "D1" }, executionMode: "sequential");
        var result = run.Testcases.SelectMany(t => t.Requests).Single();
        Assert.DoesNotContain("hunter2", result.RequestBody);
        Assert.Contains("***", result.RequestBody);
        Assert.Contains("hunter2", host.Snapshot().Single().RequestBody);
    }

    [Fact]
    public async Task ThePasswordCanComeFromAColumnAndNeitherTheLogNorTheHistoryHoldsIt()
    {
        var config = new MockServerConfig { Port = MockServerHost.FreePort(), Routes = [new MockRoute { Method = "POST", Path = "/s", Response = new MockResponse { ContentType = "text/xml", Body = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><R><Id>1</Id></R></s:Body></s:Envelope>" } }] };
        using var host = new MockServerHost(config); host.Start();
        Directory.CreateDirectory(_dir.Combine("Templates"));
        File.WriteAllText(Path.Combine(_dir.Combine("Templates"), "a.xml"), "<Go/>");
        var path = Workbooks.CreateSmall(_dir, (c, request, response) =>
        {
            c.Cell(1, 9).Value = "stepconfig";
            c.Cell(2, 4).Value = "WCF"; c.Cell(2, 5).Value = "a.xml"; c.Cell(2, 6).Value = $"http://127.0.0.1:{config.Port}/s";
            c.Cell(2, 9).Value = "{\"Username\":\"svc\",\"Password\":\"_pw_\",\"SoapAction\":\"urn:Get_Order_Details\"}";
            request.Cell(1, 6).Value = "pw"; request.Cell(2, 6).Value = "p\"w\\1";
        });
        var model = new WorkbookReader().Read(path);
        var logs = new List<string>();
        var runner = new TestRunner(new HttpClient(), new AssertionEngine(), Path.Combine(Path.GetDirectoryName(path)!, "Templates"), 30);
        var run = new RunResult { RunId = "r", StartedAt = DateTimeOffset.Now };
        await runner.RunAsync(model.Testcases[0], model, run, CancellationToken.None, selectedDataIds: new HashSet<string> { "D1" }, executionMode: "sequential");
        var result = run.Testcases.SelectMany(t => t.Requests).Single();
        Assert.True(string.IsNullOrEmpty(result.ErrorMessage), result.ErrorMessage);
        Assert.Contains("p&quot;w\\1", host.Snapshot().Single().RequestBody);   // the server got the column's value (XML-escaped in the token)
        Assert.DoesNotContain("p\"w", result.RequestBody);
        Assert.Contains("_pw_", result.StepConfig);                          // the history keeps the placeholder, not the value
        Assert.DoesNotContain("p\\\"w", result.StepConfig);
        Assert.Contains("Get_Order_Details", result.StepConfig);              // text that only looks like a placeholder is left alone
    }
}

public class StepConfigPlaceholderTests
{
    private static readonly Dictionary<string, string> Vars = new(StringComparer.OrdinalIgnoreCase) { ["pw"] = "a\"b\\c", ["user"] = "svc" };

    [Fact]
    public void JsonSettingsGetTheValueEscapedAndUnknownNamesStay()
    {
        var filled = StepConfig.Fill("{\"Password\":\"_pw_\",\"Username\":\"_user_\",\"SoapAction\":\"urn:Get_Order_Details\"}", Vars);
        using var document = System.Text.Json.JsonDocument.Parse(filled);
        Assert.Equal("a\"b\\c", document.RootElement.GetProperty("Password").GetString());
        Assert.Equal("svc", document.RootElement.GetProperty("Username").GetString());
        Assert.Equal("urn:Get_Order_Details", document.RootElement.GetProperty("SoapAction").GetString());
    }

    [Fact]
    public void KeyValueSettingsGetTheValueAsItIs()
        => Assert.Equal("Username=svc;Password=a\"b\\c", StepConfig.Fill("Username=_user_;Password=_pw_", Vars));

    [Theory]
    [InlineData("{\"Username\":\"u\",\"Password\":\"hunter2\"}", "{\"Username\":\"u\",\"Password\":\"***\"}")]
    [InlineData("{\"Password\":\"_pw_\"}", "{\"Password\":\"_pw_\"}")]
    [InlineData("Username=u;Password=hunter2;Timeout=5", "Username=u;Password=***;Timeout=5")]
    [InlineData("{\"ApiKey\":\"abc\",\"Other\":\"x\"}", "{\"ApiKey\":\"***\",\"Other\":\"x\"}")]
    public void SecretSettingsAreMaskedForLogsAndHistoryButPlaceholdersStayReadable(string config, string expected)
        => Assert.Equal(expected, StepConfig.Mask(config));

    [Fact]
    public void ReadingSettingsInsideAScopeUsesTheVariables()
    {
        var step = new RequestStep { StepName = "S", StepType = StepType.Wcf, StepConfig = "{\"Username\":\"_user_\",\"Password\":\"_pw_\"}" };
        using (StepConfig.UseVariables(Vars))
        {
            var options = StepConfig.Read<WcfStepOptions>(step);
            Assert.Equal("svc", options.Username);
            Assert.Equal("a\"b\\c", options.Password);
        }
        Assert.Equal("_user_", StepConfig.Read<WcfStepOptions>(step).Username);   // outside the scope nothing is filled
    }
}
