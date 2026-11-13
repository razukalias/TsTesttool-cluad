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
/// <summary>Look of one cell that the editor can change: a fill colour (#RRGGBB) and whether long text wraps.</summary>
public readonly record struct CellFormat(string? Fill, bool Wrap)
{
    public static readonly CellFormat None = new(null, false);
    public bool IsDefault => Fill is null && !Wrap;
}

public sealed class SheetTable
{
    private static readonly HashSet<string> NumericHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "testcaseindex", "threads", "iterations", "wait", "timeout", "manualtimeoutminutes", "threadintervalms", "iterationintervalms"
    };

    private readonly List<Column> _columns = [];
    private readonly List<TableRow> _rows = [];
    private readonly List<int> _removedSourceRows = [];
    private readonly List<int> _removedSourceColumns = [];
    private Dictionary<(TableRow Row, Column Column), CellFormat> _formats = [];
    private Dictionary<(TableRow Row, Column Column), CellFormat> _formatBaseline = [];

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

    public bool IsDirty => _removedSourceRows.Count > 0 || _removedSourceColumns.Count > 0 || _columns.Any(c => c.IsModified) || _rows.Any(r => r.IsModified) || ChangedFormats().Count > 0;

    // ---- Cell formats (highlight and wrap) -------------------------------------------------

    public CellFormat GetFormat(TableRow row, int column)
        => _formats.TryGetValue((row, _columns[column]), out var format) ? format : CellFormat.None;

    public void SetFormat(TableRow row, int column, CellFormat format)
    {
        var key = (row, _columns[column]);
        if (format.IsDefault) _formats.Remove(key);
        else _formats[key] = format;
    }

    /// <summary>Formats that differ from what the file holds, for cells that still exist.</summary>
    private List<((TableRow Row, Column Column) Key, CellFormat Now)> ChangedFormats()
    {
        var result = new List<((TableRow, Column), CellFormat)>();
        var live = _rows.Count == 0 ? null : new HashSet<TableRow>(_rows);
        var columns = new HashSet<Column>(_columns);
        foreach (var key in _formats.Keys.Concat(_formatBaseline.Keys).Distinct())
        {
            if (live is null || !live.Contains(key.Row) || !columns.Contains(key.Column)) continue;
            var now = _formats.GetValueOrDefault(key, CellFormat.None);
            var before = _formatBaseline.GetValueOrDefault(key, CellFormat.None);
            if (now != before) result.Add((key, now));
        }
        return result;
    }

    /// <summary>Writes changed fills and wrapping at the cells' final positions. Runs after rows and columns were placed.</summary>
    internal void ApplyFormats(IXLWorksheet sheet)
    {
        var inserts = PendingInserts();
        foreach (var (key, now) in ChangedFormats())
        {
            var row = key.Row.PendingSourceRow ?? (key.Row.SourceRow is int source ? source + inserts.Count(i => i.Before <= source) : (int?)null);
            var column = key.Column.PendingSourceColumn;
            if (row is null || column is null) continue;
            var cell = sheet.Cell(row.Value, column.Value);
            if (now.Fill is { } fill) cell.Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
            else cell.Style.Fill.PatternType = XLFillPatternValues.None;
            cell.Style.Alignment.WrapText = now.Wrap;
            sheet.Row(row.Value).ClearHeight(); // let Excel size the row to its wrapped text
        }
    }

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

    /// <summary>
    /// Adds an empty column immediately before the column at <paramref name="index"/>. On save a real sheet column is inserted there
    /// (columns to its right move right; formulas follow). References by header name are unaffected.
    /// </summary>
    public int InsertColumnBefore(int index, string header)
    {
        var wanted = header.Trim();
        if (wanted.Length == 0) throw new ArgumentException("A column header cannot be empty.", nameof(header));
        if (FindColumn(wanted) >= 0) throw new InvalidOperationException($"{SheetName} already has a column '{wanted}'.");
        if (index < 0 || index > _columns.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var neighbor = index < _columns.Count ? _columns[index] : null;
        _columns.Insert(index, new Column(wanted, null, null) { InsertBeforeColumn = neighbor?.SourceColumn ?? neighbor?.InsertBeforeColumn });
        foreach (var row in _rows) row.InsertValueAt(index);
        return index;
    }

    /// <summary>Removes a column. On save its sheet column is emptied (header included), not deleted, so columns to its right keep their place.</summary>
    public void RemoveColumn(int index)
    {
        if (index < 0 || index >= _columns.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (_columns[index].SourceColumn is int source) _removedSourceColumns.Add(source);
        _columns.RemoveAt(index);
        foreach (var row in _rows) row.RemoveValueAt(index);
    }

    /// <summary>Changes a column's header. References to the old header (<c>&lt;From_sheet_oldheader_N&gt;</c>) are renamed on save.</summary>
    public void RenameColumn(int index, string header)
    {
        var wanted = header.Trim();
        if (wanted.Length == 0) throw new ArgumentException("A column header cannot be empty.", nameof(header));
        var clash = FindColumn(wanted);
        if (clash >= 0 && clash != index) throw new InvalidOperationException($"{SheetName} already has a column '{wanted}'.");
        _columns[index].Header = wanted;
    }

    /// <summary>Headers that were changed since the table was loaded or last saved, old to new.</summary>
    internal Dictionary<string, string> RenamedHeaders()
        => _columns.Where(c => c.OriginalHeader is not null && !string.Equals(c.Header, c.OriginalHeader, StringComparison.Ordinal))
            .ToDictionary(c => c.OriginalHeader!, c => c.Header, StringComparer.OrdinalIgnoreCase);

    internal int PendingColumnInserts => _columns.Count(c => c.SourceColumn is null && c.InsertBeforeColumn is not null);
    internal bool HasRemovedColumns => _removedSourceColumns.Count > 0;

    /// <summary>Appends an empty row. It is written below the last used row when the document is saved.</summary>
    public TableRow AddRow()
    {
        var row = new TableRow(this, null, new string[_columns.Count], isNew: true);
        _rows.Add(row);
        return row;
    }

    /// <summary>
    /// Adds an empty row immediately above <paramref name="neighbor"/>. On save the sheet row is really inserted there
    /// (rows below move down and references to them are renumbered); above a row that is itself new, it just joins the appended rows in order.
    /// </summary>
    public TableRow InsertRowBefore(TableRow neighbor)
    {
        var index = _rows.IndexOf(neighbor);
        if (index < 0) throw new ArgumentException("That row is not in this sheet.", nameof(neighbor));
        var row = new TableRow(this, null, new string[_columns.Count], isNew: true) { InsertBeforeRow = neighbor.SourceRow ?? neighbor.InsertBeforeRow };
        _rows.Insert(index, row);
        return row;
    }

    /// <summary>Moves a row (new or inserted) to sit immediately above another row; positions are decided at save time.</summary>
    internal void MoveRowBefore(TableRow row, TableRow neighbor)
    {
        _rows.Remove(row);
        var index = _rows.IndexOf(neighbor);
        _rows.Insert(index, row);
        row.InsertBeforeRow = neighbor.SourceRow ?? neighbor.InsertBeforeRow;
    }

    internal List<(int Before, TableRow Row)> PendingInserts()
        => _rows.Where(r => r.SourceRow is null && r.InsertBeforeRow is not null && !r.IsBlank).Select(r => (r.InsertBeforeRow!.Value, r)).OrderBy(x => x.Item1).ToList();

    /// <summary>Writes rows that were inserted (see <see cref="PendingInserts"/>) into the freshly opened gaps of the sheet.</summary>
    internal void WriteInsertedRows(IXLWorksheet sheet, List<(int Before, TableRow Row)> inserts, Func<string, string> renumber)
    {
        for (var k = 0; k < inserts.Count; k++)
        {
            var (before, row) = inserts[k];
            var target = before + k; // the k earlier inserts at or above this position pushed it down
            row.PendingSourceRow = target;
            for (var i = 0; i < _columns.Count; i++)
            {
                var value = row.GetAt(i);
                if (value.Length > 0) WriteCell(sheet.Cell(target, _columns[i].PendingSourceColumn!.Value), renumber(value), _columns[i].Header);
            }
        }
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
        => new(_columns.Select(c => ((object)c, c.Header)).ToArray(), _rows.Select(r => { var (values, original) = r.Capture(); return (r, values, original); }).ToArray(), _removedSourceRows.ToArray(), _removedSourceColumns.ToArray(), _formats.Select(kv => (kv.Key.Row, (object)kv.Key.Column, kv.Value)).ToArray());

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
        foreach (var (row, values, original) in snapshot.Rows)
        {
            row.Restore(values, original);
            _rows.Add(row);
        }
        _removedSourceRows.Clear();
        _removedSourceRows.AddRange(snapshot.RemovedSourceRows);
        _removedSourceColumns.Clear();
        _removedSourceColumns.AddRange(snapshot.RemovedSourceColumns);
        _formats = snapshot.Formats.ToDictionary(f => (f.Row, (Column)f.Column), f => f.Format);
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
            var tableRow = new TableRow(table, sheetRow.RowNumber(), values, isNew: false);
            table._rows.Add(tableRow);
            for (var i = 0; i < table._columns.Count; i++)
            {
                var style = sheetRow.Cell(table._columns[i].SourceColumn!.Value).Style;
                var fill = style.Fill.PatternType == XLFillPatternValues.Solid && style.Fill.BackgroundColor.ColorType == XLColorType.Color
                    ? "#" + style.Fill.BackgroundColor.Color.R.ToString("X2") + style.Fill.BackgroundColor.Color.G.ToString("X2") + style.Fill.BackgroundColor.Color.B.ToString("X2")
                    : null;
                var format = new CellFormat(fill, style.Alignment.WrapText);
                if (!format.IsDefault) table._formats[(tableRow, table._columns[i])] = format;
            }
        }
        table._formatBaseline = new(table._formats);
        return table;
    }

    internal void ApplyTo(IXLWorksheet sheet)
    {
        foreach (var column in _columns) column.PendingSourceColumn = null;
        var nextRow = Math.Max(sheet.LastRowUsed()?.RowNumber() ?? HeaderRowNumber, HeaderRowNumber) + 1;

        // Columns first: empty the removed ones, then open gaps for the inserted ones (right to left), and work out where every column ends up.
        foreach (var removed in _removedSourceColumns) sheet.Column(removed).Clear(XLClearOptions.Contents);
        var inserted = _columns.Where(c => c.SourceColumn is null && c.InsertBeforeColumn is not null).OrderBy(c => c.InsertBeforeColumn).ToList();
        foreach (var position in inserted.Select(c => c.InsertBeforeColumn!.Value).OrderByDescending(x => x)) sheet.Column(position).InsertColumnsBefore(1);
        for (var k = 0; k < inserted.Count; k++) inserted[k].PendingSourceColumn = inserted[k].InsertBeforeColumn!.Value + k;

        var nextColumn = (sheet.LastColumnUsed()?.ColumnNumber() ?? 0) + 1;
        foreach (var column in _columns)
        {
            if (column.SourceColumn is int source) column.PendingSourceColumn = source + inserted.Count(c => c.InsertBeforeColumn <= source);
            else column.PendingSourceColumn ??= nextColumn++;
            if (column.IsModified) sheet.Cell(HeaderRowNumber, column.PendingSourceColumn.Value).Value = column.Header;
        }

        foreach (var removed in _removedSourceRows) sheet.Row(removed).Clear(XLClearOptions.Contents);

        foreach (var row in _rows)
        {
            row.PendingSourceRow = null;
            if (row.SourceRow is null && row.InsertBeforeRow is not null && !row.IsBlank) continue; // written by RowInsertion
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
        _removedSourceColumns.Clear();
        _formatBaseline = new(_formats);
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

        /// <summary>For a column inserted before an existing sheet column: that column's number when the table was loaded or last saved.</summary>
        public int? InsertBeforeColumn { get; init; }
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
    internal SheetSnapshot((object Column, string Header)[] columns, (TableRow Row, string[] Values, string[]? Original)[] rows, int[] removedSourceRows, int[] removedSourceColumns, (TableRow Row, object Column, CellFormat Format)[] formats)
    {
        Formats = formats;
        Columns = columns;
        Rows = rows;
        RemovedSourceRows = removedSourceRows;
        RemovedSourceColumns = removedSourceColumns;
    }

    internal (TableRow Row, object Column, CellFormat Format)[] Formats { get; }
    internal (object Column, string Header)[] Columns { get; }
    internal (TableRow Row, string[] Values, string[]? Original)[] Rows { get; }
    internal int[] RemovedSourceColumns { get; }
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

    /// <summary>For a row inserted above an existing sheet row: that row's number when the table was loaded or last saved.</summary>
    internal int? InsertBeforeRow { get; set; }

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

    internal (string[] Values, string[]? Original) Capture() => ((string[])_values.Clone(), (string[]?)_original?.Clone());

    internal void Restore(string[] values, string[]? original)
    {
        _values = (string[])values.Clone();
        _original = (string[]?)original?.Clone();
    }

    /// <summary>Makes room for a new, empty column at this position (the saved original stays "empty" there).</summary>
    internal void InsertValueAt(int index)
    {
        var values = _values.ToList();
        values.Insert(Math.Min(index, values.Count), string.Empty);
        _values = [.. values];
        if (_original is null) return;
        var original = _original.ToList();
        if (index <= original.Count) original.Insert(index, string.Empty);
        _original = [.. original];
    }

    internal void RemoveValueAt(int index)
    {
        var values = _values.ToList();
        if (index < values.Count) values.RemoveAt(index);
        _values = [.. values];
        if (_original is null) return;
        var original = _original.ToList();
        if (index < original.Count) original.RemoveAt(index);
        _original = [.. original];
    }

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
