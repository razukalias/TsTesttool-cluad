using System.Globalization;
using System.Security.Cryptography;
using ClosedXML.Excel;

namespace LoadTestingTool.Editing;

/// <summary>
/// An editable view of a DataEngine workbook: the config, request and response sheets as tables,
/// plus operations that keep them consistent (a request row always has a response row).
/// Every other sheet, and every cell that was not edited, is left exactly as it was on save.
/// </summary>
public sealed class WorkbookDocument
{
    private byte[] _baselineBytes;
    private string _baselineHash;

    private WorkbookDocument(string filePath, byte[] bytes, SheetTable config, SheetTable request, SheetTable response, IReadOnlyList<string> otherSheets)
    {
        FilePath = filePath;
        _baselineBytes = bytes;
        _baselineHash = Hash(bytes);
        Config = config;
        Request = request;
        Response = response;
        OtherSheets = otherSheets;
    }

    public string FilePath { get; }
    public SheetTable Config { get; }
    public SheetTable Request { get; }
    public SheetTable Response { get; }

    /// <summary>Names of the worksheets that are not edited here (for example README). They are never modified.</summary>
    public IReadOnlyList<string> OtherSheets { get; }

    public bool IsDirty => Config.IsDirty || Request.IsDirty || Response.IsDirty;

    public static WorkbookDocument Load(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Excel workbook was not found.", full);
        var bytes = ReadShared(full);
        using var stream = new MemoryStream(bytes, writable: false);
        using var workbook = new XLWorkbook(stream);
        var config = SheetTable.Load(RequireSheet(workbook, "config"));
        var request = SheetTable.Load(RequireSheet(workbook, "request"));
        var response = SheetTable.Load(RequireSheet(workbook, "response"));
        var edited = new HashSet<string>([config.SheetName, request.SheetName, response.SheetName], StringComparer.OrdinalIgnoreCase);
        var others = workbook.Worksheets.Select(w => w.Name).Where(n => !edited.Contains(n)).ToList();
        return new WorkbookDocument(full, bytes, config, request, response, others);
    }

    /// <summary>Data IDs of a testcase, in sheet order.</summary>
    public IReadOnlyList<string> GetDataIds(int testcaseIndex)
        => Request.Rows.Where(r => RowIndex(r) == testcaseIndex).Select(r => r.Get("dataid").Trim()).Where(x => x.Length > 0).ToList();

    /// <summary>
    /// Adds a request row and its matching response row for a testcase that already has steps.
    /// The runner rejects a request row with no response row, so the two are always created together.
    /// </summary>
    public (TableRow Request, TableRow Response) AddDataRow(int testcaseIndex, string dataId)
    {
        dataId = dataId.Trim();
        if (dataId.Length == 0) throw new ArgumentException("A data ID cannot be empty.", nameof(dataId));
        var testcase = Config.Rows.Where(r => RowIndex(r) == testcaseIndex).Select(r => r.Get("testcase").Trim()).FirstOrDefault(x => x.Length > 0)
            ?? throw new InvalidOperationException($"Testcase {testcaseIndex} has no steps in the config sheet.");
        if (FindRow(Request, testcaseIndex, dataId) is not null || FindRow(Response, testcaseIndex, dataId) is not null)
            throw new InvalidOperationException($"Testcase {testcaseIndex} already has a data row '{dataId}'.");

        var request = Request.AddRow();
        FillIdentity(request, testcaseIndex, testcase, dataId);
        var response = Response.AddRow();
        FillIdentity(response, testcaseIndex, testcase, dataId);
        return (request, response);
    }

    /// <summary>Removes a data row from both the request and response sheets. Returns false when neither had it.</summary>
    public bool RemoveDataRow(int testcaseIndex, string dataId)
    {
        var removed = false;
        if (FindRow(Request, testcaseIndex, dataId) is { } request) removed |= Request.RemoveRow(request);
        if (FindRow(Response, testcaseIndex, dataId) is { } response) removed |= Response.RemoveRow(response);
        return removed;
    }

    /// <summary>Checks the workbook against the same rules the runner applies when it reads it.</summary>
    public IReadOnlyList<WorkbookProblem> Validate() => WorkbookValidator.Validate(this);

    /// <summary>
    /// Writes the edits back to the workbook file. Only cells that changed are written; formatting,
    /// formulas and other sheets are untouched. Throws <see cref="WorkbookChangedException"/> if the
    /// file was changed by someone else since it was loaded.
    /// </summary>
    public SaveResult Save(WorkbookSaveOptions? options = null)
    {
        options ??= new WorkbookSaveOptions();
        if (!IsDirty) return new SaveResult(false, null);

        var current = ReadShared(FilePath);
        if (!options.OverwriteExternalChanges && !string.Equals(Hash(current), _baselineHash, StringComparison.Ordinal))
            throw new WorkbookChangedException(FilePath);

        using var stream = new MemoryStream(_baselineBytes, writable: false);
        using var workbook = new XLWorkbook(stream);
        Config.ApplyTo(workbook.Worksheet(Config.SheetName));
        Request.ApplyTo(workbook.Worksheet(Request.SheetName));
        Response.ApplyTo(workbook.Worksheet(Response.SheetName));

        var directory = Path.GetDirectoryName(FilePath)!;
        var temp = Path.Combine(directory, $"{Path.GetFileName(FilePath)}.tmp-{Guid.NewGuid():N}");
        string? backup = null;
        byte[] written;
        try
        {
            workbook.SaveAs(temp);
            written = File.ReadAllBytes(temp);
            if (options.CreateBackup)
            {
                backup = UniqueBackupPath(FilePath);
                File.Copy(FilePath, backup, overwrite: false);
            }
            File.Move(temp, FilePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
            throw;
        }

        _baselineBytes = written;
        _baselineHash = Hash(written);
        Config.AcceptChanges();
        Request.AcceptChanges();
        Response.AcceptChanges();
        return new SaveResult(true, backup);
    }

    internal static int? RowIndex(TableRow row)
        => int.TryParse(row.Get("testcaseindex").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static TableRow? FindRow(SheetTable table, int testcaseIndex, string dataId)
        => table.Rows.FirstOrDefault(r => RowIndex(r) == testcaseIndex && string.Equals(r.Get("dataid").Trim(), dataId.Trim(), StringComparison.OrdinalIgnoreCase));

    private static void FillIdentity(TableRow row, int testcaseIndex, string testcase, string dataId)
    {
        row["testcaseindex"] = testcaseIndex.ToString(CultureInfo.InvariantCulture);
        row["testcase"] = testcase;
        row["dataid"] = dataId;
    }

    private static IXLWorksheet RequireSheet(XLWorkbook workbook, string name)
        => workbook.Worksheets.FirstOrDefault(w => w.Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Required worksheet '{name}' was not found.");

    private static byte[] ReadShared(string path)
    {
        // Excel keeps an open workbook locked for writing but still allows reading.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        file.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string UniqueBackupPath(string path)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
        var candidate = $"{path}.backup.{stamp}.xlsx";
        for (var i = 1; File.Exists(candidate); i++) candidate = $"{path}.backup.{stamp}_{i}.xlsx";
        return candidate;
    }
}
