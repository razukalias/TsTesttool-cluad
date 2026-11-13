using ClosedXML.Excel;

namespace WorkbookEditTests;

/// <summary>A temporary folder that is deleted when disposed.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wbedit-" + Guid.NewGuid().ToString("N"));

    public string Path { get; }

    public string Combine(string name)
    {
        Directory.CreateDirectory(Path);
        return System.IO.Path.Combine(Path, name);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

internal static class Samples
{
    public const string IntegrationScenarios = "Instances/DataEngine_IntegrationScenarios/DataEngine_IntegrationScenarios.xlsx";
    public const string FileHttpScenarios = "Instances/FileHttpScenarios/FileHttpScenarios.xlsx";
    public const string AllParameters = "DataEngine-all-parameters.xlsx";

    public static string RepoRoot { get; } = FindRoot();

    /// <summary>Copies a repository workbook into the temp folder so tests never touch the original.</summary>
    public static string Copy(TempDir dir, string relativePath)
    {
        var target = dir.Combine(System.IO.Path.GetFileName(relativePath));
        File.Copy(System.IO.Path.Combine(RepoRoot, relativePath), target);
        return target;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "LoadTestingTool.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root (LoadTestingTool.sln).");
    }
}

internal static class Workbooks
{
    /// <summary>
    /// Builds a small valid workbook: one HTTP testcase with three data rows. The config URL points at
    /// the third data row by absolute row number, the way real workbooks do.
    /// </summary>
    public static string CreateSmall(TempDir dir, Action<IXLWorksheet, IXLWorksheet, IXLWorksheet>? customize = null)
    {
        var path = dir.Combine("small.xlsx");
        using var workbook = new XLWorkbook();
        var config = workbook.AddWorksheet("config");
        var request = workbook.AddWorksheet("request");
        var response = workbook.AddWorksheet("response");

        string[] configHeaders = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb", "enabled"];
        object[] step = [1, "Flow", "CreatePost", "HTTP", "create.json", "http://host/<From_request_title_4>", "POST", true];
        for (var i = 0; i < configHeaders.Length; i++) config.Cell(1, i + 1).Value = configHeaders[i];
        for (var i = 0; i < step.Length; i++) SetValue(config.Cell(2, i + 1), step[i]);

        string[] requestHeaders = ["testcaseindex", "testcase", "dataid", "dontrun", "title"];
        for (var i = 0; i < requestHeaders.Length; i++) request.Cell(1, i + 1).Value = requestHeaders[i];
        string[] responseHeaders = ["testcaseindex", "testcase", "dataid", "{eq}CreatePost.statusCode"];
        for (var i = 0; i < responseHeaders.Length; i++) response.Cell(1, i + 1).Value = responseHeaders[i];
        for (var n = 1; n <= 3; n++)
        {
            request.Cell(n + 1, 1).Value = 1;
            request.Cell(n + 1, 2).Value = "Flow";
            request.Cell(n + 1, 3).Value = "D" + n;
            request.Cell(n + 1, 5).Value = "title " + n;
            response.Cell(n + 1, 1).Value = 1;
            response.Cell(n + 1, 2).Value = "Flow";
            response.Cell(n + 1, 3).Value = "D" + n;
            response.Cell(n + 1, 4).Value = "201";
        }

        customize?.Invoke(config, request, response);
        workbook.SaveAs(path);
        return path;
    }

    private static void SetValue(IXLCell cell, object value)
    {
        switch (value)
        {
            case int i: cell.Value = i; break;
            case bool b: cell.Value = b; break;
            default: cell.Value = value.ToString() ?? string.Empty; break;
        }
    }

    /// <summary>Every used cell of every sheet as "sheet!A1" -> "text|formula", for exact before/after comparison.</summary>
    public static Dictionary<string, string> Snapshot(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var result = new Dictionary<string, string>();
        foreach (var sheet in workbook.Worksheets)
            foreach (var cell in sheet.CellsUsed(XLCellsUsedOptions.All))
                result[$"{sheet.Name}!{cell.Address}"] = $"{cell.GetString()}|{(cell.HasFormula ? cell.FormulaA1 : string.Empty)}|{cell.DataType}";
        return result;
    }

    public static string[] Diff(Dictionary<string, string> before, Dictionary<string, string> after)
        => before.Keys.Union(after.Keys)
            .Where(k => !before.TryGetValue(k, out var a) || !after.TryGetValue(k, out var b) || a != b)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
}
