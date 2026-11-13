using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

// Preview request: what the selected step would send for the selected data row.
public sealed partial class DataEditorViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PreviewStepNames))]
    private bool _previewVisible;

    [ObservableProperty] private string? _previewStepName;
    [ObservableProperty] private RequestPreview? _preview;

    public IReadOnlyList<string> PreviewStepNames => SelectedTestcase?.Steps ?? [];

    partial void OnPreviewVisibleChanged(bool value) => RefreshPreview();

    partial void OnPreviewStepNameChanged(string? value) => RefreshPreview();

    /// <summary>Rebuilds the preview from the current (possibly unsaved) cells; call after edits.</summary>
    public void RefreshPreview()
    {
        if (!PreviewVisible) return;
        var steps = PreviewStepNames;
        if (PreviewStepName is null || !steps.Contains(PreviewStepName)) { PreviewStepName = steps.FirstOrDefault(); return; }
        var step = StepRows.FirstOrDefault(s => string.Equals(s.CurrentName, PreviewStepName, StringComparison.OrdinalIgnoreCase));
        if (SelectedRow is not { } row || step is null) { Preview = null; return; }
        Preview = RequestPreview.Build(WorkbookPath, row.Request, _document.Request.Headers, step.Row, _document.Config.Headers);
    }

    /// <summary>Raised when the view should open the template editor for the previewed step.</summary>
    public event EventHandler<TemplateEditRequest>? EditTemplateRequested;

    public string TemplatesFolder => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(WorkbookPath)) ?? ".", "Templates");

    [RelayCommand]
    private void EditTemplate()
    {
        var step = StepRows.FirstOrDefault(s => string.Equals(s.CurrentName, PreviewStepName, StringComparison.OrdinalIgnoreCase));
        if (step is null || SelectedTestcase is not { } testcase) return;
        var type = step.Row.Get("steptype");
        EditTemplateRequested?.Invoke(this, new TemplateEditRequest(TemplatesFolder, step.Row.Get("templetsource"), type.Length == 0 ? "HTTP" : type, testcase.Name, step.CurrentName, SaveTemplateChoice(step)));
    }

    /// <summary>Called by the template editor after it wrote a file: points the step at it if the name changed, and refreshes the preview.</summary>
    private Action<string> SaveTemplateChoice(StepRowViewModel step) => relative =>
    {
        var column = StepColumns.FirstOrDefault(c => c.Title.Equals("templetsource", StringComparison.OrdinalIgnoreCase));
        if (column is not null && !string.Equals(step.Row.Get("templetsource"), relative, StringComparison.Ordinal))
        {
            Record($"set template of {step.CurrentName}");
            step.Row.Set("templetsource", relative);
            step.SetCellSilently(column.CellIndex, relative);
            StepsChanged();
        }
        RefreshPreview();
    };

    /// <summary>Autocomplete for cells: the workbook's sheets and their headers, as the document has them right now (including unsaved columns).</summary>
    public CellCompletion Completion => new(
        () => ["config", "request", "response", .. _document.OtherSheets],
        sheet => sheet.ToLowerInvariant() switch
        {
            "config" => _document.Config.Headers,
            "request" => _document.Request.Headers,
            "response" => _document.Response.Headers,
            _ => [],
        });
}

public sealed record TemplateEditRequest(string TemplatesFolder, string Source, string StepType, string Testcase, string Step, Action<string> Saved);
