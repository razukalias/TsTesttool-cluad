using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LoadTestingTool.Excel;

namespace LoadTestingTool.Execution;

/// <summary>
/// Random numbers, GUIDs and dates written in the workbook. The workbook reader leaves a marker for each one (it does not
/// make the value); the runner turns the markers into values when a step runs. Every marker has its own id (cell and position),
/// so each cell, each token and each iteration gets a fresh value, while a reference to a cell
/// (<c>&lt;From_request_X_r&gt;</c>) carries the same marker and so gets the value that cell produced for this row and iteration.
/// </summary>
public static class GeneratedTokens
{
    private const char Open = '', Close = '', Sep = '\u001F';
    private static readonly Regex MarkerPattern = new($"{Open}([^{Sep}{Close}]*){Sep}([^{Sep}{Close}]*){Sep}([^{Sep}{Close}]*){Sep}([^{Close}]*){Close}", RegexOptions.Compiled);

    /// <summary>The values made so far for one data row and iteration (id to value); set by the runner around each row execution.</summary>
    public sealed class RowScope(string rowKey)
    {
        public string RowKey { get; } = rowKey;
        internal ConcurrentDictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    }

    public static AsyncLocal<RowScope?> Scope { get; } = new();

    [ThreadStatic] private static Dictionary<string, int>? _occurrences;

    /// <summary>Starts a (re)read of the workbook: positions are counted from the start again.</summary>
    public static void BeginRead() => _occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>The marker the workbook reader writes in place of a generated value.</summary>
    public static string Marker(string sheet, int? row, string rawCellText, string token, string kind, string? argument, string? owner)
    {
        _occurrences ??= new Dictionary<string, int>(StringComparer.Ordinal);
        // No "<" or ">" in the id: the reader's later token passes would otherwise find the token text inside a marker copied through a reference.
        static string Short(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
        var baseKey = $"{sheet}|{row}|{Short(rawCellText)}|{Short(token)}";
        var n = _occurrences.GetValueOrDefault(baseKey);
        _occurrences[baseKey] = n + 1;
        return $"{Open}{kind}{Sep}{argument}{Sep}{baseKey}|{n}{Sep}{owner}{Close}";
    }

    public static bool HasMarkers(string? text) => text is not null && text.Contains(Open);

    /// <summary>Replaces every marker with its value for the current row and iteration (made now if it was not needed before).</summary>
    public static string Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(Open)) return text ?? string.Empty;
        var scope = Scope.Value;
        return MarkerPattern.Replace(text, m =>
        {
            var kind = m.Groups[1].Value;
            var argument = m.Groups[2].Value.Length == 0 ? null : m.Groups[2].Value;
            var id = m.Groups[3].Value;
            var owner = m.Groups[4].Value.Length == 0 ? null : m.Groups[4].Value;
            if (scope is null) return Make(kind, argument);
            return scope.Values.GetOrAdd(id, _ => RunValueMemory.Active is { } memory
                ? memory.GeneratedFor(scope.RowKey, id, owner, () => Make(kind, argument))
                : Make(kind, argument));
        });
    }

    /// <summary>Turns markers back into the token the author wrote (for showing a not yet resolved value).</summary>
    public static string ToDisplay(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(Open)) return text ?? string.Empty;
        return MarkerPattern.Replace(text, m => m.Groups[2].Value.Length == 0 ? $"<{m.Groups[1].Value}>" : $"<{m.Groups[1].Value}:{m.Groups[2].Value}>");
    }

    /// <summary>Makes one value now.</summary>
    public static string Make(string kind, string? argument) => kind.ToLowerInvariant() switch
    {
        "randomnumber" => WorkbookReader.FormatRandomNumberToken(argument),
        "guid" => WorkbookReader.FormatGuidToken(argument),
        _ => WorkbookReader.FormatDateToken(kind, argument),
    };
}
