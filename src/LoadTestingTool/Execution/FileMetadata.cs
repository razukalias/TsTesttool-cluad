using System.Globalization;
using System.IO.Packaging;
using System.Security.Cryptography;
using ClosedXML.Excel;

namespace LoadTestingTool.Execution;

/// <summary>The properties of a file (File step, action info): the basics for every file and more for images, Excel, Word, PDF, CSV and JSON. Values are text or numbers, keys are flat (<c>image.width</c>) so a later step can use <c>&lt;From_response_Info.image.width_r&gt;</c>.</summary>
public static class FileMetadata
{
    public static Dictionary<string, object?> Read(string path, bool includeGps = false)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException($"The file '{path}' was not found.");
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = info.Name,
            ["extension"] = info.Extension.TrimStart('.').ToLowerInvariant(),
            ["sizeBytes"] = info.Length,
            ["created"] = info.CreationTimeUtc.ToString("o", CultureInfo.InvariantCulture),
            ["modified"] = info.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture),
            ["mimeType"] = MimeOf(info.Extension)
        };
        using (var stream = File.OpenRead(path)) map["sha256"] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var kind = KindOf(info.Extension);
        map["kind"] = kind;
        // Each reader is best effort: a damaged or unusual file still returns the basics, with a note.
        try
        {
            switch (kind)
            {
                case "image": Image(path, map, includeGps); break;
                case "excel": Excel(path, map); Package(path, map); break;
                case "word": Package(path, map); Word(path, map); break;
                case "pdf": Pdf(path, map); break;
                case "csv": Csv(path, map); break;
                case "json": Json(path, map); break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { map["note"] = $"The details could not be read: {ex.Message}"; }
        return map;
    }

    /// <summary>Groups the flat <c>image.width</c>, <c>exif.model</c>, <c>gps.latitude</c> keys into nested objects, so the response reads as JSON: <c>{"image":{"width":640}}</c>.</summary>
    public static Dictionary<string, object?> Nest(Dictionary<string, object?> flat)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in flat)
        {
            var dot = key.IndexOf('.');
            if (dot < 0) { result[key] = value; continue; }
            var group = key[..dot];
            if (!result.TryGetValue(group, out var inner) || inner is not Dictionary<string, object?> map) result[group] = map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            map[key[(dot + 1)..]] = value;
        }
        return result;
    }

    public static string KindOf(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "png" or "jpg" or "jpeg" or "gif" or "bmp" or "tif" or "tiff" or "webp" or "heic" or "ico" => "image",
        "xlsx" or "xlsm" => "excel",
        "docx" or "docm" => "word",
        "pdf" => "pdf",
        "csv" or "tsv" => "csv",
        "json" => "json",
        _ => "other"
    };

    private static string MimeOf(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "png" => "image/png", "jpg" or "jpeg" => "image/jpeg", "gif" => "image/gif", "bmp" => "image/bmp", "tif" or "tiff" => "image/tiff", "webp" => "image/webp", "heic" => "image/heic", "ico" => "image/x-icon",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "pdf" => "application/pdf",
        "csv" => "text/csv", "tsv" => "text/tab-separated-values", "json" => "application/json", "txt" or "log" => "text/plain", "xml" => "application/xml", "html" or "htm" => "text/html",
        "zip" => "application/zip", "mp3" => "audio/mpeg", "mp4" => "video/mp4", "wav" => "audio/wav",
        _ => "application/octet-stream"
    };

    private static void Image(string path, Dictionary<string, object?> map, bool includeGps)
    {
        foreach (var directory in MetadataExtractor.ImageMetadataReader.ReadMetadata(path))
        {
            var isGps = directory is MetadataExtractor.Formats.Exif.GpsDirectory;
            if (isGps && !includeGps) continue;
            foreach (var tag in directory.Tags)
            {
                var name = tag.Name; var value = tag.Description;
                if (value is null) continue;
                var key = directory.Name switch
                {
                    "JPEG" or "PNG-IHDR" or "GIF Header" or "BMP Header" or "WebP" or "PNG-pHYs" => "image." + Camel(name),
                    "Exif IFD0" or "Exif SubIFD" => "exif." + Camel(name),
                    "GPS" => "gps." + Camel(name),
                    _ => null
                };
                if (key is null) continue;
                if (key is "image.imageWidth" or "image.width" or "image.imageHeight" or "image.height") key = "image." + (key.EndsWith("idth") ? "width" : "height");
                var cleaned = value.Replace(" pixels", string.Empty).Trim();
                map[key] = long.TryParse(cleaned, out var n) && key.StartsWith("image.") ? n : value;
            }
        }
    }

    private static string Camel(string name) => string.Concat(name.Split(' ', '/', '-').Where(p => p.Length > 0).Select((p, i) => i == 0 ? char.ToLowerInvariant(p[0]) + p[1..] : char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>Title, author and dates of an Office file (xlsx, docx).</summary>
    private static void Package(string path, Dictionary<string, object?> map)
    {
        using var package = System.IO.Packaging.Package.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var p = package.PackageProperties;
        if (!string.IsNullOrEmpty(p.Title)) map["title"] = p.Title;
        if (!string.IsNullOrEmpty(p.Creator)) map["author"] = p.Creator;
        if (!string.IsNullOrEmpty(p.LastModifiedBy)) map["lastModifiedBy"] = p.LastModifiedBy;
        if (p.Created is { } created) map["documentCreated"] = created.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        if (p.Modified is { } modified) map["documentModified"] = modified.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    private static void Excel(string path, Dictionary<string, object?> map)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        map["sheets"] = workbook.Worksheets.Select(w => w.Name).ToList();
        map["sheetCount"] = workbook.Worksheets.Count;
        map["sheetInfo"] = workbook.Worksheets.Select(sheet => { var used = sheet.RangeUsed(); return new Dictionary<string, object?> { ["name"] = sheet.Name, ["rows"] = used?.RowCount() ?? 0, ["columns"] = used?.ColumnCount() ?? 0 }; }).ToList();
    }

    private static void Word(string path, Dictionary<string, object?> map)
    {
        using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return;
        map["paragraphCount"] = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Count();
        map["tableCount"] = body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Table>().Count();
        var pages = document.ExtendedFilePropertiesPart?.Properties?.Pages?.Text;
        if (int.TryParse(pages, out var n)) map["pages"] = n;
    }

    private static void Pdf(string path, Dictionary<string, object?> map)
    {
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
        map["pages"] = pdf.NumberOfPages;
        map["pdfVersion"] = pdf.Version.ToString(CultureInfo.InvariantCulture);
        var i = pdf.Information;
        if (!string.IsNullOrEmpty(i.Title)) map["title"] = i.Title;
        if (!string.IsNullOrEmpty(i.Author)) map["author"] = i.Author;
        if (!string.IsNullOrEmpty(i.Creator)) map["creator"] = i.Creator;
        if (!string.IsNullOrEmpty(i.Producer)) map["producer"] = i.Producer;
        if (!string.IsNullOrEmpty(i.CreationDate)) map["documentCreated"] = i.CreationDate;
        if (!string.IsNullOrEmpty(i.ModifiedDate)) map["documentModified"] = i.ModifiedDate;
    }

    private static void Csv(string path, Dictionary<string, object?> map)
    {
        var set = RowSources.FromFile(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", Path.GetFullPath(path), new RowSourceOptions { Format = "csv" });
        map["rows"] = set.Values.Count; map["columns"] = set.Columns.ToList();
    }

    private static void Json(string path, Dictionary<string, object?> map)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        map["jsonType"] = root.ValueKind.ToString().ToLowerInvariant();
        if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var set = RowSources.FromJsonArray(root);
            map["rows"] = set.Values.Count; map["columns"] = set.Columns.ToList();
        }
        else if (root.ValueKind == System.Text.Json.JsonValueKind.Object) map["keys"] = root.EnumerateObject().Select(p => p.Name).ToList();
    }
}
