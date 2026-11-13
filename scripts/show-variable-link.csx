// Workbook request-sheet variable expected:
//   url = https://example.com/items/123
//
// The runner exposes the variable as Runtime.Get("url").
// Runtime.Get("_url_") is also supported as a fallback.

var rawUrl = Runtime.Get("url") ?? Runtime.Get("_url_");
var url = Convert.ToString(rawUrl)?.Trim() ?? string.Empty;

if (string.IsNullOrWhiteSpace(url))
{
    throw new InvalidOperationException(
        "The workbook variable 'url' is empty. Add a request-sheet column named 'url'.");
}

if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) ||
    (parsedUrl.Scheme != Uri.UriSchemeHttp && parsedUrl.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException(
        $"The workbook variable 'url' is not a valid HTTP/HTTPS URL: {url}");
}

var message = $"Open this link: {parsedUrl.AbsoluteUri}";
var markdownLink = $"[Open link]({parsedUrl.AbsoluteUri})";

// These lines appear in the internal log and Step Details viewer.
Runtime.Log(message);
Runtime.Log(markdownLink);

return message;
