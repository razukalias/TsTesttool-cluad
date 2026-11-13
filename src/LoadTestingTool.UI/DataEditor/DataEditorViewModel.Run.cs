using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Asks the host to run part of an instance. Returns null when the run was started, otherwise why it was not.</summary>
public delegate Task<string?> RunHandler(int testcaseIndex, IReadOnlyList<string> dataIds);

// Run from the editor: the whole selected testcase, or just the selected data rows.
public sealed partial class DataEditorViewModel
{
    /// <summary>Set by whoever hosts the editor; without it the run buttons are unavailable.</summary>
    public RunHandler? RunHandler { get; set; }

    public bool CanRun => RunHandler is not null && SelectedTestcase is not null;

    [ObservableProperty] private bool _isStartingRun;

    [RelayCommand]
    private Task RunTestcaseAsync() => StartRunAsync(all: true);

    [RelayCommand]
    private Task RunRowAsync() => StartRunAsync(all: false);

    private async Task StartRunAsync(bool all)
    {
        if (RunHandler is not { } handler || SelectedTestcase is not { } testcase) { ShowStatus("Running is only available when the editor is opened from the dashboard.", isError: true); return; }
        if (IsDirty) { ShowStatus("Save your changes first — a run reads the saved workbook.", isError: true); return; }
        var ids = all ? [] : VisibleRows.Where(r => ReferenceEquals(r, SelectedRow)).Select(r => r.Cells[0]).ToList();
        if (!all && ids.Count == 0) { ShowStatus("Select a data row to run.", isError: true); return; }
        IsStartingRun = true;
        try
        {
            var problem = await handler(testcase.Index, ids);
            ShowStatus(problem ?? (all
                ? $"Started testcase {testcase.Index.ToString(CultureInfo.InvariantCulture)} · {testcase.Name}. Results appear in the Result column as it runs."
                : $"Started {ids[0]} in testcase {testcase.Name}. Results appear in the Result column as it runs."), isError: problem is not null);
        }
        finally { IsStartingRun = false; }
    }
}
