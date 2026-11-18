using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LoadTestingTool.UI;

/// <summary>How a tab's text is coloured: plain text, "key  value" lines, or timestamped log lines.</summary>
public enum SectionKind { Plain, KeyValue, Log }

/// <summary>One tab of the step details window.</summary>
public sealed record StepSection(string Title, string Text, SectionKind Kind = SectionKind.Plain);

/// <summary>
/// Turns the internal log of one step into readable tabs. The log is a stream of entries (a line with a time stamp, a level and the
/// instance, followed by continuation lines); every entry the runner writes has a known shape, so each tab is built from the entries
/// and fields that belong to it instead of from keyword matching.
/// </summary>
public static class StepLogView
{
    private static readonly Regex Prefix = new(@"^(?<t>\d{4}-\d{2}-\d{2}T\S+) \[(?<lvl>[A-Z]+)\] \[(?<inst>[^\]]*)\] \[run=(?<run>[^\]]*)\] ?(?<msg>.*)$", RegexOptions.Compiled);
    private static readonly Regex KeyName = new(@"^[A-Za-z][A-Za-z0-9_\-]*$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal sealed record Entry(DateTimeOffset? Time, string Level, IReadOnlyList<string> Lines)
    {
        public string Head => Lines.Count == 0 ? string.Empty : Lines[0];
        public bool Is(string prefix) => Head.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The titles of the input and output tabs for a step type.</summary>
    public static (string Input, string Output) TabNames(string stepType) => stepType.Trim().ToUpperInvariant() switch
    {
        "HTTP" => ("Request", "Response"),
        "GRAPHQL" or "CASEQL" => ("Query", "Response"),
        "SQL" => ("Query", "Result"),
        "SCRIPT" => ("Script", "Output"),
        "FILE" => ("Operation", "Result"),
        _ => ("Input", "Output"),
    };

    /// <summary>Splits log text into entries. Lines before the first time-stamped line are kept as an entry of their own.</summary>
    internal static List<Entry> Parse(string text)
    {
        var entries = new List<Entry>();
        string? level = null;
        DateTimeOffset? time = null;
        List<string>? lines = null;
        void Flush() { if (lines is not null) entries.Add(new Entry(time, level ?? string.Empty, lines)); }
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var match = Prefix.Match(raw);
            if (match.Success)
            {
                Flush();
                level = match.Groups["lvl"].Value;
                time = DateTimeOffset.TryParse(match.Groups["t"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
                lines = [match.Groups["msg"].Value];
            }
            else if (lines is not null) lines.Add(raw);
            else lines = [raw];
        }
        Flush();
        while (entries.Count > 0 && entries[^1].Lines.Count > 0 && string.IsNullOrWhiteSpace(entries[^1].Lines[^1]) && entries[^1].Lines.Count > 1)
        {
            var trimmed = entries[^1].Lines.Take(entries[^1].Lines.Count - 1).ToList();
            entries[^1] = entries[^1] with { Lines = trimmed };
        }
        return entries;
    }

    private static readonly Regex IdInHead = new(@"\[((?:REQ|STEP|MANUAL)-[^\]]+)\]|RequestId=(?<id>[^;\s]+)", RegexOptions.Compiled);
    private static readonly Regex DataIdInHead = new(@"dataid=(?<v>[^;|]+?)\s*(?:;|\||$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StepInHead = new(@"(?:stepName=|---- STEP \d+: )(?<v>[^;|]+?)\s*(?:;|\||$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The log sections of one step of one data row. Rows run in parallel, so their entries are interleaved in the file: a section is
    /// therefore not "the lines up to the next STEP header" but every entry that carries one request id (REQ-…, STEP-…), in file order.
    /// Returns an empty list when the log has no request ids (the caller then falls back to header sections).
    /// </summary>
    public static List<string> SelectBlocks(string logText, string dataId, string stepName)
    {
        // raw entries: a time-stamped line plus the continuation lines after it
        var chunks = new List<List<string>>();
        foreach (var raw in logText.Replace("\r\n", "\n").Split('\n'))
        {
            if (Prefix.IsMatch(raw) || chunks.Count == 0) chunks.Add([raw]);
            else chunks[^1].Add(raw);
        }
        string HeadOf(List<string> chunk) => Prefix.Match(chunk[0]) is { Success: true } m ? m.Groups["msg"].Value : chunk[0];
        static string? Id(string head) => IdInHead.Match(head) is { Success: true } m ? (m.Groups["id"].Success ? m.Groups["id"].Value : m.Groups[1].Value) : null;

        var owner = new Dictionary<string, (string DataId, string Step)>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in chunks)
        {
            var head = HeadOf(chunk);
            if (Id(head) is not { } id || owner.ContainsKey(id)) continue;
            if (DataIdInHead.Match(head) is { Success: true } d && StepInHead.Match(head) is { Success: true } st) owner[id] = (d.Groups["v"].Value.Trim(), st.Groups["v"].Value.Trim());
        }
        var mine = owner.Where(o => o.Value.DataId.Equals(dataId, StringComparison.OrdinalIgnoreCase) && o.Value.Step.Equals(stepName, StringComparison.OrdinalIgnoreCase)).Select(o => o.Key).ToList();
        if (mine.Count == 0) return [];

        var manual = mine.Where(i => i.StartsWith("MANUAL-", StringComparison.OrdinalIgnoreCase)).ToList();
        var real = mine.Where(i => !manual.Contains(i)).ToList();
        string Section(IReadOnlyCollection<string> ids)
        {
            var wanted = chunks.Where(c => Id(HeadOf(c)) is { } id && ids.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
            // the "---- STEP n: name | dataid=… ----" line that opened this step for this row, for the overview
            var first = wanted.Count == 0 ? -1 : chunks.IndexOf(wanted[0]);
            var header = chunks.Take(Math.Max(first, 0)).LastOrDefault(c => HeadOf(c).StartsWith("---- STEP", StringComparison.OrdinalIgnoreCase)
                && DataIdInHead.Match(HeadOf(c)) is { Success: true } d && d.Groups["v"].Value.Trim().Equals(dataId, StringComparison.OrdinalIgnoreCase)
                && StepInHead.Match(HeadOf(c)) is { Success: true } st && st.Groups["v"].Value.Trim().Equals(stepName, StringComparison.OrdinalIgnoreCase));
            return string.Join("\n", (header is null ? [] : new List<List<string>> { header }).Concat(wanted).SelectMany(c => c));
        }
        // a manual wait of the step belongs to the step's first real run; with no real run it stands alone
        if (real.Count == 0) return [Section(manual)];
        var sections = real.Select(id => Section([id])).ToList();
        if (manual.Count > 0) sections[0] = Section(manual.Concat([real[0]]).ToList());
        return sections;
    }

    /// <summary>Builds the tabs. <paramref name="blocks"/> are the log sections that belong to the step (more than one when it ran several times).</summary>
    public static IReadOnlyList<StepSection> Build(IReadOnlyList<string> blocks, string stepType, string environment)
    {
        var (inputTab, outputTab) = TabNames(stepType);
        var views = blocks.Select(b => new BlockView(b, stepType, environment)).ToList();
        string Join(Func<BlockView, string> pick)
        {
            var parts = views.Select(pick).ToList();
            return parts.Count == 1 ? parts[0] : string.Join(Environment.NewLine + Environment.NewLine + "══════════ next run of this step ══════════" + Environment.NewLine + Environment.NewLine, parts.Select((p, i) => $"── Run {i + 1} of {parts.Count} ──{Environment.NewLine}{p}"));
        }
        return
        [
            new("Overview", Join(v => v.Overview()), SectionKind.KeyValue),
            new(inputTab, Join(v => v.Input()), SectionKind.Plain),
            new(outputTab, Join(v => v.Output()), SectionKind.Plain),
            new("Template", Join(v => v.Template()), SectionKind.Plain),
            new("Variables", Join(v => v.Variables()), SectionKind.KeyValue),
            new("Assertions", Join(v => v.Assertions()), SectionKind.Plain),
            new("Errors", Join(v => v.Errors()), SectionKind.Log),
            new("Complete log", Join(v => v.CleanLog()), SectionKind.Log),
            new("Raw log", string.Join(Environment.NewLine + Environment.NewLine + "==================== NEXT MATCH ====================" + Environment.NewLine, blocks), SectionKind.Plain),
        ];
    }

    // ---- One run of the step -----------------------------------------------------------------------------------------------

    private sealed class BlockView
    {
        private readonly List<Entry> _entries;
        private readonly string _type;
        private readonly string _environment;
        private readonly bool _http;
        private bool IsGraphQl => _type.Equals("GRAPHQL", StringComparison.OrdinalIgnoreCase) || _type.Equals("CASEQL", StringComparison.OrdinalIgnoreCase);

        public BlockView(string block, string stepType, string environment)
        {
            _entries = Parse(block);
            _type = string.IsNullOrWhiteSpace(stepType) ? "HTTP" : stepType.Trim();
            _environment = environment;
            _http = _type.Equals("HTTP", StringComparison.OrdinalIgnoreCase);
        }

        private Entry? Find(string prefix) => _entries.FirstOrDefault(e => e.Is(prefix));

        private static string Missing(string what) => $"{what} was not recorded for this step. The step probably stopped before this point; see the Errors tab.";

        // ---- Overview ----

        public string Overview()
        {
            var header = _entries.FirstOrDefault(e => e.Is("---- STEP"));
            var headerFacts = header is null ? [] : HeaderFacts(header.Head);
            var starting = Find("Request starting.") is { } s ? KeyValues(After(s.Head, "Request starting. ")) : null;
            var load = Find("STEP TEMPLATE LOAD") is { } l ? KeyValues(AfterBracket(l.Head)) : null;
            var done = _entries.LastOrDefault(e => e.Is("REQUEST COMPLETED") || e.Is("STEP COMPLETED")) is { } d ? KeyValues(AfterBracket(d.Head)) : null;
            string? Pick(string key) => (done?.GetValueOrDefault(key)) ?? (load?.GetValueOrDefault(key)) ?? (starting?.GetValueOrDefault(key)) ?? headerFacts.GetValueOrDefault(key);

            var text = new StringBuilder();
            void Line(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) text.AppendLine($"{key,-14}{value}"); }
            var step = Pick("stepName") ?? headerFacts.GetValueOrDefault("step");
            Line("Step", step is null ? null : $"{step}  ({_type})");
            var testcase = Pick("testcaseName") ?? Pick("testcase");
            Line("Testcase", testcase is null ? null : Pick("testcaseName") is not null && Pick("testcase") is { } number ? $"{testcase}  (#{number})" : testcase);
            Line("Row", Pick("dataid"));
            Line("Environment", string.IsNullOrWhiteSpace(_environment) ? Pick("environment") : _environment);
            Line("Iteration", Pick("iteration") is { } iteration ? Pick("thread") is { } thread ? $"{iteration}   (thread {thread})" : iteration : null);
            if (done is not null)
            {
                var result = done.GetValueOrDefault("result") ?? string.Empty;
                var status = done.GetValueOrDefault("status");
                var duration = done.GetValueOrDefault("durationMs");
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(status) && status != "0") parts.Add($"status {status}");
                if (!string.IsNullOrEmpty(duration)) parts.Add($"{duration} ms");
                Line("Result", result.Length == 0 ? string.Join(", ", parts) : parts.Count == 0 ? result : $"{result}   ({string.Join(", ", parts)})");
            }
            else Line("Result", "not finished — the log ends before the step completed");
            var started = _entries.FirstOrDefault(e => e.Time is not null)?.Time;
            var finished = _entries.LastOrDefault(e => e.Time is not null)?.Time;
            Line("Started", started?.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            if (finished is not null && started is not null && finished != started) Line("Finished", finished.Value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            Line("Request id", RequestId());
            var target = Pick("resolvedTargetUrl") ?? Pick("url") ?? Pick("targetUrl");
            Line(_http ? "URL" : "Target", target);
            Line("Template", Pick("template") is { } template ? template.Split(new[] { (char)92, (char)47 }).Last() : null);
            if (Find("ASSERTIONS COMPLETED") is { } assertions)
            {
                var fields = Fields(assertions, "PASSED", "FAILED");
                Line("Assertions", $"{fields.GetValueOrDefault("PASSED", "0")} passed, {fields.GetValueOrDefault("FAILED", "0")} failed");
            }
            Line("Error", done?.GetValueOrDefault("error"));
            if (text.Length == 0) text.AppendLine("Nothing recognisable was found for this step in the log; see Raw log.");
            return text.ToString().TrimEnd();
        }

        private string? RequestId()
        {
            var match = _entries.Select(e => Regex.Match(e.Head, @"\[((?:REQ|STEP)-\d+)\]")).FirstOrDefault(m => m.Success);
            return match?.Groups[1].Value;
        }

        // ---- Request / input ----

        private static readonly string[] RequestLabels = ["TYPE", "STEP", "ENVIRONMENT", "TARGET", "METHOD", "TEMPLATE", "HEADERS", "DETAILS", "BODY"];
        private static readonly string[] ResponseLabels = ["STATUS", "HEADERS", "BODY", "ERROR"];

        /// <summary>The heading over the main text a step sends.</summary>
        private string BodyTitle => _type.ToUpperInvariant() switch
        {
            "GRAPHQL" or "CASEQL" => "QUERY",
            "SQL" => "COMMAND",
            "SCRIPT" => "CODE",
            "FILE" => "CONTENT",
            _ => "BODY",
        };

        public string Input()
        {
            var request = Find("STEP REQUEST");
            if (request is null) return Missing("The request");
            var f = Fields(request, RequestLabels);
            var sb = new StringBuilder();
            var method = f.GetValueOrDefault("METHOD");
            var target = f.GetValueOrDefault("TARGET");
            if (!string.IsNullOrWhiteSpace(method) || !string.IsNullOrWhiteSpace(target)) sb.AppendLine($"{method?.Trim()}  {target?.Trim()}".Trim());
            AppendPair(sb, "Template", f.GetValueOrDefault("TEMPLATE"));
            AppendHeaders(sb, f.GetValueOrDefault("HEADERS"), alwaysShow: _http);
            var details = DetailLines(f.GetValueOrDefault("DETAILS"));
            foreach (var (key, value) in details.Where(d => !IsStructured(d.Value) && !d.Key.Equals("stepConfig", StringComparison.OrdinalIgnoreCase))) AppendPair(sb, key, value);
            foreach (var (key, value) in details.Where(d => IsStructured(d.Value) && !d.Key.Equals("stepConfig", StringComparison.OrdinalIgnoreCase))) AppendSection(sb, key.ToUpperInvariant(), Pretty(value), "(none)");
            AppendSection(sb, BodyTitle, Pretty(f.GetValueOrDefault("BODY") ?? string.Empty), _http ? "(no body)" : "(empty)");
            foreach (var (key, value) in details.Where(d => d.Key.Equals("stepConfig", StringComparison.OrdinalIgnoreCase))) AppendSection(sb, "STEP SETTINGS", Pretty(value), "(none)");
            return sb.ToString().TrimEnd();
        }

        // ---- Response / output ----

        public string Output()
        {
            var response = Find("STEP RESPONSE");
            if (response is null) return Missing("The response");
            var f = Fields(response, ResponseLabels);
            var sb = new StringBuilder();
            AppendPair(sb, "Status", f.GetValueOrDefault("STATUS"));
            if (f.TryGetValue("ERROR", out var error) && !string.IsNullOrWhiteSpace(error)) sb.AppendLine($"✗ {error.Trim()}");
            if (!string.IsNullOrWhiteSpace(f.GetValueOrDefault("HEADERS"))) AppendHeaders(sb, f.GetValueOrDefault("HEADERS"), alwaysShow: false);
            var body = f.GetValueOrDefault("BODY") ?? string.Empty;
            if (_type.Equals("SQL", StringComparison.OrdinalIgnoreCase) && Table(body) is { } table) AppendSection(sb, $"ROWS ({table.Count})", table.Text, "(no rows)");
            else AppendSection(sb, _http ? "BODY" : "RESULT", Pretty(body), "(empty)");
            if (IsGraphQl && GraphQlErrors(body) is { Length: > 0 } graphQl) AppendSection(sb, "GRAPHQL ERRORS", graphQl, string.Empty);
            return sb.ToString().TrimEnd();
        }

        private string? ResponseBody() => Find("STEP RESPONSE") is { } r ? Fields(r, ResponseLabels).GetValueOrDefault("BODY") : null;

        // ---- Template and variables ----

        private Dictionary<string, string>? Resolution()
        {
            var entry = Find("STEP RESOLUTION");
            return entry is null ? null : Fields(entry, "STEP TYPE", "ORIGINAL TARGET", "RESOLVED TARGET", "ORIGINAL TEMPLATE", "RESOLVED TEMPLATE", "VARIABLES");
        }

        public string Template()
        {
            if (Resolution() is not { } f) return Missing("The template");
            var sb = new StringBuilder();
            AppendPair(sb, "Configured", f.GetValueOrDefault("ORIGINAL TARGET"));
            AppendPair(sb, "Resolved", f.GetValueOrDefault("RESOLVED TARGET"));
            AppendSection(sb, "TEMPLATE AS WRITTEN", f.GetValueOrDefault("ORIGINAL TEMPLATE") ?? string.Empty, "(this step uses no template)");
            AppendSection(sb, "TEMPLATE WITH VALUES FILLED IN", Pretty(f.GetValueOrDefault("RESOLVED TEMPLATE") ?? string.Empty), "(this step uses no template)");
            return sb.ToString().TrimEnd();
        }

        public string Variables()
        {
            if (Resolution() is not { } f) return Missing("The variables");
            var lines = (f.GetValueOrDefault("VARIABLES") ?? string.Empty).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0 || lines is ["(none)"]) return "This step ran with no variables.";
            var pairs = lines.Select(l => l.IndexOf('=') is var i and > 0 ? (l[..i], l[(i + 1)..]) : (string.Empty, l)).ToList();
            var width = Math.Clamp(pairs.Max(p => p.Item1.Length), 4, 40) + 2;
            return string.Join(Environment.NewLine, pairs.Select(p => p.Item1.Length == 0 ? p.Item2 : $"{p.Item1.PadRight(width)}{p.Item2}"));
        }

        // ---- Assertions and errors ----

        public string Assertions()
        {
            var summary = Find("ASSERTIONS COMPLETED");
            var failed = _entries.Where(e => e.Is("ASSERTION FAILED")).ToList();
            if (summary is null && failed.Count == 0) return "No assertions were evaluated for this step.";
            var sb = new StringBuilder();
            if (summary is not null)
            {
                var f = Fields(summary, "PASSED", "FAILED");
                sb.AppendLine($"✓ {f.GetValueOrDefault("PASSED", "0")} passed     ✗ {f.GetValueOrDefault("FAILED", "0")} failed");
            }
            foreach (var entry in failed)
            {
                var kv = KeyValues(AfterBracket(entry.Head));
                sb.AppendLine().AppendLine($"✗ {kv.GetValueOrDefault("responsePath", "?")}   {kv.GetValueOrDefault("operator")}");
                AppendPair(sb, "  expected", kv.GetValueOrDefault("expected"));
                AppendPair(sb, "  actual", kv.GetValueOrDefault("actual"));
                AppendPair(sb, "  message", kv.GetValueOrDefault("message"));
            }
            if (failed.Count == 0) sb.AppendLine().AppendLine("Every assertion passed. The log lists failed assertions only; the Overview of the run lists all of them.");
            return sb.ToString().TrimEnd();
        }

        public string Errors()
        {
            var problems = _entries.Where(e => e.Level is "ERROR" or "WARN").Select(FormatEntry).ToList();
            if (IsGraphQl && GraphQlErrors(ResponseBody() ?? string.Empty) is { Length: > 0 } graphQl) problems.Add("The response carries GraphQL errors:" + Environment.NewLine + graphQl);
            if (problems.Count == 0) return "No errors or warnings were recorded for this step.";
            return string.Join(Environment.NewLine + Environment.NewLine, problems);
        }

        public string CleanLog() => string.Join(Environment.NewLine, _entries.Select(e => FormatEntry(e) + (e.Lines.Count > 1 ? Environment.NewLine : string.Empty)));

        private static string FormatEntry(Entry e)
        {
            var stamp = e.Time is { } t ? t.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) : "            ";
            var sb = new StringBuilder($"{stamp} {e.Level,-5} {e.Head}");
            foreach (var line in e.Lines.Skip(1)) sb.AppendLine().Append("    ").Append(line);
            return sb.ToString();
        }
    }

    // ---- Text helpers ------------------------------------------------------------------------------------------------------

    private static void AppendPair(StringBuilder sb, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"{key,-12}{value.Trim()}");
    }

    private static void AppendSection(StringBuilder sb, string title, string body, string whenEmpty)
    {
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine($"── {title} ──");
        sb.AppendLine(string.IsNullOrWhiteSpace(body) ? whenEmpty : body.TrimEnd());
    }

    private static void AppendHeaders(StringBuilder sb, string? headers, bool alwaysShow)
    {
        if (string.IsNullOrWhiteSpace(headers)) { if (alwaysShow) AppendSection(sb, "HEADERS", string.Empty, "(none)"); return; }
        var pairs = KeyValues(headers.Replace("\r", string.Empty).Replace("\n", "; "));
        AppendSection(sb, "HEADERS", string.Join(Environment.NewLine, pairs.Select(p => $"{p.Key}: {p.Value}")), "(none)");
    }

    /// <summary>The DETAILS lines of a step request, <c>name=value</c> each.</summary>
    private static List<KeyValuePair<string, string>> DetailLines(string? details)
        => (details ?? string.Empty).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l != "(none)")
            .Select(l => l.IndexOf('=') is var i and > 0 ? new KeyValuePair<string, string>(l[..i], l[(i + 1)..]) : new KeyValuePair<string, string>("note", l)).ToList();

    private static bool IsStructured(string value) => value.TrimStart() is { Length: > 1 } t && (t[0] == '{' || t[0] == '[');

    /// <summary>SQL rows (a JSON array of flat objects) as an aligned table; null when the text is not that.</summary>
    internal static (string Text, int Count)? Table(string json)
    {
        const int MaxRows = 200, MaxWidth = 40;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var rows = document.RootElement.EnumerateArray().ToList();
            if (rows.Count == 0 || rows.Any(r => r.ValueKind != JsonValueKind.Object)) return null;
            var columns = rows.SelectMany(r => r.EnumerateObject().Select(p => p.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            static string Cell(JsonElement row, string column)
            {
                foreach (var property in row.EnumerateObject())
                    if (property.Name.Equals(column, StringComparison.OrdinalIgnoreCase))
                    {
                        var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ValueKind == JsonValueKind.Null ? "NULL" : property.Value.GetRawText();
                        text = text.Replace('\n', ' ').Replace('\r', ' ');
                        return text.Length > MaxWidth ? text[..(MaxWidth - 1)] + "…" : text;
                    }
                return string.Empty;
            }
            var shown = rows.Take(MaxRows).ToList();
            var widths = columns.Select(c => Math.Max(c.Length, shown.Max(r => Cell(r, c).Length))).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join("  ", columns.Select((c, i) => c.PadRight(widths[i]))).TrimEnd());
            sb.AppendLine(string.Join("  ", widths.Select(w => new string('─', w))));
            foreach (var row in shown) sb.AppendLine(string.Join("  ", columns.Select((c, i) => Cell(row, c).PadRight(widths[i]))).TrimEnd());
            if (rows.Count > shown.Count) sb.AppendLine($"… {rows.Count - shown.Count} more rows (see Raw log)");
            return (sb.ToString().TrimEnd(), rows.Count);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The messages of a GraphQL response's "errors" array, one per line; empty when there are none.</summary>
    internal static string GraphQlErrors(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array) return string.Empty;
            return string.Join(Environment.NewLine, errors.EnumerateArray().Select(e => "✗ " + (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var message) ? message.ToString() : e.ToString())));
        }
        catch (JsonException) { return string.Empty; }
    }

    /// <summary>JSON and XML are shown indented; anything else is left exactly as logged.</summary>
    internal static string Pretty(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2) return text;
        try
        {
            if (trimmed[0] is '{' or '[')
            {
                using var document = JsonDocument.Parse(trimmed);
                return JsonSerializer.Serialize(document.RootElement, Indented);
            }
            if (trimmed[0] == '<') return XDocument.Parse(trimmed).ToString();
        }
        catch (Exception ex) when (ex is JsonException or System.Xml.XmlException) { }
        return text;
    }

    private static string After(string text, string marker) => text.StartsWith(marker, StringComparison.OrdinalIgnoreCase) ? text[marker.Length..] : text;

    /// <summary>The text after the "[REQ-00000001]" token of a log entry's first line.</summary>
    private static string AfterBracket(string head) => head.IndexOf("] ", StringComparison.Ordinal) is var i and >= 0 ? head[(i + 2)..] : head;

    /// <summary><c>a=1; b=two; c=x; y</c> into pairs; a part that does not start with a key belongs to the value before it.</summary>
    internal static Dictionary<string, string> KeyValues(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? last = null;
        foreach (var part in text.Split("; "))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && KeyName.IsMatch(part[..eq])) { last = part[..eq]; result[last] = part[(eq + 1)..]; }
            else if (last is not null) result[last] += "; " + part;
        }
        return result;
    }

    private static Dictionary<string, string> HeaderFacts(string head)
    {
        // "---- STEP 1: ReadBinary | type=File | dataid=B1 | iteration=1 ----"
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var match = Regex.Match(head, @"^---- STEP \d+: (?<step>[^|]+?) \|");
        if (match.Success) result["step"] = match.Groups["step"].Value.Trim();
        foreach (Match pair in Regex.Matches(head, @"\| (?<k>\w+)=(?<v>[^|]+?)(?= \||\s*-*$)")) result[pair.Groups["k"].Value] = pair.Groups["v"].Value.Trim();
        return result;
    }

    /// <summary>
    /// The labelled fields of a multi-line entry: a line that is exactly <c>LABEL:</c> or starts with <c>LABEL: </c> opens a field and the
    /// lines that follow belong to it. A "STEP RESPONSE [id]" line closes the current field (the runner writes both in one entry).
    /// </summary>
    internal static Dictionary<string, string> Fields(Entry entry, params string[] labels)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        var value = new StringBuilder();
        void Close() { if (current is not null) result[current] = value.ToString().TrimEnd('\r', '\n'); current = null; value.Clear(); }
        foreach (var line in entry.Lines.Skip(1))
        {
            if (line.StartsWith("STEP RESPONSE [", StringComparison.Ordinal)) { Close(); continue; }
            var label = labels.FirstOrDefault(l => line == l + ":" || line.StartsWith(l + ": ", StringComparison.Ordinal));
            if (label is not null)
            {
                Close();
                current = label;
                var rest = line.Length > label.Length + 1 ? line[(label.Length + 2)..] : string.Empty;
                value.Append(rest);
                continue;
            }
            if (current is not null) { if (value.Length > 0) value.Append('\n'); value.Append(line); }
        }
        Close();
        return result;
    }
}
