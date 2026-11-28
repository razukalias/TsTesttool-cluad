using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace LoadTestingTool.Execution;

/// <summary>What the File step's Read gives for Excel, CSV, Word and PDF files, in the shape chosen by "Return as".</summary>
public static class FileContent
{
    public sealed record Options(string ReturnAs, string Sheet, bool Header, string Range, string Delimiter, int MaxRows);

    /// <summary>True when the file needs a reader of its own (Excel, Word, PDF) or a CSV with a "Return as" other than the plain text.</summary>
    public static bool Handles(string path, string returnAs)
    {
        var kind = FileMetadata.KindOf(Path.GetExtension(path));
        return kind is "excel" or "word" or "pdf" || (kind == "csv" && returnAs.Length > 0 && returnAs != "text");
    }

    /// <summary>The value for the step output and the text for the response. Throws InvalidDataException with a plain message.</summary>
    public static (object Value, string Text) Read(string path, Options options)
    {
        var kind = FileMetadata.KindOf(Path.GetExtension(path));
        var returnAs = options.ReturnAs.Trim().ToLowerInvariant();
        try
        {
            return kind switch
            {
                "excel" or "csv" => Table(path, kind, returnAs, options),
                "word" => Word(path, returnAs),
                "pdf" => Pdf(path, returnAs),
                _ => throw new InvalidDataException($"Return as is for Excel, CSV, Word and PDF files, not {Path.GetExtension(path)}.")
            };
        }
        catch (Exception ex) when (ex is IOException or OpenXmlPackageException or UglyToad.PdfPig.Core.PdfDocumentFormatException or InvalidOperationException or ArgumentException)
        { throw new InvalidDataException($"{Path.GetFileName(path)} could not be read as {Path.GetExtension(path).TrimStart('.')}: {ex.Message}"); }
    }

    /// <summary>The response text of a list: <c>{"rows": [...]}</c>, so a later step writes <c>&lt;From_response_Step.rows[0].name_r&gt;</c>. (The step's <c>output</c> is the plain list.)</summary>
    private static string Wrap(string name, object list) => JsonSerializer.Serialize(new Dictionary<string, object> { [name] = list }, new JsonSerializerOptions { WriteIndented = true });

    // ---- Excel and CSV ---------------------------------------------------------------------------

    private static (object, string) Table(string path, string kind, string returnAs, Options o)
    {
        if (returnAs is "" or "rows" or "json" or "csv" or "text") { }
        else throw new InvalidDataException($"Return as '{o.ReturnAs}' does not fit {kind}. Use rows, csv or text.");
        var set = RowSources.FromFile(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", Path.GetFullPath(path),
            new RowSourceOptions { Format = kind, Sheet = o.Sheet, Header = o.Header, Range = o.Range, Delimiter = o.Delimiter });
        if (o.MaxRows > 0 && set.Values.Count > o.MaxRows)
            throw new InvalidDataException($"{Path.GetFileName(path)} has {set.Values.Count} rows, more than the limit of {o.MaxRows}. Raise Max rows, or use a ForEach step with Skip/Take.");
        var records = (set.Records ?? []).Select(r => set.Columns.ToDictionary(c => c, c => (object?)(r.GetValueOrDefault(c) ?? string.Empty))).ToList();
        if (returnAs is "csv" or "text") { var text = ToCsv(set.Columns, set.Records ?? [], returnAs == "text" ? '\t' : ','); return (text, text); }
        return (records, Wrap("rows", records));
    }

    public static string ToCsv(IReadOnlyList<string> columns, IEnumerable<IReadOnlyDictionary<string, string>> rows, char delimiter = ',')
    {
        string Cell(string v) => v.IndexOfAny([delimiter, '"', '\n', '\r']) >= 0 && delimiter == ',' ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(delimiter, columns.Select(Cell)));
        foreach (var row in rows) builder.AppendLine(string.Join(delimiter, columns.Select(c => Cell(row.GetValueOrDefault(c) ?? string.Empty))));
        return builder.ToString();
    }

    // ---- Word ------------------------------------------------------------------------------------

    private static (object, string) Word(string path, string returnAs)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body ?? throw new InvalidDataException($"{Path.GetFileName(path)} has no text.");
        var paragraphs = body.Descendants<W.Paragraph>().Where(p => p.Ancestors<W.TableCell>().FirstOrDefault() is null)
            .Select(p => new Dictionary<string, object?> { ["style"] = StyleOf(p), ["text"] = TextOf(p) }).Where(d => ((string)d["text"]!).Length > 0).ToList();
        switch (returnAs)
        {
            case "" or "text":
            {
                var text = string.Join("\n", body.Descendants<W.Paragraph>().Select(TextOf));
                return (text, text);
            }
            case "paragraphs": return (paragraphs, Wrap("paragraphs", paragraphs));
            case "tables":
            {
                var tables = body.Descendants<W.Table>().Select(TableRows).ToList();
                return (tables, Wrap("tables", tables));
            }
            case "markdown":
            {
                var markdown = new StringBuilder();
                foreach (var element in body.ChildElements)
                {
                    if (element is W.Paragraph p)
                    {
                        var text = TextOf(p); if (text.Length == 0) continue;
                        var style = StyleOf(p);
                        if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(style["Heading".Length..], out var level)) markdown.AppendLine(new string('#', Math.Clamp(level, 1, 6)) + " " + text).AppendLine();
                        else if (p.ParagraphProperties?.NumberingProperties is not null || style.Contains("List", StringComparison.OrdinalIgnoreCase)) markdown.AppendLine("- " + text);
                        else markdown.AppendLine(text).AppendLine();
                    }
                    else if (element is W.Table t)
                    {
                        var rows = t.Elements<W.TableRow>().Select(r => r.Elements<W.TableCell>().Select(c => string.Join(" ", c.Descendants<W.Paragraph>().Select(TextOf)).Replace("|", "\\|")).ToList()).ToList();
                        if (rows.Count == 0) continue;
                        markdown.AppendLine("| " + string.Join(" | ", rows[0]) + " |").AppendLine("|" + string.Concat(rows[0].Select(_ => " --- |")));
                        foreach (var row in rows.Skip(1)) markdown.AppendLine("| " + string.Join(" | ", row) + " |");
                        markdown.AppendLine();
                    }
                }
                var result = markdown.ToString().TrimEnd();
                return (result, result);
            }
            default: throw new InvalidDataException($"Return as '{returnAs}' does not fit a Word file. Use text, paragraphs, tables or markdown.");
        }
    }

    private static string TextOf(W.Paragraph p) => string.Concat(p.Descendants().Select(e => e switch { W.Text t => t.Text, W.TabChar => "\t", W.Break => "\n", _ => string.Empty }));

    private static string StyleOf(W.Paragraph p) => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "Normal";

    /// <summary>One table as rows of objects: the first row names the columns.</summary>
    private static List<Dictionary<string, object?>> TableRows(W.Table table)
    {
        var rows = table.Elements<W.TableRow>().Select(r => r.Elements<W.TableCell>().Select(c => string.Join("\n", c.Descendants<W.Paragraph>().Select(TextOf))).ToList()).ToList();
        if (rows.Count == 0) return [];
        var names = rows[0].Select((n, i) => n.Trim().Length > 0 ? n.Trim() : $"col{i + 1}").ToList();
        return rows.Skip(1).Select(r => names.Select((n, i) => (n, v: i < r.Count ? r[i] : string.Empty)).ToDictionary(x => x.n, x => (object?)x.v)).ToList();
    }

    // ---- PDF -------------------------------------------------------------------------------------

    private static (object, string) Pdf(string path, string returnAs)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
        var pages = pdf.GetPages().Select(p => new Dictionary<string, object?> { ["page"] = p.Number, ["text"] = p.Text }).ToList();
        switch (returnAs)
        {
            case "" or "text":
            {
                var text = string.Join("\n", pages.Select(p => (string)p["text"]!));
                return (text, text);
            }
            case "pages": return (pages, Wrap("pages", pages));
            default: throw new InvalidDataException($"Return as '{returnAs}' does not fit a PDF file. Use text or pages.");
        }
    }
}
