using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Tick boxes in the grid: choose which data rows the tree's "Run ticked" covers.</summary>
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

    /// <summary>The ticked data rows that still exist, as data IDs per testcase index.</summary>
    internal IReadOnlyDictionary<int, IReadOnlyList<string>> TickedRowsByTestcase()
    {
        RememberTicks();
        if (_tickMemory.Count == 0) return new Dictionary<int, IReadOnlyList<string>>();
        var existing = _document.Request.Rows.Where(r => !r.IsBlank).Select(r => $"{WorkbookDocument.EffectiveIndex(r)}|{r.Get("dataid")}").ToHashSet();
        return _tickMemory.Where(existing.Contains)
            .Select(k => { var at = k.IndexOf('|'); return (Index: int.Parse(k[..at]), Id: k[(at + 1)..]); })
            .GroupBy(k => k.Index).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(k => k.Id).Distinct().ToList());
    }

    /// <summary>How many data rows are ticked (across testcases).</summary>
    public int TickedRowCount => TickedRowsByTestcase().Sum(p => p.Value.Count);

    /// <summary>For the header box of the Run column: all shown rows ticked, none, or some (null).</summary>
    public bool? ShownTickState
    {
        get
        {
            var ticked = VisibleRows.Count(r => r.IsTicked);
            return ticked == 0 ? false : ticked == VisibleRows.Count ? true : null;
        }
    }

    /// <summary>Called by the grid's tick boxes after a click, and whenever the shown rows change.</summary>
    public void NotifyTickChanged()
    {
        var byTestcase = TickedRowsByTestcase();
        foreach (var testcase in Testcases) testcase.TickedRows = byTestcase.TryGetValue(testcase.Index, out var ids) ? ids.Count : 0;
        OnPropertyChanged(nameof(TickedRowCount));
        OnPropertyChanged(nameof(ShownTickState));
        TickedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Ticks (or clears) every row currently shown, so a filter narrows what "all" means.</summary>
    public void SetShownTicks(bool ticked)
    {
        foreach (var row in VisibleRows) row.IsTicked = ticked;
        NotifyTickChanged();
    }

    /// <summary>Clears every tick in the grid, shown or not.</summary>
    public void ClearRowTicks()
    {
        foreach (var row in _allRows) row.IsTicked = false;
        _tickMemory.Clear();
        NotifyTickChanged();
    }

    /// <summary>The run behind a ▶ in the tree when rows are ticked: only the ticked rows that belong to the item. For a testcase or a step pass the
    /// testcase (and the step); for the whole instance pass null. Null when none of the item's rows are ticked, so ▶ then runs it all as usual.</summary>
    internal (RunRequest Request, string Label)? BuildTickedRowsRequest(TestcaseItem? testcase, IReadOnlyList<string>? steps, bool? parallel)
    {
        var rows = TickedRowsByTestcase()
            .Where(p => Testcases.Any(t => t.Index == p.Key) && (testcase is null || p.Key == testcase.Index))
            .ToDictionary(p => p.Key, p => p.Value);
        if (rows.Count == 0) return null;
        var chosen = rows.Keys.OrderBy(i => i).Select(i => (Testcases.First(t => t.Index == i), i == testcase?.Index ? steps : null)).ToList();
        var request = BuildRequest(chosen, parallel) with { DataIds = rows.Values.SelectMany(v => v).Distinct().ToList(), RowsByTestcase = rows };
        var count = rows.Values.Sum(v => v.Count);
        return (request, $"{count} ticked {(count == 1 ? "row" : "rows")}");
    }
}
