using System.Diagnostics;
using Avalonia.Threading;

namespace LoadTestingTool.UI.DataEditor;

public sealed partial class DataEditorViewModel
{
    private DispatcherTimer? _recoveryTimer;
    private int _recoveryEvery = 1;
    private int _recoveryWait;

    /// <summary>Writes a recovery copy of the unsaved edits now, or removes it when everything is saved. True when a copy was written.</summary>
    public bool SaveRecoveryNow()
    {
        if (!IsDirty) { RecoveryStore.Delete(WorkbookPath); return false; }
        var clock = Stopwatch.StartNew();
        try { _document.SaveRecoveryCopy(RecoveryStore.PathFor(WorkbookPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { CrashGuard.Log("recovery copy", ex); return false; }
        // a big workbook is copied less often: the copy may take at most about 3% of the time
        _recoveryEvery = clock.Elapsed.TotalSeconds > 1 ? Math.Min(10, (int)Math.Ceiling(clock.Elapsed.TotalSeconds * 3)) : 1;
        return true;
    }

    /// <summary>Starts the half-minute recovery copy (the window does this; the interval is a parameter for tests).</summary>
    public void StartRecovery(TimeSpan? interval = null)
    {
        _recoveryTimer?.Stop();
        _recoveryTimer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(30) };
        _recoveryTimer.Tick += (_, _) =>
        {
            if (_recoveryWait > 0 && IsDirty) { _recoveryWait--; return; }
            CrashGuard.Guard(() => SaveRecoveryNow(), "the recovery copy");
            _recoveryWait = _recoveryEvery - 1;
        };
        _recoveryTimer.Start();
    }

    public void StopRecovery(bool windowClosing)
    {
        _recoveryTimer?.Stop();
        if (windowClosing && !IsDirty) RecoveryStore.Delete(WorkbookPath);
    }

    /// <summary>Puts unsaved edits of a session that ended badly beside the workbook. Returns the message to show, or null.</summary>
    private string? RecoverEarlierEdits()
    {
        var recovered = RecoveryStore.Recover(WorkbookPath);
        return recovered is null ? null
            : $"Unsaved edits from the last session were recovered as “{Path.GetFileName(recovered)}” in the same folder. This workbook is unchanged; open the copy to take over what you need.";
    }
}
