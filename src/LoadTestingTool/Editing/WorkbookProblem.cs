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
public sealed record WorkbookProblem(ProblemSeverity Severity, string Sheet, int? RowNumber, string Message);

/// <summary>Result of <see cref="WorkbookDocument.Save"/>.</summary>
/// <param name="Saved">False when there was nothing to write.</param>
/// <param name="BackupPath">Path of the backup copy of the previous file, if one was made.</param>
public sealed record SaveResult(bool Saved, string? BackupPath);

public sealed class WorkbookSaveOptions
{
    /// <summary>Copy the previous file to "&lt;name&gt;.backup.&lt;timestamp&gt;.xlsx" before replacing it.</summary>
    public bool CreateBackup { get; init; } = true;

    /// <summary>Replace the file even if it changed on disk after it was loaded.</summary>
    public bool OverwriteExternalChanges { get; init; }
}

/// <summary>The workbook file changed on disk (for example in Excel) after it was loaded for editing.</summary>
public sealed class WorkbookChangedException : IOException
{
    public WorkbookChangedException(string path)
        : base($"'{path}' changed on disk after it was loaded. Reload it, or save with OverwriteExternalChanges to replace those changes.")
    {
    }
}
