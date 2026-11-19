using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// One column of the selected data row in Record view: its header and the value as parts. Every change writes the joined text
/// into the grid cell, so the workbook keeps exactly the same kind of text as before (and Undo, dirty state and validation work as usual).
/// </summary>
/// <summary>Anything that holds a row of cell texts the Record view's line can edit: a data row, or a row of another sheet.</summary>
public interface ICellRow { ObservableCollection<string> Cells { get; } }

public sealed partial class RecordLineViewModel : ObservableObject
{
    private readonly ICellRow _row;
    private readonly int _cell;
    private readonly string _headerVerb;
    private bool _writing;

    private readonly Func<IEnumerable<LoadTestingTool.Editing.ValuePart>, string?, List<string>>? _warn;

    public RecordLineViewModel(ICellRow row, int cell, string title, bool expected, Func<IEnumerable<LoadTestingTool.Editing.ValuePart>, string?, List<string>>? warn = null)
    {
        _warn = warn;
        FullTitle = title;
        _row = row;
        _cell = cell;
        IsExpected = expected;
        var (verb, field) = expected ? ValueParts.SplitHeader(title) : ("eq", title);
        _headerVerb = verb;
        Title = field;
        HasCondition = expected && field.Contains('.');
        Load();
        _row.Cells.CollectionChanged += (_, _) => { if (!_writing) Load(); };
    }

    public string Title { get; }

    /// <summary>The header as the grid shows it (with the condition for an expected value).</summary>
    public string FullTitle { get; }
    public bool IsExpected { get; }

    /// <summary>What looks wrong about the value, in plain words; empty when nothing does.</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>The step this column belongs to (the part of the header before the first dot).</summary>
    public string? Step => Title.Contains('.') ? Title[..Title.IndexOf('.')] : null;

    /// <summary>Only expected values on a response path (Step.path) have a condition; other columns hold plain values.</summary>
    public bool HasCondition { get; }

    public ObservableCollection<ValuePart> Parts { get; } = [];

    /// <summary>The request column <c>dontrun</c>: the steps this one row skips.</summary>
    public bool IsDontRun => !IsExpected && Title.Equals("dontrun", StringComparison.OrdinalIgnoreCase);

    public const string DontRunHelp = "Steps that are skipped for this row only. Write step names separated by commas (or |), for example CreatePost1, Step3. "
        + "Only the listed steps are skipped; the row's other steps still run, and every other row is unaffected. Use \"Choose steps\" to tick them instead of typing.";

    /// <summary>The step names written in the cell, in the same way the runner reads them (commas or |).</summary>
    public IReadOnlyList<string> ListedSteps => ListedIn(Raw);

    public static IReadOnlyList<string> ListedIn(string text)
        => string.IsNullOrWhiteSpace(text) ? [] : text.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Writes the chosen step names as the cell's value.</summary>
    public void SetListedSteps(IEnumerable<string> names) => SetRaw(string.Join(", ", names));

    [ObservableProperty] private string _verb = "eq";
    [ObservableProperty] private string _raw = string.Empty;
    [ObservableProperty] private string _example = string.Empty;

    public bool ShowsValue => !HasCondition || ValueParts.NeedsValue(Verb);

    /// <summary>The condition in plain words (for the dropdown).</summary>
    public string ConditionWords
    {
        get => ValueParts.Conditions.FirstOrDefault(c => c.Verb == Verb).Words ?? Verb;
        set { if (ValueParts.Conditions.FirstOrDefault(c => c.Words == value).Verb is { } verb) SetCondition(verb); }
    }

    public static IReadOnlyList<string> ConditionChoices { get; } = ValueParts.Conditions.Select(c => c.Words).ToList();

    private void Load()
    {
        var text = _row.Cells[_cell] ?? string.Empty;
        var (cellVerb, value) = HasCondition ? ValueParts.SplitCell(text) : (null, text);
        Verb = cellVerb ?? _headerVerb;
        Parts.Clear();
        foreach (var part in ValueParts.Parse(value)) Parts.Add(part);
        Refresh();
    }

    private void Refresh()
    {
        Raw = _row.Cells[_cell] ?? string.Empty;
        Warnings = _warn?.Invoke(Parts, Step) ?? [];
        OnPropertyChanged(nameof(Warnings));
        Example = ValueParts.Example(Parts);
        OnPropertyChanged(nameof(ShowsValue));
        OnPropertyChanged(nameof(ConditionWords));
    }

    private void Write()
    {
        var value = ValueParts.Format(Parts);
        var text = HasCondition ? ValueParts.JoinCell(Verb, _headerVerb, ShowsValue ? value : string.Empty) : value;
        _writing = true;
        try { if ((_row.Cells[_cell] ?? string.Empty) != text) _row.Cells[_cell] = text; }
        finally { _writing = false; }
        Refresh();
    }

    public void Add(ValuePart part) { Parts.Add(part); Write(); }
    public void Remove(int index) { if (index >= 0 && index < Parts.Count) { Parts.RemoveAt(index); Write(); } }
    public void Replace(int index, ValuePart part) { if (index >= 0 && index < Parts.Count) { Parts[index] = part; Write(); } }

    public void Move(int index, int delta)
    {
        var to = index + delta;
        if (index < 0 || index >= Parts.Count || to < 0 || to >= Parts.Count) return;
        Parts.Move(index, to);
        Write();
    }

    public void SetCondition(string verb)
    {
        if (!HasCondition || verb == Verb) return;
        Verb = verb;
        if (!ValueParts.NeedsValue(verb)) Parts.Clear();
        Write();
    }

    /// <summary>Replaces the whole value with text typed as raw (for text that is too complex for chips).</summary>
    public void SetRaw(string text)
    {
        Parts.Clear();
        foreach (var part in ValueParts.Parse(text)) Parts.Add(part);
        Write();
    }
}

/// <summary>One entry of the [+] menu. A folder has <see cref="Children"/>; a final item has <see cref="Result"/> or asks for text first (<see cref="Prompt"/>).</summary>
public sealed class MenuNode
{
    public required string Title { get; init; }
    public Func<IReadOnlyList<MenuNode>>? Children { get; init; }
    public ValuePart? Result { get; init; }
    public string? Prompt { get; init; }
    public Func<string, ValuePart?>? FromInput { get; init; }
    /// <summary>An entry that opens a dialog of its own; it calls the given action with the part when the dialog gives one.</summary>
    public Action<Action<ValuePart>>? Open { get; init; }
    public bool IsFolder => Children is not null;
    public string Display => IsFolder ? Title + "  ›" : Title;
}

/// <summary>Builds the [+] menu: one list at a time, Text / From / Random number / GUID / Date and time / File.</summary>
public static class RecordMenu
{
    public sealed record Sources(IReadOnlyList<string> RequestHeaders, IReadOnlyList<string> ResponseFields, IReadOnlyList<string> ConfigHeaders, IReadOnlyList<string> Steps);

    /// <summary>What the Script entries do: use an existing template, or write a new one. Both end by handing over a part.</summary>
    public sealed record ScriptHooks(Action<Action<ValuePart>> UseTemplate, Action<Action<ValuePart>> NewTemplate);

    public static IReadOnlyList<MenuNode> Root(Sources sources, ScriptHooks? scripts = null) =>
    [
        new() { Title = "Text", Prompt = "Text", FromInput = t => t.Length == 0 ? null : ValuePart.Text(t) },
        new() { Title = "From", Children = () => FromSheets(sources) },
        new() { Title = "Random number", Children = RandomFormats },
        new() { Title = "GUID", Children = GuidFormats },
        new() { Title = "Date and time", Children = DateFormats },
        new() { Title = "File", Children = () => [
            new MenuNode { Title = "File content as text", Prompt = "File name or path", FromInput = p => p.Trim().Length == 0 ? null : ValuePart.File(p.Trim()) },
            new MenuNode { Title = "File content as Base64", Prompt = "File name or path", FromInput = p => p.Trim().Length == 0 ? null : ValuePart.File(p.Trim(), base64: true) } ] },
        .. (scripts is null ? Array.Empty<MenuNode>() : [new MenuNode { Title = "Script", Children = () => [
            new MenuNode { Title = "Use an existing script…", Open = scripts.UseTemplate },
            new MenuNode { Title = "Write a new script template…", Open = scripts.NewTemplate } ] }]),
    ];

    private static IReadOnlyList<MenuNode> FromSheets(Sources s) =>
    [
        new() { Title = "request", Children = () => s.RequestHeaders.Select(h => Row("request", h)).ToList() },
        new() { Title = "response (an earlier step's result)", Children = () => s.ResponseFields.Select(f => (MenuNode)new() { Title = f, Result = ValuePart.From("response", f) }).ToList() },
        new() { Title = "config", Children = () => s.ConfigHeaders.Select(h => Row("config", h)).ToList() },
    ];

    private static MenuNode Row(string sheet, string header) => new()
    {
        Title = header,
        Children = () =>
        [
            new MenuNode { Title = "this row", Result = ValuePart.From(sheet, header) },
            new MenuNode { Title = "row number…", Prompt = "Row number in the sheet", FromInput = n => int.TryParse(n.Trim(), out var row) && row > 0 ? ValuePart.From(sheet, header, row.ToString()) : null },
        ],
    };

    private static IReadOnlyList<MenuNode> RandomFormats() =>
    [
        new() { Title = "Any number (0 – 999999)", Result = ValuePart.Random() },
        new() { Title = "6 digits (000000 – 999999)", Result = ValuePart.Random("6") },
        new() { Title = "8 digits", Result = ValuePart.Random("8") },
        new() { Title = "Between two numbers…", Prompt = "Range, e.g. 100-999", FromInput = f => System.Text.RegularExpressions.Regex.IsMatch(f.Trim(), @"^\d+-\d+$") ? ValuePart.Random(f.Trim()) : null },
        new() { Title = "Number of digits…", Prompt = "Digits, e.g. 10", FromInput = f => f.Trim().Length > 0 && f.Trim().All(char.IsDigit) ? ValuePart.Random(f.Trim()) : null },
    ];

    private static IReadOnlyList<MenuNode> GuidFormats() =>
    [
        new() { Title = "Standard (xxxxxxxx-xxxx-…)", Result = ValuePart.Guid() },
        new() { Title = "Without dashes", Result = ValuePart.Guid("N") },
        new() { Title = "In braces { }", Result = ValuePart.Guid("B") },
        new() { Title = "In parentheses ( )", Result = ValuePart.Guid("P") },
    ];

    private static IReadOnlyList<MenuNode> DateFormats() =>
    [
        new() { Title = "Today (yyyy-MM-dd)", Result = ValuePart.Date("datum") },
        new() { Title = "Current time", Result = ValuePart.Date("currenttime") },
        new() { Title = "Current date and time", Result = ValuePart.Date("currentdatetime") },
        new() { Title = "Unix timestamp (ms)", Result = ValuePart.Date("currenttimestamp") },
        new() { Title = "Custom format…", Prompt = "Format, e.g. yyyy-MM-dd HH:mm", FromInput = f => f.Trim().Length == 0 ? null : ValuePart.Date("currentdatetime", f.Trim()) },
    ];

    /// <summary>Filter over one level: matches anywhere in the title, case-insensitive.</summary>
    public static IReadOnlyList<MenuNode> Filter(IReadOnlyList<MenuNode> level, string text)
        => text.Trim().Length == 0 ? level : level.Where(n => n.Title.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
}
