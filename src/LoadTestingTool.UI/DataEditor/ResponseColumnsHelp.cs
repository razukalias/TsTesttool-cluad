using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI;

/// <summary>The short manual of the "Template and response" window.</summary>
public static class ResponseColumnsHelp
{
    public sealed record Section(string Title, string[] Lines);

    public static readonly IReadOnlyList<Section> Sections =
    [
        new("What this window does", [
            "It turns a step's JSON response into expected columns of the response sheet (headers like {eq}Step.customer.name), and the template placeholders into request columns. Nothing is written until you press Add ticked columns, and the whole add is one Undo step.",
        ]),
        new("1. Pick the response", [
            "Step and Row choose whose response you look at. ◀ ▶ walk through the rows of the step; each row shows its own latest response from the history.",
            "No response yet? Use Paste a response… or Pick a file…. A pasted or picked response belongs to the row shown.",
        ]),
        new("2. Choose headers in the tree (left)", [
            "Tick a value to take it as one header. Tick an object or an array to take the whole thing as ONE header: it becomes a count header such as {eq}Step.items.#count (the number of properties or elements), so you can assert size or use it later.",
            "Right-click a node: Take as one header · Flatten ▸ 1, 2, 3, 5 or all levels (one header per value below it) · Flatten with more array elements ▸ 3, 5, 10, all · Remove the choice · Untick everything below · Copy the path.",
            "By default an array is expanded by its first element only (items[0]...), the depth is 8 and at most 500 headers are made. A key that contains a dot or a bracket is marked, because the runner cannot tell it from a nested path.",
        ]),
        new("3. Check the headers (right)", [
            "Every resulting header is listed with its verb and the value that will be written. Choose the Default verb for all, or change the verb of one header (eq, ne, gt, contains, exists, notempty ... ). Headers already in the sheet are shown as existing and are not duplicated.",
        ]),
        new("4. Fill the cells", [
            "None: only the header columns are added. This row: the cell of the row shown gets the value from its response. Every row: each row gets the value from its own response (rows without a response stay empty).",
        ]),
        new("Good to know", [
            "Once any expected column exists for a step, the runner reports every response value that has no column as 'Unexpected response path'. Add the rest of the fields, or flatten them in here, until the testcase passes.",
            "The Response header tab does the same for HTTP response headers: Step.responseHeader.Name, with a verb each.",
            "Try it with the ComplexResponses instance (large nested orders) and its README.",
        ]),
    ];

    public static Window CreateWindow()
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(18) };
        foreach (var section in Sections)
        {
            panel.Children.Add(new TextBlock { Text = section.Title, FontWeight = FontWeight.SemiBold, FontSize = 14, Margin = new Thickness(0, 10, 0, 2), Foreground = Brush.Parse("#C9D8EA") });
            foreach (var line in section.Lines)
                panel.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#AFC2D8"), Margin = new Thickness(0, 0, 0, 4) });
        }
        var close = new Button { Content = "Close", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true, Margin = new Thickness(18, 0, 18, 14) };
        var window = new Window
        {
            Title = "Help · Template and response", Width = 640, Height = 640, MinWidth = 420, MinHeight = 320, Tag = "response-help",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel { Children = { close, new ScrollViewer { Content = panel } } },
        };
        DockPanel.SetDock(close, Dock.Bottom);
        close.Click += (_, _) => window.Close();
        return window;
    }
}
