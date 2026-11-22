using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

// At save time: has the workbook file been changed by someone else (Excel, another editor window, a run) since it was opened or last saved here? Nothing is checked in the background.
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
    /// Called when saving: compares the file's modified time with the one remembered when the workbook was opened or last saved here; when it differs,
    /// shows the question (reload, overwrite or cancel) and returns true, and the save does not go ahead. A missing file is left alone.
    /// </summary>
    public bool CheckFileChanged()
    {
        if (FileChangedVisible) return true;
        if (_knownWriteUtc == DateTime.MinValue) return false;
        var now = ReadWriteTimeUtc();
        if (now == DateTime.MinValue || now == _knownWriteUtc) return false;
        FileChangedText = $"This workbook was changed on disk at {now.ToLocalTime():HH:mm:ss} (you opened or saved it at {_knownWriteUtc.ToLocalTime():HH:mm:ss})."
            + " Saving now would overwrite that version. Reload to take it (your unsaved edits are lost), or overwrite it with yours.";
        FileChangedVisible = true;
        return true;
    }

    [RelayCommand]
    private void ReloadChangedFile() { Reload(); }

    /// <summary>"Cancel": nothing is saved; the next save asks again.</summary>
    [RelayCommand]
    private void CancelSaveOfChangedFile()
    {
        FileChangedVisible = false;
        ShowStatus("Not saved: the file on disk is newer. Your edits are still here.", isError: true);
    }

    /// <summary>"Overwrite with my edits": saves over the changed file.</summary>
    [RelayCommand]
    private void OverwriteChangedFile() { FileChangedVisible = false; SaveCore(new LoadTestingTool.Editing.WorkbookSaveOptions { OverwriteExternalChanges = true }); }
}
