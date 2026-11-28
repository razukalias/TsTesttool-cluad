using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace LoadTestingTool.Execution;

/// <summary>What the File step's Write makes of its Content for a .xlsx, .docx, .pdf, .csv or .json file. Content may be rows (a JSON list of objects, or {"rows":[…]}), CSV text, or plain text.</summary>
public static class FileWriter
{
    /// <summary>True when Write should build the file instead of saving the text as it is.</summary>
    public static bool Handles(string path, string content)
    {
        var kind = FileMetadata.KindOf(Path.GetExtension(path));
        return kind is "excel" or "word" or "pdf" || (kind == "csv" && ParseRows(content) is not null);
    }

    /// <summary>Writes the file and says what was written (rows, paragraphs, pages).</summary>
    public static string Write(string path, string content, string sheetName)
    {
        var kind = FileMetadata.KindOf(Path.GetExtension(path));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var rows = ParseRows(content);
        switch (kind)
        {
            case "excel": return WriteExcel(path, rows ?? FromCsvText(content), string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName.Trim());
            case "word": return WriteWord(path, content, rows);
            case "pdf": return WritePdf(path, content, rows);
            case "csv":
                var table = rows!;
                File.WriteAllText(path, FileContent.ToCsv(table.Columns, table.Rows), new UTF8Encoding(false));
                return $"[csv written: {table.Rows.Count} rows]";
            default: throw new InvalidDataException($"Cannot build a {Path.GetExtension(path)} file; use .xlsx, .docx, .pdf, .csv or .json.");
        }
    }

    public sealed record Table(List<string> Columns, List<Dictionary<string, string>> Rows);

    /// <summary>Rows from a JSON list of objects or {"rows":[…]}; null when the text is not that.</summary>
    public static Table? ParseRows(string content)
    {
        var text = content.TrimStart();
        if (!(text.StartsWith('[') || text.StartsWith('{'))) return null;
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rows", out var inner)) root = inner;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object)) return null;
            var set = RowSources.FromJsonArray(root);
            return new Table(set.Columns.ToList(), (set.Records ?? []).Select(r => new Dictionary<string, string>(r)).ToList());
        }
        catch (JsonException) { return null; }
    }

    private static Table FromCsvText(string content)
    {
        var parsed = RowSources.ParseCsv(content, content.Split('\n', 2)[0].Contains(';') && !content.Split('\n', 2)[0].Contains(',') ? ';' : ',');
        if (parsed.Count == 0) return new Table([], []);
        var columns = parsed[0].Select((c, i) => c.Trim().Length > 0 ? c.Trim() : $"col{i + 1}").ToList();
        return new Table(columns, parsed.Skip(1).Select(r => columns.Select((c, i) => (c, v: i < r.Count ? r[i] : string.Empty)).ToDictionary(x => x.c, x => x.v)).ToList());
    }

    private static string WriteExcel(string path, Table table, string sheetName)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        for (var c = 0; c < table.Columns.Count; c++) { sheet.Cell(1, c + 1).Value = table.Columns[c]; sheet.Cell(1, c + 1).Style.Font.Bold = true; }
        for (var r = 0; r < table.Rows.Count; r++)
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var value = table.Rows[r].GetValueOrDefault(table.Columns[c]) ?? string.Empty;
                // numbers and true/false become real cells so the sheet sorts and sums; anything else stays text
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) && !(value.Length > 1 && value.StartsWith('0') && !value.StartsWith("0.")))
                    sheet.Cell(r + 2, c + 1).Value = number;
                else sheet.Cell(r + 2, c + 1).Value = value;
            }
        sheet.Columns().AdjustToContents(1, Math.Min(table.Rows.Count + 1, 200));
        book.SaveAs(path);
        return $"[xlsx written: {table.Rows.Count} rows, sheet {sheet.Name}]";
    }

    private static string WriteWord(string path, string content, Table? rows)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var body = new W.Body();
        W.Paragraph Paragraph(string text, string? style = null)
        {
            var p = new W.Paragraph(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
            if (style is not null) p.ParagraphProperties = new W.ParagraphProperties(new W.ParagraphStyleId { Val = style });
            return p;
        }
        var count = 0;
        if (rows is not null)
        {
            var table = new W.Table(new W.TableProperties(new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4 }, new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 }, new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 4 }, new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 }, new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 })));
            void Row(IEnumerable<string> cells) { var r = new W.TableRow(); foreach (var cell in cells) r.Append(new W.TableCell(Paragraph(cell))); table.Append(r); }
            Row(rows.Columns);
            foreach (var row in rows.Rows) Row(rows.Columns.Select(c => row.GetValueOrDefault(c) ?? string.Empty));
            body.Append(table); count = rows.Rows.Count;
        }
        else
            foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
            {
                var heading = line.TakeWhile(ch => ch == '#').Count();
                if (heading is >= 1 and <= 6 && line.Length > heading && line[heading] == ' ') body.Append(Paragraph(line[(heading + 1)..], "Heading" + heading));
                else if (line.StartsWith("- ") || line.StartsWith("* ")) body.Append(Paragraph("• " + line[2..]));
                else body.Append(Paragraph(line));
                count++;
            }
        main.Document = new W.Document(body);
        return rows is not null ? $"[docx written: table with {count} rows]" : $"[docx written: {count} paragraphs]";
    }

    private static string WritePdf(string path, string content, Table? rows)
    {
        var lines = rows is not null
            ? new[] { string.Join(" | ", rows.Columns) }.Concat(rows.Rows.Select(r => string.Join(" | ", rows.Columns.Select(c => r.GetValueOrDefault(c) ?? string.Empty)))).ToList()
            : content.Replace("\r\n", "\n").Split('\n').ToList();
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        const int size = 11; const double top = 800, bottom = 50, left = 40; const int maxChars = 90;
        var pages = 0; UglyToad.PdfPig.Writer.PdfPageBuilder? page = null; var y = 0.0;
        foreach (var raw in lines)
        {
            // standard fonts hold Latin-1 only: other characters become '?'
            var line = new string(raw.Select(ch => ch < 256 && (ch >= 32 || ch == '\t') ? (ch == '\t' ? ' ' : ch) : '?').ToArray());
            var pieces = line.Length == 0 ? [string.Empty] : Enumerable.Range(0, (line.Length + maxChars - 1) / maxChars).Select(i => line.Substring(i * maxChars, Math.Min(maxChars, line.Length - i * maxChars))).ToList();
            foreach (var piece in pieces)
            {
                if (page is null || y < bottom) { page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4); pages++; y = top; }
                if (piece.Length > 0) page.AddText(piece, size, new UglyToad.PdfPig.Core.PdfPoint(left, y), font);
                y -= size + 4;
            }
        }
        if (page is null) { builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4); pages = 1; }
        File.WriteAllBytes(path, builder.Build());
        return $"[pdf written: {pages} pages]";
    }
}
