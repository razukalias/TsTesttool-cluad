using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LoadTestingTool.Domain;
using LoadTestingTool.Excel;

namespace LoadTestingTool.UI;

internal sealed record ValidationIssue(string Severity, string Location, string Problem, string Fix)
{
    public override string ToString() => $"[{Severity}] {Location}\n  {Problem}\n  Fix: {Fix}";
}

internal sealed record InstanceValidationResult(string Instance, IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(x => x.Severity == "ERROR");
    public bool HasWarnings => Issues.Any(x => x.Severity == "WARNING");
}

internal static class GlobalValidationService
{
    public static IReadOnlyList<InstanceValidationResult> ValidateAll(IEnumerable<InstanceInfo> instances, IReadOnlyList<string> globalEnvironments)
    {
        var list = instances.ToList();
        if (list.Count == 0) return [new InstanceValidationResult("Global", [Issue("ERROR", "Instances", "No instances are registered.", "Add an instance to the central appsettings.json file.")])];
        return list.Select(instance => ValidateInstance(instance, globalEnvironments)).ToList();
    }

    private static InstanceValidationResult ValidateInstance(InstanceInfo item, IReadOnlyList<string> globalEnvironments)
    {
        var issues = new List<ValidationIssue>();
        var name = string.IsNullOrWhiteSpace(item.Name) ? "Unnamed instance" : item.Name;
        try
        {
            if (string.IsNullOrWhiteSpace(item.FolderPath))
                issues.Add(Issue("ERROR", name, "The instance folder is empty.", "Set a valid Folder value in the central appsettings.json file."));
            else if (!Directory.Exists(item.FolderPath))
                issues.Add(Issue("ERROR", $"{name} / folder", $"Instance folder does not exist: {item.FolderPath}", "Create the folder or correct the instance Folder path."));
            else if (!CanReadDirectory(item.FolderPath, out var folderError))
                issues.Add(Issue("ERROR", $"{name} / folder", $"Instance folder cannot be read: {folderError}", "Check permissions and ensure the folder is available."));

            ValidateConfiguredFolder(issues, name, "Templates", item.TemplatesFolder);
            ValidateConfiguredFolder(issues, name, "Scripts/workspace", item.FolderPath);
            ValidateConfiguredFolder(issues, name, "History", item.HistoryFolder);
            ValidateConfiguredFolder(issues, name, "Logs", item.LogsFolder);
            ValidateConfiguredFolder(issues, name, "Results", item.ResultsFolder);
            ValidateGlobalEnvironments(issues, name, globalEnvironments, item.EnvironmentSelection);
            ValidateExecutionSettings(issues, name, item);

            if (!File.Exists(item.WorkbookPath))
            {
                issues.Add(Issue("ERROR", $"{name} / workbook", $"Workbook was not found: {item.WorkbookPath}", "Place the workbook at the configured path or update the central instance Workbook value."));
                return Finish(name, issues);
            }
            if (!CanReadFile(item.WorkbookPath, out var workbookError))
            {
                issues.Add(Issue("ERROR", $"{name} / workbook", $"Workbook cannot be read: {workbookError}", "Close the workbook in Excel and check file permissions."));
                return Finish(name, issues);
            }

            WorkbookModel workbook;
            try { workbook = new WorkbookReader().Read(item.WorkbookPath); }
            catch (Exception ex)
            {
                issues.Add(Issue("ERROR", $"{name} / workbook", ex.Message, "Open the workbook and correct the reported sheet, header, row, or configuration problem."));
                return Finish(name, issues);
            }

            if (workbook.Testcases.Count == 0)
                issues.Add(Issue("ERROR", $"{name} / workbook", "No testcases were found in the config worksheet.", "Add at least one valid enabled testcase and step."));
            foreach (var testcase in workbook.Testcases)
            {
                var location = $"{name} / testcase {testcase.TestcaseIndex} ({testcase.Testcase})";
                var generatedArtifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (testcase.Steps.Count == 0) issues.Add(Issue("ERROR", location, "The testcase has no steps.", "Add at least one step to the config worksheet."));
                if (!testcase.Steps.Any(x => x.Enabled)) issues.Add(Issue("WARNING", location, "The testcase has no enabled steps.", "Enable at least one step if this testcase should run."));
                foreach (var step in testcase.Steps.Where(x => x.Enabled)) ValidateStep(issues, item, testcase, step, generatedArtifacts);
            }
            ValidateSavedSelection(issues, item, workbook, globalEnvironments);
            if (!item.HistorySheets.Values.Any(x => x)) issues.Add(Issue("WARNING", $"{name} / history", "All history sheets are disabled.", "Enable at least one history sheet in the instance History settings tab."));
        }
        catch (Exception ex)
        {
            issues.Add(Issue("ERROR", name, $"Unexpected validation failure: {ex.Message}", "Review the central configuration and instance files, then validate again."));
        }
        return Finish(name, issues);
    }

    private static InstanceValidationResult Finish(string name, List<ValidationIssue> issues)
    {
        if (issues.Count == 0) issues.Add(Issue("PASS", name, "Workbook and referenced instance resources passed validation.", "No action required."));
        return new InstanceValidationResult(name, issues);
    }

    private static void ValidateStep(List<ValidationIssue> issues, InstanceInfo item, TestcaseDefinition testcase, RequestStep step, HashSet<string> generatedArtifacts)
    {
        var location = $"{item.Name} / testcase {testcase.TestcaseIndex} / step '{step.StepName}'";
        try { StepConfig.Parse(step); }
        catch (Exception ex) { issues.Add(Issue("ERROR", location, ex.Message, "Correct the StepConfig JSON or key=value entries in the config worksheet.")); }

        if (!string.IsNullOrWhiteSpace(step.TemplateSource))
        {
            try
            {
                var templatePath = WorkspaceGuard.Resolve(item.TemplatesFolder, step.TemplateSource);
                if (!File.Exists(templatePath)) issues.Add(Issue("ERROR", location, $"Template file was not found: {step.TemplateSource}", $"Create '{templatePath}' or update the template source."));
                else if (!CanReadFile(templatePath, out var error)) issues.Add(Issue("ERROR", location, $"Template file cannot be read: {error}", "Check the template file permissions and close any locking application."));
                else if (Path.GetExtension(templatePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    try { JsonDocument.Parse(NormalizeRuntimeVariablesForJson(File.ReadAllText(templatePath))); }
                    catch (JsonException ex) { issues.Add(Issue("ERROR", location, $"Template JSON is invalid: {ex.Message}", "Fix the JSON syntax in the referenced template file.")); }
                }
            }
            catch (Exception ex) { issues.Add(Issue("ERROR", location, $"Template path is unsafe or invalid: {ex.Message}", "Use a template path inside the instance Templates folder.")); }
        }

        try
        {
            switch (step.StepType)
            {
                case StepType.GraphQL:
                    ValidateSource(issues, item, location, StepConfig.Read<GraphQlStepOptions>(step).Source, "GraphQL source");
                    break;
                case StepType.Sql:
                    var sql = StepConfig.Read<SqlStepOptions>(step);
                    ValidateSource(issues, item, location, sql.Source, "SQL source");
                    if (string.IsNullOrWhiteSpace(sql.CommandText) && string.IsNullOrWhiteSpace(sql.Procedure) && string.IsNullOrWhiteSpace(sql.Source))
                        issues.Add(Issue("ERROR", location, "SQL step has no command text, procedure, or source file.", "Provide CommandText, Procedure, or Source in StepConfig."));
                    break;
                case StepType.Script:
                    var script = StepConfig.Read<ScriptStepOptions>(step);
                    ValidateSource(issues, item, location, script.Source, "Script source");
                    if (string.IsNullOrWhiteSpace(script.Source) && string.IsNullOrWhiteSpace(script.Inline))
                        issues.Add(Issue("ERROR", location, "Script step has no inline code or source file.", "Provide Inline or Source in StepConfig."));
                    break;
                case StepType.File:
                    var file = StepConfig.Read<FileStepOptions>(step);
                    if (string.IsNullOrWhiteSpace(file.Path)) issues.Add(Issue("ERROR", location, "File step has no Path.", "Provide a Path in StepConfig."));
                    else ValidateFilePath(issues, item, location, file.Path, file.Action, generatedArtifacts);
                    break;
            }
        }
        catch (Exception ex) { issues.Add(Issue("ERROR", location, ex.Message, "Correct the step-specific StepConfig values.")); }
    }

    private static void ValidateSource(List<ValidationIssue> issues, InstanceInfo item, string location, string? source, string label)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        try
        {
            var path = WorkspaceGuard.Resolve(item.FolderPath, source);
            if (!File.Exists(path)) issues.Add(Issue("ERROR", location, $"{label} file was not found: {source}", $"Create '{path}' or update the source path."));
            else if (!CanReadFile(path, out var error)) issues.Add(Issue("ERROR", location, $"{label} file cannot be read: {error}", "Check file permissions and close any locking application."));
        }
        catch (Exception ex) { issues.Add(Issue("ERROR", location, $"{label} path is unsafe: {ex.Message}", "Use a relative path inside the instance workspace.")); }
    }

    private static void ValidateFilePath(List<ValidationIssue> issues, InstanceInfo item, string location, string path, string action, HashSet<string> generatedArtifacts)
    {
        try
        {
            var fullPath = WorkspaceGuard.Resolve(item.FolderPath, path);
            var normalized = Path.GetFullPath(fullPath);
            var createsFile = action.Equals("write", StringComparison.OrdinalIgnoreCase) || action.Equals("append", StringComparison.OrdinalIgnoreCase) || action.Equals("writebinary", StringComparison.OrdinalIgnoreCase) || action.Equals("appendbinary", StringComparison.OrdinalIgnoreCase);
            var readsFile = action.Equals("read", StringComparison.OrdinalIgnoreCase) || action.Equals("json", StringComparison.OrdinalIgnoreCase) || action.Equals("readbinary", StringComparison.OrdinalIgnoreCase) || action.Equals("copy", StringComparison.OrdinalIgnoreCase) || action.Equals("upload", StringComparison.OrdinalIgnoreCase);
            if (createsFile)
            {
                generatedArtifacts.Add(normalized);
                return;
            }
            if (readsFile && !File.Exists(fullPath) && !generatedArtifacts.Contains(normalized))
                issues.Add(Issue("ERROR", location, $"Required input file was not found: {path}", $"Create '{fullPath}', add an earlier enabled step that writes it, or change the file action/path."));
            else if (readsFile && !File.Exists(fullPath) && generatedArtifacts.Contains(normalized))
                issues.Add(Issue("INFO", location, $"Runtime artifact will be available: {path}", "An earlier enabled Write or Append step creates this file before it is read. No action required."));
        }
        catch (Exception ex) { issues.Add(Issue("ERROR", location, $"File path is unsafe: {ex.Message}", "Use a relative path inside the instance workspace.")); }
    }

    private static void ValidateSavedSelection(List<ValidationIssue> issues, InstanceInfo item, WorkbookModel workbook, IReadOnlyList<string> globalEnvironments)
    {
        var location = $"{item.Name} / saved selection";
        if (!string.IsNullOrWhiteSpace(item.TestcaseSelection) && item.TestcaseSelection != "0")
            foreach (var token in item.TestcaseSelection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(token, out var index) && workbook.Testcases.All(x => x.TestcaseIndex != index)) issues.Add(Issue("ERROR", location, $"Selected testcase {index} no longer exists.", "Open the execution selection editor and choose existing testcases."));
        foreach (var environment in item.EnvironmentSelection.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!globalEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase)) issues.Add(Issue("ERROR", location, $"Selected environment '{environment}' is not defined globally.", "Add the environment in Manage global environments or remove it from this instance selection."));
        if (!string.IsNullOrWhiteSpace(item.StepSelectionJson))
        {
            try
            {
                var selected = JsonSerializer.Deserialize<Dictionary<int, string[]>>(item.StepSelectionJson) ?? [];
                foreach (var pair in selected)
                {
                    var testcase = workbook.Testcases.FirstOrDefault(x => x.TestcaseIndex == pair.Key);
                    foreach (var step in pair.Value)
                        if (testcase is null || testcase.Steps.All(x => !x.StepName.Equals(step, StringComparison.OrdinalIgnoreCase) || !x.Enabled))
                            issues.Add(Issue("ERROR", location, $"Selected step '{step}' is missing or disabled for testcase {pair.Key}.", "Open the execution selection editor and select enabled steps."));
                }
            }
            catch (Exception ex) { issues.Add(Issue("ERROR", location, $"Saved step selection is invalid: {ex.Message}", "Re-save the instance selection from the execution editor.")); }
        }
        try
        {
            foreach (var pair in DataIdSelectionCodec.Parse(item.DataIdSelection))
                foreach (var dataId in pair.Value)
                    if (!workbook.RequestData.Keys.Any(x => x.Index == pair.Key && x.DataId.Equals(dataId, StringComparison.OrdinalIgnoreCase)))
                        issues.Add(Issue("ERROR", location, $"Selected data ID '{dataId}' does not exist for testcase {pair.Key}.", "Edit the dataset or update the saved data-ID selection."));
        }
        catch (Exception ex) { issues.Add(Issue("ERROR", location, $"Saved data-ID selection is invalid: {ex.Message}", "Re-save the data-ID selection from the execution editor.")); }
    }

    private static void ValidateConfiguredFolder(List<ValidationIssue> issues, string instance, string label, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) { issues.Add(Issue("ERROR", $"{instance} / {label}", "The configured path is empty.", "Set a valid path in the central instance configuration.")); return; }
        if (!Directory.Exists(path)) issues.Add(Issue("WARNING", $"{instance} / {label}", $"Folder does not exist yet: {path}", "Create the folder or run the instance once if the folder is created automatically."));
        else if (!CanReadDirectory(path, out var error)) issues.Add(Issue("ERROR", $"{instance} / {label}", $"Folder cannot be read: {error}", "Check folder permissions."));
    }

    private static void ValidateGlobalEnvironments(List<ValidationIssue> issues, string instance, IReadOnlyList<string> environments, string selection)
    {
        var duplicates = environments.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
        if (environments.Any(string.IsNullOrWhiteSpace)) issues.Add(Issue("ERROR", $"{instance} / global environments", "An environment name is empty.", "Remove empty environment entries from central appsettings.json."));
        if (duplicates.Count > 0) issues.Add(Issue("ERROR", $"{instance} / global environments", $"Environment names are duplicated: {string.Join(", ", duplicates)}", "Keep each global environment name unique."));
        foreach (var environment in environments.Where(x => !string.IsNullOrWhiteSpace(x)))
            if (environment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || environment.Contains(',') || environment.Contains('|')) issues.Add(Issue("ERROR", $"{instance} / global environments", $"Environment name '{environment}' contains unsupported path or selection characters.", "Use a simple environment name without commas, pipes, or path separators."));
        if (environments.Count == 0) issues.Add(Issue("WARNING", $"{instance} / global environments", "No global environments are defined.", "Add an environment in Manage global environments, or use the default environment intentionally."));
    }

    private static void ValidateExecutionSettings(List<ValidationIssue> issues, string instance, InstanceInfo item)
    {
        if (item.MaxParallelRows < 1) issues.Add(Issue("ERROR", $"{instance} / execution", "MaxParallelRows must be at least 1.", "Save a valid MaxParallelRows value in the instance Execution settings."));
        if (item.Iterations < 1) issues.Add(Issue("ERROR", $"{instance} / execution", "Iterations must be at least 1.", "Save a valid Iterations value in the instance Execution settings."));
        if (item.ParallelRowStartIntervalMs < 0 || item.IterationIntervalMs < 0) issues.Add(Issue("ERROR", $"{instance} / execution", "Execution intervals cannot be negative.", "Use zero or a positive interval value."));
        if (!item.RowsExecutionMode.Equals("parallel", StringComparison.OrdinalIgnoreCase) && !item.RowsExecutionMode.Equals("sequential", StringComparison.OrdinalIgnoreCase)) issues.Add(Issue("ERROR", $"{instance} / execution", $"Unsupported RowsExecutionMode '{item.RowsExecutionMode}'.", "Use parallel or sequential."));
    }

    private static bool CanReadFile(string path, out string error)
    {
        try { using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); error = string.Empty; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static bool CanReadDirectory(string path, out string error)
    {
        try { _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToList(); error = string.Empty; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static string NormalizeRuntimeVariablesForJson(string content)
    {
        // The runner resolves underscore-delimited variables such as _userId_ before rendering JSON.
        // Replace placeholders outside JSON string literals with a valid numeric token for static validation.
        return Regex.Replace(content, "(?<![\\\"\\w])_[\\p{L}\\p{N}._-]+_(?![\\\"\\w])", "0");
    }

    private static ValidationIssue Issue(string severity, string location, string problem, string fix) => new(severity, location, problem, fix);
}

internal sealed class GlobalValidationWindow : Window
{
    private readonly StackPanel _results = new() { Spacing = 10 };
    private readonly TextBlock _summary = DashboardStyle.Text("Validating…", 14, true);
    private readonly Button _validateAgain;
    private readonly Func<Task<IReadOnlyList<InstanceValidationResult>>> _validate;

    public GlobalValidationWindow(Func<Task<IReadOnlyList<InstanceValidationResult>>> validate)
    {
        _validate = validate;
        Title = "Validate all instances";
        Width = 980;
        Height = 720;
        MinWidth = 700;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _validateAgain = DashboardStyle.Action("Validate again", () => _ = RunValidationAsync(), "primary");
        var close = DashboardStyle.Action("Close", Close);
        var header = new StackPanel { Spacing = 6, Children = { DashboardStyle.Text("✓ Validate all instances", 22, true), DashboardStyle.Text("Checks workbooks, selections, global environments, templates, scripts, source files, and output folders without executing requests.", 12, false, DashboardStyle.Muted), _summary } };
        var scroll = new ScrollViewer { Content = _results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _validateAgain, close } };
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        root.Children.Add(header); Grid.SetRow(scroll, 1); root.Children.Add(scroll); Grid.SetRow(buttons, 2); root.Children.Add(buttons);
        Content = root;
        Opened += (_, _) => _ = RunValidationAsync();
    }

    private async Task RunValidationAsync()
    {
        _validateAgain.IsEnabled = false;
        _summary.Text = "Validating all instances…";
        _summary.Foreground = DashboardStyle.Muted;
        _results.Children.Clear();
        IReadOnlyList<InstanceValidationResult> results;
        try { results = await _validate(); }
        catch (Exception ex)
        {
            _summary.Text = "Validation could not complete.";
            _summary.Foreground = DashboardStyle.Red;
            _results.Children.Add(DashboardStyle.Card(DashboardStyle.Text($"[ERROR] Global validation\n  {ex.Message}\n  Fix: Review central appsettings.json and validate again.", 13)));
            _validateAgain.IsEnabled = true;
            return;
        }
        var errors = results.Sum(x => x.Issues.Count(i => i.Severity == "ERROR"));
        var warnings = results.Sum(x => x.Issues.Count(i => i.Severity == "WARNING"));
        _summary.Text = errors == 0 ? warnings == 0 ? $"Passed — {results.Count} instance(s) validated with no warnings." : $"Passed with warnings — {results.Count} instance(s), {warnings} warning(s)." : $"Failed — {errors} error(s), {warnings} warning(s) across {results.Count} instance(s). Fix the red items and validate again.";
        _summary.Foreground = errors > 0 ? DashboardStyle.Red : warnings > 0 ? DashboardStyle.Brush("#F6C76B") : DashboardStyle.Green;
        foreach (var result in results)
        {
            var heading = result.HasErrors ? "FAILED" : result.HasWarnings ? "WARNINGS" : "PASSED";
            var color = result.HasErrors ? DashboardStyle.Red : result.HasWarnings ? DashboardStyle.Brush("#F6C76B") : DashboardStyle.Green;
            var text = DashboardStyle.Text($"{heading}  {result.Instance}\n\n{string.Join("\n\n", result.Issues)}", 12, false, color);
            text.TextWrapping = TextWrapping.Wrap;
            _results.Children.Add(DashboardStyle.Card(text));
        }
        _validateAgain.IsEnabled = true;
    }
}
