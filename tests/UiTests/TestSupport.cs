using Avalonia;
using Avalonia.Headless;
using ClosedXML.Excel;
using LoadTestingTool.UI;
using UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace UiTests;

/// <summary>Starts the real <see cref="App"/> (styles and all) on the headless Skia backend so windows render for real.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal sealed class TempDir : IDisposable
{
    public TempDir() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uitests-" + Guid.NewGuid().ToString("N"));

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

    public static string Copy(TempDir dir, string relativePath)
    {
        var target = dir.Combine(System.IO.Path.GetFileName(relativePath));
        File.Copy(System.IO.Path.Combine(FindRoot(), relativePath), target);
        return target;
    }

    /// <summary>A workbook with one testcase of 300 data rows, for checking that a realistic dataset renders and edits.</summary>
    public static string CreateLarge(TempDir dir, int rows = 300)
    {
        var path = dir.Combine("large.xlsx");
        using var workbook = new XLWorkbook();
        var config = workbook.AddWorksheet("config");
        var request = workbook.AddWorksheet("request");
        var response = workbook.AddWorksheet("response");
        string[] configHeaders = ["testcaseindex", "testcase", "stepname", "steptype", "templetsource", "targetUrl", "verb"];
        string[] configRow = ["1", "Customers", "CreateCustomer", "HTTP", "create.json", "https://api.example.test/customers", "POST"];
        for (var i = 0; i < configHeaders.Length; i++) { config.Cell(1, i + 1).Value = configHeaders[i]; config.Cell(2, i + 1).Value = configRow[i]; }
        string[] requestHeaders = ["testcaseindex", "testcase", "dataid", "dontrun", "CreateCustomer.name", "CreateCustomer.email", "CreateCustomer.country"];
        for (var i = 0; i < requestHeaders.Length; i++) request.Cell(1, i + 1).Value = requestHeaders[i];
        string[] responseHeaders = ["testcaseindex", "testcase", "dataid", "{eq}CreateCustomer.statusCode", "{notempty}CreateCustomer.id", "{eq}CreateCustomer.name"];
        for (var i = 0; i < responseHeaders.Length; i++) response.Cell(1, i + 1).Value = responseHeaders[i];
        string[] countries = ["SE", "NO", "DK", "FI", "DE", "NL"];
        for (var n = 1; n <= rows; n++)
        {
            var id = "CUST" + n.ToString("000");
            request.Cell(n + 1, 1).Value = 1;
            request.Cell(n + 1, 2).Value = "Customers";
            request.Cell(n + 1, 3).Value = id;
            request.Cell(n + 1, 5).Value = "Customer " + n;
            request.Cell(n + 1, 6).Value = $"customer{n}@example.test";
            request.Cell(n + 1, 7).Value = countries[n % countries.Length];
            response.Cell(n + 1, 1).Value = 1;
            response.Cell(n + 1, 2).Value = "Customers";
            response.Cell(n + 1, 3).Value = id;
            response.Cell(n + 1, 4).Value = "201";
            response.Cell(n + 1, 5).Value = "{notempty}";
            response.Cell(n + 1, 6).Value = "Customer " + n;
        }
        workbook.SaveAs(path);
        return path;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "LoadTestingTool.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root (LoadTestingTool.sln).");
    }
}
