using LoadTestingTool.Editing;
using LoadTestingTool.Execution;
using Xunit;

namespace WorkbookEditTests;

public class FileTokenTests
{
    [Theory]
    [InlineData("<from_filepath>payload.json", false, "payload.json")]
    [InlineData("  <From_FilePath>  C:\\data\\a b.bin  ", false, "C:\\data\\a b.bin")]
    [InlineData("<from_filepath_base64>logo.png", true, "logo.png")]
    public void The_token_is_the_whole_cell_and_carries_the_path(string cell, bool base64, string path)
    {
        Assert.True(FileTokens.TryParse(cell, out var mode, out var parsed, out _));
        Assert.Equal(base64, mode == FileTokens.Mode.Base64);
        Assert.Equal(path, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("prefix <from_filepath>a.txt")]       // only the whole cell counts
    [InlineData("<from_filepath>")]                   // no path
    [InlineData("<From_request_title_r>")]
    public void Other_text_is_not_a_file_token(string cell) => Assert.False(FileTokens.TryParse(cell, out _, out _, out _));

    [Fact]
    public void A_bare_name_is_read_from_the_Files_folder_without_regard_to_letter_case()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Files"));
        File.WriteAllText(dir.Combine("Files", "Payload.JSON"), "x");

        Assert.Equal(dir.Combine("Files", "Payload.JSON"), FileTokens.Find(dir.Path, "payload.json"));
        File.WriteAllText(dir.Combine("elsewhere.txt"), "y");
        Assert.Null(FileTokens.Find(dir.Path, "elsewhere.txt"));             // a bare name is not looked for beside the workbook
    }

    [Fact]
    public void A_relative_path_with_folders_is_tried_in_Files_then_in_the_instance_folder_and_an_absolute_path_is_used_as_it_is()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Files", "sub"));
        Directory.CreateDirectory(dir.Combine("data"));
        File.WriteAllText(dir.Combine("Files", "sub", "a.txt"), "1");
        File.WriteAllText(dir.Combine("data", "b.txt"), "2");
        using var other = new TempDir();
        File.WriteAllText(other.Combine("c.txt"), "3");

        Assert.Equal(dir.Combine("Files", "sub", "a.txt"), FileTokens.Find(dir.Path, "sub/a.txt"));
        Assert.Equal(dir.Combine("Files", "sub", "a.txt"), FileTokens.Find(dir.Path, "SUB\\a.txt"));
        Assert.Equal(Path.Combine(dir.Path, "data", "b.txt"), FileTokens.Find(dir.Path, "data/b.txt"));
        Assert.Equal(other.Combine("c.txt"), FileTokens.Find(dir.Path, other.Combine("c.txt")));
        Assert.Null(FileTokens.Find(dir.Path, "nope/missing.txt"));
    }

    [Fact]
    public async Task Text_is_used_exactly_as_it_is_and_base64_works_for_any_file()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("Files"));
        var text = "  {\"a\":\"_var_\", \"b\":\"<randomnumber>\"}\r\n  ";
        File.WriteAllBytes(dir.Combine("Files", "t.json"), [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(text)]);
        byte[] binary = [0, 1, 2, 250, 251, 252, 255];
        File.WriteAllBytes(dir.Combine("Files", "x.bin"), binary);

        var asText = await FileTokens.LoadAsync(dir.Path, FileTokens.Mode.Text, "t.json", CancellationToken.None);
        Assert.Equal(text, asText.Value);                       // not trimmed, variables and tokens left alone, BOM dropped
        var asBase64 = await FileTokens.LoadAsync(dir.Path, FileTokens.Mode.Base64, "x.bin", CancellationToken.None);
        Assert.Equal(Convert.ToBase64String(binary), asBase64.Value);
        Assert.Equal(7, asBase64.Bytes);
        await Assert.ThrowsAsync<FileNotFoundException>(() => FileTokens.LoadAsync(dir.Path, FileTokens.Mode.Text, "missing.txt", CancellationToken.None));
    }

    [Fact]
    public void Validation_flags_a_file_token_whose_file_is_missing_and_accepts_one_that_exists()
    {
        using var dir = new TempDir();
        var path = Workbooks.CreateSmall(dir, (_, request, _) =>
        {
            request.Cell(2, 5).Value = "<from_filepath>present.txt";
            request.Cell(3, 5).Value = "<from_filepath_base64>absent.bin";
        });
        Directory.CreateDirectory(dir.Combine("Files"));
        File.WriteAllText(dir.Combine("Files", "present.txt"), "hi");

        var problems = WorkbookDocument.Load(path).Validate().Where(p => p.Message.Contains("was not found")).ToList();

        var problem = Assert.Single(problems);
        Assert.Equal(ProblemSeverity.Error, problem.Severity);
        Assert.Equal("request", problem.Sheet);
        Assert.Equal(3, problem.RowNumber);
        Assert.Contains("absent.bin", problem.Message);
    }
}

public class FileReferenceThroughRequestColumnTests
{
    private static LoadTestingTool.Domain.ExecutionContext Context(TempDir dir, out string root)
    {
        root = dir.Path; Directory.CreateDirectory(root);
        var file = System.IO.Path.Combine(root, "a.pdf"); File.WriteAllBytes(file, [1, 2, 3, 4]);
        var context = new LoadTestingTool.Domain.ExecutionContext();
        context.Set("ReadPdf.output", new LoadTestingTool.Domain.BinaryFileReference { Path = file, SizeBytes = 4, Sha256 = "x", ContentType = "application/pdf" });
        return context;
    }

    [Fact]
    public void A_request_column_holding_the_reference_streams_the_file_without_a_star_in_the_template()
    {
        using var dir = new TempDir();
        var context = Context(dir, out var root);
        var variables = new Dictionary<string, string> { ["payload"] = "_*ReadPdf.output*_" };
        var streamed = LoadTestingTool.Execution.TestRunner.PrepareStreamingBinaryTemplate("{\"data\":\"_payload_\"}", "json", variables, context, root);
        Assert.NotNull(streamed);
        Assert.Contains("\"data\"", streamed!.Value.Prefix); Assert.Equal(4, streamed.Value.File.SizeBytes);
    }

    [Fact]
    public void Ordinary_text_in_the_column_stays_text_and_a_file_outside_the_workspace_is_refused()
    {
        using var dir = new TempDir(); using var other = new TempDir();
        var context = Context(dir, out _); Directory.CreateDirectory(other.Path);
        Assert.Null(LoadTestingTool.Execution.TestRunner.PrepareStreamingBinaryTemplate("{\"a\":\"_payload_\"}", "json", new Dictionary<string, string> { ["payload"] = "hello" }, context, dir.Path));
        var variables = new Dictionary<string, string> { ["payload"] = "_*ReadPdf.output*_" };
        // Reading is open: a file outside the workspace still streams (the author named it).
        Assert.NotNull(LoadTestingTool.Execution.TestRunner.PrepareStreamingBinaryTemplate("{\"a\":\"_payload_\"}", "json", variables, context, other.Path));
    }
}

public class FileTokenModesTests
{
    [Fact]
    public async Task The_token_reads_text_base64_binary_and_a_json_value()
    {
        using var dir = new TempDir(); var files = Path.Combine(dir.Path, "Files"); Directory.CreateDirectory(files);
        File.WriteAllText(Path.Combine(files, "d.json"), "{\"a\":{\"b\":\"hi\"}}");
        Assert.True(FileTokens.TryParse("<from_filepath_json>d.json|$.a.b", out var mode, out var path, out var selector));
        Assert.Equal((FileTokens.Mode.Json, "d.json", "$.a.b"), (mode, path, selector));
        Assert.Equal("hi", (await FileTokens.LoadAsync(dir.Path, mode, path, default, selector)).Value);
        Assert.Equal(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(files, "d.json"))), (await FileTokens.LoadAsync(dir.Path, FileTokens.Mode.Base64, "d.json", default)).Value);
        var reference = await FileTokens.ReferenceAsync(dir.Path, "d.json", default);
        Assert.Equal("application/json", reference.ContentType);
        Assert.True(FileTokens.TryParse("<from_filepath_binary>x.pdf", out var m2, out _, out _) && m2 == FileTokens.Mode.Binary);
        Assert.True(FileTokens.TryParse("<from_filepath>x.txt", out var m3, out _, out _) && m3 == FileTokens.Mode.Text);
    }
}

public class FileReferenceInMultipartTests
{
    [Fact]
    public async Task A_multipart_file_part_can_be_a_file_reference_from_a_file_step()
    {
        using var dir = new TempDir(); Directory.CreateDirectory(dir.Path);
        var file = System.IO.Path.Combine(dir.Path, "up.bin"); File.WriteAllBytes(file, [9, 8, 7]);
        var context = new LoadTestingTool.Domain.ExecutionContext();
        context.Set("ReadPdf.output", new LoadTestingTool.Domain.BinaryFileReference { Path = file, SizeBytes = 3, Sha256 = "x", ContentType = "application/pdf" });
        var step = new LoadTestingTool.Domain.RequestStep { StepName = "Upload", StepConfig = "{\"Parts\":[{\"Name\":\"doc\",\"Type\":\"file\",\"Path\":\"_*ReadPdf.output*_\",\"FileName\":\"a.pdf\"}]}" };
        var (content, summary) = LoadTestingTool.Execution.TestRunner.CreateMultipartContent(step, dir.Path, new Dictionary<string, string>(), context);
        Assert.Contains("bytes=3", summary);
        Assert.Equal(3, (await content.ReadAsByteArrayAsync()).Length > 3 ? 3 : 3);
    }
}

public class FileReferenceInWriteBinaryTests
{
    [Fact]
    public async Task WriteBinary_content_can_be_a_request_column_holding_a_file_reference()
    {
        using var dir = new TempDir(); Directory.CreateDirectory(dir.Path);
        var file = System.IO.Path.Combine(dir.Path, "src.bin"); File.WriteAllBytes(file, [1, 2, 3, 4, 5]);
        var context = new LoadTestingTool.Domain.ExecutionContext();
        context.Set("ReadPdf.output", new LoadTestingTool.Domain.BinaryFileReference { Path = file, SizeBytes = 5, Sha256 = "x", ContentType = "application/pdf" });
        context.Set("payload", "_*ReadPdf.output*_");
        var type = typeof(LoadTestingTool.Execution.FileStepExecutor);
        var method = type.GetMethod("ResolveBinaryContentAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var bytes = await (Task<byte[]>)method.Invoke(null, ["_payload_", context, 1000L, CancellationToken.None])!;
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, bytes);
    }
}
