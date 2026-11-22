namespace LoadTestingTool.UI;

/// <summary>The starter bodies of the SOAP button in the Mock server window.</summary>
internal static class SoapSample
{
    public const string Response =
        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>" +
        "<GetOrderResponse xmlns=\"http://tempuri.org/\"><Id>{{xml.GetOrder.OrderId}}</Id><Status>SHIPPED</Status><Total>59.90</Total></GetOrderResponse>" +
        "</s:Body></s:Envelope>";

    public const string Wsdl =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<wsdl:definitions xmlns:wsdl=\"http://schemas.xmlsoap.org/wsdl/\" xmlns:soap=\"http://schemas.xmlsoap.org/wsdl/soap/\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\" xmlns:tns=\"http://tempuri.org/\" targetNamespace=\"http://tempuri.org/\" name=\"Orders\">" +
        "<wsdl:types><xs:schema targetNamespace=\"http://tempuri.org/\" elementFormDefault=\"qualified\">" +
        "<xs:element name=\"GetOrder\"><xs:complexType><xs:sequence><xs:element name=\"OrderId\" type=\"xs:string\"/></xs:sequence></xs:complexType></xs:element>" +
        "<xs:element name=\"GetOrderResponse\"><xs:complexType><xs:sequence><xs:element name=\"Id\" type=\"xs:string\"/><xs:element name=\"Status\" type=\"xs:string\"/><xs:element name=\"Total\" type=\"xs:decimal\"/></xs:sequence></xs:complexType></xs:element>" +
        "</xs:schema></wsdl:types>" +
        "<wsdl:message name=\"GetOrderIn\"><wsdl:part name=\"parameters\" element=\"tns:GetOrder\"/></wsdl:message>" +
        "<wsdl:message name=\"GetOrderOut\"><wsdl:part name=\"parameters\" element=\"tns:GetOrderResponse\"/></wsdl:message>" +
        "<wsdl:portType name=\"IOrders\"><wsdl:operation name=\"GetOrder\"><wsdl:input message=\"tns:GetOrderIn\"/><wsdl:output message=\"tns:GetOrderOut\"/></wsdl:operation></wsdl:portType>" +
        "<wsdl:binding name=\"BasicHttpBinding_IOrders\" type=\"tns:IOrders\"><soap:binding transport=\"http://schemas.xmlsoap.org/soap/http\"/>" +
        "<wsdl:operation name=\"GetOrder\"><soap:operation soapAction=\"http://tempuri.org/IOrders/GetOrder\" style=\"document\"/><wsdl:input><soap:body use=\"literal\"/></wsdl:input><wsdl:output><soap:body use=\"literal\"/></wsdl:output></wsdl:operation></wsdl:binding>" +
        "<wsdl:service name=\"Orders\"><wsdl:port name=\"BasicHttpBinding_IOrders\" binding=\"tns:BasicHttpBinding_IOrders\"><soap:address location=\"http://{{header.Host}}/Orders.svc\"/></wsdl:port></wsdl:service>" +
        "</wsdl:definitions>";
}
