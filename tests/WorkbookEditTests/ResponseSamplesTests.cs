using ClosedXML.Excel;
using LoadTestingTool.Reporting;
using Xunit;

namespace WorkbookEditTests;

public class ResponseSamplesTests
{
    [Fact]
    public void Json_is_flattened_to_the_dotted_paths_the_runner_uses()
    {
        var fields = ResponseSamples.Flatten("{\"id\":7,\"user\":{\"name\":\"Ann\",\"ok\":true,\"none\":null},\"cases\":[{\"id\":\"c1\"},{\"id\":\"c2\"}],\"tags\":[\"a\",\"b\"]}");

        Assert.Equal(["id", "user.name", "user.ok", "user.none", "cases[0].id", "cases[1].id", "tags[0]", "tags[1]"], fields.Select(f => f.Path));
        Assert.Equal("7", fields[0].Value);
        Assert.Equal("Ann", fields[1].Value);
        Assert.Equal("true", fields[2].Value);
        Assert.Equal("", fields[3].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"cut\":")]
    [InlineData("[omitted]")]
    public void Text_that_is_neither_json_nor_xml_gives_no_fields(string text) => Assert.Empty(ResponseSamples.Flatten(text));

    [Fact]
    public void Long_arrays_are_limited_and_the_field_count_is_capped()
    {
        var items = string.Join(",", Enumerable.Range(0, 50).Select(i => "{\"id\":" + i + "}"));
        Assert.Equal(5, ResponseSamples.Flatten("{\"rows\":[" + items + "]}").Count);
        var wide = "{" + string.Join(",", Enumerable.Range(0, 2000).Select(i => "\"f" + i + "\":1")) + "}";
        Assert.Equal(ResponseSamples.MaxFields, ResponseSamples.Flatten(wide).Count);
    }

    private static string History(TempDir dir, string name, DateTime written, params (string Step, string DataId, string Body)[] rows)
    {
        Directory.CreateDirectory(dir.Combine("History"));
        var path = System.IO.Path.Combine(dir.Combine("History"), name);
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("RequestHistory");
            string[] headers = ["runid", "dataid", "stepname", "responsebody"];
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            for (var r = 0; r < rows.Length; r++)
            {
                sheet.Cell(r + 2, 1).Value = name;
                sheet.Cell(r + 2, 2).Value = rows[r].DataId;
                sheet.Cell(r + 2, 3).Value = rows[r].Step;
                sheet.Cell(r + 2, 4).Value = rows[r].Body;
            }
            book.SaveAs(path);
        }
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    [Fact]
    public void The_newest_stored_body_of_the_step_is_found_preferring_the_rows_data_id_and_skipping_omitted_bodies()
    {
        using var dir = new TempDir();
        History(dir, "old.xlsx", DateTime.UtcNow.AddDays(-2), ("Login", "d1", "{\"old\":1}"));
        History(dir, "new.xlsx", DateTime.UtcNow.AddDays(-1), ("Login", "d1", "{\"a\":1}"), ("Login", "d2", "{\"b\":2}"), ("Other", "d1", "{\"c\":3}"), ("Login", "d3", "[omitted]"));
        var folder = dir.Combine("History");

        Assert.Equal("{\"b\":2}", ResponseSamples.Latest(folder, "Login", "d2")!.Body);
        Assert.Equal("{\"b\":2}", ResponseSamples.Latest(folder, "Login")!.Body);   // the last row that kept its body in the newest file (d3 was omitted)
        Assert.Equal("new.xlsx", ResponseSamples.Latest(folder, "Login", "d1")!.Source);
        Assert.Null(ResponseSamples.Latest(folder, "Nobody"));
        Assert.Null(ResponseSamples.Latest(dir.Combine("none"), "Login"));
    }
}
