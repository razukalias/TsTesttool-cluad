using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>
/// The "Template and response" window: creates data columns from what a step already shows. Request: the template's _variable_ placeholders. Response: the JSON of a
/// row's response as a tree to tick and flatten, with a verb per header (<see cref="ResponseColumnsPane"/>). Response header: the HTTP headers the server sent.
/// It is opened from Record view and from the right-click menu of a step in the tree; ticked items of all three tabs are added together as one undo step.
/// </summary>
public sealed class AddColumnsWindow : Window
{
    private readonly DataEditorViewModel _editor;
    private readonly string? _onlyStep;
    private readonly StackPanel _requestList = new() { Spacing = 2 };
    private readonly TextBlock _requestNote = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "request-note" };
    private readonly ResponseColumnsPane _responsePane;
    private readonly ComboBox _headerVerb = new() { MinWidth = 100, Tag = "header-default-verb" };
    private readonly ComboBox _headerStep = new() { MinWidth = 260, Tag = "header-step" };
    private readonly TextBlock _headerNote = new() { Foreground = Brush.Parse("#8EA4BC"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Tag = "header-note" };
    private readonly StackPanel _headerList = new() { Spacing = 2 };
    private readonly CheckBox _headerFill = new() { Content = "Fill the expected values for this row from the response", IsChecked = true, Tag = "header-fill" };
    private readonly TabControl _tabs = new();

    public AddColumnsWindow(DataEditorViewModel editor, string? onlyStep = null)
    {
        _editor = editor;
        _onlyStep = onlyStep;
        Title = onlyStep is null ? "Template and response" : $"Template and response · {onlyStep}";
        Width = 1180;
        Height = 760;
        MinWidth = 760;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var steps = editor.ResponseSteps();
        _responsePane = new ResponseColumnsPane(editor, steps, onlyStep);
        _headerStep.ItemsSource = steps;
        _headerStep.SelectedItem = onlyStep is not null && steps.Contains(onlyStep, StringComparer.OrdinalIgnoreCase) ? steps.First(s => s.Equals(onlyStep, StringComparison.OrdinalIgnoreCase)) : steps.LastOrDefault();
        _headerVerb.ItemsSource = DataEditorViewModel.AssertionVerbs;
        _headerVerb.SelectedItem = "eq";
        _headerVerb.SelectionChanged += (_, _) => { if (_headerVerb.SelectedItem is string verb) { foreach (var choice in _editor.ResponseHeaderChoices) choice.Verb = verb; LoadHeaderList(); } };
        _headerStep.SelectionChanged += (_, _) => LoadHeaders();
        var pickHeaders = new Button { Content = "Choose a sample file…", Tag = "header-file" };
        pickHeaders.Click += async (_, _) =>
        {
            if (StorageProvider is not { } storage) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Sample response headers (Name: value per line)", AllowMultiple = false, SuggestedStartLocation = await PickerFolders.StartAsync(storage, "response-headers") });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { PickerFolders.Remember("response-headers", path); LoadHeaders(path); }
        };

        _tabs.Items.Add(new TabItem { Header = "Request", Tag = "tab-request", Content = Tab(_requestNote, _requestList) });
        _tabs.Items.Add(new TabItem { Header = "Response", Tag = "tab-response", Content = new Border { Margin = new Thickness(0, 6, 0, 0), Child = _responsePane } });
        _tabs.Items.Add(new TabItem { Header = "Response header", Tag = "tab-header", Content = Tab(Labeled("Step", _headerStep), Labeled("Verb for the headers", _headerVerb), _headerNote, _headerList, _headerFill, pickHeaders) });

        var add = new Button { Content = "Add ticked columns", Classes = { "primary" }, Tag = "add-columns", MinWidth = 140 };
        var close = new Button { Content = "Close", MinWidth = 80, IsCancel = true };
        var help = new Button { Content = "Help", MinWidth = 80, Tag = "help" };
        help.Click += (_, _) => ResponseColumnsHelp.CreateWindow().Show(this);
        add.Click += (_, _) => { AddTicked(); Close(); };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { help, close, add } };
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(16), Children = { buttons, _tabs } };

        LoadRequest();
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

    private void LoadHeaders(string? file = null)
    {
        _editor.FindResponseHeaders(_headerStep.SelectedItem as string ?? string.Empty, file);
        LoadHeaderList();
        _headerNote.Text = _editor.ResponseHeadersNote;
        _headerFill.IsVisible = _editor.ResponseHeaderChoices.Count > 0;
    }

    /// <summary>One row per HTTP header: a tick and the verb it is compared with.</summary>
    private void LoadHeaderList()
    {
        _headerList.Children.Clear();
        foreach (var choice in _editor.ResponseHeaderChoices)
        {
            var box = new CheckBox { Content = choice.Display, IsChecked = choice.IsChosen, Tag = "header-choice", VerticalAlignment = VerticalAlignment.Center };
            box.IsCheckedChanged += (_, _) => choice.IsChosen = box.IsChecked == true;
            var verb = new ComboBox { ItemsSource = DataEditorViewModel.AssertionVerbs, SelectedItem = choice.Verb, MinWidth = 96, FontSize = 12, Tag = "header-verb" };
            verb.SelectionChanged += (_, _) => { if (verb.SelectedItem is string v) choice.Verb = v; };
            _headerList.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { verb, box } });
        }
    }

    /// <summary>Adds the ticked items of every tab as one undo step.</summary>
    public void AddTicked() => _editor.AddTickedColumns(_headerFill.IsChecked == true, _responsePane.Session);

    internal ResponseColumnsPane ResponsePane => _responsePane;

    internal void SelectTab(string header) => _tabs.SelectedItem = _tabs.Items.OfType<TabItem>().First(t => (string?)t.Header == header);
}
