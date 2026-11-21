using System.Globalization;
using System.Text.RegularExpressions;

namespace LoadTestingTool.Editing;

/// <summary>
/// Gives a Data ID that is already taken the next free number of its name: <c>LOGIN</c> next to <c>LOGIN</c>, <c>LOGIN2</c>, <c>LOGIN3</c> becomes
/// <c>LOGIN4</c>; <c>CUST002</c> next to <c>CUST001</c>…<c>CUST002</c> becomes <c>CUST003</c> (the zero padding of the others is kept); an ID without a number
/// counts as number 1, so the first copy is <c>2</c>. An ID that is free is returned as it is. Several calls with the same set number the IDs one after another.
/// </summary>
public static class DataIdNumbering
{
    private static readonly Regex Numbered = new(@"^(.*?)(\d{1,9})$", RegexOptions.Compiled);

    /// <summary>The ID to use for <paramref name="wanted"/>; it is added to <paramref name="taken"/> (compared without regard to case).</summary>
    public static string Unique(string wanted, ISet<string> taken)
    {
        wanted = wanted.Trim();
        if (wanted.Length > 0 && !taken.Contains(wanted)) { taken.Add(wanted); return wanted; }
        var match = Numbered.Match(wanted);
        var stem = match.Success && match.Groups[1].Length > 0 ? match.Groups[1].Value : wanted;
        if (stem.Length == 0) stem = "ROW";
        var highest = 1;   // a bare name is the first of its kind
        var width = 0;
        foreach (var other in taken)
        {
            if (other.Equals(stem, StringComparison.OrdinalIgnoreCase)) continue;
            var m = Numbered.Match(other);
            if (!m.Success || !m.Groups[1].Value.Equals(stem, StringComparison.OrdinalIgnoreCase)) continue;
            var digits = m.Groups[2].Value;
            highest = Math.Max(highest, int.Parse(digits, CultureInfo.InvariantCulture));
            if (digits.Length > 1 && digits[0] == '0') width = Math.Max(width, digits.Length);
        }
        string candidate;
        do { highest++; candidate = stem + highest.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'); }
        while (taken.Contains(candidate));
        taken.Add(candidate);
        return candidate;
    }

    public static HashSet<string> TakenSet(IEnumerable<string> ids) => new(ids.Select(i => i.Trim()).Where(i => i.Length > 0), StringComparer.OrdinalIgnoreCase);
}

/// <summary>One Data ID renamed to remove a duplicate.</summary>
public readonly record struct DataIdRename(int TestcaseIndex, string Old, string New);
