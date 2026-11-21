using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>One sheet (or the single table of a CSV file) read as text cells; row 0 / column 0 are the first row / column of the file.</summary>
public sealed record ImportSheet(string Name, string[][] Cells)
{
    public int RowCount => Cells.Length;
    public int ColumnCount => Cells.Length == 0 ? 0 : Cells.Max(r => r.Length);
    public string Get(int row, int column) => row >= 0 && row < Cells.Length && column >= 0 && column < Cells[row].Length ? Cells[row][column] : string.Empty;
}

/// <summary>A block of cells, 1-based like Excel: <c>B3:F40</c>, <c>B:F</c> (whole columns), <c>3:40</c> (whole rows) or one cell. 0 = open ended.</summary>
public readonly record struct ImportRange(int FirstRow, int FirstColumn, int LastRow, int LastColumn)
{
    public static ImportRange Whole => new(0, 0, 0, 0);
    public bool IsWhole => this == Whole;

    public static bool TryParse(string? text, out ImportRange range)
    {
        range = Whole;
        text = text?.Trim().Replace("$", string.Empty);
        if (string.IsNullOrEmpty(text) || text.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        var parts = text.Split(':');
        if (parts.Length > 2) return false;
        if (!TryCorner(parts[0], out var r1, out var c1)) return false;
        var r2 = r1;
        var c2 = c1;
        if (parts.Length == 2 && !TryCorner(parts[1], out r2, out c2)) return false;
        if (r1 > 0 && r2 > 0 && r2 < r1) (r1, r2) = (r2, r1);
        if (c1 > 0 && c2 > 0 && c2 < c1) (c1, c2) = (c2, c1);
        range = new ImportRange(r1, c1, r2, c2);
        return true;
    }

    private static bool TryCorner(string text, out int row, out int column)
    {
        row = 0; column = 0;
        var i = 0;
        while (i < text.Length && char.IsLetter(text[i])) { column = column * 26 + (char.ToUpperInvariant(text[i]) - 'A' + 1); i++; }
        if (i == text.Length) return column > 0;
        if (!int.TryParse(text.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out row) || row < 1) return false;
        return i == 0 || column > 0;
    }

    public static string Letter(int column)
    {
        var text = string.Empty;
        for (; column > 0; column = (column - 1) / 26) text = (char)('A' + (column - 1) % 26) + text;
        return text;
    }

    public override string ToString()
        => IsWhole ? "all" : $"{(FirstColumn > 0 ? Letter(FirstColumn) : string.Empty)}{(FirstRow > 0 ? FirstRow : string.Empty)}:{(LastColumn > 0 ? Letter(LastColumn) : string.Empty)}{(LastRow > 0 ? LastRow : string.Empty)}";
}

/// <summary>The part of a sheet the user chose: a header row (names of the columns) and the data rows below it, each remembering its row number in the file.</summary>
public sealed class ImportTable(string[] headers, int[] columnNumbers, List<(int Row, string[] Cells)> rows)
{
    public string[] Headers { get; } = headers;
    /// <summary>The 1-based column number in the file of each header (so a range starting at column C shows C, D, E).</summary>
    public int[] ColumnNumbers { get; } = columnNumbers;
    public List<(int Row, string[] Cells)> Rows { get; } = rows;
    public int ColumnCount => Headers.Length;
}

/// <summary>An Excel workbook or a CSV file opened for import. Values only (no formatting).</summary>
public sealed class ImportSource
{
    public const int MaxRows = 50_000;
    public const int MaxColumns = 200;

    public string FileName { get; private init; } = string.Empty;
    public bool IsCsv { get; private init; }
    public IReadOnlyList<ImportSheet> Sheets { get; private init; } = [];
    /// <summary>Set when the file had more rows or columns than one import takes; says what was cut.</summary>
    public string? Truncated { get; private set; }
    public char Delimiter { get; private init; } = ',';
    public string EncodingName { get; private init; } = "UTF-8";

    public static readonly IReadOnlyList<string> EncodingChoices = ["Auto", "UTF-8", "Windows-1252", "UTF-16"];
    public static readonly IReadOnlyList<(string Label, char Value)> DelimiterChoices = [("Auto", '\0'), ("Comma", ','), ("Semicolon", ';'), ("Tab", '\t'), ("Pipe", '|')];

    public static bool IsExcel(string path) => Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm";

    public static ImportSource Open(string path, char? delimiter = null, string? encoding = null)
        => IsExcel(path) ? FromWorkbook(path) : FromCsvBytes(Path.GetFileName(path), File.ReadAllBytes(path), delimiter, encoding);

    public static ImportSource FromWorkbook(string path)
    {
        using var workbook = new XLWorkbook(path);
        var truncated = new List<string>();
        var sheets = new List<ImportSheet>();
        foreach (var sheet in workbook.Worksheets)
        {
            var used = sheet.LastCellUsed();
            if (used is null) { sheets.Add(new ImportSheet(sheet.Name, [])); continue; }
            var rows = Math.Min(used.Address.RowNumber, MaxRows);
            var columns = Math.Min(used.Address.ColumnNumber, MaxColumns);
            if (used.Address.RowNumber > MaxRows) truncated.Add($"{sheet.Name}: only the first {MaxRows:N0} of {used.Address.RowNumber:N0} rows");
            if (used.Address.ColumnNumber > MaxColumns) truncated.Add($"{sheet.Name}: only the first {MaxColumns} of {used.Address.ColumnNumber} columns");
            var cells = new string[rows][];
            for (var r = 0; r < rows; r++) cells[r] = new string[columns];
            foreach (var cell in sheet.CellsUsed())
            {
                var (r, c) = (cell.Address.RowNumber - 1, cell.Address.ColumnNumber - 1);
                if (r < rows && c < columns) cells[r][c] = Text(cell);
            }
            foreach (var row in cells) for (var c = 0; c < row.Length; c++) row[c] ??= string.Empty;
            sheets.Add(new ImportSheet(sheet.Name, cells));
        }
        return new ImportSource { FileName = Path.GetFileName(path), Sheets = sheets, Truncated = truncated.Count == 0 ? null : string.Join("; ", truncated) + "." };
    }

    private static string Text(IXLCell cell)
    {
        XLCellValue value;
        try { value = cell.Value; } catch (Exception) { value = cell.CachedValue; }
        if (value.IsBlank) return string.Empty;
        if (value.IsText) return value.GetText();
        if (value.IsBoolean) return value.GetBoolean() ? "TRUE" : "FALSE";
        if (value.IsNumber) return value.GetNumber().ToString("R", CultureInfo.InvariantCulture);
        if (value.IsDateTime)
        {
            var date = value.GetDateTime();
            return date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        if (value.IsTimeSpan) return value.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public static ImportSource FromCsvBytes(string fileName, byte[] bytes, char? delimiter = null, string? encoding = null)
    {
        var (text, used) = Decode(bytes, encoding);
        return FromCsvText(fileName, text, delimiter, used);
    }

    public static ImportSource FromCsvText(string fileName, string text, char? delimiter = null, string encodingName = "UTF-8")
    {
        text = text.TrimStart('﻿');
        var chosen = delimiter is { } d && d != '\0' ? d : Detect(text);
        var table = ClipboardTable.Parse(text, chosen).Where(r => r.Any(c => c.Length > 0)).ToArray();
        string? truncated = null;
        if (table.Length > MaxRows) { truncated = $"only the first {MaxRows:N0} of {table.Length:N0} rows."; table = table[..MaxRows]; }
        if (table.Any(r => r.Length > MaxColumns))
        {
            truncated = (truncated is null ? string.Empty : truncated + " ") + $"only the first {MaxColumns} columns.";
            table = table.Select(r => r.Length > MaxColumns ? r[..MaxColumns] : r).ToArray();
        }
        return new ImportSource { FileName = fileName, IsCsv = true, Delimiter = chosen, EncodingName = encodingName, Sheets = [new ImportSheet(Path.GetFileNameWithoutExtension(fileName), table)], Truncated = truncated };
    }

    public static char Detect(string text)
    {
        var first = text.Split('\n', 2)[0];
        return new[] { ',', ';', '\t', '|' }.MaxBy(d => first.Count(c => c == d));
    }

    private static (string Text, string Name) Decode(byte[] bytes, string? encoding)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16");
        switch (encoding)
        {
            case "UTF-16": return (Encoding.Unicode.GetString(bytes), "UTF-16");
            case "Windows-1252": return (Encoding.GetEncoding(1252).GetString(bytes), "Windows-1252");
            case "UTF-8": return (Encoding.UTF8.GetString(bytes), "UTF-8");
        }
        try { return (new UTF8Encoding(false, true).GetString(bytes), "UTF-8"); }
        catch (DecoderFallbackException) { return (Encoding.GetEncoding(1252).GetString(bytes), "Windows-1252"); }
    }

    /// <summary>
    /// The chosen part of a sheet. <paramref name="headerRow"/> is the 1-based row of the file that holds the column names (0 = none, the columns are then
    /// named after their letters); data rows are the rows of the range below the header row.
    /// </summary>
    public ImportTable Slice(int sheetIndex, ImportRange range, int headerRow)
    {
        var sheet = Sheets[Math.Clamp(sheetIndex, 0, Sheets.Count - 1)];
        var lastColumn = range.LastColumn > 0 ? Math.Min(range.LastColumn, sheet.ColumnCount) : sheet.ColumnCount;
        var firstColumn = Math.Max(1, range.FirstColumn);
        var firstRow = Math.Max(1, range.FirstRow);
        var lastRow = range.LastRow > 0 ? Math.Min(range.LastRow, sheet.RowCount) : sheet.RowCount;
        var count = Math.Max(0, lastColumn - firstColumn + 1);
        var numbers = Enumerable.Range(firstColumn, count).ToArray();
        var headers = numbers.Select(n => headerRow > 0 ? sheet.Get(headerRow - 1, n - 1).Trim() : $"Column {ImportRange.Letter(n)}").ToArray();
        var rows = new List<(int, string[])>();
        for (var r = firstRow; r <= lastRow; r++)
        {
            if (r == headerRow || (headerRow > 0 && r < headerRow)) continue;
            var cells = numbers.Select(n => sheet.Get(r - 1, n - 1)).ToArray();
            if (cells.All(c => c.Length == 0)) continue;
            rows.Add((r, cells));
        }
        return new ImportTable(headers, numbers, rows);
    }
}
