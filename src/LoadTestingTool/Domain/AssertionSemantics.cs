namespace LoadTestingTool.Domain;

public static class AssertionSemantics
{
    public static string Type(string verb, string actualValue = "", string failureMessage = "")
    {
        if (verb.Equals("contract", StringComparison.OrdinalIgnoreCase))
        {
            if (actualValue.Equals("<missing>", StringComparison.OrdinalIgnoreCase) || failureMessage.StartsWith("Missing response path", StringComparison.OrdinalIgnoreCase)) return "Missing response field";
            if (failureMessage.StartsWith("Unexpected response path", StringComparison.OrdinalIgnoreCase)) return "Unexpected response field";
            return "Response contract validation";
        }
        return verb.ToLowerInvariant() is "exists" or "notexists" or "empty" or "notempty" ? "Structural assertion" : "Value assertion";
    }

    public static string Meaning(string verb, string actualValue = "", string failureMessage = "")
    {
        var normalized = verb.Trim().ToLowerInvariant();
        if (normalized == "contract")
        {
            if (actualValue.Equals("<missing>", StringComparison.OrdinalIgnoreCase) || failureMessage.StartsWith("Missing response path", StringComparison.OrdinalIgnoreCase)) return "The response worksheet declares this field, but the API response did not return it.";
            if (failureMessage.StartsWith("Unexpected response path", StringComparison.OrdinalIgnoreCase)) return "The API response returned this field, but it is not declared in the response worksheet.";
            return "The runner compared the actual response structure with the fields declared in the response worksheet.";
        }
        return normalized switch
        {
            "eq" => "The actual response value must equal the expected value.",
            "ne" => "The actual response value must not equal the expected value.",
            "lt" => "The actual numeric value must be less than the expected value.",
            "lte" => "The actual numeric value must be less than or equal to the expected value.",
            "gt" => "The actual numeric value must be greater than the expected value.",
            "gte" => "The actual numeric value must be greater than or equal to the expected value.",
            "contains" => "The actual response value must contain the expected text.",
            "notcontains" => "The actual response value must not contain the expected text.",
            "startswith" => "The actual response value must start with the expected text.",
            "endswith" => "The actual response value must end with the expected text.",
            "regex" => "The actual response value must match the expected regular expression.",
            "exists" => "The response field must exist.",
            "notexists" => "The response field must not exist.",
            "empty" => "The response field must be empty.",
            "notempty" => "The response field must contain a value.",
            "in" => "The actual response value must be one of the pipe-separated expected values.",
            "size" => "The actual response value length must equal the expected size.",
            _ => "The runner evaluates the response field using the configured assertion rule."
        };
    }
}
