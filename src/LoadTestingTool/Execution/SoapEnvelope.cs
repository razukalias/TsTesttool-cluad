using System.Security;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LoadTestingTool.Domain;

namespace LoadTestingTool.Execution;

/// <summary>The settings of a WCF (SOAP over HTTP) step, from its StepConfig.</summary>
public sealed class WcfStepOptions
{
    /// <summary>The SOAPAction of the operation, for example http://tempuri.org/IOrders/GetOrder.</summary>
    public string SoapAction { get; set; } = string.Empty;
    /// <summary>1.1 (basicHttpBinding, the default) or 1.2 (wsHttpBinding without message security).</summary>
    public string SoapVersion { get; set; } = "1.1";
    /// <summary>Extra XML put inside the SOAP header (for example a custom header element).</summary>
    public string HeaderXml { get; set; } = string.Empty;
    /// <summary>With a Username a WS-Security UsernameToken (plain text password) is added to the header.</summary>
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Builds the SOAP envelope of a WCF step and finds SOAP faults in an answer.</summary>
public static class SoapEnvelope
{
    public const string Ns11 = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string Ns12 = "http://www.w3.org/2003/05/soap-envelope";
    private const string WsseNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private const string PasswordTextType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordText";

    public static bool Is12(string? version) => (version ?? "").Trim() is "1.2" or "12" or "soap12";

    public static WcfStepOptions Options(RequestStep step)
    {
        if (string.IsNullOrWhiteSpace(step.StepConfig) || step.StepConfig.Trim() == "{}") return new WcfStepOptions();
        return StepConfig.Read<WcfStepOptions>(step);
    }

    /// <summary>The Content-Type to send: text/xml for SOAP 1.1, application/soap+xml with the action for SOAP 1.2.</summary>
    public static string ContentType(WcfStepOptions options)
        => Is12(options.SoapVersion) ? "application/soap+xml; charset=utf-8" + (options.SoapAction.Length > 0 ? $"; action=\"{options.SoapAction}\"" : "") : "text/xml; charset=utf-8";

    /// <summary>
    /// A template that already is an Envelope is sent as it is; anything else is the body (one or more elements) and is put in an envelope,
    /// with the header settings added.
    /// </summary>
    public static string Wrap(string template, WcfStepOptions options)
    {
        var trimmed = StripDeclaration(template).Trim();
        if (IsEnvelope(trimmed)) return trimmed;
        var ns = Is12(options.SoapVersion) ? Ns12 : Ns11;
        var header = new System.Text.StringBuilder();
        if (options.Username.Length > 0)
            header.Append($"<wsse:Security xmlns:wsse=\"{WsseNs}\"><wsse:UsernameToken><wsse:Username>{SecurityElement.Escape(options.Username)}</wsse:Username><wsse:Password Type=\"{PasswordTextType}\">{SecurityElement.Escape(options.Password)}</wsse:Password></wsse:UsernameToken></wsse:Security>");
        header.Append(options.HeaderXml);
        return $"<soap:Envelope xmlns:soap=\"{ns}\">" + (header.Length > 0 ? $"<soap:Header>{header}</soap:Header>" : "") + $"<soap:Body>{trimmed}</soap:Body></soap:Envelope>";
    }

    /// <summary>The envelope for logs and history: the WS-Security password is replaced by ***.</summary>
    public static string MaskSecrets(string envelope) => Regex.Replace(envelope, @"(<wsse:Password\b[^>]*>)[^<]*(</wsse:Password>)", "$1***$2");

    public static string StripDeclaration(string xml) => Regex.Replace(xml, @"^\s*<\?xml[^>]*\?>", "", RegexOptions.IgnoreCase);

    public static bool IsEnvelope(string xml)
    {
        try { return XDocument.Parse(xml).Root is { } root && root.Name.LocalName == "Envelope" && (root.Name.NamespaceName is Ns11 or Ns12); }
        catch (System.Xml.XmlException) { return false; }
    }

    /// <summary>The fault in a SOAP answer as "code: text", or null when the answer is not a fault (or not XML).</summary>
    public static string? FaultOf(string responseXml)
    {
        if (string.IsNullOrWhiteSpace(responseXml) || !responseXml.TrimStart().StartsWith('<')) return null;
        try
        {
            var fault = XDocument.Parse(responseXml).Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault" && (e.Name.NamespaceName is Ns11 or Ns12) && e.Parent?.Name.LocalName == "Body");
            if (fault is null) return null;
            string? Part(string name) => fault.Descendants().FirstOrDefault(e => e.Name.LocalName == name && !e.HasElements)?.Value.Trim();
            var code = Part("faultcode") ?? Part("Value") ?? "Fault";
            var text = Part("faultstring") ?? Part("Text") ?? "(no fault text)";
            return $"{code}: {text}";
        }
        catch (System.Xml.XmlException) { return null; }
    }
}
