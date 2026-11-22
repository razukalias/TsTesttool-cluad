using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

// Noticing that the workbook file was changed by someone else (Excel, another editor window, a run) after it was opened or last saved here.
public sealed partial class DataEditorViewModel
{
    private DateTime _knownWriteUtc = DateTime.MinValue;

    /// <summary>True while the question "the file changed on disk, reload?" is shown above the table.</summary>
    [ObservableProperty] private bool _fileChangedVisible;
    [ObservableProperty] private string _fileChangedText = string.Empty;

    /// <summary>Remembers the file's modified time as the version this editor shows (after opening, reloading and saving).</summary>
    private void RememberFileTime()
    {
        _knownWriteUtc = ReadWriteTimeUtc();
        FileChangedVisible = false;
    }

    private DateTime ReadWriteTimeUtc()
    {
        try { return File.Exists(WorkbookPath) ? File.GetLastWriteTimeUtc(WorkbookPath) : DateTime.MinValue; }
        catch (IOException) { return DateTime.MinValue; }
    }

    /// <summary>
    /// Compares the file's modified time with the one remembered when the workbook was opened or last saved here; when it differs, shows the question.
    /// Returns true when the question is (now) shown. A missing file, a running instance and an already shown question are left alone.
    /// </summary>
    public bool CheckFileChanged()
    {
        if (FileChangedVisible) return true;
        if (IsRunning || _knownWriteUtc == DateTime.MinValue) return false;
        var now = ReadWriteTimeUtc();
        if (now == DateTime.MinValue || now == _knownWriteUtc) return false;
        FileChangedText = $"This workbook was changed on disk at {now.ToLocalTime():HH:mm:ss} (you opened or saved it at {_knownWriteUtc.ToLocalTime():HH:mm:ss})."
            + (IsDirty ? " You have unsaved edits: reloading would lose them." : " Reload to see the new version.");
        FileChangedVisible = true;
        return true;
    }

    [RelayCommand]
    private void ReloadChangedFile() { Reload(); }

    /// <summary>"Keep what I see": the changed file is accepted as seen. Saving still stops if the content really differs (the check at save time).</summary>
    [RelayCommand]
    private void KeepMyVersion()
    {
        _knownWriteUtc = ReadWriteTimeUtc();
        FileChangedVisible = false;
        ShowStatus("Kept your version. Saving will ask again if the file really differs.", isError: false);
    }
}
