using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LoadTestingTool.UI;

public sealed class UiConfig
{
    public string InstancesRoot { get; set; } = "../Instances";
    public string WorkbookName { get; set; } = "DataEngine.xlsx";
    public string RunnerDll { get; set; } = "../Runner/LoadTestingTool.exe";
    public string SettingsFile { get; set; } = "../appsettings.json";
    public string InstancePrefix { get; set; } = "DataEngine_";
    public string DocumentationFile { get; set; } = "../APPLICATION_GUIDE.pdf";
    public string UiLogFile { get; set; } = "Logs/ui-internal.log";
    public int Threads { get; set; } = 1;
    public int Iterations { get; set; } = 1;
    public int ParallelRowStartIntervalMs { get; set; }
    public int IterationIntervalMs { get; set; }
    public int MaxParallelRows { get; set; } = 100;
    public string RowsExecutionMode { get; set; } = "parallel";
    public bool SpoolResultsToDisk { get; set; } = true;
    public bool RunEnvironmentsParallel { get; set; }
    public bool GenerateResults { get; set; } = true;
    public List<string> Environments { get; set; } = [];
    public Dictionary<string, UiInstanceDefinition> Instances { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UiInstanceDefinition
{
    public string Folder { get; set; } = string.Empty;
    public string Workbook { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int? Threads { get; set; }
    public int? Iterations { get; set; }
    public int? ParallelRowStartIntervalMs { get; set; }
    public int? ThreadIntervalMs { get; set; }
    public int? IterationIntervalMs { get; set; }
    public int? MaxParallelRows { get; set; }
    public int? MaxConcurrentRows { get; set; }
    public bool? SpoolResultsToDisk { get; set; }
    public string? RowsExecutionMode { get; set; }
    public string? ExecutionMode { get; set; }
    public bool? RunEnvironmentsParallel { get; set; }
    public bool? GenerateResults { get; set; }
    public bool? GenerateAggregateHistory { get; set; }
    public bool? GenerateRowHistory { get; set; }
    public bool? InternalLoggingEnabled { get; set; }
    public bool? SaveRequestBodyToHistory { get; set; }
    public bool? SaveResponseBodyToHistory { get; set; }
    public bool? MaskSensitiveData { get; set; }
    public Dictionary<string, bool>? HistorySheets { get; set; }
    public string? TestcaseSelection { get; set; }
    public string? StepSelection { get; set; }
    public string? EnvironmentSelection { get; set; }
    public string? DataIdSelection { get; set; }
    public string? RunId { get; set; }
}

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            var editorIndex = Array.FindIndex(args, x => x.Equals("--dataset-editor", StringComparison.OrdinalIgnoreCase));
            if (editorIndex >= 0 && editorIndex + 1 < args.Length)
                desktop.MainWindow = new SpreadsheetPreviewWindow(args[editorIndex + 1]);
            else
            {
                var detailsIndex = Array.FindIndex(args, x => x.Equals("--step-details", StringComparison.OrdinalIgnoreCase));
                if (detailsIndex >= 0 && detailsIndex + 6 < args.Length)
                {
                    desktop.MainWindow = new StepDetailsWindow(args[detailsIndex + 1], args[detailsIndex + 2], args[detailsIndex + 3], args[detailsIndex + 4], args[detailsIndex + 5], args[detailsIndex + 6]);
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
                var historyIndex = Array.FindIndex(args, x => x.Equals("--history", StringComparison.OrdinalIgnoreCase));
                desktop.MainWindow = historyIndex >= 0 && historyIndex + 1 < args.Length
                    ? new HistoryWorkspaceWindow(args[historyIndex + 1], ArgAfter(args, "--history-instance"), ArgAfter(args, "--history-testcase"))
                    : StartupWindow(args);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The new data editor is the application; the original dashboard only opens with --dashboard.</summary>
    private static Avalonia.Controls.Window StartupWindow(string[] args)
    {
        var dashboard = new MainWindow();
        if (args.Any(x => x.Equals("--dashboard", StringComparison.OrdinalIgnoreCase))) return dashboard;
        return (Avalonia.Controls.Window?)dashboard.CreateStandaloneEditor() ?? dashboard;
    }

    private static string? ArgAfter(string[] args, string name)
    {
        var at = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
