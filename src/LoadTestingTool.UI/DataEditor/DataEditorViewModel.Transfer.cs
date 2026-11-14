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
    public string? DuplicateStepNamed(string name)
    {
        var step = StepRows.FirstOrDefault(r => string.Equals(r.CurrentName, name, StringComparison.OrdinalIgnoreCase));
        if (step is null || SelectedTestcase is not { } testcase) return null;
        Record($"duplicate step {name}");
        try
        {
            var result = _document.DuplicateStep(step.Row);
            MarkChanged();
            LoadTestcases(testcase.Index);
            ShowStatus($"Duplicated step {name} as {result.NewName} ({result.ColumnsAdded} new data {(result.ColumnsAdded == 1 ? "column" : "columns")}). It shares the template file {result.TemplateSource ?? "(none)"}.", isError: false);
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
            var files = CopyTemplateFiles(source, result.TemplateSources);
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
            var files = CopyTemplateFiles(source, result.TemplateSource is null ? [] : [result.TemplateSource]);
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

    /// <summary>Copies template files into this instance's Templates folder; files that already exist there are kept as they are.</summary>
    private string CopyTemplateFiles(DataEditorViewModel source, IReadOnlyList<string> relativePaths)
    {
        int copied = 0, kept = 0, missing = 0;
        foreach (var relative in relativePaths)
        {
            try
            {
                var from = TemplateEditorWindow.ResolveInside(source.TemplatesFolder, relative);
                var to = TemplateEditorWindow.ResolveInside(TemplatesFolder, relative);
                if (!File.Exists(from)) { missing++; continue; }
                if (File.Exists(to)) { kept++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { missing++; }
        }
        if (relativePaths.Count == 0) return "no template files";
        var parts = new List<string> { $"{copied.ToString(CultureInfo.InvariantCulture)} template {(copied == 1 ? "file" : "files")} copied" };
        if (kept > 0) parts.Add($"{kept} already there");
        if (missing > 0) parts.Add($"{missing} not found");
        return string.Join(", ", parts);
    }
}
