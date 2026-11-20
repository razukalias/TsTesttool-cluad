using System.Globalization;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// "+" and copying: new testcases and steps, duplicates, and copies into another instance's workbook.
public sealed partial class DataEditorViewModel
{
    public WorkbookDocument Document => _document;

    /// <summary>The "+" on an instance: a new testcase with one step and one data row, shown straight away.</summary>
    public int? AddTestcaseQuick()
    {
        Record("add testcase");
        try
        {
            var index = _document.AddBlankTestcase();
            MarkChanged();
            LoadTestcases(index);
            ShowStatus($"Added testcase {index}. Rename it, then fill in its step and data.", isError: false);
            return index;
        }
        catch (InvalidOperationException ex)
        {
            DiscardLastRecord();
            ShowStatus(ex.Message, isError: true);
            return null;
        }
    }

    /// <summary>Copies the step next to the original inside its testcase.</summary>
    public string? DuplicateStepNamed(string name, string? newName = null, bool copyTemplate = false)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase));
        if (step is null || SelectedTestcase is not { } testcase) return null;
        Record($"duplicate step {name}");
        try
        {
            var result = _document.DuplicateStep(step.Row, newName);
            var templateNote = $"It shares the template file {result.TemplateSource ?? "(none)"}.";
            if (copyTemplate)
            {
                var copy = _document.Config.Rows.FirstOrDefault(r => !r.IsBlank && string.Equals(r.Get("stepname").Trim(), result.NewName, StringComparison.OrdinalIgnoreCase));
                if (copy is not null && (result.TemplateSource is not null || StepConfigFiles.SourceOf(copy.Get("stepconfig")) is not null)) templateNote = "It has " + GiveOwnTemplates([copy]) + GiveOwnQueryFiles([copy]) + ".";
            }
            MarkChanged();
            LoadTestcases(testcase.Index);
            ShowStatus($"Duplicated step {name} as {result.NewName} ({result.ColumnsAdded} new data {(result.ColumnsAdded == 1 ? "column" : "columns")}). {templateNote}", isError: false);
            return result.NewName;
        }
        catch (InvalidOperationException ex)
        {
            DiscardLastRecord();
            ShowStatus(ex.Message, isError: true);
            return null;
        }
    }

    /// <summary>Copies a testcase of <paramref name="source"/> into this instance.</summary>
    public int? ImportTestcase(DataEditorViewModel source, int sourceIndex)
    {
        Record($"copy testcase from {source.InstanceName}");
        try
        {
            var result = source._document.CopyTestcaseTo(_document, sourceIndex);
            var files = CopyTemplateFiles(source, result.TemplateSources) + CopyQueryFiles(source, _document.Config.Rows.Where(r => !r.IsBlank && WorkbookDocument.EffectiveIndex(r) == result.NewIndex).ToList());
            MarkChanged();
            LoadTestcases(result.NewIndex);
            ShowStatus($"Copied testcase from {source.InstanceName} as {result.NewIndex} · {result.NewName} ({result.Steps} {(result.Steps == 1 ? "step" : "steps")}, {result.DataRows} data {(result.DataRows == 1 ? "row" : "rows")}, {files}). Save {InstanceName} to keep it.", isError: false);
            return result.NewIndex;
        }
        catch (InvalidOperationException ex)
        {
            DiscardLastRecord();
            ShowStatus(ex.Message, isError: true);
            return null;
        }
    }

    /// <summary>Copies one step of <paramref name="source"/> into a testcase of this instance.</summary>
    public string? ImportStep(DataEditorViewModel source, StepRowViewModel step, int targetTestcaseIndex)
    {
        Record($"copy step {step.CurrentName} from {source.InstanceName}");
        try
        {
            var result = source._document.CopyStepTo(_document, step.Row, targetTestcaseIndex);
            var files = CopyTemplateFiles(source, result.TemplateSource is null ? [] : [result.TemplateSource])
                + CopyQueryFiles(source, _document.Config.Rows.Where(r => !r.IsBlank && string.Equals(r.Get("stepname").Trim(), result.NewName, StringComparison.OrdinalIgnoreCase)).ToList());
            MarkChanged();
            LoadTestcases(targetTestcaseIndex);
            ShowStatus($"Copied step {step.CurrentName} from {source.InstanceName} as {result.NewName} ({result.ColumnsAdded} new data {(result.ColumnsAdded == 1 ? "column" : "columns")}, {files}). Save {InstanceName} to keep it.", isError: false);
            return result.NewName;
        }
        catch (InvalidOperationException ex)
        {
            DiscardLastRecord();
            ShowStatus(ex.Message, isError: true);
            return null;
        }
    }

    /// <summary>
    /// Gives each of the (new) config rows its own copy of the template file it points at, named "name copy.ext" (then "copy2"…),
    /// and points the row at the copy. Rows that used the same file share one new copy. Files are not removed by Undo.
    /// </summary>
    private string GiveOwnTemplates(IReadOnlyList<TableRow> rows)
    {
        var made = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int missing = 0;
        foreach (var row in rows)
        {
            var relative = row.Get("templetsource").Trim();
            if (relative.Length == 0) continue;
            if (made.TryGetValue(relative, out var existing)) { row.Set("templetsource", existing); continue; }
            try
            {
                var from = TemplateEditorWindow.ResolveInside(TemplatesFolder, relative);
                if (!File.Exists(from)) { missing++; continue; }
                var folder = Path.GetDirectoryName(relative.Replace('\\', '/')) ?? string.Empty;
                var stem = Path.GetFileNameWithoutExtension(relative);
                var extension = Path.GetExtension(relative);
                string candidate;
                var n = 1;
                do
                {
                    var name = stem + " copy" + (n == 1 ? string.Empty : n.ToString(CultureInfo.InvariantCulture)) + extension;
                    candidate = folder.Length == 0 ? name : folder + "/" + name;
                    n++;
                }
                while (File.Exists(TemplateEditorWindow.ResolveInside(TemplatesFolder, candidate)));
                File.Copy(from, TemplateEditorWindow.ResolveInside(TemplatesFolder, candidate));
                made[relative] = candidate;
                row.Set("templetsource", candidate);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { missing++; }
        }
        var text = made.Count == 0 ? "no template files copied" : "its own template " + (made.Count == 1 ? "file " : "files ") + string.Join(", ", made.Values);
        return missing > 0 ? text + $" ({missing} not found, still shared)" : text;
    }

    private string WorkspaceDir => Path.GetDirectoryName(Path.GetFullPath(WorkbookPath)) ?? ".";

    private static string? InsideOrNull(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// The graphql, sql or script file named by <c>source</c> in a (new) step's stepconfig: gives each row its own copy ("name copy.ext", then
    /// "copy2"…) and points the stepconfig at it. Rows that named the same file share one new copy. Returns text for the status message, or nothing.
    /// </summary>
    private string GiveOwnQueryFiles(IReadOnlyList<TableRow> rows)
    {
        var made = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int missing = 0;
        foreach (var row in rows)
        {
            var config = row.Get("stepconfig");
            if (StepConfigFiles.SourceOf(config) is not { } relative) continue;
            if (made.TryGetValue(relative, out var existing)) { row.Set("stepconfig", StepConfigFiles.WithSource(config, existing)); continue; }
            var from = InsideOrNull(WorkspaceDir, relative);
            if (from is null || !File.Exists(from)) { missing++; continue; }
            var folder = Path.GetDirectoryName(relative.Replace('\\', '/')) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(relative);
            var extension = Path.GetExtension(relative);
            string candidate;
            var n = 1;
            do
            {
                var name = stem + " copy" + (n == 1 ? string.Empty : n.ToString(CultureInfo.InvariantCulture)) + extension;
                candidate = folder.Length == 0 ? name : folder + "/" + name;
                n++;
            }
            while (InsideOrNull(WorkspaceDir, candidate) is not { } target || File.Exists(target));
            File.Copy(from, InsideOrNull(WorkspaceDir, candidate)!);
            made[relative] = candidate;
            row.Set("stepconfig", StepConfigFiles.WithSource(config, candidate));
        }
        if (made.Count == 0 && missing == 0) return string.Empty;
        var text = made.Count == 0 ? "no query files copied" : "its own query " + (made.Count == 1 ? "file " : "files ") + string.Join(", ", made.Values);
        return ", " + (missing > 0 ? text + $" ({missing} not found, still shared)" : text);
    }

    /// <summary>Copies the <c>source</c> files named in copied steps' stepconfig into this instance, at the same relative path; existing files are kept.</summary>
    private string CopyQueryFiles(DataEditorViewModel source, IReadOnlyList<TableRow> rows)
    {
        int copied = 0, kept = 0, differs = 0, missing = 0;
        foreach (var relative in rows.Select(r => StepConfigFiles.SourceOf(r.Get("stepconfig"))).Where(p => p is not null).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var from = InsideOrNull(source.WorkspaceDir, relative);
            var to = InsideOrNull(WorkspaceDir, relative);
            if (from is null || to is null || !File.Exists(from)) { missing++; continue; }
            try
            {
                if (File.Exists(to)) { if (File.ReadAllBytes(from).AsSpan().SequenceEqual(File.ReadAllBytes(to))) kept++; else differs++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { missing++; }
        }
        if (copied + kept + differs + missing == 0) return string.Empty;
        var parts = new List<string> { $"{copied.ToString(CultureInfo.InvariantCulture)} query {(copied == 1 ? "file" : "files")} copied" };
        if (kept > 0) parts.Add($"{kept} already there");
        if (differs > 0) parts.Add($"{differs} with the same name but different content were kept as they are, check {(differs == 1 ? "it" : "them")}");
        if (missing > 0) parts.Add($"{missing} not found");
        return ", " + string.Join(", ", parts);
    }

    /// <summary>Copies template files into this instance's Templates folder; files that already exist there are kept as they are.</summary>
    private string CopyTemplateFiles(DataEditorViewModel source, IReadOnlyList<string> relativePaths)
    {
        int copied = 0, kept = 0, differs = 0, missing = 0;
        foreach (var relative in relativePaths)
        {
            try
            {
                var from = TemplateEditorWindow.ResolveInside(source.TemplatesFolder, relative);
                var to = TemplateEditorWindow.ResolveInside(TemplatesFolder, relative);
                if (!File.Exists(from)) { missing++; continue; }
                if (File.Exists(to)) { if (File.ReadAllBytes(from).AsSpan().SequenceEqual(File.ReadAllBytes(to))) kept++; else differs++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { missing++; }
        }
        if (relativePaths.Count == 0) return "no template files";
        var parts = new List<string> { $"{copied.ToString(CultureInfo.InvariantCulture)} template {(copied == 1 ? "file" : "files")} copied" };
        if (kept > 0) parts.Add($"{kept} already there");
        if (differs > 0) parts.Add($"{differs} with the same name but different content were kept as they are, check {(differs == 1 ? "it" : "them")}");
        if (missing > 0) parts.Add($"{missing} not found");
        return string.Join(", ", parts);
    }
}
