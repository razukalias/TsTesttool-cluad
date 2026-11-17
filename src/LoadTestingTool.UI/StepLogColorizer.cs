using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace LoadTestingTool.UI;

/// <summary>Colours the text of a step details tab: headings, "key  value" labels, log levels and pass/fail marks.</summary>
internal sealed class StepLogColorizer(SectionKind kind) : DocumentColorizingTransformer
{
    private static readonly Regex LogLine = new(@"^(?<time>\d\d:\d\d:\d\d\.\d{3}) (?<level>INFO |WARN |ERROR)", RegexOptions.Compiled);
    private static readonly Regex Key = new(@"^(?<key>\S+(?: \S+)*) {2,}\S", RegexOptions.Compiled);
    private static readonly Regex Verdict = new(@"\b(?<pass>PASS(?:ED)?|OK)\b|\b(?<fail>FAIL(?:ED)?|ERROR)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6F87A1"));
    private static readonly IBrush Info = new SolidColorBrush(Color.Parse("#8EA4BC"));
    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#F6C76B"));
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#FF8998"));
    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#39D39B"));
    private static readonly IBrush Heading = new SolidColorBrush(Color.Parse("#79BCFF"));

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        var text = CurrentContext.Document.GetText(line);

        if (text.StartsWith("── ", StringComparison.Ordinal) || text.StartsWith("══", StringComparison.Ordinal)) { Paint(line, 0, text.Length, Heading, bold: true); return; }
        if (text.StartsWith('✗')) { Paint(line, 0, text.Length, Error); return; }
        if (text.StartsWith('✓')) { Paint(line, 0, text.Length, Good); return; }

        if (kind == SectionKind.Log && LogLine.Match(text) is { Success: true } log)
        {
            Paint(line, 0, log.Groups["time"].Length, Muted);
            var level = log.Groups["level"];
            Paint(line, level.Index, level.Length, level.Value.StartsWith("ERROR") ? Error : level.Value.StartsWith("WARN") ? Warn : Info, bold: !level.Value.StartsWith("INFO"));
            if (level.Value.StartsWith("ERROR")) Paint(line, level.Index + level.Length, text.Length - level.Index - level.Length, Error);
            else if (level.Value.StartsWith("WARN")) Paint(line, level.Index + level.Length, text.Length - level.Index - level.Length, Warn);
            return;
        }

        if (kind == SectionKind.KeyValue && Key.Match(text) is { Success: true } key)
        {
            var length = key.Groups["key"].Length;
            Paint(line, 0, length, Muted);
            if (key.Groups["key"].Value is "Result")
                foreach (Match verdict in Verdict.Matches(text, length))
                    Paint(line, verdict.Index, verdict.Length, verdict.Groups["pass"].Success ? Good : Error, bold: true);
            if (key.Groups["key"].Value == "Error") Paint(line, length, text.Length - length, Error);
        }
    }

    private void Paint(DocumentLine line, int from, int length, IBrush brush, bool bold = false)
    {
        if (length <= 0) return;
        ChangeLinePart(line.Offset + from, line.Offset + from + length, element =>
        {
            element.TextRunProperties.SetForegroundBrush(brush);
            if (bold) element.TextRunProperties.SetTypeface(new Typeface(element.TextRunProperties.Typeface.FontFamily, FontStyle.Normal, FontWeight.Bold));
        });
    }
}
