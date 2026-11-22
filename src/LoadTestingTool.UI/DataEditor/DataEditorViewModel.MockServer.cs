using LoadTestingTool.MockServer;

namespace LoadTestingTool.UI.DataEditor;

public sealed partial class DataEditorViewModel
{
    /// <summary>The distinct server addresses (scheme://host:port) in the targetUrl cells of every testcase.</summary>
    public IReadOnlyList<string> UrlOrigins()
        => _document.Config.Rows.Where(r => !r.IsBlank).Select(r => MockUrls.OriginOf(r.Get("targetUrl"))).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Points the targetUrl of every step (all testcases) at <paramref name="origin"/>, keeping the path, the query and any placeholders.
    /// Only local addresses (127.0.0.1, localhost) unless <paramref name="includeRemote"/>. One undo step; returns how many steps changed.
    /// </summary>
    public int PointUrlsAt(string origin, bool includeRemote)
    {
        var keep = SelectedTestcase?.Index;
        var made = RunBatch($"point URLs at {origin}", () =>
        {
            var count = 0;
            foreach (var row in _document.Config.Rows.Where(r => !r.IsBlank))
                if (MockUrls.Repoint(row.Get("targetUrl"), origin, includeRemote) is { } fresh) { row.Set("targetUrl", fresh); count++; }
            return count;
        });
        if (made == 0)
        {
            ShowStatus(includeRemote ? $"No step has an http(s) URL that needs changing to {origin}." : $"No step has a local URL (127.0.0.1 or localhost) that needs changing to {origin}.", isError: false);
            return 0;
        }
        MarkChanged();
        LoadTestcases(keep);
        ShowStatus($"Pointed {made} {(made == 1 ? "step" : "steps")} at {origin}. Undo (Ctrl+Z) puts the old URLs back; Save to keep it.", isError: false);
        return made;
    }
}
