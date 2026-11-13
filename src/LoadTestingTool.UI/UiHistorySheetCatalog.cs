namespace LoadTestingTool.UI;

internal static class UiHistorySheetCatalog
{
    public static readonly string[] Names =
    [
        "RunSummary", "Metrics", "TestcaseSummary", "RequestHistory", "RuntimeReferenceErrors",
        "AssertionHistory", "Variables", "CorrelationHistory", "DataEngineRequestRows", "DataEngineConfig"
    ];

    public static Dictionary<string, bool> CreateDefaults() => Names.ToDictionary(x => x, _ => true, StringComparer.OrdinalIgnoreCase);
}
