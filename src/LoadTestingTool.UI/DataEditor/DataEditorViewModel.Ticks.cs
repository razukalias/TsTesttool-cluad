using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Tick boxes in the grid: choose which data rows the next run covers.</summary>
public sealed partial class DataEditorViewModel
{
    // Ticks survive filtering, sorting, undo and switching testcase because they are remembered by testcase + data id.
    private readonly HashSet<string> _tickMemory = [];

    private static string TickKey(DataRowViewModel row) => $"{row.TestcaseIndex}|{row.CurrentDataId}";

    private void RememberTicks()
    {
        foreach (var row in _allRows)
        {
            if (row.IsTicked) _tickMemory.Add(TickKey(row)); else _tickMemory.Remove(TickKey(row));
        }
    }

    private void RestoreTicks()
    {
        foreach (var row in _allRows) row.IsTicked = _tickMemory.Contains(TickKey(row));
        NotifyTickChanged();
    }

    public int TickedCount => _allRows.Count(r => r.IsTicked) + _tickMemory.Count(k => !_allRows.Any(r => TickKey(r) == k));
    public string TickText => TickedCount == 0 ? "▶ Run ticked" : $"▶ Run ticked ({TickedCount})";
    public bool CanRunTicked => CanRun && TickedCount > 0;

    /// <summary>Called by the grid's tick boxes after a click.</summary>
    public void NotifyTickChanged()
    {
        OnPropertyChanged(nameof(TickedCount));
        OnPropertyChanged(nameof(TickText));
        OnPropertyChanged(nameof(CanRunTicked));
    }

    [RelayCommand]
    private void TickAllShown()
    {
        foreach (var row in VisibleRows) row.IsTicked = true;
        NotifyTickChanged();
    }

    [RelayCommand]
    private void ClearTicks()
    {
        foreach (var row in _allRows) row.IsTicked = false;
        _tickMemory.Clear();
        NotifyTickChanged();
    }

    /// <summary>The run that covers exactly the ticked rows, or why it cannot be expressed.</summary>
    public (RunRequest? Request, string Text) TickedRun()
    {
        RememberTicks();
        var keys = _tickMemory.Select(k => { var at = k.IndexOf('|'); return (Index: int.Parse(k[..at]), Id: k[(at + 1)..]); }).ToList();
        if (keys.Count == 0) return (null, "Tick the rows you want to run first.");
        var testcases = keys.Select(k => k.Index).Distinct().Order().ToList();
        var byTestcase = testcases.ToDictionary(t => t, t => (IReadOnlyList<string>)keys.Where(k => k.Index == t).Select(k => k.Id).Distinct().ToList());
        var ids = keys.Select(k => k.Id).Distinct().ToList();
        return (new RunRequest(testcases, ids, RowsByTestcase: byTestcase), $"{keys.Count} ticked {(keys.Count == 1 ? "row" : "rows")}");
    }

    [RelayCommand]
    private Task RunTickedAsync()
    {
        var (request, text) = TickedRun();
        if (request is null) { ShowStatus(text, isError: true); return Task.CompletedTask; }
        return StartRunAsync(request, text);
    }
}
