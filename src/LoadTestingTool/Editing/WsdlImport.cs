using System.Globalization;
using System.Xml.Linq;

namespace LoadTestingTool.Editing;

/// <summary>One operation of a WSDL as a WCF step needs it: where to post, the SOAP action and version, a request body with <c>_name_</c> placeholders, and a sample answer.</summary>
public sealed record WsdlOperation(string Name, string Binding, string SoapAction, string SoapVersion, string Endpoint, string Namespace, string Template, string ResponseSample, IReadOnlyList<string> Variables)
{
    /// <summary>The name shown in a list: the operation, and the binding when the WSDL has several.</summary>
    public string Display(bool severalBindings) => severalBindings ? $"{Name}  ({Binding})" : Name;
}

public sealed class WsdlInfo
{
    public string ServiceName { get; init; } = string.Empty;
    public List<WsdlOperation> Operations { get; } = [];

    /// <summary>What could not be followed or was simplified (an import that could not be read, a choice that took its first option …).</summary>
    public List<string> Notes { get; } = [];
}

/// <summary>
/// Reads a WSDL 1.1 (with its imported WSDL and XSD files) into the operations of its SOAP bindings. Document/literal and RPC/literal are covered;
/// the request body is a skeleton of the input element with a <c>_name_</c> placeholder for every value, so the columns of a testcase can fill it.
/// </summary>
public static class WsdlImporter
{
    private static readonly XNamespace Wsdl = "http://schemas.xmlsoap.org/wsdl/";
    private static readonly XNamespace Soap11 = "http://schemas.xmlsoap.org/wsdl/soap/";
    private static readonly XNamespace Soap12 = "http://schemas.xmlsoap.org/wsdl/soap12/";
    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";
    private const int MaxDepth = 8;
    private const int MaxFiles = 40;

    /// <param name="wsdl">The text of the WSDL.</param>
    /// <param name="location">Where it came from (a URL or a file path); imports are resolved against it.</param>
    /// <param name="load">Reads an imported file by its absolute location; null (or a null result) means it could not be read.</param>
    public static WsdlInfo Parse(string wsdl, string? location = null, Func<string, string?>? load = null)
    {
        var info = new WsdlInfo();
        XDocument root;
        try { root = XDocument.Parse(wsdl); }
        catch (System.Xml.XmlException ex) { info.Notes.Add($"The text is not XML: {ex.Message}"); return info; }
        if (root.Root?.Name != Wsdl + "definitions") { info.Notes.Add("This is not a WSDL 1.1 document (no wsdl:definitions at the top)."); return info; }

        var documents = new List<XElement>();
        var schemas = new List<XElement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = 0;

        void Collect(XElement definitions, string? where)
        {
            documents.Add(definitions);
            foreach (var schema in definitions.Elements(Wsdl + "types").Elements(Xs + "schema")) CollectSchema(schema, where);
            foreach (var import in definitions.Elements(Wsdl + "import"))
                Follow((string?)import.Attribute("location"), where, text => { var doc = TryParse(text, info, where); if (doc?.Root is { } r && r.Name == Wsdl + "definitions") Collect(r, Resolve(where, (string?)import.Attribute("location"))); });
        }

        void CollectSchema(XElement schema, string? where)
        {
            schemas.Add(schema);
            foreach (var reference in schema.Elements().Where(e => e.Name == Xs + "import" || e.Name == Xs + "include"))
                Follow((string?)reference.Attribute("schemaLocation"), where, text => { var doc = TryParse(text, info, where); if (doc?.Root is { } r && r.Name == Xs + "schema") CollectSchema(r, Resolve(where, (string?)reference.Attribute("schemaLocation"))); });
        }

        void Follow(string? target, string? where, Action<string> read)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            var absolute = Resolve(where, target);
            if (absolute is null || !seen.Add(absolute)) return;
            if (++files > MaxFiles) { info.Notes.Add("Too many imported files; the rest were skipped."); return; }
            var text = load?.Invoke(absolute);
            if (text is null) { info.Notes.Add($"Could not read the imported file {target}; types defined there are shown as plain values."); return; }
            read(text);
        }

        Collect(root.Root, location);

        var model = new Model(schemas, documents);
        info = Build(model, info);
        return info;
    }

    private static XDocument? TryParse(string text, WsdlInfo info, string? where)
    {
        try { return XDocument.Parse(text); }
        catch (System.Xml.XmlException) { info.Notes.Add($"An imported file could not be read as XML{(where is null ? string.Empty : " (near " + where + ")")}."); return null; }
    }

    private static string? Resolve(string? baseLocation, string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute)) return absolute.IsFile ? absolute.LocalPath : absolute.ToString();
        if (string.IsNullOrWhiteSpace(baseLocation)) return null;
        if (Uri.TryCreate(baseLocation, UriKind.Absolute, out var baseUri) && baseUri.Scheme is "http" or "https")
            return Uri.TryCreate(baseUri, target, out var combined) ? combined.ToString() : null;
        var folder = Path.GetDirectoryName(baseLocation) ?? string.Empty;
        return Path.GetFullPath(Path.Combine(folder, target));
    }

    // ---- The model: messages, port types, bindings, services and the schema -----------------------------------------------

    private sealed class Model(List<XElement> schemas, List<XElement> definitions)
    {
        public List<XElement> Definitions { get; } = definitions;
        public Dictionary<XName, (XElement Element, XElement Schema)> Elements { get; } = Index(schemas, "element");
        public Dictionary<XName, (XElement Element, XElement Schema)> ComplexTypes { get; } = Index(schemas, "complexType");
        public Dictionary<XName, (XElement Element, XElement Schema)> SimpleTypes { get; } = Index(schemas, "simpleType");
        public Dictionary<XName, (XElement Element, XElement Schema)> Groups { get; } = Index(schemas, "group");

        private static Dictionary<XName, (XElement, XElement)> Index(List<XElement> schemas, string kind)
        {
            var map = new Dictionary<XName, (XElement, XElement)>();
            foreach (var schema in schemas)
            {
                var ns = (string?)schema.Attribute("targetNamespace") ?? string.Empty;
                foreach (var item in schema.Elements(Xs + kind))
                    if ((string?)item.Attribute("name") is { } name) map.TryAdd(XName.Get(name, ns), (item, schema));
            }
            return map;
        }
    }

    private static XName? QName(XElement context, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var colon = value.IndexOf(':');
        var prefix = colon < 0 ? string.Empty : value[..colon];
        var local = colon < 0 ? value : value[(colon + 1)..];
        var ns = colon < 0 ? context.GetDefaultNamespace().NamespaceName : context.GetNamespaceOfPrefix(prefix)?.NamespaceName ?? string.Empty;
        return XName.Get(local, ns);
    }

    private static WsdlInfo Build(Model model, WsdlInfo info)
    {
        var messages = new Dictionary<XName, XElement>();
        var portTypes = new Dictionary<XName, XElement>();
        var bindings = new List<(XName Name, XElement Binding, string Target)>();
        var addresses = new Dictionary<XName, string>();
        string serviceName = string.Empty;
        foreach (var definitions in model.Definitions)
        {
            var target = (string?)definitions.Attribute("targetNamespace") ?? string.Empty;
            foreach (var m in definitions.Elements(Wsdl + "message")) messages.TryAdd(XName.Get((string)m.Attribute("name")!, target), m);
            foreach (var p in definitions.Elements(Wsdl + "portType")) portTypes.TryAdd(XName.Get((string)p.Attribute("name")!, target), p);
            foreach (var b in definitions.Elements(Wsdl + "binding")) bindings.Add((XName.Get((string)b.Attribute("name")!, target), b, target));
            foreach (var service in definitions.Elements(Wsdl + "service"))
            {
                if (serviceName.Length == 0) serviceName = (string?)service.Attribute("name") ?? string.Empty;
                foreach (var port in service.Elements(Wsdl + "port"))
                {
                    var binding = QName(port, (string?)port.Attribute("binding"));
                    var address = port.Elements().FirstOrDefault(e => e.Name.LocalName == "address" && e.Name.Namespace != Wsdl)?.Attribute("location")?.Value;
                    if (binding is not null && address is not null) addresses.TryAdd(binding, address);
                }
            }
        }

        var soapBindings = bindings.Select(b => (b, Soap: b.Binding.Elements().FirstOrDefault(e => e.Name == Soap11 + "binding" || e.Name == Soap12 + "binding"))).Where(x => x.Soap is not null).ToList();
        var result = new WsdlInfo { ServiceName = serviceName };
        result.Notes.AddRange(info.Notes);
        foreach (var ((name, binding, _), soap) in soapBindings)
        {
            var version = soap!.Name == Soap12 + "binding" ? "1.2" : "1.1";
            var defaultStyle = (string?)soap.Attribute("style") ?? "document";
            var portType = QName(binding, (string?)binding.Attribute("type")) is { } pt && portTypes.TryGetValue(pt, out var found) ? found : null;
            addresses.TryGetValue(name, out var endpoint);
            foreach (var operation in binding.Elements(Wsdl + "operation"))
            {
                var opName = (string?)operation.Attribute("name") ?? string.Empty;
                var soapOperation = operation.Elements().FirstOrDefault(e => e.Name == Soap11 + "operation" || e.Name == Soap12 + "operation");
                var action = (string?)soapOperation?.Attribute("soapAction") ?? string.Empty;
                var style = (string?)soapOperation?.Attribute("style") ?? defaultStyle;
                var portOperation = portType?.Elements(Wsdl + "operation").FirstOrDefault(o => (string?)o.Attribute("name") == opName);
                var input = MessageOf(portOperation?.Element(Wsdl + "input"), messages);
                var output = MessageOf(portOperation?.Element(Wsdl + "output"), messages);
                var bodyNamespace = operation.Element(Wsdl + "input")?.Elements().FirstOrDefault(e => e.Name.LocalName == "body")?.Attribute("namespace")?.Value ?? (string?)binding.Document?.Root?.Attribute("targetNamespace") ?? string.Empty;
                var variables = new List<string>();
                var template = Skeleton(model, input, style, opName, bodyNamespace, result.Notes, request: true, variables);
                var sample = Skeleton(model, output, style, opName + "Response", bodyNamespace, result.Notes, request: false, null);
                result.Operations.Add(new WsdlOperation(opName, name.LocalName, action, version, endpoint ?? string.Empty, bodyNamespace, template, sample, variables));
            }
        }
        if (result.Operations.Count == 0 && result.Notes.Count == 0) result.Notes.Add("The WSDL has no operation in a SOAP binding.");
        return result;
    }

    private static XElement? MessageOf(XElement? reference, Dictionary<XName, XElement> messages)
        => reference is not null && QName(reference, (string?)reference.Attribute("message")) is { } name && messages.TryGetValue(name, out var message) ? message : null;

    // ---- Skeletons --------------------------------------------------------------------------------------------------------

    private sealed class Builder(Model model, List<string> notes, bool request, List<string>? variables)
    {
        private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
        public Model Model { get; } = model;
        public List<string> Notes { get; } = notes;

        public string Leaf(string path, string name, string typeLocal)
        {
            if (!request) return Sample(typeLocal);
            var placeholder = name;
            if (!_used.Add(placeholder))
            {
                placeholder = path;   // the same name twice: tell them apart by their path (and a number when even that repeats)
                var n = 2;
                while (!_used.Add(placeholder)) placeholder = $"{path}{n++}";
            }
            variables?.Add(placeholder);
            return $"_{placeholder}_";
        }

        private static string Sample(string type) => type.ToLowerInvariant() switch
        {
            "int" or "integer" or "long" or "short" or "byte" or "unsignedint" or "unsignedlong" or "unsignedshort" or "unsignedbyte" or "nonnegativeinteger" or "positiveinteger" => "1",
            "decimal" or "double" or "float" => "1.5",
            "boolean" => "true",
            "datetime" => "2024-01-01T00:00:00",
            "date" => "2024-01-01",
            "time" => "00:00:00",
            "guid" => "00000000-0000-0000-0000-000000000000",
            _ => "text",
        };
    }

    private static string Skeleton(Model model, XElement? message, string style, string operation, string bodyNamespace, List<string> notes, bool request, List<string>? variables)
    {
        if (message is null) return string.Empty;
        var builder = new Builder(model, notes, request, variables);
        var parts = message.Elements(Wsdl + "part").ToList();
        var pieces = new List<XElement>();
        if (style.Equals("rpc", StringComparison.OrdinalIgnoreCase))
        {
            var wrapper = new XElement(XNamespace.Get(bodyNamespace) + operation);
            foreach (var part in parts)
            {
                var name = (string?)part.Attribute("name") ?? "part";
                var typeName = QName(part, (string?)part.Attribute("type"));
                var child = new XElement(name);
                if (typeName is not null && model.ComplexTypes.TryGetValue(typeName, out var complex)) FillComplex(builder, child, complex.Element, complex.Schema, name, new HashSet<XName> { typeName }, 1);
                else child.Value = builder.Leaf(name, name, typeName?.LocalName ?? "string");
                wrapper.Add(child);
            }
            pieces.Add(wrapper);
        }
        else
        {
            foreach (var part in parts)
            {
                if (QName(part, (string?)part.Attribute("element")) is { } elementName && model.Elements.TryGetValue(elementName, out var declaration))
                    pieces.Add(BuildElement(builder, declaration.Element, declaration.Schema, string.Empty, new HashSet<XName>(), 0, topLevel: true));
                else if ((string?)part.Attribute("name") is { } partName)
                {
                    var typeName = QName(part, (string?)part.Attribute("type"));
                    var holder = new XElement(partName);
                    if (typeName is not null && model.ComplexTypes.TryGetValue(typeName, out var complex)) FillComplex(builder, holder, complex.Element, complex.Schema, partName, new HashSet<XName> { typeName }, 1);
                    else holder.Value = builder.Leaf(partName, partName, typeName?.LocalName ?? "string");
                    pieces.Add(holder);
                }
            }
        }
        return string.Join(Environment.NewLine, pieces.Select(p => p.ToString()));
    }

    private static string TargetNamespace(XElement schema) => (string?)schema.Attribute("targetNamespace") ?? string.Empty;
    private static bool Qualified(XElement schema) => string.Equals((string?)schema.Attribute("elementFormDefault"), "qualified", StringComparison.OrdinalIgnoreCase);

    private static XElement BuildElement(Builder builder, XElement declaration, XElement schema, string parentPath, HashSet<XName> active, int depth, bool topLevel)
    {
        // a reference to a global element takes that element's declaration
        if (QName(declaration, (string?)declaration.Attribute("ref")) is { } reference && builder.Model.Elements.TryGetValue(reference, out var global))
            return BuildElement(builder, global.Element, global.Schema, parentPath, active, depth, topLevel: true);

        var name = (string?)declaration.Attribute("name") ?? "item";
        var form = (string?)declaration.Attribute("form");
        var qualified = topLevel || (form is not null ? form == "qualified" : Qualified(schema));
        var element = new XElement(qualified ? XNamespace.Get(TargetNamespace(schema)) + name : XName.Get(name));
        var path = parentPath.Length == 0 ? name : $"{parentPath}.{name}";
        var typeName = QName(declaration, (string?)declaration.Attribute("type"));
        var anonymous = declaration.Element(Xs + "complexType");
        if (anonymous is not null) { FillComplex(builder, element, anonymous, schema, path, active, depth + 1); return element; }
        if (typeName is not null && builder.Model.ComplexTypes.TryGetValue(typeName, out var complex))
        {
            if (active.Contains(typeName) || depth >= MaxDepth) { element.Value = string.Empty; return element; }   // a type that contains itself: stop here
            var next = new HashSet<XName>(active) { typeName };
            FillComplex(builder, element, complex.Element, complex.Schema, path, next, depth + 1);
            return element;
        }
        var simpleBase = typeName?.LocalName ?? (declaration.Element(Xs + "simpleType") is { } simple ? (QName(simple, (string?)simple.Element(Xs + "restriction")?.Attribute("base"))?.LocalName ?? "string") : "string");
        if (typeName is not null && builder.Model.SimpleTypes.TryGetValue(typeName, out var st)) simpleBase = QName(st.Element, (string?)st.Element.Element(Xs + "restriction")?.Attribute("base"))?.LocalName ?? "string";
        element.Value = builder.Leaf(path, name, simpleBase);
        return element;
    }

    private static void FillComplex(Builder builder, XElement target, XElement complex, XElement schema, string path, HashSet<XName> active, int depth)
    {
        if (depth > MaxDepth) return;
        foreach (var attribute in complex.Descendants(Xs + "attribute").Where(a => !a.Ancestors(Xs + "element").Any(e => e != complex && e.Ancestors().Contains(complex))))
            if ((string?)attribute.Attribute("name") is { } attributeName && !(string.Equals((string?)attribute.Attribute("use"), "prohibited", StringComparison.OrdinalIgnoreCase)))
                target.SetAttributeValue(attributeName, builder.Leaf($"{path}.@{attributeName}", attributeName, QName(attribute, (string?)attribute.Attribute("type"))?.LocalName ?? "string"));
        foreach (var content in complex.Elements())
        {
            if (content.Name == Xs + "sequence" || content.Name == Xs + "all") Particles(builder, target, content, schema, path, active, depth);
            else if (content.Name == Xs + "choice") { builder.Notes.Add($"{path}: a choice between elements; the first one is used."); Particles(builder, target, content, schema, path, active, depth, firstOnly: true); }
            else if (content.Name == Xs + "complexContent" || content.Name == Xs + "simpleContent")
            {
                var derived = content.Elements().FirstOrDefault(e => e.Name == Xs + "extension" || e.Name == Xs + "restriction");
                if (derived is null) continue;
                if (content.Name == Xs + "simpleContent") { target.Value = builder.Leaf(path, path.Split('.').Last(), QName(derived, (string?)derived.Attribute("base"))?.LocalName ?? "string"); }
                else if (derived.Name == Xs + "extension" && QName(derived, (string?)derived.Attribute("base")) is { } baseName && builder.Model.ComplexTypes.TryGetValue(baseName, out var baseType) && !active.Contains(baseName))
                    FillComplex(builder, target, baseType.Element, baseType.Schema, path, new HashSet<XName>(active) { baseName }, depth + 1);
                foreach (var inner in derived.Elements().Where(e => e.Name == Xs + "sequence" || e.Name == Xs + "all" || e.Name == Xs + "choice"))
                    Particles(builder, target, inner, schema, path, active, depth, firstOnly: inner.Name == Xs + "choice");
                foreach (var attribute in derived.Elements(Xs + "attribute"))
                    if ((string?)attribute.Attribute("name") is { } attributeName) target.SetAttributeValue(attributeName, builder.Leaf($"{path}.@{attributeName}", attributeName, QName(attribute, (string?)attribute.Attribute("type"))?.LocalName ?? "string"));
            }
        }
    }

    private static void Particles(Builder builder, XElement target, XElement group, XElement schema, string path, HashSet<XName> active, int depth, bool firstOnly = false)
    {
        foreach (var particle in group.Elements())
        {
            if (particle.Name == Xs + "element") { target.Add(BuildElement(builder, particle, schema, path, active, depth, topLevel: false)); if (firstOnly) return; }
            else if (particle.Name == Xs + "sequence" || particle.Name == Xs + "all") Particles(builder, target, particle, schema, path, active, depth);
            else if (particle.Name == Xs + "choice") Particles(builder, target, particle, schema, path, active, depth, firstOnly: true);
            else if (particle.Name == Xs + "group" && QName(particle, (string?)particle.Attribute("ref")) is { } groupName && builder.Model.Groups.TryGetValue(groupName, out var named))
                foreach (var inner in named.Element.Elements().Where(e => e.Name == Xs + "sequence" || e.Name == Xs + "all" || e.Name == Xs + "choice")) Particles(builder, target, inner, named.Schema, path, active, depth, firstOnly: inner.Name == Xs + "choice");
        }
    }
}
