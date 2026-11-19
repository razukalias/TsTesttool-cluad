using System.Collections.Specialized;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>Request cells and expected-value cells are never selected together.</summary>
public enum CellKind { Request, Expected }

/// <summary>
/// An Excel-style selection of cells in the table: click selects one, Ctrl+click adds or removes one, Shift+click or dragging selects a block, the arrow keys move it.
/// A selection holds one kind of cell only (request cells or expected-value cells). The grid itself only selects rows, so this is kept here and drawn by the columns.
/// </summary>
public sealed class CellSelection
{
    private readonly DataEditorViewModel _editor;
    private HashSet<(DataRowViewModel Row, int Cell)> _cells = [];

    public CellSelection(DataEditorViewModel editor)
    {
        _editor = editor;
        editor.VisibleRows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>The cell the selection started from (Shift extends from it).</summary>
    public (DataRowViewModel Row, int Cell)? Anchor { get; private set; }

    /// <summary>The cell that was clicked or moved to last (the one that gets the wizard and the arrow keys).</summary>
    public (DataRowViewModel Row, int Cell)? Active { get; private set; }

    public event EventHandler? Changed;

    /// <summary>The cell indexes in the order they appear on screen (visible data columns, left to right); the window sets it, the default is every data column.</summary>
    public Func<IReadOnlyList<int>>? CellOrder { get; set; }

    public int Count => _cells.Count;

    public bool Contains(DataRowViewModel row, int cell) => _cells.Contains((row, cell));

    public CellKind? Kind => Active is { } active && Selectable(active.Cell) ? KindOf(active.Cell) : null;

    /// <summary>The selected cells, top to bottom and left to right.</summary>
    public IReadOnlyList<(DataRowViewModel Row, int Cell)> Cells
    {
        get
        {
            var order = Order();
            var rowIndex = _editor.VisibleRows.Select((row, index) => (row, index)).ToDictionary(x => x.row, x => x.index);
            return _cells.OrderBy(c => rowIndex.GetValueOrDefault(c.Row, int.MaxValue)).ThenBy(c => order.IndexOf(c.Cell)).ToList();
        }
    }

    public bool Selectable(int cell) => cell >= 1 && cell <= _editor.Columns.Count;

    public CellKind KindOf(int cell) => _editor.Columns[cell - 1].Group == ColumnGroup.Response ? CellKind.Expected : CellKind.Request;

    private List<int> Order() => (CellOrder?.Invoke() ?? Enumerable.Range(1, _editor.Columns.Count).ToList()).ToList();

    public void Select(DataRowViewModel row, int cell)
    {
        if (!Selectable(cell)) { Clear(); return; }
        _cells = [(row, cell)];
        Anchor = Active = (row, cell);
        Raise();
    }

    /// <summary>Ctrl+click: adds the cell, or removes it when it is already selected. A cell of the other kind is refused (returns false).</summary>
    public bool Toggle(DataRowViewModel row, int cell)
    {
        if (!Selectable(cell)) return false;
        if (Kind is { } kind && KindOf(cell) != kind) return false;
        if (!_cells.Remove((row, cell))) _cells.Add((row, cell));
        Anchor = Active = (row, cell);
        Raise();
        return true;
    }

    /// <summary>Shift+click or dragging: the block between the anchor and this cell, limited to cells of the anchor's kind.</summary>
    public void ExtendTo(DataRowViewModel row, int cell)
    {
        if (Anchor is not { } anchor || !Selectable(cell)) { Select(row, cell); return; }
        var order = Order();
        var from = order.IndexOf(anchor.Cell); var to = order.IndexOf(cell);
        var rowFrom = _editor.VisibleRows.IndexOf(anchor.Row); var rowTo = _editor.VisibleRows.IndexOf(row);
        if (from < 0 || to < 0 || rowFrom < 0 || rowTo < 0) { Select(row, cell); return; }
        var kind = KindOf(anchor.Cell);
        var block = new HashSet<(DataRowViewModel, int)>();
        for (var r = Math.Min(rowFrom, rowTo); r <= Math.Max(rowFrom, rowTo); r++)
            for (var c = Math.Min(from, to); c <= Math.Max(from, to); c++)
                if (Selectable(order[c]) && KindOf(order[c]) == kind) block.Add((_editor.VisibleRows[r], order[c]));
        _cells = block;
        Active = KindOf(cell) == kind ? (row, cell) : anchor;
        Raise();
    }

    /// <summary>Moves the active cell by rows and columns (arrow keys); with <paramref name="extend"/> the selection grows from the anchor, otherwise it is just that cell.</summary>
    public bool Move(int rows, int columns, bool extend)
    {
        if (Active is not { } active) return false;
        var order = Order();
        var position = order.IndexOf(active.Cell);
        var rowIndex = _editor.VisibleRows.IndexOf(active.Row);
        if (position < 0 || rowIndex < 0) return false;
        var newRow = Math.Clamp(rowIndex + rows, 0, _editor.VisibleRows.Count - 1);
        var newPosition = Math.Clamp(position + columns, 0, order.Count - 1);
        var target = order[newPosition];
        if (extend) ExtendTo(_editor.VisibleRows[newRow], target); else Select(_editor.VisibleRows[newRow], target);
        return true;
    }

    /// <summary>Ctrl+A: every cell of the active cell's kind in the rows shown.</summary>
    public void SelectAll()
    {
        if (Active is not { } active) return;
        var kind = KindOf(active.Cell);
        var cells = Order().Where(c => Selectable(c) && KindOf(c) == kind).ToList();
        _cells = _editor.VisibleRows.SelectMany(row => cells.Select(c => (row, c))).ToHashSet();
        Raise();
    }

    public void Clear()
    {
        if (_cells.Count == 0 && Active is null) return;
        _cells = [];
        Anchor = Active = null;
        Raise();
    }

    /// <summary>The rows were rebuilt (a filter, Undo, a step switch): keep the cells whose row still exists, found again by testcase and Data ID.</summary>
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_cells.Count == 0 && Active is null) return;
        if (e.Action != NotifyCollectionChangedAction.Reset) { Clear(); return; }
        var rows = new Dictionary<(int, string), DataRowViewModel>();
        foreach (var row in _editor.VisibleRows) rows[(row.TestcaseIndex, row.CurrentDataId.Trim().ToUpperInvariant())] = row;
        DataRowViewModel? Again(DataRowViewModel old) => _editor.VisibleRows.Contains(old) ? old : rows.GetValueOrDefault((old.TestcaseIndex, old.CurrentDataId.Trim().ToUpperInvariant()));
        var kept = new HashSet<(DataRowViewModel, int)>();
        foreach (var (row, cell) in _cells) if (Again(row) is { } found && cell <= _editor.Columns.Count) kept.Add((found, cell));
        (DataRowViewModel, int)? Map((DataRowViewModel Row, int Cell)? at) => at is { } a && Again(a.Row) is { } r && a.Cell <= _editor.Columns.Count ? (r, a.Cell) : null;
        _cells = kept;
        Anchor = Map(Anchor); Active = Map(Active);
        if (_cells.Count == 0) { Anchor = Active = null; }
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
