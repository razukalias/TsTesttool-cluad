using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class StepTypeTests
{
    private static (DataEditorWindow Window, EditorShellViewModel Shell, StepRowViewModel Step) Open(TempDir dir)
    {
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 3), null);
        window.Width = 1300; window.Height = 900;
        window.Show();
        var shell = window.Shell;
        shell.SelectedNode = shell.Instances[0].Testcases[0].StepNodes[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, shell, shell.Current!.FocusedStep!);
    }

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    private static void Leave(Control c) => c.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));

    private static IEnumerable<string> Labels(Control root) => root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    [AvaloniaFact]
    public void Writing_a_column_the_sheet_lacks_adds_it_and_can_be_undone()
    {
        using var dir = new TempDir();
        var (window, shell, step) = Open(dir);
        var editor = shell.Current!;
        Assert.DoesNotContain("contenttype", editor.Document.Config.Headers, StringComparer.OrdinalIgnoreCase);
        step.Write("contenttype", "multipart/form-data");
        Pump();
        Assert.Contains("contenttype", editor.Document.Config.Headers);
        Assert.Equal("multipart/form-data", editor.FocusedStep!.Value("contenttype"));
        editor.UndoCommand.Execute(null);
        Pump();
        Assert.DoesNotContain("contenttype", editor.Document.Config.Headers, StringComparer.OrdinalIgnoreCase);
        window.Close();
    }

    [AvaloniaFact]
    public void Each_type_shows_only_its_own_fields()
    {
        using var dir = new TempDir();
        var (window, shell, step) = Open(dir);
        var form = window.GetVisualDescendants().OfType<StepFormView>().Single();
        Assert.Contains("Verb", Labels(form));
        Assert.Contains("Target URL", Labels(form));
        step.Write("steptype", "SQL");
        Pump();
        form = window.GetVisualDescendants().OfType<StepFormView>().Single();
        Assert.DoesNotContain("Verb", Labels(form));
        Assert.DoesNotContain("Target URL", Labels(form));
        Assert.Contains("SQL SETTINGS", Labels(form));
        Assert.Contains("Connection", Labels(form));
        step.Write("steptype", "ActionManual");
        Pump();
        form = window.GetVisualDescendants().OfType<StepFormView>().Single();
        Assert.Contains("Wait limit (min)", Labels(form));
        Assert.DoesNotContain("Template", Labels(form));
        window.Close();
    }

    [AvaloniaFact]
    public void Graphql_settings_use_the_runners_keys_and_keep_other_keys()
    {
        using var dir = new TempDir();
        var (window, shell, step) = Open(dir);
        step.Write("steptype", "GraphQL");
        step.Write("stepconfig", "{\"OperationName\":\"Old\",\"Extra\":1}");
        Pump();
        var section = window.GetVisualDescendants().OfType<StepTypeSection>().Single();
        var box = section.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == "Old");
        box.Text = "GetCustomers";
        Leave(box);
        var query = section.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark == "Used when no template is set.");
        query.Text = "query { customers { id } }";
        Leave(query);
        var json = JsonNode.Parse(shell.Current!.FocusedStep!.Value("stepconfig"))!.AsObject();
        Assert.Equal("GetCustomers", json["OperationName"]!.ToString());
        Assert.Equal("query { customers { id } }", json["Inline"]!.ToString());
        Assert.Equal(1, (int)json["Extra"]!);
        window.Close();
    }

    [AvaloniaFact]
    public void Multipart_content_type_shows_a_parts_editor_that_writes_parts()
    {
        using var dir = new TempDir();
        var (window, shell, step) = Open(dir);
        Assert.Empty(window.GetVisualDescendants().OfType<StepTypeSection>().SelectMany(s => Labels(s)).Where(t => t == "MULTIPART FORM PARTS"));
        step.Write("contenttype", "multipart/form-data");
        Pump();
        var section = window.GetVisualDescendants().OfType<StepTypeSection>().Single();
        Assert.Contains("MULTIPART FORM PARTS", Labels(section));
        section.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "＋ File").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        var name = section.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark == "Form field name");
        name.Text = "document";
        Leave(name);
        var path = section.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark == "Path, e.g. Files/sample.pdf");
        path.Text = "Files/sample.pdf";
        Leave(path);
        var part = JsonNode.Parse(shell.Current!.FocusedStep!.Value("stepconfig"))!["Parts"]!.AsArray()[0]!;
        Assert.Equal("document", part["Name"]!.ToString());
        Assert.Equal("file", part["Type"]!.ToString());
        Assert.Equal("Files/sample.pdf", part["Path"]!.ToString());
        Assert.Equal("application/octet-stream", part["ContentType"]!.ToString());
        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "step-multipart.png"));
        window.Close();
    }

    [Fact]
    public void Old_global_columns_are_hidden_and_opening_leaves_the_workbook_unchanged_until_saved()
    {
        using var dir = new TempDir();
        var path = Samples.CreateLarge(dir, rows: 3);
        var doc = LoadTestingTool.Editing.WorkbookDocument.Load(path);
        foreach (var header in new[] { "Threads", "Iterations", "Output Variable", "Environments", "ThreadIntervalMs", "IterationIntervalMs" }) doc.Config.EnsureColumn(header);
        doc.Save();
        var vm = new DataEditorViewModel(path);
        Assert.False(vm.IsDirty);
        Assert.DoesNotContain(vm.StepColumns, c => LegacyColumns.IsLegacy(c.Title));
        vm.StepRows[0].Cells[vm.StepRows[0].CellOf("verb")] = "PUT";
        vm.SaveCommand.Execute(null);
        var saved = LoadTestingTool.Editing.WorkbookDocument.Load(path);
        Assert.DoesNotContain(saved.Config.Headers, h => LegacyColumns.IsLegacy(h));
    }

    [AvaloniaFact]
    public void Headers_with_a_value_show_and_empty_ones_hide_until_added()
    {
        using var dir = new TempDir();
        var (window, shell, step) = Open(dir);
        shell.AddStep(shell.Instances[0].Testcases[0]);
        var editor = shell.Current!;
        editor.StepRows[0].Write("header_Authorization", "Bearer x");
        Pump();
        editor = shell.Current!;
        var first = editor.StepRows[0];
        editor.ShowAllStepsCommand.Execute(null);
        editor.StepRows[0].IsExpanded = true;
        editor.StepRows[1].IsExpanded = true;
        Pump();
        var forms = window.GetVisualDescendants().OfType<StepFormView>().ToList();
        Assert.Equal(2, forms.Count);
        Assert.Contains("Authorization", Labels(forms[0]));
        Assert.DoesNotContain("Authorization", Labels(forms[1]));
        Assert.Equal("", editor.StepRows[1].Value("header_Authorization"));
        // adding it to the second step writes into its own, empty cell
        var add = forms[1].GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "＋ Add");
        forms[1].GetVisualDescendants().OfType<AutoCompleteBox>().First().Text = "authorization";
        forms[1].GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark == "Value").Text = "Bearer y";
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        Assert.Equal("Bearer y", shell.Current!.StepRows[1].Value("header_Authorization"));
        Assert.Equal("Bearer x", shell.Current!.StepRows[0].Value("header_Authorization"));
        Assert.DoesNotContain(shell.Current!.Document.Config.Headers, h => h.Equals("header_authorization", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaFact]
    public void Sql_and_non_http_steps_show_no_headers_and_every_type_keeps_an_editable_template()
    {
        using var dir = new TempDir();
        var (window, shell, step) = Open(dir);
        step.Write("steptype", "SQL");
        Pump();
        var form = window.GetVisualDescendants().OfType<StepFormView>().Single();
        Assert.DoesNotContain("HEADERS", Labels(form));
        Assert.Contains("TEMPLATE · create.json", Labels(form));
        shell.Current!.FocusedStep!.Write("templetsource", "");
        Pump();
        form = window.GetVisualDescendants().OfType<StepFormView>().Single();
        Assert.False(form.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single().IsReadOnly);
        form.TemplateText = "select 1";
        Assert.True(form.SaveTemplate());
        Pump();
        var updated = shell.Current!.FocusedStep!.Value("templetsource");
        Assert.EndsWith(".sql", updated);
        Assert.True(File.Exists(System.IO.Path.Combine(shell.Current.TemplatesFolder, updated)));
        window.Close();
    }

    [AvaloniaFact]
    public void A_template_named_in_the_field_is_created_in_that_folder_on_save()
    {
        using var dir = new TempDir();
        var (window, shell, step) = Open(dir);
        step.Write("templetsource", "requests/new-customer.json");
        Pump();
        var form = window.GetVisualDescendants().OfType<StepFormView>().Single();
        form.TemplateText = "{\"id\": 1}";
        Assert.True(form.SaveTemplate());
        Assert.True(File.Exists(System.IO.Path.Combine(shell.Current!.TemplatesFolder, "requests", "new-customer.json")));
        Assert.Equal("requests/new-customer.json", shell.Current.FocusedStep!.Value("templetsource"));
        window.Close();
    }
}
