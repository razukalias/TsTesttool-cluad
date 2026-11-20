using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// One form for creating data columns from what a step already shows: the template's _variable_ placeholders (Request),
/// the fields of the last response (Response) and the HTTP headers the server sent (Response header). It is opened from
/// Record view and from the right-click menu of a step in the tree; ticked items of all three tabs are added together.
/// </summary>
public sealed class AddColumnsWindow : Window
{
    private readonly DataEditorViewModel _editor;
    private readonly string? _onlyStep;
    private readonly StackPanel _requestList = new() { Spacing = 2 };
    private readonly TextBlock _requestNote = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "request-note" };
    private readonly ComboBox _responseStep = new() { MinWidth = 260, Tag = "response-step" };
    private readonly TextBlock _responseNote = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "response-note" };
    private readonly StackPanel _responseList = new() { Spacing = 2 };
    private readonly CheckBox _responseFill = new() { Content = "Fill the expected values for this row from the response", IsChecked = true, Tag = "response-fill" };
    private readonly ComboBox _headerStep = new() { MinWidth = 260, Tag = "header-step" };
    private readonly TextBlock _headerNote = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "header-note" };
    private readonly StackPanel _headerList = new() { Spacing = 2 };
    private readonly CheckBox _headerFill = new() { Content = "Fill the expected values for this row from the response", IsChecked = true, Tag = "header-fill" };
    private readonly TabControl _tabs = new();

    public AddColumnsWindow(DataEditorViewModel editor, string? onlyStep = null)
    {
        _editor = editor;
        _onlyStep = onlyStep;
        Title = onlyStep is null ? "Add columns" : $"Add columns for {onlyStep}";
        Width = 560;
        Height = 560;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var steps = editor.ResponseSteps();
        foreach (var box in new[] { _responseStep, _headerStep })
        {
            box.ItemsSource = steps;
            box.SelectedItem = onlyStep is not null && steps.Contains(onlyStep, StringComparer.OrdinalIgnoreCase) ? steps.First(s => s.Equals(onlyStep, StringComparison.OrdinalIgnoreCase)) : steps.LastOrDefault();
        }
        _responseStep.SelectionChanged += (_, _) => LoadResponse();
        _headerStep.SelectionChanged += (_, _) => LoadHeaders();
        var pickHeaders = new Button { Content = "Choose a sample file…", Tag = "header-file" };
        pickHeaders.Click += async (_, _) =>
        {
            if (StorageProvider is not { } storage) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Sample response headers (Name: value per line)", AllowMultiple = false });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) LoadHeaders(path);
        };

        var pick = new Button { Content = "Choose a sample file…", Tag = "response-file" };
        pick.Click += async (_, _) =>
        {
            if (StorageProvider is not { } storage) return;
            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions { Title = "Sample response", AllowMultiple = false });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) LoadResponse(path);
        };

        _tabs.Items.Add(new TabItem { Header = "Request", Tag = "tab-request", Content = Tab(_requestNote, _requestList) });
        _tabs.Items.Add(new TabItem { Header = "Response", Tag = "tab-response", Content = Tab(Labeled("Step", _responseStep), _responseNote, _responseList, _responseFill, pick) });
        _tabs.Items.Add(new TabItem { Header = "Response header", Tag = "tab-header", Content = Tab(Labeled("Step", _headerStep), _headerNote, _headerList, _headerFill, pickHeaders) });

        var add = new Button { Content = "Add ticked columns", Classes = { "primary" }, Tag = "add-columns", MinWidth = 140 };
        var close = new Button { Content = "Close", MinWidth = 80, IsCancel = true };
        add.Click += (_, _) => { AddTicked(); Close(); };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { close, add } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(16), Children = { buttons, _tabs } };

        LoadRequest();
        LoadResponse();
        LoadHeaders();
    }

    private static Control Labeled(string label, Control box) => new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, Foreground = Brush.Parse("#8EA4BC"), FontSize = 11 }, box } };

    private static Control Tab(params Control[] children)
    {
        var scroll = new ScrollViewer { Content = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) } };
        foreach (var child in children) ((StackPanel)scroll.Content!).Children.Add(child);
        return scroll;
    }

    private static void Fill<T>(StackPanel list, IEnumerable<T> choices, Func<T, string> display, Func<T, bool> chosen, Action<T, bool> set, string tag)
    {
        list.Children.Clear();
        foreach (var choice in choices)
        {
            var box = new CheckBox { Content = display(choice), IsChecked = chosen(choice), Tag = tag };
            box.IsCheckedChanged += (_, _) => set(choice, box.IsChecked == true);
            list.Children.Add(box);
        }
    }

    private void LoadRequest()
    {
        _editor.FindTemplateColumns(_onlyStep);
        Fill(_requestList, _editor.TemplateColumns, c => c.Header, c => c.IsChosen, (c, on) => c.IsChosen = on, "template-column");
        _requestNote.Text = _editor.TemplateColumns.Count == 0
            ? "Every _variable_ placeholder in the template already has a column."
            : $"{_editor.TemplateColumns.Count} placeholders in the template have no data column yet; ticked ones are created as Step.variable.";
    }

    private void LoadResponse(string? file = null)
    {
        _editor.FindResponseFields(_responseStep.SelectedItem as string ?? string.Empty, file);
        Fill(_responseList, _editor.ResponseFields, c => c.Display, c => c.IsChosen, (c, on) => c.IsChosen = on, "response-field");
        _responseNote.Text = _editor.ResponseFieldsNote;
        _responseFill.IsVisible = _editor.ResponseFields.Count > 0;
    }

    private void LoadHeaders(string? file = null)
    {
        _editor.FindResponseHeaders(_headerStep.SelectedItem as string ?? string.Empty, file);
        Fill(_headerList, _editor.ResponseHeaderChoices, c => c.Display, c => c.IsChosen, (c, on) => c.IsChosen = on, "header-choice");
        _headerNote.Text = _editor.ResponseHeadersNote;
        _headerFill.IsVisible = _editor.ResponseHeaderChoices.Count > 0;
    }

    /// <summary>Adds the ticked items of every tab as one undo step.</summary>
    public void AddTicked() => _editor.AddTickedColumns(_responseFill.IsChecked == true, _headerFill.IsChecked == true);

    internal void SelectTab(string header) => _tabs.SelectedItem = _tabs.Items.OfType<TabItem>().First(t => (string?)t.Header == header);
}
