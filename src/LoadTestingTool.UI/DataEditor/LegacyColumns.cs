namespace LoadTestingTool.UI.DataEditor;

/// <summary>Config-sheet columns from before threads, iterations, their intervals and environments became global settings; steps no longer use them.</summary>
public static class LegacyColumns
{
    private static readonly HashSet<string> Names = new(["outputvariable", "threads", "thread", "iterations", "iteration", "environments", "environment", "threadintervalms", "iterationintervalms"], StringComparer.Ordinal);

    public static bool IsLegacy(string header) => Names.Contains(new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant());
}
