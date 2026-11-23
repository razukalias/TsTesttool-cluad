using System.Text.Json;
using LoadTestingTool.UI;
using Xunit;

namespace UiTests;

public class TemplateValidationTests
{
    [Theory]
    [InlineData("{ \"isPrimary\": _isPrimary[0]_ }")]                 // an indexed placeholder where a value goes without quotes
    [InlineData("{ \"count\": _*total*_ }")]                          // the starred form
    [InlineData("{ \"a\": [ _list[0]_, _list[1]_ ], \"b\": _userId_ }")]
    [InlineData("{ \"name\": \"_name_\", \"order\": \"<From_request_GetOrder.orderid_r>\" }")]
    public void Placeholders_the_runner_accepts_do_not_make_the_template_look_invalid(string template)
        => JsonDocument.Parse(GlobalValidationService.NormalizeRuntimeVariablesForJson(template)).Dispose();

    [Fact]
    public void A_genuinely_broken_template_is_still_reported()
        => Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(GlobalValidationService.NormalizeRuntimeVariablesForJson("{ \"a\": _x_ ")));
}
