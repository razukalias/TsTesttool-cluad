using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>What the latest run saw behind a cell that did not fail: the response value (green), or that the field was not in the response (yellow).</summary>
public sealed record CellPass(bool Absent, string Value, string Environments, string Detail);

// Green and yellow cell marks, the read-only columns for response fields the workbook has no column for, and the two column filters that go with them.
public sealed partial class DataEditorViewModel
{
    public const string MissingText = "not in the response";

    /// <summary>The value of a cell that passed in the latest run, or the note that its field was not in the response; null for a cell that failed, was not asserted or has no run.</summary>
    public CellPass? PassAt(DataRowViewModel row, int cellIndex)
    {
        if (row.Outcome is not { } outcome || cellIndex < 1 || cellIndex >= row.Cells.Count) return null;
        EnsureFailureMaps();
        if (!_cellKeys.TryGetValue(cellIndex, out var key) || !outcome.PassMarks.TryGetValue(key, out var marks) || marks.Count == 0) return null;
        var absent = marks.Where(m => m.Absent).ToList();
        var present = marks.Where(m => !m.Absent).ToList();
        string Env(IEnumerable<PassMark> list) => string.Join(", ", list.Select(m => m.Environment).Where(e => e.Length > 0).Distinct());
        var detail = string.Join("\n", marks.Select(m => (m.Environment.Length > 0 ? m.Environment + ": " : string.Empty) + (m.Absent ? MissingText : m.Value.Length == 0 ? "(empty)" : m.Value)));
        // yellow wins over green: a field missing in one environment is what to look at
        if (absent.Count > 0) return new CellPass(true, MissingText, Env(absent), detail);
        var values = present.Select(m => m.Value).Distinct().ToList();
        return new CellPass(false, values.Count == 1 ? values[0] : string.Join(" / ", values), Env(present), detail);
    }

    /// <summary>"Passed columns": the table shows only the expected-value columns that have a passed cell (with a value) in the rows shown.</summary>
    [ObservableProperty] private bool _showPassedColumns;

    /// <summary>"Missing columns": the table shows only the expected-value columns whose field was not in the response, in the rows shown.</summary>
    [ObservableProperty] private bool _showMissingColumns;

    public HashSet<int> PassedColumnCells { get; private set; } = [];
    public HashSet<int> MissingColumnCells { get; private set; } = [];

    /// <summary>Any of the options that hide the columns they do not name.</summary>
    public bool ColumnFilterOn => ShowFailedColumns || ShowBugColumns || ShowPassedColumns || ShowMissingColumns;

    partial void OnShowPassedColumnsChanged(bool value) { OnPropertyChanged(nameof(ShowOptionsTitle)); RefreshFailedColumns(); }
    partial void OnShowMissingColumnsChanged(bool value) { OnPropertyChanged(nameof(ShowOptionsTitle)); RefreshFailedColumns(); }

    private void RefreshPassColumns()
    {
        var passed = new HashSet<int>(); var missing = new HashSet<int>();
        if (ShowPassedColumns || ShowMissingColumns)
        {
            EnsureFailureMaps();
            foreach (var row in VisibleRows)
            {
                if (row.Outcome is not { } outcome) continue;
                foreach (var (key, marks) in outcome.PassMarks)
                {
                    if (!_failureCells.TryGetValue(key, out var cell)) continue;
                    if (marks.Any(m => m.Absent)) missing.Add(cell);
                    if (marks.Any(m => !m.Absent)) passed.Add(cell);
                }
            }
        }
        PassedColumnCells = ShowPassedColumns ? passed : [];
        MissingColumnCells = ShowMissingColumns ? missing : [];
    }

    /// <summary>"Step.path" of the response fields that came back with a value but have no column in the workbook, for the rows shown: the table adds a read-only column for each, after the environment column.</summary>
    public IReadOnlyList<string> ExtraColumnKeys { get; private set; } = [];

    /// <summary>Raised when the set of extra columns changed, so the grid can add or remove them.</summary>
    public event EventHandler? ExtraColumnsChanged;

    public void RefreshExtraColumns()
    {
        EnsureFailureMaps();
        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var step = FilteringByStep ? StepFilter : null;
        foreach (var row in VisibleRows)
            if (row.Outcome is { } outcome)
                foreach (var key in outcome.Extras.Keys)
                    if (!_failureCells.ContainsKey(key) && (step is null || key.StartsWith(step + ".", StringComparison.OrdinalIgnoreCase))) keys.Add(key);
        var list = keys.Take(60).ToList();   // a response with hundreds of unconfigured fields would drown the table
        if (list.SequenceEqual(ExtraColumnKeys, StringComparer.OrdinalIgnoreCase)) return;
        ExtraColumnKeys = list;
        ExtraColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What the latest run got for an extra column in this row (the values of every environment when they differ); empty when the row did not return that field.</summary>
    public string ExtraValue(DataRowViewModel row, string key)
    {
        if (row.Outcome is not { } outcome || !outcome.Extras.TryGetValue(key, out var marks) || marks.Count == 0) return string.Empty;
        var values = marks.Select(m => m.Value).Distinct().ToList();
        return values.Count == 1 ? values[0] : string.Join(" / ", values);
    }

    public string ExtraDetail(DataRowViewModel row, string key)
        => row.Outcome is { } outcome && outcome.Extras.TryGetValue(key, out var marks)
            ? string.Join("\n", marks.Select(m => (m.Environment.Length > 0 ? m.Environment + ": " : string.Empty) + m.Value)) : string.Empty;
}
