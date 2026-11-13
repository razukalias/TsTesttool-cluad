using System.Text;
using CommunityToolkit.Mvvm.Input;

namespace LoadTestingTool.UI.DataEditor;

// CSV import and export of the selected testcase's data rows.
public sealed partial class DataEditorViewModel
{
    /// <summary>Raised when the view should ask for a CSV file; true = import, false = export.</summary>
    public event EventHandler<bool>? CsvFileRequested;

    [RelayCommand] private void ImportCsvFile() => CsvFileRequested?.Invoke(this, true);
    [RelayCommand] private void ExportCsvFile() => CsvFileRequested?.Invoke(this, false);

    /// <summary>Every row of the selected testcase (not just the filtered ones): Data ID, then the shown columns by their titles.</summary>
    public string BuildCsv()
    {
        var rows = new List<IEnumerable<string>> { new[] { "Data ID" }.Concat(Columns.Select(CsvHeader)) };
        rows.AddRange(_allRows.Select(r => (IEnumerable<string>)r.Cells.ToList()));
        return ClipboardTable.Format(rows, ',');
    }

    /// <summary>
    /// Imports CSV (comma, semicolon or tab separated) for the selected testcase. Columns are matched by title; a row whose
    /// Data ID exists is updated, any other becomes a new row. One undo step covers the whole import.
    /// </summary>
    public void ImportCsv(string text)
    {
        if (SelectedTestcase is not { } testcase) return;
        text = text.TrimStart('﻿');
        var firstLine = text.Split('\n', 2)[0];
        var delimiter = new[] { ',', ';', '\t' }.MaxBy(d => firstLine.Count(c => c == d));
        var table = ClipboardTable.Parse(text, delimiter).Where(r => r.Any(c => c.Length > 0)).ToArray();
        if (table.Length < 2) { ShowStatus("The file has no data rows.", isError: true); return; }

        var header = table[0].Select(h => h.Trim()).ToArray();
        var idColumn = Array.FindIndex(header, h => h.Equals("Data ID", StringComparison.OrdinalIgnoreCase) || h.Equals("dataid", StringComparison.OrdinalIgnoreCase));
        var map = new int[header.Length]; // grid cell index per CSV column, -1 when ignored
        var unknown = new List<string>();
        for (var i = 0; i < header.Length; i++)
        {
            if (i == idColumn) { map[i] = 0; continue; }
            var column = Columns.FirstOrDefault(c => CsvHeader(c).Equals(header[i], StringComparison.OrdinalIgnoreCase))
                ?? Columns.FirstOrDefault(c => c.Title.Equals(header[i], StringComparison.OrdinalIgnoreCase));
            map[i] = column?.CellIndex ?? -1;
            if (column is null && header[i].Length > 0) unknown.Add(header[i]);
        }
        if (map.All(m => m < 0)) { ShowStatus("None of the file's columns match this testcase's columns. Export a CSV first to see the expected headers.", isError: true); return; }

        ClearRowFilters();
        Record($"import {table.Length - 1} CSV rows");
        int added = 0, updated = 0, changed = 0, rejected = 0;
        for (var r = 1; r < table.Length; r++)
        {
            var line = table[r];
            var id = idColumn >= 0 && idColumn < line.Length ? line[idColumn].Trim() : string.Empty;
            var row = id.Length == 0 ? null : _allRows.FirstOrDefault(x => x.Cells[0].Equals(id, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                AddPastedRow(testcase, id);
                row = _allRows[^1];
                added++;
            }
            else updated++;
            for (var c = 0; c < header.Length && c < line.Length; c++)
            {
                if (map[c] <= 0) continue; // the Data ID was used to find the row
                switch (ApplyCell(row, map[c], line[c])) { case CellResult.Changed: changed++; break; case CellResult.Rejected: rejected++; break; }
            }
        }
        if (added + changed == 0) { DiscardLastRecord(); ShowStatus("Nothing changed: the file matches the current data.", isError: false); return; }
        MarkChanged();
        ApplyFilter();
        OnPropertyChanged(nameof(RowCountText));
        var notes = new List<string>();
        if (unknown.Count > 0) notes.Add("ignored columns: " + string.Join(", ", unknown.Take(5)) + (unknown.Count > 5 ? "…" : ""));
        if (rejected > 0) notes.Add($"{rejected} cells refused");
        ShowStatus($"Imported {added + updated} rows ({added} new, {updated} updated, {changed} cells changed)" + (notes.Count == 0 ? "." : "; " + string.Join("; ", notes) + "."), isError: false);
    }

    public void ReportStatus(string message, bool isError = false) => ShowStatus(message, isError);

    private const string ExpectedSuffix = " [expected]";

    /// <summary>Request columns keep their title; expected-value columns get a suffix, since a request and a response column can share a title.</summary>
    private static string CsvHeader(GridColumn column) => column.Group == ColumnGroup.Response ? column.Title + ExpectedSuffix : column.Title;

    public static string CsvFileName(string instance, string testcase) => $"{instance}-{testcase}.csv".Replace(' ', '_');
    public static Encoding CsvEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
}
