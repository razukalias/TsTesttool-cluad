namespace LoadTestingTool.Editing;

public enum ProblemSeverity
{
    /// <summary>The runner will refuse to read the workbook, or the step/row cannot run.</summary>
    Error,
    /// <summary>The runner accepts the workbook but will behave in a way the author may not expect.</summary>
    Warning
}

/// <summary>One thing wrong with a workbook, in terms an editor can show next to the offending cell.</summary>
/// <param name="Severity">Error or warning.</param>
/// <param name="Sheet">Worksheet name the problem belongs to.</param>
/// <param name="RowNumber">Sheet row number, or null for header-level problems and rows not saved yet.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Row">The row the problem is about, including rows not saved yet; null for header-level problems.</param>
public sealed record WorkbookProblem(ProblemSeverity Severity, string Sheet, int? RowNumber, string Message, TableRow? Row = null);

/// <summary>A testcase from the config sheet: its index, name, and step names in run order.</summary>
public sealed record TestcaseInfo(int Index, string Name, IReadOnlyList<string> Steps, IReadOnlyList<StepNesting>? Nesting = null);

/// <summary>Result of <see cref="WorkbookDocument.Save"/>.</summary>
/// <param name="Saved">False when there was nothing to write.</param>
/// <param name="BackupPath">Path of the backup copy of the previous file, if one was made.</param>
/// <param name="RowsInserted">How many rows were inserted in the middle of a sheet. Rows below them moved, so reload the document afterwards.</param>
/// <param name="OtherRowsInserted">How many rows were inserted in the middle of the other sheets (a row there is one sheet row, not a request and response pair).</param>
/// <param name="RowsReordered">How many rows changed place because the rows of each testcase were put together in testcase order (summed over all sheets).</param>
/// <param name="ReferencesRenumbered"><c>&lt;From_…_N&gt;</c> references that were rewritten because rows moved.</param>
public sealed record SaveResult(bool Saved, string? BackupPath, int RowsInserted = 0, int ReferencesRenumbered = 0, int ColumnsChanged = 0, int OtherRowsInserted = 0, int RowsReordered = 0)
{
    /// <summary>True when sheet positions moved or references were rewritten, so the document must be read again.</summary>
    public bool RequiresReload => RowsInserted > 0 || OtherRowsInserted > 0 || ReferencesRenumbered > 0 || ColumnsChanged > 0 || RowsReordered > 0;
}

public sealed class WorkbookSaveOptions
{
    /// <summary>Copy the previous file to "&lt;name&gt;.backup.&lt;timestamp&gt;.xlsx" before replacing it.</summary>
    public bool CreateBackup { get; init; } = true;

    /// <summary>
    /// Central folder for backups. When set, the copy goes to "&lt;folder&gt;/&lt;workbook folder name&gt;/&lt;name&gt;_&lt;timestamp&gt;.backup.xlsx"
    /// instead of beside the workbook, so one place holds every backup. Null keeps the copy beside the workbook.
    /// </summary>
    public string? BackupFolder { get; set; }

    /// <summary>Replace the file even if it changed on disk after it was loaded.</summary>
    public bool OverwriteExternalChanges { get; init; }

    /// <summary>When a column was renamed, rewrite the <c>&lt;From_…&gt;</c> references that point at it (default). False renames only.</summary>
    public bool FixRenamedReferences { get; init; } = true;
}

/// <summary>The workbook file changed on disk (for example in Excel) after it was loaded for editing.</summary>
public sealed class WorkbookChangedException : IOException
{
    public WorkbookChangedException(string path)
        : base($"'{path}' changed on disk after it was loaded. Reload it, or save with OverwriteExternalChanges to replace those changes.")
    {
    }
}

/// <summary>An opaque capture of a whole <see cref="WorkbookDocument"/>'s edits, used for undo and redo.</summary>
public sealed record DocumentSnapshot(SheetSnapshot Config, SheetSnapshot Request, SheetSnapshot Response, OtherSheetsSnapshot? Others = null);

/// <summary>The other worksheets (tables, names, removals) at one moment, for undo.</summary>
public sealed record OtherSheetsSnapshot(IReadOnlyList<(SheetTable Table, SheetSnapshot Snapshot, string Name)> Tables, IReadOnlyList<string> RemovedSheets);
