using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace LoadTestingTool.Execution;

/// <summary>The rows a ForEach loop goes through: values (what the loop variable holds) and, when the rows have columns or fields, one record per row.</summary>
public sealed record RowSet(IReadOnlyList<string> Values, IReadOnlyList<IReadOnlyDictionary<string, string>>? Records, IReadOnlyList<string> Columns);

/// <summary>Which part of a file the loop reads (all texts are already filled in).</summary>
public sealed class RowSourceOptions
{
    /// <summary>csv, excel or json; empty = from the file name.</summary>
    public string Format { get; set; } = string.Empty;
    public string Sheet { get; set; } = string.Empty;
    /// <summary>Does the first row hold the column names? (csv and excel; default yes)</summary>
    public bool Header { get; set; } = true;
    public string Delimiter { get; set; } = string.Empty;
    /// <summary>json: the path of the array, for example $.users (default: the whole file).</summary>
    public string JsonPath { get; set; } = "$";
    /// <summary>Only this column: the loop variable then holds its plain value.</summary>
    public string Column { get; set; } = string.Empty;
    /// <summary>excel: only these cells, for example A1:D20 (the first row of the range is the header when Header is yes).</summary>
    public string Range { get; set; } = string.Empty;
    public int Skip { get; set; }
    public int Take { get; set; }
}

/// <summary>Reads the rows of a CSV file, an Excel sheet, a JSON array in a file, or a JSON array in a step response, for ForEach.</summary>
public static class RowSources
{
    public static string FormatOf(string path, string format)
    {
        var f = format.Trim().ToLowerInvariant();
        if (f is "csv" or "excel" or "json") return f;
        if (f is "xlsx" or "xlsm" or "xls") return "excel";
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" => "excel",
            ".json" => "json",
            _ => "csv"
        };
    }

    /// <summary>The rows of the file. Throws InvalidDataException with a message that says what is wrong.</summary>
    public static RowSet FromFile(string instanceFolder, string path, RowSourceOptions options)
    {
        var found = FileTokens.Find(instanceFolder, path) ?? throw new InvalidDataException($"The file '{path}' was not found (looked in the instance's Files folder{(Path.IsPathRooted(path) ? " and at that path" : " and the instance folder")}).");
        var format = FormatOf(found, options.Format);
        List<string> columns; List<Dictionary<string, string>> rows;
        try
        {
            switch (format)
            {
                case "excel": (columns, rows) = ReadExcel(found, options); break;
                case "json":
                    using (var document = JsonDocument.Parse(File.ReadAllText(found)))
                    {
                        var array = Select(document.RootElement, string.IsNullOrWhiteSpace(options.JsonPath) ? "$" : options.JsonPath);
                        if (array is not JsonElement element || element.ValueKind != JsonValueKind.Array)
                            throw new InvalidDataException($"There is no array at {options.JsonPath} in {Path.GetFileName(found)}. Give the path of a list, for example $.users.");
                        return FromJsonArray(element, options);
                    }
                default: (columns, rows) = ReadCsv(found, options); break;
            }
        }
        catch (JsonException ex) { throw new InvalidDataException($"{Path.GetFileName(found)} is not valid JSON: {ex.Message}"); }
        catch (IOException ex) { throw new InvalidDataException($"{Path.GetFileName(found)} could not be read: {ex.Message}"); }
        return Build(columns, rows, options);
    }

    /// <summary>A JSON array: objects give one record per object (nested values stay JSON text), anything else gives plain values.</summary>
    public static RowSet FromJsonArray(JsonElement array, RowSourceOptions? options = null)
    {
        options ??= new RowSourceOptions();
        var columns = new List<string>(); var rows = new List<Dictionary<string, string>>(); var plain = new List<string>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    if (!columns.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) columns.Add(property.Name);
                    row[property.Name] = Text(property.Value);
                }
                rows.Add(row);
            }
            else plain.Add(Text(element));
        }
        if (rows.Count == 0)
        {
            var values = Page(plain, options);
            return new RowSet(values, null, []);
        }
        return Build(columns, rows, options);
    }

    /// <summary>The element at a path like $.data.users or $.groups[0].members (names and [n] indexes); null when it is not there.</summary>
    public static JsonElement? Select(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Trim().TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part; var indexes = new List<int>();
            var bracket = part.IndexOf('[');
            if (bracket >= 0)
            {
                name = part[..bracket];
                foreach (var piece in part[bracket..].Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(piece, out var n)) indexes.Add(n); else return null;
            }
            if (name.Length > 0 && (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))) return null;
            foreach (var i in indexes)
            {
                if (current.ValueKind != JsonValueKind.Array || i < 0 || i >= current.GetArrayLength()) return null;
                current = current[i];
            }
        }
        return current;
    }

    private static RowSet Build(List<string> columns, List<Dictionary<string, string>> rows, RowSourceOptions options)
    {
        if (options.Column.Length > 0 && !columns.Contains(options.Column, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"There is no column '{options.Column}'. The columns are: {(columns.Count == 0 ? "none" : string.Join(", ", columns))}.");
        var page = Page(rows, options);
        var records = page.Select(r => (IReadOnlyDictionary<string, string>)r).ToList();
        var values = page.Select(r => options.Column.Length > 0 ? r.GetValueOrDefault(options.Column) ?? string.Empty
            : columns.Count == 1 ? r.GetValueOrDefault(columns[0]) ?? string.Empty
            : JsonSerializer.Serialize(columns.ToDictionary(c => c, c => r.GetValueOrDefault(c) ?? string.Empty))).ToList();
        return new RowSet(values, records, columns);
    }

    private static List<T> Page<T>(List<T> all, RowSourceOptions options)
    {
        IEnumerable<T> page = all.Skip(Math.Max(0, options.Skip));
        if (options.Take > 0) page = page.Take(options.Take);
        return page.ToList();
    }

    private static string Text(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString() ?? string.Empty, JsonValueKind.Null => string.Empty, _ => value.GetRawText() };

    // ---- CSV -------------------------------------------------------------------------------------

    private static (List<string> Columns, List<Dictionary<string, string>> Rows) ReadCsv(string file, RowSourceOptions options)
    {
        var text = File.ReadAllText(file, Encoding.UTF8);
        var delimiter = options.Delimiter == "\\t" || options.Delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? '\t' : options.Delimiter.Length > 0 ? options.Delimiter[0] : GuessDelimiter(text);
        var table = ParseCsv(text, delimiter);
        return Table(table, options.Header);
    }

    private static char GuessDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        return new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(x => x == c)).First();
    }

    public static List<List<string>> ParseCsv(string text, char delimiter)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); var quoted = false; var any = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) { quoted = true; any = true; }
            else if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); any = true; }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (any || cell.Length > 0) { row.Add(cell.ToString()); rows.Add(row); }
                row = []; cell.Clear(); any = false;
            }
            else { cell.Append(c); any = true; }
        }
        if (any || cell.Length > 0) { row.Add(cell.ToString()); rows.Add(row); }
        if (rows.Count > 0 && rows[0].Count > 0 && rows[0][0].StartsWith('﻿')) rows[0][0] = rows[0][0][1..];
        return rows;
    }

    private static (List<string>, List<Dictionary<string, string>>) Table(List<List<string>> table, bool header)
    {
        if (table.Count == 0) return ([], []);
        var width = table.Max(r => r.Count);
        var columns = header ? table[0].Select((c, i) => c.Trim().Length > 0 ? c.Trim() : $"col{i + 1}").ToList() : Enumerable.Range(1, width).Select(i => $"col{i}").ToList();
        for (var i = columns.Count; i < width; i++) columns.Add($"col{i + 1}");
        var rows = new List<Dictionary<string, string>>();
        foreach (var raw in table.Skip(header ? 1 : 0))
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Count; i++) row[columns[i]] = i < raw.Count ? raw[i] : string.Empty;
            rows.Add(row);
        }
        return (columns, rows);
    }

    // ---- Excel -----------------------------------------------------------------------------------

    public static IReadOnlyList<string> SheetNames(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        return workbook.Worksheets.Select(w => w.Name).ToList();
    }

    private static (List<string> Columns, List<Dictionary<string, string>> Rows) ReadExcel(string file, RowSourceOptions options)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        IXLWorksheet sheet;
        if (options.Sheet.Length == 0) sheet = workbook.Worksheets.First();
        else if (workbook.TryGetWorksheet(options.Sheet, out var named)) sheet = named;
        else if (int.TryParse(options.Sheet, out var number) && number >= 1 && number <= workbook.Worksheets.Count) sheet = workbook.Worksheet(number);
        else throw new InvalidDataException($"There is no sheet '{options.Sheet}' in {Path.GetFileName(file)}. The sheets are: {string.Join(", ", workbook.Worksheets.Select(w => w.Name))}.");
        IXLRange? used;
        try { used = options.Range.Length > 0 ? sheet.Range(options.Range) : sheet.RangeUsed(); }
        catch (ArgumentException) { throw new InvalidDataException($"'{options.Range}' is not a cell range. Write it like A1:D20."); }
        if (used is null) return ([], []);
        var first = used.FirstColumn().ColumnNumber(); var last = used.LastColumn().ColumnNumber();
        var table = new List<List<string>>();
        foreach (var row in used.Rows())
        {
            var cells = new List<string>();
            for (var c = first; c <= last; c++) cells.Add(row.Worksheet.Cell(row.RowNumber(), c).GetFormattedString());
            if (cells.Any(x => x.Length > 0)) table.Add(cells);
        }
        return Table(table, options.Header);
    }

    /// <summary>For the editor's Browse window: what the file holds (sheets, columns, the first rows) without reading more than needed.</summary>
    public sealed record Preview(string Format, IReadOnlyList<string> Sheets, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows, int TotalRows, IReadOnlyList<string> Arrays);

    public static Preview Inspect(string instanceFolder, string path, RowSourceOptions options, int sample = 8)
    {
        var found = FileTokens.Find(instanceFolder, path) ?? throw new InvalidDataException($"The file '{path}' was not found.");
        var format = FormatOf(found, options.Format);
        var sheets = format == "excel" ? SheetNames(found) : [];
        var arrays = new List<string>();
        if (format == "json")
        {
            try { using var document = JsonDocument.Parse(File.ReadAllText(found)); FindArrays(document.RootElement, "$", arrays, 0); }
            catch (JsonException ex) { throw new InvalidDataException($"{Path.GetFileName(found)} is not valid JSON: {ex.Message}"); }
        }
        var all = FromFile(instanceFolder, path, new RowSourceOptions { Format = options.Format, Sheet = options.Sheet, Header = options.Header, Delimiter = options.Delimiter, JsonPath = options.JsonPath });
        return new Preview(format, sheets, all.Columns, (all.Records ?? []).Take(sample).ToList(), all.Values.Count, arrays);
    }

    private static void FindArrays(JsonElement element, string path, List<string> arrays, int depth)
    {
        if (depth > 4 || arrays.Count >= 30) return;
        if (element.ValueKind == JsonValueKind.Array) { arrays.Add(path); return; }
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) FindArrays(property.Value, path + "." + property.Name, arrays, depth + 1);
    }
}
