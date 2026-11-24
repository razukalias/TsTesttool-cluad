using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LoadTestingTool.Editing;

namespace LoadTestingTool.UI;

/// <summary>The WSDL as it was read: its text, where it came from, and the operations found.</summary>
public sealed record WsdlLoaded(string Text, string Location, WsdlInfo Info);

/// <summary>Asks for a WSDL (a URL or a file), lists its operations and hands the chosen ones to <c>apply</c>. Used by the WCF step form (one operation) and the mock server (any number).</summary>
public sealed class WsdlImportWindow : Window
{
    private readonly TextBox _location = new() { Tag = "wsdl-location", Watermark = "https://host/Service.svc?wsdl   or a file path", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _operations = new() { Tag = "wsdl-operations", MinHeight = 140, MaxHeight = 220 };
    private readonly TextBlock _status = new() { Tag = "wsdl-status", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBox _preview = new() { Tag = "wsdl-preview", IsReadOnly = true, AcceptsReturn = true, MinHeight = 110, MaxHeight = 200, FontFamily = new FontFamily("Consolas,Menlo,monospace"), FontSize = 12 };
    private readonly Button _apply;
    private readonly Action<WsdlLoaded, IReadOnlyList<WsdlOperation>> _applyTo;
    private WsdlLoaded? _loaded;

    public WsdlImportWindow(string title, string applyText, bool several, Action<WsdlLoaded, IReadOnlyList<WsdlOperation>> apply)
    {
        _applyTo = apply;
        Title = title; Width = 640; Height = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _operations.SelectionMode = several ? SelectionMode.Multiple | SelectionMode.Toggle : SelectionMode.Single;
        _operations.SelectionChanged += (_, _) => { ShowPreview(); _apply!.IsEnabled = _operations.SelectedItems?.Count > 0; };

        var browse = new Button { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a WSDL file", AllowMultiple = false });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { _location.Text = path; Load(); }
        };
        var load = new Button { Content = "Load", Tag = "wsdl-load", Margin = new Thickness(6, 0, 0, 0) };
        load.Click += (_, _) => Load();
        _location.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { Load(); e.Handled = true; } };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(load, Dock.Right); DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(load); row.Children.Add(browse); row.Children.Add(_location);

        _apply = new Button { Content = applyText, Tag = "wsdl-apply", IsEnabled = false };
        _apply.Click += (_, _) => Apply();
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(6, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _apply, cancel } };

        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children =
            {
                new TextBlock { Text = several ? "Choose the operations the mock should answer (Ctrl-click for several)." : "Choose the operation this step calls.", TextWrapping = TextWrapping.Wrap },
                row, _status, _operations,
                new TextBlock { Text = "Request body that will be used", FontSize = 12, Opacity = 0.7 }, _preview, buttons
            }
        };
    }

    /// <summary>Reads text from a URL (http/https) or a file; null when it cannot be read.</summary>
    public static string? Read(string location)
    {
        try
        {
            if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                return Task.Run(() => client.GetStringAsync(uri)).GetAwaiter().GetResult();
            }
            var path = Uri.TryCreate(location, UriKind.Absolute, out var file) && file.IsFile ? file.LocalPath : location;
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    private void Load()
    {
        var location = (_location.Text ?? "").Trim();
        if (location.Length == 0) { Say("Enter a URL or choose a file.", true); return; }
        var text = Read(location);
        if (text is null) { Say("Could not read that location.", true); return; }
        LoadFrom(text, location);
    }

    /// <summary>Parses already-read WSDL text and lists its operations (also what Load does after reading).</summary>
    public void LoadFrom(string text, string location)
    {
        WsdlInfo info;
        try { info = WsdlImporter.Parse(text, location, Read); }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or ArgumentException) { Say("That is not a WSDL: " + ex.Message, true); return; }
        _loaded = new WsdlLoaded(text, location, info);
        var several = info.Operations.Select(o => o.Binding).Distinct().Count() > 1;
        _operations.ItemsSource = info.Operations.Select(o => new ListBoxItem { Content = o.Display(several), Tag = o }).ToList();
        _preview.Text = string.Empty; _apply.IsEnabled = false;
        var notes = info.Notes.Count == 0 ? "" : "  Note: " + string.Join(" ", info.Notes);
        Say(info.Operations.Count == 0 ? "No SOAP operations found in that WSDL." + notes : $"{info.Operations.Count} operation(s) found.{notes}", info.Operations.Count == 0);
    }

    private IReadOnlyList<WsdlOperation> Chosen => (_operations.SelectedItems ?? new List<object>()).OfType<ListBoxItem>().Select(i => i.Tag).OfType<WsdlOperation>().ToList();

    private void ShowPreview()
    {
        var chosen = Chosen;
        _preview.Text = chosen.Count == 1 ? $"SOAP action: {chosen[0].SoapAction}   ({chosen[0].SoapVersion})\nEndpoint: {chosen[0].Endpoint}\n\n{chosen[0].Template}" : chosen.Count > 1 ? $"{chosen.Count} operations chosen." : string.Empty;
    }

    private void Apply()
    {
        var chosen = Chosen;
        if (_loaded is null || chosen.Count == 0) return;
        _applyTo(_loaded, chosen);
        Close();
    }

    private void Say(string text, bool error) { _status.Text = text; _status.Foreground = error ? Brush.Parse("#FF7A7A") : Brush.Parse("#8A94A6"); }
}
