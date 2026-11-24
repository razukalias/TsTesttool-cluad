using System.Net.Http;
using System.Text;
using LoadTestingTool.Editing;
using LoadTestingTool.MockServer;
using Xunit;

namespace WorkbookEditTests;

public class WsdlImportTests
{
    private const string Orders = """
        <?xml version="1.0" encoding="utf-8"?>
        <wsdl:definitions xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:tns="http://tempuri.org/" targetNamespace="http://tempuri.org/" name="Orders">
          <wsdl:types><xs:schema targetNamespace="http://tempuri.org/" elementFormDefault="qualified">
            <xs:element name="GetOrder"><xs:complexType><xs:sequence><xs:element name="OrderId" type="xs:string"/></xs:sequence></xs:complexType></xs:element>
            <xs:element name="GetOrderResponse"><xs:complexType><xs:sequence><xs:element name="Id" type="xs:string"/><xs:element name="Status" type="xs:string"/><xs:element name="Total" type="xs:decimal"/></xs:sequence></xs:complexType></xs:element>
          </xs:schema></wsdl:types>
          <wsdl:message name="GetOrderIn"><wsdl:part name="parameters" element="tns:GetOrder"/></wsdl:message>
          <wsdl:message name="GetOrderOut"><wsdl:part name="parameters" element="tns:GetOrderResponse"/></wsdl:message>
          <wsdl:portType name="IOrders"><wsdl:operation name="GetOrder"><wsdl:input message="tns:GetOrderIn"/><wsdl:output message="tns:GetOrderOut"/></wsdl:operation></wsdl:portType>
          <wsdl:binding name="BasicHttpBinding_IOrders" type="tns:IOrders"><soap:binding transport="http://schemas.xmlsoap.org/soap/http"/>
            <wsdl:operation name="GetOrder"><soap:operation soapAction="http://tempuri.org/IOrders/GetOrder" style="document"/><wsdl:input><soap:body use="literal"/></wsdl:input><wsdl:output><soap:body use="literal"/></wsdl:output></wsdl:operation></wsdl:binding>
          <wsdl:service name="Orders"><wsdl:port name="BasicHttpBinding_IOrders" binding="tns:BasicHttpBinding_IOrders"><soap:address location="http://host/Orders.svc"/></wsdl:port></wsdl:service>
        </wsdl:definitions>
        """;

    [Fact]
    public void An_operation_gets_its_action_endpoint_version_template_and_sample_answer()
    {
        var info = WsdlImporter.Parse(Orders);
        var operation = Assert.Single(info.Operations);
        Assert.Equal("Orders", info.ServiceName);
        Assert.Equal("GetOrder", operation.Name);
        Assert.Equal("http://tempuri.org/IOrders/GetOrder", operation.SoapAction);
        Assert.Equal("1.1", operation.SoapVersion);
        Assert.Equal("http://host/Orders.svc", operation.Endpoint);
        var template = System.Xml.Linq.XElement.Parse(operation.Template);
        Assert.Equal("{http://tempuri.org/}GetOrder", template.Name.ToString());
        Assert.Equal("_OrderId_", template.Elements().Single().Value);
        Assert.Equal(["OrderId"], operation.Variables);
        var sample = System.Xml.Linq.XElement.Parse(operation.ResponseSample);
        Assert.Equal("1.5", sample.Elements().Single(e => e.Name.LocalName == "Total").Value);
        Assert.Equal("text", sample.Elements().Single(e => e.Name.LocalName == "Status").Value);
    }

    private const string Shop = """
        <wsdl:definitions xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:soap12="http://schemas.xmlsoap.org/wsdl/soap12/" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:tns="urn:shop" targetNamespace="urn:shop">
          <wsdl:types><xs:schema targetNamespace="urn:shop"><xs:import namespace="urn:shop:types" schemaLocation="types.xsd"/></xs:schema></wsdl:types>
          <wsdl:message name="PlaceIn"><wsdl:part name="p" element="t:Place" xmlns:t="urn:shop:types"/></wsdl:message>
          <wsdl:portType name="IShop"><wsdl:operation name="Place"><wsdl:input message="tns:PlaceIn"/></wsdl:operation></wsdl:portType>
          <wsdl:binding name="B1" type="tns:IShop"><soap12:binding transport="http://schemas.xmlsoap.org/soap/http"/><wsdl:operation name="Place"><soap12:operation soapAction="urn:place"/></wsdl:operation></wsdl:binding>
          <wsdl:binding name="B2" type="tns:IShop"><soap12:binding transport="http://schemas.xmlsoap.org/soap/http"/><wsdl:operation name="Place"><soap12:operation soapAction="urn:place2"/></wsdl:operation></wsdl:binding>
          <wsdl:service name="Shop"><wsdl:port name="p1" binding="tns:B1"><soap12:address location="http://host/shop"/></wsdl:port></wsdl:service>
        </wsdl:definitions>
        """;

    private const string Types = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:t="urn:shop:types" targetNamespace="urn:shop:types" elementFormDefault="qualified">
          <xs:element name="Place"><xs:complexType><xs:sequence>
            <xs:element name="Customer" type="t:Person"/>
            <xs:element name="Line" type="t:Line" maxOccurs="unbounded"/>
            <xs:element name="Pay"><xs:complexType><xs:choice><xs:element name="Card" type="xs:string"/><xs:element name="Cash" type="xs:string"/></xs:choice></xs:complexType></xs:element>
          </xs:sequence><xs:attribute name="channel" type="xs:string"/></xs:complexType></xs:element>
          <xs:complexType name="Base"><xs:sequence><xs:element name="Name" type="xs:string"/></xs:sequence></xs:complexType>
          <xs:complexType name="Person"><xs:complexContent><xs:extension base="t:Base"><xs:sequence><xs:element name="Age" type="xs:int"/><xs:element name="Friend" type="t:Person" minOccurs="0"/></xs:sequence></xs:extension></xs:complexContent></xs:complexType>
          <xs:complexType name="Line"><xs:sequence><xs:element name="Name" type="xs:string"/><xs:element name="Qty" type="xs:int"/></xs:sequence><xs:attribute name="sku" type="xs:string"/></xs:complexType>
        </xs:schema>
        """;

    [Fact]
    public void Imported_schemas_extensions_attributes_choices_and_self_referencing_types_are_followed()
    {
        var asked = new List<string>();
        var info = WsdlImporter.Parse(Shop, "/data/shop.wsdl", location => { asked.Add(location); return location.EndsWith("types.xsd") ? Types : null; });
        Assert.Contains(asked, a => a.Replace('\\', '/').EndsWith("/data/types.xsd"));
        Assert.Equal(2, info.Operations.Count);   // two bindings of the same operation
        var operation = info.Operations[0];
        Assert.Equal("1.2", operation.SoapVersion);
        Assert.Equal("urn:place", operation.SoapAction);
        Assert.Equal("http://host/shop", operation.Endpoint);
        Assert.Equal(string.Empty, info.Operations[1].Endpoint);   // the second binding has no port

        var template = System.Xml.Linq.XElement.Parse(operation.Template);
        XNs(template, "Customer", "Name", out var customerName);
        Assert.Equal("_Name_", customerName);                       // inherited from the base type
        Assert.Equal("_channel_", (string?)template.Attribute("channel") is { } c ? c.Replace(".@", "") : null);
        Assert.Single(template.Elements().Where(e => e.Name.LocalName == "Line"));   // a repeating element once
        Assert.Contains(template.Descendants(), e => e.Name.LocalName == "Card");
        Assert.DoesNotContain(template.Descendants(), e => e.Name.LocalName == "Cash");   // a choice takes its first option
        Assert.Contains(info.Notes, n => n.Contains("choice"));
        // names do not clash: the second Name is told apart by its path
        Assert.Equal(operation.Variables.Count, operation.Variables.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Place.Line.Name", operation.Variables);   // the second Name carries its path
    }

    private static void XNs(System.Xml.Linq.XElement root, string parent, string child, out string value)
        => value = root.Elements().Single(e => e.Name.LocalName == parent).Elements().Single(e => e.Name.LocalName == child).Value;

    [Fact]
    public void An_import_that_cannot_be_read_is_reported_and_the_rest_still_works()
    {
        var info = WsdlImporter.Parse(Shop, "/data/shop.wsdl", _ => null);
        Assert.Contains(info.Notes, n => n.Contains("types.xsd"));
        Assert.Equal(2, info.Operations.Count);   // the operations are still listed
    }

    [Fact]
    public void An_rpc_operation_is_wrapped_in_an_element_named_after_it()
    {
        const string rpc = """
            <wsdl:definitions xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:tns="urn:calc" targetNamespace="urn:calc">
              <wsdl:message name="AddIn"><wsdl:part name="a" type="xs:int"/><wsdl:part name="b" type="xs:int"/></wsdl:message>
              <wsdl:portType name="ICalc"><wsdl:operation name="Add"><wsdl:input message="tns:AddIn"/></wsdl:operation></wsdl:portType>
              <wsdl:binding name="B" type="tns:ICalc"><soap:binding style="rpc" transport="http://schemas.xmlsoap.org/soap/http"/><wsdl:operation name="Add"><soap:operation soapAction="urn:calc#Add"/><wsdl:input><soap:body use="literal" namespace="urn:calc"/></wsdl:input></wsdl:operation></wsdl:binding>
              <wsdl:service name="Calc"><wsdl:port name="P" binding="tns:B"><soap:address location="http://host/calc"/></wsdl:port></wsdl:service>
            </wsdl:definitions>
            """;
        var operation = Assert.Single(WsdlImporter.Parse(rpc).Operations);
        var template = System.Xml.Linq.XElement.Parse(operation.Template);
        Assert.Equal("{urn:calc}Add", template.Name.ToString());
        Assert.Equal(["_a_", "_b_"], template.Elements().Select(e => e.Value).ToArray());
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<html><body/></html>")]
    public void Something_that_is_not_a_wsdl_gives_a_note_and_no_operations(string text)
    {
        var info = WsdlImporter.Parse(text);
        Assert.Empty(info.Operations);
        Assert.NotEmpty(info.Notes);
    }

    [Fact]
    public async Task The_mock_answers_each_operation_from_the_wsdl_and_serves_the_wsdl_with_its_own_address()
    {
        var info = WsdlImporter.Parse(Orders);
        var routes = WsdlMock.Routes(info, Orders);
        Assert.Equal(["SOAP GetOrder", "WSDL /Orders.svc"], routes.Select(r => r.Name).ToArray());
        var config = new MockServerConfig { Port = MockServerHost.FreePort(), Routes = routes };
        using var host = new MockServerHost(config); host.Start();
        using var client = new HttpClient();
        var soap = new StringContent("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><GetOrder xmlns=\"http://tempuri.org/\"><OrderId>7</OrderId></GetOrder></s:Body></s:Envelope>", Encoding.UTF8, "text/xml");
        var answer = await client.PostAsync($"http://127.0.0.1:{config.Port}/Orders.svc", soap);
        var body = await answer.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, answer.StatusCode);
        Assert.Contains("<Total xmlns=\"http://tempuri.org/\">1.5</Total>", body.Replace("<Total>", "<Total xmlns=\"http://tempuri.org/\">"));
        Assert.Contains("GetOrderResponse", body);

        var wsdl = await client.GetStringAsync($"http://127.0.0.1:{config.Port}/Orders.svc?wsdl");
        Assert.Contains($"http://127.0.0.1:{config.Port}/Orders.svc", wsdl);   // the address points at the mock itself
        Assert.DoesNotContain("http://host/Orders.svc", wsdl);
        // and the served WSDL reads back to the same operation
        Assert.Equal("GetOrder", Assert.Single(WsdlImporter.Parse(wsdl).Operations).Name);
    }

    [Fact]
    public void Only_the_chosen_operations_get_a_route()
    {
        var info = WsdlImporter.Parse(Orders);
        Assert.Empty(WsdlMock.Routes(info, Orders, ["Other"]));
        Assert.Single(WsdlMock.Routes(info, Orders, ["getorder"]).Where(r => r.Method == "POST"));
    }
}

public class WcfVerbValidationTests
{
    private static List<string> Problems(string verb)
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (config, request, response) =>
        {
            config.Cell(3, 1).Value = 1; config.Cell(3, 2).Value = "Flow"; config.Cell(3, 3).Value = "Call"; config.Cell(3, 4).Value = "WCF";
            config.Cell(3, 5).Value = "t.xml"; config.Cell(3, 6).Value = "http://h/s.svc"; config.Cell(3, 7).Value = verb; config.Cell(3, 8).Value = true;
        });
        return WorkbookValidator.Validate(WorkbookDocument.Load(path)).Select(p => p.Message).ToList();
    }

    [Fact]
    public void A_wcf_step_with_another_verb_than_POST_gets_a_note_instead_of_being_ignored_silently()
    {
        Assert.Contains(Problems("GET"), m => m.Contains("always send POST") && m.Contains("'GET'"));
        Assert.DoesNotContain(Problems("POST"), m => m.Contains("always send POST"));
    }
}
