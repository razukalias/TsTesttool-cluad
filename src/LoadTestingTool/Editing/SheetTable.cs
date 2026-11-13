using System.Globalization;
using ClosedXML.Excel;

namespace LoadTestingTool.Editing;

/// <summary>
/// An editable, in-memory copy of one worksheet: a header row plus data rows of text cells.
/// Columns are addressed by header (case-insensitive, trimmed) or by position, so a sheet with
/// duplicate headers still loads and saves without losing data.
/// </summary>
/// <remarks>
/// Saving never moves an existing row. Removed rows are cleared and new rows are appended below
/// the last used row, because workbook cells can refer to other rows by absolute number
/// (for example &lt;From_request_title_4&gt;).
/// </remarks>
public sealed class SheetTable
{
    private static readonly HashSet<string> NumericHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "testcaseindex", "threads", "iterations", "wait", "timeout", "manualtimeoutminutes", "threadintervalms", "iterationintervalms"
    };

    private readonly List<Column> _columns = [];
    private readonly List<TableRow> _rows = [];
    private readonly List<int> _removedSourceRows = [];

    private SheetTable(string sheetName, int headerRowNumber)
    {
        SheetName = sheetName;
        HeaderRowNumber = headerRowNumber;
    }

    public string SheetName { get; }
    public int HeaderRowNumber { get; }
    public IReadOnlyList<TableRow> Rows => _rows;
    public int ColumnCount => _columns.Count;
    public IReadOnlyList<string> Headers => _columns.Select(c => c.Header).ToList();

    public bool IsDirty => _removedSourceRows.Count > 0 || _columns.Any(c => c.IsModified) || _rows.Any(r => r.IsModified);

    /// <summary>Index of the first column with this header, or -1.</summary>
    public int FindColumn(string header)
    {
        var wanted = header.Trim();
        for (var i = 0; i < _columns.Count; i++)
            if (string.Equals(_columns[i].Header, wanted, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Index of the column with this header, adding it at the end when it does not exist.</summary>
    public int EnsureColumn(string header)
    {
        var wanted = header.Trim();
        if (wanted.Length == 0) throw new ArgumentException("A column header cannot be empty.", nameof(header));
        var existing = FindColumn(wanted);
        if (existing >= 0) return existing;
        _columns.Add(new Column(wanted, null, null));
        foreach (var row in _rows) row.Resize(_columns.Count);
        return _columns.Count - 1;
    }

    /// <summary>Appends an empty row. It is written below the last used row when the document is saved.</summary>
    public TableRow AddRow()
    {
        var row = new TableRow(this, null, new string[_columns.Count], isNew: true);
        _rows.Add(row);
        return row;
    }

    /// <summary>Removes a row. On save its sheet row is cleared, not deleted, so rows below keep their numbers.</summary>
    public bool RemoveRow(TableRow row)
    {
        if (!_rows.Remove(row)) return false;
        if (row.SourceRow is int sourceRow) _removedSourceRows.Add(sourceRow);
        return true;
    }

    /// <summary>Captures the current edits so <see cref="Restore"/> can return to this exact state (used for undo).</summary>
    public SheetSnapshot Snapshot()
        => new(_columns.Select(c => ((object)c, c.Header)).ToArray(), _rows.Select(r => (r, r.CaptureValues())).ToArray(), _removedSourceRows.ToArray());

    /// <summary>Returns to a state captured by <see cref="Snapshot"/>. Only valid until the table is next saved.</summary>
    public void Restore(SheetSnapshot snapshot)
    {
        _columns.Clear();
        foreach (var (captured, header) in snapshot.Columns)
        {
            var column = (Column)captured;
            column.Header = header;
            _columns.Add(column);
        }
        _rows.Clear();
        foreach (var (row, values) in snapshot.Rows)
        {
            row.RestoreValues(values);
            _rows.Add(row);
        }
        _removedSourceRows.Clear();
        _removedSourceRows.AddRange(snapshot.RemovedSourceRows);
    }

    internal static SheetTable Load(IXLWorksheet sheet)
    {
        var header = sheet.FirstRowUsed() ?? throw new InvalidDataException($"Worksheet '{sheet.Name}' has no header row.");
        var table = new SheetTable(sheet.Name, header.RowNumber());
        foreach (var cell in header.CellsUsed())
        {
            var text = cell.GetString().Trim();
            if (text.Length == 0) continue;
            table._columns.Add(new Column(text, text, cell.Address.ColumnNumber));
        }
        foreach (var sheetRow in sheet.RowsUsed().Where(r => r.RowNumber() > table.HeaderRowNumber))
        {
            var values = new string[table._columns.Count];
            for (var i = 0; i < values.Length; i++) values[i] = sheetRow.Cell(table._columns[i].SourceColumn!.Value).GetString();
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            table._rows.Add(new TableRow(table, sheetRow.RowNumber(), values, isNew: false));
        }
        return table;
    }

    internal void ApplyTo(IXLWorksheet sheet)
    {
        var nextRow = Math.Max(sheet.LastRowUsed()?.RowNumber() ?? HeaderRowNumber, HeaderRowNumber) + 1;
        var nextColumn = (sheet.LastColumnUsed()?.ColumnNumber() ?? 0) + 1;

        foreach (var column in _columns)
        {
            column.PendingSourceColumn = column.SourceColumn ?? nextColumn++;
            if (column.IsModified) sheet.Cell(HeaderRowNumber, column.PendingSourceColumn.Value).Value = column.Header;
        }

        foreach (var removed in _removedSourceRows) sheet.Row(removed).Clear(XLClearOptions.Contents);

        foreach (var row in _rows)
        {
            row.PendingSourceRow = null;
            int target;
            if (row.SourceRow is int existing)
            {
                if (!row.IsModified) continue;
                target = existing;
            }
            else
            {
                if (row.IsBlank) continue;
                target = nextRow++;
            }
            row.PendingSourceRow = target;
            for (var i = 0; i < _columns.Count; i++)
            {
                var value = row.GetAt(i);
                var original = row.OriginalAt(i);
                if (original is null ? value.Length == 0 : string.Equals(original, value, StringComparison.Ordinal)) continue;
                WriteCell(sheet.Cell(target, _columns[i].PendingSourceColumn!.Value), value, _columns[i].Header);
            }
        }
    }

    internal void AcceptChanges()
    {
        foreach (var column in _columns) column.AcceptChanges();
        foreach (var row in _rows) row.AcceptChanges();
        _removedSourceRows.Clear();
    }

    private static void WriteCell(IXLCell cell, string value, string header)
    {
        if (value.Length == 0)
        {
            cell.Clear(XLClearOptions.Contents);
            return;
        }
        // Keep a cell's existing type where the new text still fits it, so numbers stay numbers
        // (Excel shows a warning triangle on numbers stored as text).
        if (cell.DataType == XLDataType.Number && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            cell.Value = number;
            return;
        }
        if (cell.DataType == XLDataType.Boolean && bool.TryParse(value, out var flag))
        {
            cell.Value = flag;
            return;
        }
        if (NumericHeaders.Contains(header) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
        {
            cell.Value = whole;
            return;
        }
        cell.Value = value;
    }

    private sealed class Column
    {
        public Column(string header, string? originalHeader, int? sourceColumn)
        {
            Header = header;
            OriginalHeader = originalHeader;
            SourceColumn = sourceColumn;
        }

        public string Header { get; set; }
        public string? OriginalHeader { get; private set; }
        public int? SourceColumn { get; private set; }
        public int? PendingSourceColumn { get; set; }
        public bool IsModified => OriginalHeader is null || !string.Equals(Header, OriginalHeader, StringComparison.Ordinal);

        public void AcceptChanges()
        {
            SourceColumn = PendingSourceColumn ?? SourceColumn;
            OriginalHeader = Header;
            PendingSourceColumn = null;
        }
    }
}

/// <summary>An opaque capture of a <see cref="SheetTable"/>'s edits, taken with <see cref="SheetTable.Snapshot"/>.</summary>
public sealed class SheetSnapshot
{
    internal SheetSnapshot((object Column, string Header)[] columns, (TableRow Row, string[] Values)[] rows, int[] removedSourceRows)
    {
        Columns = columns;
        Rows = rows;
        RemovedSourceRows = removedSourceRows;
    }

    internal (object Column, string Header)[] Columns { get; }
    internal (TableRow Row, string[] Values)[] Rows { get; }
    internal int[] RemovedSourceRows { get; }
}

/// <summary>One data row of a <see cref="SheetTable"/>. Missing cells read as an empty string.</summary>
public sealed class TableRow
{
    private readonly SheetTable _table;
    private string[] _values;
    private string[]? _original;

    internal TableRow(SheetTable table, int? sourceRow, string[] values, bool isNew)
    {
        _table = table;
        _values = values;
        for (var i = 0; i < _values.Length; i++) _values[i] ??= string.Empty;
        SourceRow = sourceRow;
        _original = isNew ? null : (string[])_values.Clone();
    }

    /// <summary>Row number in the worksheet, or null for a row that has not been saved yet.</summary>
    public int? SourceRow { get; private set; }

    internal int? PendingSourceRow { get; set; }

    /// <summary>Gets a cell by header; setting it adds the column when the sheet does not have it yet.</summary>
    public string this[string header]
    {
        get => Get(header);
        set => Set(header, value);
    }

    public string Get(string header)
    {
        var index = _table.FindColumn(header);
        return index < 0 ? string.Empty : _values[index];
    }

    public void Set(string header, string? value) => SetAt(_table.EnsureColumn(header), value);

    public string GetAt(int column)
    {
        if (column < 0 || column >= _values.Length) throw new ArgumentOutOfRangeException(nameof(column));
        return _values[column];
    }

    public void SetAt(int column, string? value)
    {
        if (column < 0 || column >= _values.Length) throw new ArgumentOutOfRangeException(nameof(column));
        _values[column] = value ?? string.Empty;
    }

    public bool IsBlank => _values.All(string.IsNullOrWhiteSpace);

    internal bool IsModified
    {
        get
        {
            if (_original is null) return !IsBlank;
            for (var i = 0; i < _values.Length; i++)
                if (!string.Equals(_values[i], OriginalAt(i), StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>Value the cell had when loaded or last saved; null for a row that was never saved.</summary>
    internal string? OriginalAt(int column)
    {
        if (_original is null) return null;
        return column < _original.Length ? _original[column] : string.Empty;
    }

    internal string[] CaptureValues() => (string[])_values.Clone();

    internal void RestoreValues(string[] values) => _values = (string[])values.Clone();

    internal void Resize(int count)
    {
        var old = _values.Length;
        if (count <= old) return;
        Array.Resize(ref _values, count);
        for (var i = old; i < count; i++) _values[i] = string.Empty;
    }

    internal void AcceptChanges()
    {
        if (PendingSourceRow is int row)
        {
            SourceRow = row;
            _original = (string[])_values.Clone();
        }
        PendingSourceRow = null;
    }
}
