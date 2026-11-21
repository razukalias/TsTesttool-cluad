namespace LoadTestingTool.UI.DataEditor;

/// <summary>Outline numbers for steps like the lines of code: 1, 2, then 2.1, 2.2, 2.2.1 inside a control step, then 3 again at the top level.</summary>
public static class StepNumbering
{
    public static string[] Compute(IReadOnlyList<int> depths)
    {
        var counters = new List<int>();
        var result = new string[depths.Count];
        for (var i = 0; i < depths.Count; i++)
        {
            var depth = Math.Max(0, depths[i]);
            while (counters.Count > depth + 1) counters.RemoveAt(counters.Count - 1);
            while (counters.Count < depth + 1) counters.Add(0);
            counters[depth]++;
            result[i] = string.Join('.', counters);
        }
        return result;
    }
}
