namespace LoadTestingTool.UI.DataEditor;

public sealed partial class StepRowViewModel
{
    /// <summary>The cell holding a config column, or -1 when the sheet does not have that column.</summary>
    public int CellOf(string header)
        => _columns.FirstOrDefault(c => c.Title.Equals(header, StringComparison.OrdinalIgnoreCase))?.CellIndex ?? -1;

    /// <summary>
    /// Writes a config column by header. A column the sheet lacks is added first (undoable together with the value), so a
    /// type's fields can be filled in even in a workbook that never had them.
    /// </summary>
    public void Write(string header, string value)
    {
        var cell = CellOf(header);
        if (cell >= 0) { if (Cells[cell] != value) Cells[cell] = value; return; }
        if (value.Length == 0) return;
        Editor?.SetStepCellAddingColumn(this, header, value);
    }
}
