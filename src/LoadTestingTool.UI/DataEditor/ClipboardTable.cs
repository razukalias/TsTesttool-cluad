using System.Text;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Reads and writes the tab-separated text Excel puts on the clipboard (quoted cells may contain tabs, quotes and line breaks).</summary>
public static class ClipboardTable
{
    public static string[][] Parse(string text, char delimiter = '\t')
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var atCellStart = true;

        void EndCell() { row.Add(cell.ToString()); cell.Clear(); atCellStart = true; }
        void EndRow() { EndCell(); rows.Add([.. row]); row.Clear(); }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else cell.Append(c);
                continue;
            }
            switch (c)
            {
                case '"' when atCellStart: inQuotes = true; atCellStart = false; break;
                case var d when d == delimiter: EndCell(); break;
                case '\r': if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); break;
                case '\n': EndRow(); break;
                default: cell.Append(c); atCellStart = false; break;
            }
        }
        if (cell.Length > 0 || row.Count > 0) EndRow();
        return [.. rows];
    }

    public static string Format(IEnumerable<IEnumerable<string>> rows, char delimiter = '\t')
        => string.Join("\r\n", rows.Select(r => string.Join(delimiter, r.Select(v => Quote(v, delimiter)))));

    private static string Quote(string value, char delimiter)
        => value.IndexOfAny([delimiter, '\n', '\r', '"']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
