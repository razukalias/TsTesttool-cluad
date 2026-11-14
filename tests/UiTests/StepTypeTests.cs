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
}
