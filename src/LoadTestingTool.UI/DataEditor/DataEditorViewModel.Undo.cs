using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

// Undo and redo: before each change the whole edited workbook is captured (cheap: text cells only), together
// with where the user was, so undoing also takes them back to the place that changed.
public sealed partial class DataEditorViewModel
{
    private const int MaxUndo = 200;

    private sealed record UndoState(DocumentSnapshot Snapshot, string Description, int? TestcaseIndex, bool ShowSteps, TableRow? SelectedRequest, TableRow? SelectedStepRow);

    private readonly List<UndoState> _undo = [];
    private readonly List<UndoState> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoTip => CanUndo ? $"Undo: {_undo[^1].Description} (Ctrl+Z)" : "Nothing to undo";
    public string RedoTip => CanRedo ? $"Redo: {_redo[^1].Description} (Ctrl+Y)" : "Nothing to redo";

    /// <summary>Call before changing the workbook, so the change can be undone.</summary>
    private void Record(string description)
    {
        _undo.Add(Capture(description));
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        NotifyUndoChanged();
    }

    /// <summary>The change that was just recorded turned out not to happen (it was rejected).</summary>
    private void DiscardLastRecord()
    {
        if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
        NotifyUndoChanged();
    }

    /// <summary>Saving and reloading make older captures unsafe to restore (saved rows now have sheet rows), so history starts over.</summary>
    private void ClearUndoHistory()
    {
        _undo.Clear();
        _redo.Clear();
        NotifyUndoChanged();
    }

    private UndoState Capture(string description)
        => new(_document.Snapshot(), description, SelectedTestcase?.Index, ShowSteps, SelectedRow?.Request, SelectedStep?.Row);

    private void NotifyUndoChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoTip));
        OnPropertyChanged(nameof(RedoTip));
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0) return;
        var state = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(state with { Snapshot = _document.Snapshot() });
        Apply(state);
        ShowStatus($"Undid: {state.Description}.", isError: false);
        NotifyUndoChanged();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redo.Count == 0) return;
        var state = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(state with { Snapshot = _document.Snapshot() });
        Apply(state);
        ShowStatus($"Redid: {state.Description}.", isError: false);
        NotifyUndoChanged();
    }

    private void Apply(UndoState state)
    {
        _document.Restore(state.Snapshot);
        _dataStale = false;
        SyncTestcases();
        var target = Testcases.FirstOrDefault(t => t.Index == state.TestcaseIndex) ?? Testcases.FirstOrDefault();
        _loading = true;
        try { SelectedTestcase = target; }
        finally { _loading = false; }
        ShowSteps = state.ShowSteps;
        AddColumnVisible = false;
        BuildRows();
        if (state.SelectedRequest is not null && _allRows.FirstOrDefault(r => r.Request == state.SelectedRequest) is { } row) SelectedRow = row;
        if (state.SelectedStepRow is not null && StepRows.FirstOrDefault(r => r.Row == state.SelectedStepRow) is { } step) SelectedStep = step;
        IsDirty = _document.IsDirty;
        Revalidate();
    }

    /// <summary>Brings the testcase list in line with the workbook after a restore, keeping the existing items (and the tree's selection) where possible.</summary>
    private void SyncTestcases()
    {
        var infos = _document.GetTestcases();
        for (var i = Testcases.Count - 1; i >= 0; i--)
            if (infos.All(info => info.Index != Testcases[i].Index)) Testcases.RemoveAt(i);
        for (var position = 0; position < infos.Count; position++)
        {
            var info = infos[position];
            var count = _document.GetDataIds(info.Index).Count;
            var existing = Testcases.FirstOrDefault(t => t.Index == info.Index);
            if (existing is null)
            {
                Testcases.Insert(Math.Min(position, Testcases.Count), new TestcaseItem(info, count));
                continue;
            }
            existing.UpdateSteps(info.Steps);
            existing.RowCount = count;
        }
    }
}
