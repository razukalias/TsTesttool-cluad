namespace LoadTestingTool.Validation;

public sealed class TestcaseSelectionParser
{
    public IReadOnlyList<int> Parse(string? expression, IEnumerable<int> available)
    {
        var indexes = available.Distinct().OrderBy(x => x).ToHashSet();
        expression = string.IsNullOrWhiteSpace(expression) ? "0" : expression.Trim();
        if (expression.Equals("0", StringComparison.OrdinalIgnoreCase)) return indexes.OrderBy(x => x).ToList();
        var selected = new HashSet<int>();
        foreach (var token in expression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Contains('-'))
            {
                var parts = token.Split('-', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !int.TryParse(parts[0], out var start) || !int.TryParse(parts[1], out var end) || start > end) throw new FormatException($"Invalid testcase range '{token}'.");
                for (var i = start; i <= end; i++) selected.Add(i);
            }
            else if (int.TryParse(token, out var index)) selected.Add(index);
            else throw new FormatException($"Invalid testcase selection token '{token}'.");
        }
        var missing = selected.Where(x => !indexes.Contains(x)).OrderBy(x => x).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"Selected testcase index(es) do not exist: {string.Join(", ", missing)}.");
        return selected.OrderBy(x => x).ToList();
    }
}
