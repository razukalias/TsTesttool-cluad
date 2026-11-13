using CommunityToolkit.Mvvm.ComponentModel;

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
}
