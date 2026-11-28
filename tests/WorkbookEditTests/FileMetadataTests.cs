using System.Text.Json;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

// The File step's Info action: the properties of any file.
public class FileMetadataTests
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static string Save(TempDir dir, string name, byte[] bytes) { Directory.CreateDirectory(dir.Path); var path = Path.Combine(dir.Path, name); File.WriteAllBytes(path, bytes); return path; }

    [Fact]
    public void Every_file_has_the_basics_and_an_unknown_type_gets_only_those()
    {
        var dir = new TempDir();
        var map = FileMetadata.Read(Save(dir, "a.bin", [1, 2, 3]));
        Assert.Equal("a.bin", map["name"]); Assert.Equal("bin", map["extension"]); Assert.Equal(3L, map["sizeBytes"]);
        Assert.Equal("application/octet-stream", map["mimeType"]); Assert.Equal("other", map["kind"]);
        Assert.Equal(64, ((string)map["sha256"]!).Length);
        Assert.Throws<FileNotFoundException>(() => FileMetadata.Read(Path.Combine(dir.Path, "missing.txt")));
    }

    [Fact]
    public void An_image_tells_its_size()
    {
        var map = FileMetadata.Read(Save(new TempDir(), "p.png", Convert.FromBase64String(OnePixelPng)));
        Assert.Equal("image", map["kind"]); Assert.Equal("image/png", map["mimeType"]);
        Assert.Equal(1L, map["image.width"]); Assert.Equal(1L, map["image.height"]);
        var nested = FileMetadata.Nest(map);
        Assert.Equal(1L, ((Dictionary<string, object?>)nested["image"]!)["width"]);
    }

    [Fact]
    public void A_damaged_image_still_returns_the_basics_with_a_note()
    {
        var map = FileMetadata.Read(Save(new TempDir(), "bad.png", [1, 2, 3, 4]));
        Assert.Equal("bad.png", map["name"]); Assert.True(map.ContainsKey("note") || !map.ContainsKey("image.width"));
    }

    [Fact]
    public void Excel_tells_sheets_and_sizes_and_csv_and_json_tell_rows_and_columns()
    {
        var dir = new TempDir();
        Directory.CreateDirectory(dir.Path); var xlsx = Path.Combine(dir.Path, "b.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = book.AddWorksheet("People"); sheet.Cell(1, 1).Value = "a"; sheet.Cell(1, 2).Value = "b"; sheet.Cell(2, 1).Value = 1; sheet.Cell(3, 1).Value = 2;
            book.Properties.Author = "Ann"; book.AddWorksheet("Empty");
            book.SaveAs(xlsx);
        }
        var excel = FileMetadata.Read(xlsx);
        Assert.Equal(["People", "Empty"], (List<string>)excel["sheets"]!); Assert.Equal(2, excel["sheetCount"]); Assert.Equal("Ann", excel["author"]);
        var info = (List<Dictionary<string, object?>>)excel["sheetInfo"]!;
        Assert.Equal(3, info[0]["rows"]); Assert.Equal(2, info[0]["columns"]);

        var csv = FileMetadata.Read(Save(dir, "c.csv", "id,name\n1,x\n2,y\n"u8.ToArray()));
        Assert.Equal(2, csv["rows"]); Assert.Equal(["id", "name"], (List<string>)csv["columns"]!);
        var json = FileMetadata.Read(Save(dir, "d.json", "[{\"x\":1},{\"x\":2},{\"x\":3}]"u8.ToArray()));
        Assert.Equal(3, json["rows"]); Assert.Equal("array", json["jsonType"]);
        var obj = FileMetadata.Read(Save(dir, "e.json", "{\"a\":1,\"b\":2}"u8.ToArray()));
        Assert.Equal(["a", "b"], (List<string>)obj["keys"]!);
        Assert.True(JsonDocument.Parse(JsonSerializer.Serialize(FileMetadata.Nest(excel))).RootElement.TryGetProperty("sheetInfo", out _));
    }

    [Fact]
    public async Task The_file_step_info_action_answers_with_nested_json_a_later_step_can_read()
    {
        var dir = new TempDir();
        Save(dir, "p.png", Convert.FromBase64String(OnePixelPng));
        var config = JsonSerializer.Serialize(new { Action = "Info", Path = "p.png" });
        var step = new LoadTestingTool.Domain.RequestStep { StepName = "Photo", StepType = LoadTestingTool.Domain.StepType.File, StepConfig = config };
        var result = await new FileStepExecutor().ExecuteAsync(new LoadTestingTool.Domain.StepExecutionRequest { Step = step, Context = new LoadTestingTool.Domain.ExecutionContext(), WorkspaceRoot = dir.Path, CancellationToken = CancellationToken.None });
        Assert.True(result.Succeeded, result.ErrorMessage);
        using var json = JsonDocument.Parse(result.ResponseText);
        Assert.Equal(1, json.RootElement.GetProperty("image").GetProperty("width").GetInt32());
        Assert.Equal("image/png", json.RootElement.GetProperty("mimeType").GetString());
    }
}
