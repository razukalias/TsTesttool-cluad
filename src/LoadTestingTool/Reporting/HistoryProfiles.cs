using LoadTestingTool.Domain;

namespace LoadTestingTool.Reporting;

/// <summary>What a history/log profile sets. Profiles are chosen by hand; "Custom" is what any other combination is called.</summary>
public sealed record HistoryOptions(
    bool InternalLog, bool InternalLogErrorsOnly, bool AggregateHistory, bool RowHistory, bool SaveRequestBodies, bool SaveResponseBodies, bool MaskSensitive,
    string Detail, int SampleEvery, IReadOnlyDictionary<string, bool> Sheets, HistoryStorage Storage = HistoryStorage.Files);

public static class HistoryProfiles
{
    public const string DetailAll = "All";
    public const string DetailFailedAndSample = "FailedAndSample";
    public const int DefaultSampleEvery = 100;

    public const string Functional = "Functional", Debug = "Debug", LoadTest = "Load test", Custom = "Custom";
    public static readonly string[] Names = [Functional, Debug, LoadTest];

    private static readonly string[] LoadTestSheets = ["RunSummary", "Metrics", "TestcaseSummary", "RequestHistory", "AssertionHistory", "RuntimeReferenceErrors"];

    public static HistoryOptions For(string profile, int sampleEvery = DefaultSampleEvery) => profile switch
    {
        LoadTest => new HistoryOptions(true, true, true, false, true, true, true, DetailFailedAndSample, Math.Max(1, sampleEvery),
            HistorySheetCatalog.Names.ToDictionary(n => n, n => LoadTestSheets.Contains(n), StringComparer.OrdinalIgnoreCase)),
        // Functional keeps everything a person debugging a test needs; Debug is the same and is kept as its own name so it can grow apart.
        _ => new HistoryOptions(true, false, profile == Debug, true, true, true, true, DetailAll, DefaultSampleEvery, HistorySheetCatalog.CreateDefaults()),
    };

    /// <summary>The profile whose settings these are exactly, or "Custom".</summary>
    public static string Detect(HistoryOptions options)
    {
        foreach (var name in Names)
            if (Same(options, For(name, options.SampleEvery))) return name;
        return Custom;
    }

    private static bool Same(HistoryOptions a, HistoryOptions b)
        => a.InternalLog == b.InternalLog && a.InternalLogErrorsOnly == b.InternalLogErrorsOnly && a.AggregateHistory == b.AggregateHistory && a.RowHistory == b.RowHistory
           && a.SaveRequestBodies == b.SaveRequestBodies && a.SaveResponseBodies == b.SaveResponseBodies && a.MaskSensitive == b.MaskSensitive
           && string.Equals(a.Detail, b.Detail, StringComparison.OrdinalIgnoreCase)
           && HistorySheetCatalog.Names.All(n => HistorySheetCatalog.IsEnabled(a.Sheets, n) == HistorySheetCatalog.IsEnabled(b.Sheets, n));

    /// <summary>The requests that keep full detail in history: all of them, or (Load test) the failed ones and every <c>HistorySampleEvery</c>th passing one. Null means all.</summary>
    public static HashSet<RequestResult>? DetailedRequests(AppConfig config, IReadOnlyList<RequestResult> requests)
    {
        if (!string.Equals(config.HistoryDetail, DetailFailedAndSample, StringComparison.OrdinalIgnoreCase)) return null;
        var every = Math.Max(1, config.HistorySampleEvery);
        var keep = new HashSet<RequestResult>(ReferenceEqualityComparer.Instance as IEqualityComparer<RequestResult>);
        var passing = 0;
        foreach (var request in requests)
        {
            if (request.Result == "FAIL") { keep.Add(request); continue; }
            if (passing++ % every == 0) keep.Add(request);
        }
        return keep;
    }

    /// <summary>True when a run is big enough that the Functional profile will be slow and produce very large history (threads x iterations x rows).</summary>
    public static bool RunIsLarge(long threads, long iterations, long rows) => Math.Max(1, threads) * Math.Max(1, iterations) * Math.Max(1, rows) >= 1000;
}
