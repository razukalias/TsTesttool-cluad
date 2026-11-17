using LoadTestingTool.Editing;
using Xunit;

namespace WorkbookEditTests;

public class ValuePartsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("user-<randomnumber:6>@test.com")]
    [InlineData("<guid>")]
    [InlineData("id=<GUID:n>&at=<datum>")]
    [InlineData("<From_request_email_r>")]
    [InlineData("Bearer <From_response_Login.token_r> x")]
    [InlineData("<From_request_title_4> and <From_config_url_2>")]
    [InlineData("<randomnumber_8><randomnumber:100-999>")]
    [InlineData("<currentdatetime:yyyy-MM-dd HH:mm>")]
    [InlineData("<from_filepath>docs\\a.pdf")]
    [InlineData("<from_filepath_base64>C:\\files\\a b.png")]
    [InlineData("text with < and > but no token <notatoken>")]
    public void Parsing_then_formatting_gives_back_exactly_the_same_text(string text)
        => Assert.Equal(text, ValueParts.Format(ValueParts.Parse(text)));

    [Fact]
    public void Parts_are_typed()
    {
        var parts = ValueParts.Parse("user-<randomnumber:6>-<guid:N>-<From_response_Login.id_r>-<datum>");
        Assert.Equal([ValuePartKind.Text, ValuePartKind.RandomNumber, ValuePartKind.Text, ValuePartKind.Guid, ValuePartKind.Text, ValuePartKind.Reference, ValuePartKind.Text, ValuePartKind.DateTime],
            parts.Select(p => p.Kind));
        Assert.Equal("From_response_Login.id_r", parts[5].Label);
    }

    [Fact]
    public void A_whole_cell_file_reference_is_one_part_and_builders_write_the_documented_text()
    {
        Assert.Equal([ValuePartKind.File], ValueParts.Parse("<from_filepath>a.txt").Select(p => p.Kind));
        Assert.Equal("<randomnumber>", ValuePart.Random().Raw);
        Assert.Equal("<randomnumber:100-999>", ValuePart.Random("100-999").Raw);
        Assert.Equal("<guid:N>", ValuePart.Guid("n").Raw);
        Assert.Equal("<From_request_email_r>", ValuePart.From("request", "email").Raw);
        Assert.Equal("<From_response_Login.token_r>", ValuePart.From("response", "Login.token").Raw);
        Assert.Equal("<from_filepath_base64>a.png", ValuePart.File("a.png", base64: true).Raw);
        Assert.Equal("<currentdatetime:yyyy>", ValuePart.Date("currentdatetime", "yyyy").Raw);
    }

    [Fact]
    public void Reordering_and_removing_parts_changes_the_text_accordingly()
    {
        var parts = ValueParts.Parse("a<guid>b<randomnumber>");
        (parts[0], parts[1]) = (parts[1], parts[0]);
        parts.RemoveAt(3);
        Assert.Equal("<guid>ab", ValueParts.Format(parts));
    }

    [Fact]
    public void Conditions_map_to_the_runners_verbs_and_the_header_and_cell_verbs_split()
    {
        Assert.Equal(17, ValueParts.Conditions.Count);
        Assert.Equal(("contains", "Login.token"), ValueParts.SplitHeader("{contains}Login.token"));
        Assert.Equal(("eq", "Login.token"), ValueParts.SplitHeader("Login.token"));
        Assert.Equal(("ne", "abc"), ValueParts.SplitCell("{NE}abc"));
        Assert.Equal((null, "{notaverb}abc"), ValueParts.SplitCell("{notaverb}abc"));
        Assert.Equal("{gt}5", ValueParts.JoinCell("gt", "eq", "5"));
        Assert.Equal("5", ValueParts.JoinCell("eq", "eq", "5"));
        Assert.False(ValueParts.NeedsValue("notempty"));
        Assert.True(ValueParts.NeedsValue("contains"));
    }

    [Fact]
    public void An_example_shows_generated_parts_as_samples()
    {
        var example = ValueParts.Example(ValueParts.Parse("n-<randomnumber:6>-<From_request_x_r>"));
        Assert.StartsWith("n-482913-‹From_request_x_r›", example);
    }
}
