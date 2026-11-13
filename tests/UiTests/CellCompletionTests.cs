using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using LoadTestingTool.UI.DataEditor;
using Xunit;

namespace UiTests;

public class CellCompletionTests
{
    private static readonly CellCompletion Completion = new(
        () => ["config", "request", "response", "notes"],
        sheet => sheet.ToLowerInvariant() switch
        {
            "request" => ["testcaseindex", "dataid", "CreatePost.title", "CreatePost.body"],
            "response" => ["dataid", "{eq}CreatePost.statusCode", "{notempty}CreatePost.id"],
            _ => [],
        });

    private static string[] Labels(string text, bool verbs = false) => Completion.Suggest(text, text.Length, verbs)?.Items.Select(i => i.Label).ToArray() ?? [];

    [Fact]
    public void A_lone_angle_bracket_lists_the_functions_and_narrows_as_you_type()
    {
        Assert.Contains("From_", Labels("<"));
        Assert.Contains("randomnumber", Labels("<"));
        Assert.Equal(["guid"], Labels("x <gu"));
        Assert.Empty(Labels("<zzz"));
    }

    [Fact]
    public void From_offers_sheets_then_that_sheets_headers_then_the_r_form()
    {
        Assert.Equal(["config", "notes", "request", "response"], Labels("<From_"));
        Assert.Equal(["request", "response"], Labels("<From_re"));
        Assert.Equal(new[] { "CreatePost.body", "CreatePost.title", "dataid", "testcaseindex" }, Labels("<From_request_").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal(["CreatePost.title"], Labels("<From_request_tit"));
        Assert.Equal(["r"], Labels("<From_request_CreatePost.title_"));
        Assert.Empty(Labels("<From_nosuchsheet_"));
    }

    [Fact]
    public void Response_references_offer_step_paths_without_the_assertion_verb()
    {
        Assert.Equal(new[] { "CreatePost.id", "CreatePost.statusCode", "dataid" }, Labels("<From_response_").OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Braces_offer_the_assertion_verbs_only_at_the_start_of_expected_value_cells()
    {
        Assert.Equal(CellCompletion.Verbs.Count, Labels("{", verbs: true).Length);
        Assert.Equal(["contains", "notcontains"], Labels("{cont", verbs: true).OrderBy(x => x == "contains" ? 0 : 1));
        Assert.Empty(Labels("{", verbs: false));
        Assert.Empty(Labels("text {eq", verbs: true));
    }

    [Fact]
    public void Applying_a_suggestion_replaces_only_what_was_typed_and_places_the_caret_after_it()
    {
        var text = "http://host/<From_requ> tail";
        var caret = "http://host/<From_requ".Length;
        var completion = Completion.Suggest(text, caret, false)!;
        var (result, newCaret) = CellCompletion.Apply(text, caret, completion, completion.Items.First(i => i.Label == "request"));

        Assert.Equal("http://host/<From_request_> tail", result);
        Assert.Equal("http://host/<From_request_".Length, newCaret);
        // and the suggestions chain on: headers of that sheet now
        Assert.Contains("CreatePost.title", Completion.Suggest(result, newCaret, false)!.Items.Select(i => i.Label));

        var verb = Completion.Suggest("{ne", 3, true)!;
        Assert.Equal(("{ne}", 4), CellCompletion.Apply("{ne", 3, verb, verb.Items.First(i => i.Label == "ne")));
    }
}

public class CellCompletionWindowTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Typing_in_a_cell_shows_the_suggestion_list_and_accepting_one_fills_the_cell()
    {
        using var dir = new TempDir();
        var window = DataEditorWindow.Open(Samples.CreateLarge(dir, rows: 4), "Customers");
        window.Width = 1440; window.Height = 860;
        window.Show();
        var grid = window.FindControl<DataGrid>("Grid")!;
        var vm = window.ViewModel;
        grid.SelectedItem = vm.VisibleRows[1];
        var nameColumn = grid.Columns.First(c => c.Tag is int tag && tag > 0 && vm.Columns[tag - 1].Title == "CreateCustomer.name");
        grid.CurrentColumn = nameColumn;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(grid.BeginEdit());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var box = grid.GetVisualDescendants().OfType<TextBox>().First(t => t.IsFocused || t.Text == "Customer 2");
        box.Text = "<From_re";
        box.CaretIndex = box.Text.Length;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.ItemsSource is IEnumerable<CompletionItem>);
        Assert.True(list.GetVisualAncestors().OfType<Border>().First().IsVisible);
        Assert.Equal(["request", "response"], ((IEnumerable<CompletionItem>)list.ItemsSource!).Select(i => i.Label));

        var shots = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ui-shots");
        Directory.CreateDirectory(shots);
        window.CaptureRenderedFrame()!.Save(System.IO.Path.Combine(shots, "editor-autocomplete.png"));

        list.SelectedIndex = 0;
        HeadlessWindowExtensions.KeyPressQwerty(window, Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("<From_request_", box.Text);
        window.Close();
    }
}
