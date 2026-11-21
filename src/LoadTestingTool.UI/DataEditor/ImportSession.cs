namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// What the Import window is doing, without any controls: the opened file, the part chosen (sheet, range, header row), the ticks, the mode and the
/// column matching. The window shows it; <see cref="BuildPlan"/> turns it into what <see cref="DataEditorViewModel.ApplyImport"/> applies.
/// </summary>
public sealed class ImportSession(DataEditorViewModel editor, EditorSettings? settings = null)
{
    private readonly EditorSettings _settings = settings ?? EditorSettings.Load();

    public DataEditorViewModel Editor { get; } = editor;
    public ImportSource? Source { get; private set; }
    public string? FilePath { get; private set; }
    public ImportTable? Table { get; private set; }
    public string? RangeError { get; private set; }

    public int SheetIndex { get; set; }
    public string RangeText { get; set; } = string.Empty;
    public int HeaderRow { get; set; } = 1;
    public ImportMode Mode { get; set; } = ImportMode.AppendRows;
    public ImportMatch Match { get; set; } = ImportMatch.ByHeader;
    public ImportRowMatch RowMatch { get; set; } = ImportRowMatch.DataId;
    public ImportPlacement Placement { get; set; } = ImportPlacement.End;
    public bool CreateUnmatched { get; set; }
    public bool ClearOnEmpty { get; set; }
    public string? Step { get; set; }
    public bool Expected { get; set; }
    public string Verb { get; set; } = "eq";
    public char Delimiter { get; set; }
    public string Encoding { get; set; } = "Auto";

    public bool[] TakeColumn { get; private set; } = [];
    public bool[] TakeRow { get; private set; } = [];
    /// <summary>For the by-hand matching: per file column its target key, <see cref="ImportPlan.Skip"/> or <see cref="ImportPlan.Create"/>.</summary>
    public int[] Manual { get; private set; } = [];

    public IReadOnlyList<ImportTarget> Targets => Editor.ImportTargetList();
    public string Memory => $"{(Editor.IsOtherSheet ? "sheet" : "testcase")}|{Path.GetFileName(FilePath ?? string.Empty)}";

    /// <summary>Reads the file; returns an error text, or null when it opened. The remembered choices for a file of this name are applied.</summary>
    public string? Open(string path)
    {
        try
        {
            Source = ImportSource.Open(path, Delimiter == '\0' ? null : Delimiter, Encoding == "Auto" ? null : Encoding);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Source = null; Table = null;
            return $"Could not read {Path.GetFileName(path)}: {ex.Message}";
        }
        FilePath = path;
        if (Source.Sheets.Count == 0 || Source.Sheets.All(s => s.RowCount == 0)) { Table = null; return $"{Path.GetFileName(path)} has no cells with values."; }
        SheetIndex = Math.Max(0, Source.Sheets.ToList().FindIndex(s => s.RowCount > 0));
        RangeText = string.Empty;
        HeaderRow = GuessHeaderRow(Source.Sheets[SheetIndex]);
        Mode = ImportMode.AppendRows; Match = ImportMatch.ByHeader; RowMatch = Editor.ImportHasDataId ? ImportRowMatch.DataId : ImportRowMatch.Position;
        Placement = ImportPlacement.End; CreateUnmatched = false; ClearOnEmpty = false; Expected = false; Verb = "eq";
        Step = Editor.ImportStepNames.LastOrDefault();
        var remembered = RestoreChoice();
        Refresh();
        if (remembered) RestoreManual();
        return null;
    }

    /// <summary>Re-reads the file with another delimiter or encoding.</summary>
    public string? Reopen() => FilePath is null ? null : Open(FilePath);

    /// <summary>The first row with the most filled cells among the first ten: the usual place of the column names.</summary>
    public static int GuessHeaderRow(ImportSheet sheet)
    {
        var best = 0;
        var most = 0;
        for (var r = 0; r < Math.Min(10, sheet.RowCount); r++)
        {
            var filled = sheet.Cells[r].Count(c => c.Trim().Length > 0);
            if (filled > most) { most = filled; best = r + 1; }
        }
        return Math.Max(1, best);
    }

    /// <summary>Slices the chosen sheet and range again (after the sheet, range or header row changed). Ticks start all on.</summary>
    public void Refresh()
    {
        RangeError = null;
        if (Source is null) { Table = null; return; }
        if (!ImportRange.TryParse(RangeText, out var range)) { RangeError = "Write the range like B3:F40, B:F or 3:40, or leave it empty for the whole sheet."; Table = null; return; }
        SheetIndex = Math.Clamp(SheetIndex, 0, Source.Sheets.Count - 1);
        Table = Source.Slice(SheetIndex, range, HeaderRow);
        TakeColumn = Enumerable.Repeat(true, Table.ColumnCount).ToArray();
        TakeRow = Enumerable.Repeat(true, Table.Rows.Count).ToArray();
        Manual = new int[Table.ColumnCount];
        var targets = Targets;
        for (var i = 0; i < Manual.Length; i++) Manual[i] = ImportMapper.ByHeader(Table.Headers[i], targets) ?? ImportPlan.Skip;
    }

    public void TickColumnsWhere(Func<int, bool> chosen) { for (var i = 0; i < TakeColumn.Length; i++) TakeColumn[i] = chosen(i); }
    public void TickRowsWhere(Func<int, bool> chosen) { for (var i = 0; i < TakeRow.Length; i++) TakeRow[i] = chosen(i); }

    public ImportPlan? BuildPlan() => Table is null ? null : new ImportPlan
    {
        Table = Table, TakeColumn = [.. TakeColumn], TakeRow = [.. TakeRow], Mode = Mode, Match = Match, Manual = [.. Manual], CreateUnmatched = CreateUnmatched,
        ClearOnEmpty = ClearOnEmpty, RowMatch = Editor.ImportHasDataId ? RowMatch : ImportRowMatch.Position, Step = Step, Expected = Expected, Verb = Verb, Placement = Placement,
    };

    /// <summary>Where each file column goes with the current choices, as text for the preview header ("→ name", "new column", "skipped").</summary>
    public string[] Labels()
    {
        if (BuildPlan() is not { } plan) return [];
        var targets = Targets;
        var notes = new List<string>();
        var map = Mode == ImportMode.NewColumns
            ? Enumerable.Range(0, plan.Table.ColumnCount).Select(i => !plan.TakeColumn[i] ? ImportPlan.Skip : i == ImportMapper.DataIdColumn(plan.Table) && Editor.ImportHasDataId && plan.RowMatch == ImportRowMatch.DataId ? 0 : ImportPlan.Create).ToArray()
            : ImportMapper.Resolve(plan, targets, notes);
        return map.Select(m => m switch { ImportPlan.Skip => "skipped", ImportPlan.Create => "new column", _ => "→ " + targets.FirstOrDefault(t => t.Key == m)?.Display }).ToArray();
    }

    public string Impact() => BuildPlan() is { } plan ? Editor.DescribeImpact(plan) : string.Empty;

    public bool NeedsConfirmation => BuildPlan() is { } plan && DataEditorViewModel.NeedsConfirmation(plan);

    /// <summary>Runs the import; remembers the choices for this file name when something happened.</summary>
    public ImportReport? Run()
    {
        if (BuildPlan() is not { } plan) return null;
        var report = Editor.ApplyImport(plan);
        if (report.Error is null) SaveChoice();
        return report;
    }

    // ---- Remembered choices -----------------------------------------------------------------------------------

    private void SaveChoice()
    {
        if (FilePath is null || Source is null || Table is null) return;
        var targets = Targets;
        var choice = new ImportChoice
        {
            Sheet = Source.Sheets[SheetIndex].Name, Range = RangeText, HeaderRow = HeaderRow, Mode = Mode.ToString(), Match = Match.ToString(), RowMatch = RowMatch.ToString(),
            Placement = Placement.ToString(), CreateUnmatched = CreateUnmatched, ClearOnEmpty = ClearOnEmpty, Step = Step, Expected = Expected, Verb = Verb,
        };
        for (var i = 0; i < Manual.Length; i++)
        {
            if (Table.Headers[i].Length == 0) continue;
            choice.Manual[Table.Headers[i]] = Manual[i] switch { ImportPlan.Skip => "skip", ImportPlan.Create => "create", var key => targets.FirstOrDefault(t => t.Key == key)?.Display ?? "skip" };
        }
        _settings.ImportChoices[Memory] = choice;
        _settings.Save();
    }

    private bool RestoreChoice()
    {
        if (Source is null || !_settings.ImportChoices.TryGetValue(Memory, out var c)) return false;
        var sheet = Source.Sheets.ToList().FindIndex(s => s.Name.Equals(c.Sheet, StringComparison.OrdinalIgnoreCase));
        if (sheet >= 0) SheetIndex = sheet;
        RangeText = c.Range; HeaderRow = c.HeaderRow;
        Mode = Enum.TryParse<ImportMode>(c.Mode, out var mode) ? mode : Mode;
        Match = Enum.TryParse<ImportMatch>(c.Match, out var match) ? match : Match;
        RowMatch = Enum.TryParse<ImportRowMatch>(c.RowMatch, out var rowMatch) ? rowMatch : RowMatch;
        Placement = Enum.TryParse<ImportPlacement>(c.Placement, out var placement) ? placement : Placement;
        CreateUnmatched = c.CreateUnmatched; ClearOnEmpty = c.ClearOnEmpty; Expected = c.Expected; Verb = c.Verb;
        if (c.Step is { } step && Editor.ImportStepNames.Contains(step)) Step = step;
        return true;
    }

    private void RestoreManual()
    {
        if (Table is null || !_settings.ImportChoices.TryGetValue(Memory, out var c)) return;
        var targets = Targets;
        for (var i = 0; i < Manual.Length; i++)
        {
            if (!c.Manual.TryGetValue(Table.Headers[i], out var wanted)) continue;
            Manual[i] = wanted == "skip" ? ImportPlan.Skip : wanted == "create" ? ImportPlan.Create : targets.FirstOrDefault(t => t.Display == wanted)?.Key ?? Manual[i];
        }
    }
}
