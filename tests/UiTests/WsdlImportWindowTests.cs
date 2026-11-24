using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using LoadTestingTool.Editing;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class WsdlImportWindowTests
{
    private const string Wsdl = """
        <wsdl:definitions xmlns:wsdl="http://schemas.xmlsoap.org/wsdl/" xmlns:soap="http://schemas.xmlsoap.org/wsdl/soap/" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:tns="http://t/" targetNamespace="http://t/" name="Orders">
          <wsdl:types><xs:schema targetNamespace="http://t/" elementFormDefault="qualified">
            <xs:element name="GetOrder"><xs:complexType><xs:sequence><xs:element name="Id" type="xs:int"/></xs:sequence></xs:complexType></xs:element>
            <xs:element name="GetOrderResponse"><xs:complexType><xs:sequence><xs:element name="Total" type="xs:decimal"/></xs:sequence></xs:complexType></xs:element>
          </xs:schema></wsdl:types>
          <wsdl:message name="In"><wsdl:part name="p" element="tns:GetOrder"/></wsdl:message>
          <wsdl:message name="Out"><wsdl:part name="p" element="tns:GetOrderResponse"/></wsdl:message>
          <wsdl:portType name="P"><wsdl:operation name="GetOrder"><wsdl:input message="tns:In"/><wsdl:output message="tns:Out"/></wsdl:operation></wsdl:portType>
          <wsdl:binding name="B" type="tns:P"><soap:binding transport="http://schemas.xmlsoap.org/soap/http"/><wsdl:operation name="GetOrder"><soap:operation soapAction="http://t/GetOrder"/><wsdl:input><soap:body use="literal"/></wsdl:input><wsdl:output><soap:body use="literal"/></wsdl:output></wsdl:operation></wsdl:binding>
          <wsdl:service name="Orders"><wsdl:port name="X" binding="tns:B"><soap:address location="http://h/Orders.svc"/></wsdl:port></wsdl:service>
        </wsdl:definitions>
        """;

    private static T Find<T>(Window w, string tag) where T : Control => w.GetVisualDescendants().OfType<T>().First(c => (string?)c.Tag == tag);

    [AvaloniaFact]
    public void The_window_lists_the_operations_and_hands_the_chosen_one_to_apply()
    {
        WsdlOperation? got = null;
        var window = new WsdlImportWindow("Import", "Use", false, (_, ops) => got = ops[0]);
        window.Show();
        window.LoadFrom(Wsdl, "x.wsdl");
        var list = Find<ListBox>(window, "wsdl-operations");
        Assert.Equal(1, list.ItemCount);
        Assert.False(Find<Button>(window, "wsdl-apply").IsEnabled);
        list.SelectedIndex = 0;
        Assert.True(Find<Button>(window, "wsdl-apply").IsEnabled);
        Assert.Contains("http://t/GetOrder", Find<TextBox>(window, "wsdl-preview").Text);
        Assert.Contains("_Id_", Find<TextBox>(window, "wsdl-preview").Text);
        window.GetVisualDescendants().OfType<Button>().First(b => (string?)b.Tag == "wsdl-apply").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("GetOrder", got?.Name);
    }

    [AvaloniaFact]
    public void Text_that_is_not_a_wsdl_is_reported_and_nothing_is_listed()
    {
        var window = new WsdlImportWindow("Import", "Use", false, (_, _) => { });
        window.Show();
        window.LoadFrom("<nope/>", "x");
        Assert.Contains("No SOAP operations", Find<TextBlock>(window, "wsdl-status").Text);
    }
}
