using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using System.Linq;
using System.Text.RegularExpressions;

namespace LoadTestingTool.UI;

public sealed record AssertionEditResult(string ResponseHeader, string ExpectedValue);

public sealed class AssertionEditorWindow : Window
{
    private readonly TextBox _header = new() { MinHeight = 34 };
    private readonly TextBox _expected = new() { MinHeight = 34 };
    private readonly TextBox _actual = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 72 };
    private readonly TextBox _failure = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 100 };
    private readonly TextBox _meaning = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 72 };
    private readonly TextBlock _error = new();
    private readonly Button _save;
    private readonly Popup _popup = new() { PlacementMode = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = false };
    private readonly ListBox _suggestions = new() { MaxHeight = 220, MinWidth = 360 };
    private TextBox? _activeBox;
    private bool _accepting;
    private bool _updatingSuggestions;
    private readonly string[] _referenceSuggestions;
    private static readonly string[] Verbs = { "eq", "ne", "lt", "lte", "gt", "gte", "contains", "notcontains", "startswith", "endswith", "regex", "exists", "notexists", "empty", "notempty", "in", "size" };

    public AssertionEditResult? Result { get; private set; }

    public AssertionEditorWindow(AssertionInfo assertion)
    {
        Title = "Edit assertion"; Width = 820; Height = 760; MinWidth = 680; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _header.Text = string.IsNullOrWhiteSpace(assertion.ResponseHeader) ? $"{assertion.StepName}.{assertion.ResponsePath}" : assertion.ResponseHeader;
        var cleanHeader = CleanAutocompleteHeader(assertion.ResponseHeader);
        _referenceSuggestions = string.IsNullOrWhiteSpace(cleanHeader)
            ? []
            : [$"<From_{cleanHeader}_r>"];
        if (!string.IsNullOrWhiteSpace(assertion.AssertionVerb) && assertion.AssertionVerb != "eq")
            _header.Text = $"{{{assertion.AssertionVerb}}}{_header.Text}";
        _expected.Text = assertion.ExpectedValue;
        _actual.Text = $"Actual value: {assertion.ActualValue}\nResult: {DashboardStyle.Status(assertion.Result)}\n{assertion.Environment} / {assertion.Testcase} / {assertion.StepName} / {assertion.DataId}";
        _failure.Text = string.IsNullOrWhiteSpace(assertion.FailureMessage) ? "No failure message." : assertion.FailureMessage;
        var assertionType = string.IsNullOrWhiteSpace(assertion.AssertionType) ? AssertionSemanticsUi.Type(assertion.AssertionVerb, assertion.ActualValue, assertion.FailureMessage) : assertion.AssertionType;
        var assertionMeaning = string.IsNullOrWhiteSpace(assertion.AssertionMeaning) ? AssertionSemanticsUi.Meaning(assertion.AssertionVerb, assertion.ActualValue, assertion.FailureMessage) : assertion.AssertionMeaning;
        _meaning.Text = "Type: " + assertionType + Environment.NewLine + assertionMeaning;
        _error.Foreground = DashboardStyle.Red; _error.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _save = DashboardStyle.Action("Save correction", Save, "primary");
        _suggestions.SelectionChanged += (_, _) => { if (!_updatingSuggestions && _popup.IsOpen && _suggestions.SelectedItem is string value) AcceptSuggestion(value); };
        _suggestions.PointerReleased += (_, _) => { if (_suggestions.SelectedItem is string value) AcceptSuggestion(value); };
        _popup.Child = _suggestions;
        _header.TextChanged += (_, _) => { Validate(); UpdateSuggestions(_header); };
        _expected.TextChanged += (_, _) => { Validate(); UpdateSuggestions(_expected); };
        _header.GotFocus += (_, _) => { _activeBox = _header; UpdateSuggestions(_header); };
        _expected.GotFocus += (_, _) => { _activeBox = _expected; UpdateSuggestions(_expected); };
        _header.AddHandler(InputElement.KeyDownEvent, SuggestionKeyDown, RoutingStrategies.Tunnel);
        _expected.AddHandler(InputElement.KeyDownEvent, SuggestionKeyDown, RoutingStrategies.Tunnel);
        _header.PointerPressed += (_, _) => { _activeBox = _header; };
        _expected.PointerPressed += (_, _) => { _activeBox = _expected; };

        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(DashboardStyle.Text("Edit assertion", 22, true));
        form.Children.Add(DashboardStyle.Text("Edit the complete response-sheet header and assertion cell syntax. The workbook is backed up before saving.", 12, false, DashboardStyle.Muted));
        form.Children.Add(Field("Actual response value", _actual));
        form.Children.Add(Field("Failure assertion details (select and copy)", _failure));
        form.Children.Add(Field("What this assertion means", _meaning));
        form.Children.Add(Field("Response header", _header));
        form.Children.Add(Field("Assertion cell / expected value", _expected));
        form.Children.Add(DashboardStyle.Text("Examples: {contains}GetUser.name + completed; GetUser.statusCode + {eq}200; <from_response_GetUser.userId_r>.", 11, false, DashboardStyle.Muted));
        form.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { DashboardStyle.Action("Help", ShowHelp), DashboardStyle.Action("Cancel", Close), _save } };
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(22) };
        root.Children.Add(new ScrollViewer { Content = form }); Grid.SetRow(buttons, 1); root.Children.Add(buttons);
        // Popup controls must be part of the visual tree; otherwise Avalonia accepts
        // IsOpen=true but has no host from which to render the suggestion list.
        root.Children.Add(_popup);
        Content = root;
        Validate();
    }

    private static Control Field(string label, Control control) => new StackPanel { Spacing = 5, Children = { DashboardStyle.Text(label, 12, false, DashboardStyle.Muted), control } };

    private void Validate()
    {
        var header = _header.Text?.Trim() ?? "";
        var expected = _expected.Text ?? "";
        var verb = HeaderVerb(header);
        var needsExpected = verb is not ("exists" or "notexists" or "empty" or "notempty");
        _error.Text = string.IsNullOrWhiteSpace(header) ? "Response header is required." : !HasPath(header) ? "Use stepname.responsePath, optionally prefixed with {assertionVerb}." : needsExpected && string.IsNullOrWhiteSpace(expected) ? $"Expected value is required for {verb}." : "";
        _save.IsEnabled = string.IsNullOrWhiteSpace(_error.Text);
    }

    private void SuggestionKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_popup.IsOpen) return;
        if (e.Key == Key.Down) { _suggestions.SelectedIndex = Math.Min(_suggestions.ItemCount - 1, _suggestions.SelectedIndex + 1); e.Handled = true; }
        else if (e.Key == Key.Up) { _suggestions.SelectedIndex = Math.Max(0, _suggestions.SelectedIndex - 1); e.Handled = true; }
        else if (e.Key is Key.Enter or Key.Tab) { if (_suggestions.SelectedItem is string value) AcceptSuggestion(value); e.Handled = true; }
        else if (e.Key == Key.Escape) _popup.IsOpen = false;
    }

    private void UpdateSuggestions(TextBox box)
    {
        if (_accepting || !box.IsFocused) return;
        _activeBox = box;
        var text = box.Text ?? "";
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);
        var before = text[..caret];
        var lt = before.LastIndexOf('<');
        var brace = before.LastIndexOf('{');
        var tokenStart = Math.Max(lt, brace);
        var token = tokenStart >= 0 ? before[tokenStart..] : "";
        var source = Verbs.Select(v => "{" + v + "}").Concat(new[] { "<From_", "<from_request_", "<from_response_", "<guid>", "<currentdatetime>", "{contains}", "{exists}" }).Concat(_referenceSuggestions);
        var values = source.Where(x => string.IsNullOrWhiteSpace(token) || x.Contains(token, StringComparison.OrdinalIgnoreCase)).Distinct().Take(60).ToList();
        if (values.Count == 0) { _popup.IsOpen = false; return; }
        _updatingSuggestions = true;
        _suggestions.ItemsSource = values; _suggestions.SelectedIndex = 0;
        _updatingSuggestions = false;
        _popup.PlacementTarget = box; _popup.IsOpen = true;
    }

    private void AcceptSuggestion(string value)
    {
        if (_activeBox is null || _accepting) return;
        _accepting = true;
        var text = _activeBox.Text ?? "";
        var caret = Math.Clamp(_activeBox.CaretIndex, 0, text.Length);
        var before = text[..caret];
        var lt = before.LastIndexOf('<');
        var brace = before.LastIndexOf('{');
        var start = Math.Max(lt, brace);
        if (start < 0) start = caret;
        _activeBox.Text = text[..start] + value + text[caret..];
        _activeBox.CaretIndex = start + value.Length;
        _activeBox.Focus(); _popup.IsOpen = false; _accepting = false; Validate();
    }

    private void ShowHelp()
    {
        ContextHelpWindow.Show(this, "Assertion editor help", "Response header is the complete response-sheet column header: StepName.field.path. You can prefix it with a header assertion verb, for example {contains}GetUser.name.\n\nThe assertion cell contains the expected value. A cell-level verb overrides the header verb: use {eq}200, {contains}completed, {exists}, {notempty}, or a plain value.\n\nDynamic references such as <from_request_step.field_r> and <from_response_step.field_r> are supported where the runner supports them. Type { or < to open suggestions, use Up/Down to select, and Enter, Tab, or mouse click to accept.\n\nThe failure details box is read-only but selectable, so you can copy the complete failure text.");
    }

    private void Save()
    {
        if (!_save.IsEnabled) return;
        Result = new AssertionEditResult(_header.Text!.Trim(), _expected.Text ?? ""); Close();
    }

    private static string HeaderVerb(string header)
    {
        if (header.StartsWith("{", StringComparison.Ordinal) && header.IndexOf('}') > 1)
            return header[1..header.IndexOf('}')].Trim().ToLowerInvariant();
        return "eq";
    }

    private static bool HasPath(string header)
    {
        var value = header.Trim();
        if (value.StartsWith("{", StringComparison.Ordinal) && value.IndexOf('}') > 1) value = value[(value.IndexOf('}') + 1)..].Trim();
        return value.Contains('.', StringComparison.Ordinal) || value.StartsWith("<from_", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanAutocompleteHeader(string header)
        => Regex.Replace(header ?? string.Empty, @"\{[^{}\r\n]*\}", string.Empty).Trim();
}
