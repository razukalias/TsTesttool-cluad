using LoadTestingTool.Execution;
using LoadTestingTool.Reporting;
using Xunit;

namespace UiTests;

public class XmlResponseTreeTests
{
    private const string Envelope = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:t="http://tempuri.org/">
          <soap:Header><t:Trace id="h1">abc</t:Trace></soap:Header>
          <soap:Body>
            <t:GetOrderResponse status="ok">
              <t:Id>42</t:Id>
              <t:Price currency="USD">10.5</t:Price>
              <t:Lines>
                <t:Line sku="a1"><t:Qty>1</t:Qty></t:Line>
                <t:Line sku="b2"><t:Qty>3</t:Qty></t:Line>
              </t:Lines>
              <t:Note></t:Note>
            </t:GetOrderResponse>
          </soap:Body>
        </soap:Envelope>
        """;

    private const string Fault = """
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultcode>s:Server</faultcode><faultstring>Boom</faultstring></s:Fault></s:Body></s:Envelope>
        """;

    private const string Plain = "<orders><order id=\"1\"><name>A</name></order><order id=\"2\"><name>B</name></order></orders>";

    private static Dictionary<string, string> TreeLeaves(string xml)
    {
        var root = ResponseTree.ParseXml(xml, out _)!;
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Walk(ResponseNode node)
        {
            if (node.IsLeaf && node.Path.Length > 0) all[node.Path] = node.Value;
            foreach (var child in node.Children) Walk(child);
        }
        Walk(root);
        return all;
    }

    // The tree and the runner must agree on every path: this is what makes a ticked node a working expected column.
    [Theory]
    [InlineData(Envelope)]
    [InlineData(Fault)]
    [InlineData(Plain)]
    public void The_tree_has_exactly_the_paths_the_runner_reads(string xml)
    {
        var runner = new AssertionEngine().Normalize(xml, "xml");
        var tree = TreeLeaves(xml);
        Assert.Equal(runner.OrderBy(p => p.Key, StringComparer.Ordinal), tree.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void A_soap_envelope_is_read_from_its_body_with_header_elements_apart()
    {
        var tree = TreeLeaves(Envelope);
        Assert.Equal("ok", tree["GetOrderResponse.@status"]);
        Assert.Equal("10.5", tree["GetOrderResponse.Price"]);
        Assert.Equal("USD", tree["GetOrderResponse.Price.@currency"]);
        Assert.Equal("b2", tree["GetOrderResponse.Lines.Line[1].@sku"]);
        Assert.Equal("abc", tree["Header.Trace"]);
        Assert.Equal("Boom", TreeLeaves(Fault)["Fault.faultstring"]);
    }

    [Fact]
    public void Ticking_nodes_makes_headers_with_the_runner_paths_and_flatten_includes_attributes()
    {
        var root = ResponseTree.ParseAny(Envelope, out var note)!;
        Assert.Null(note);
        var choices = new Dictionary<string, NodeChoice>
        {
            ["GetOrderResponse.Price"] = new(NodeMode.AsOne),
            ["GetOrderResponse.Lines.Line"] = new(NodeMode.Flatten, 0, 2),
        };
        var headers = ResponseTree.Generate(root, choices, out _, out _).Select(h => (h.Path, h.Value)).ToList();
        Assert.Contains(("GetOrderResponse.Price", "10.5"), headers);
        Assert.DoesNotContain(headers, h => h.Path == "GetOrderResponse.Price.@currency");   // ticking the element takes the element only
        Assert.Contains(("GetOrderResponse.Lines.Line[0].@sku", "a1"), headers);
        Assert.Contains(("GetOrderResponse.Lines.Line[1].Qty", "3"), headers);
        var flat = ResponseTree.Generate(root, new Dictionary<string, NodeChoice> { ["GetOrderResponse"] = new(NodeMode.Flatten) }, out _, out _).Select(h => h.Path).ToList();
        Assert.Contains("GetOrderResponse.Price.@currency", flat);   // flattening the parent takes the attribute of a leaf too
        Assert.Contains("GetOrderResponse.@status", flat);
        // and each path works as an expected column on a response of the same shape
        Assert.Equal("a1", ResponseTree.ValueAt(root, "GetOrderResponse.Lines.Line[0].@sku"));
    }

    [Fact]
    public void Xml_nodes_are_marked_and_text_that_is_neither_json_nor_xml_has_no_tree()
    {
        Assert.True(ResponseTree.ParseAny(Plain, out _)!.Xml);
        Assert.False(ResponseTree.ParseAny("{\"a\":1}", out _)!.Xml);
        Assert.Null(ResponseTree.ParseAny("just text", out var note));
        Assert.Contains("no tree", note);
        Assert.Null(ResponseTree.ParseAny("<broken", out var broken));
        Assert.Contains("XML", broken);
    }

    [Fact]
    public void Response_fields_for_the_from_menu_include_xml_responses()
    {
        var fields = ResponseSamples.Flatten(Envelope);
        Assert.Contains(fields, f => f.Path == "GetOrderResponse.Lines.Line[1].@sku" && f.Value == "b2");
    }

    [Fact]
    public void The_raw_view_indents_xml_and_json_and_leaves_other_text_alone()
    {
        Assert.Contains("\n", ResponseText.Pretty("<a><b>1</b></a>"));
        Assert.Contains("\n", ResponseText.Pretty("{\"a\":{\"b\":1}}"));
        Assert.Equal("plain", ResponseText.Pretty("plain"));
    }
}
