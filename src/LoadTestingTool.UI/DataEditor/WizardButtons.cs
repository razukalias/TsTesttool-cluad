using Avalonia.Controls;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>The small ✦ button that sits on the border of the current table cell and opens the wizard for it.</summary>
internal static class WizardButtons
{
    /// <summary>
    /// A host for the button. The button itself (a templated control, costly to build) is only created the first time its cell is the current cell
    /// or is hovered, so a grid of thousands of cells opens without thousands of buttons.
    /// </summary>
    public static Control Create(DataGridCell cell, Action<DataGridCell, Control> open)
    {
        var host = new Panel();
        Button? button = null;
        var hovered = false;
        void Update()
        {
            var wanted = hovered || cell.Classes.Contains(":current");
            if (button is null && !wanted) return;
            if (button is null)
            {
                button = new Button
                {
                    Content = "✦", Tag = "cell-wizard", Width = 20, Height = 20, Padding = new Avalonia.Thickness(0), FontSize = 11,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                ToolTip.SetTip(button, "Open the wizard for this cell. With several rows selected, the result is written to all of them");
                var made = button;
                button.Click += (_, e) => { e.Handled = true; open(cell, made); };
                button.PointerPressed += (_, e) => e.Handled = true;   // do not let the grid start editing or move the selection
                host.Children.Add(button);
            }
            button.IsVisible = wanted;
        }
        cell.Classes.CollectionChanged += (_, _) => Update();
        cell.PointerEntered += (_, _) => { hovered = true; Update(); };
        cell.PointerExited += (_, _) => { hovered = false; Update(); };
        Update();
        return host;
    }
}

/// <summary>A plain text column for another sheet; the current cell shows the wizard button when <see cref="OpenWizard"/> is set.</summary>
public sealed class SheetTextColumn : DataGridTextColumn
{
    public Action<DataGridCell, Control>? OpenWizard { get; set; }

    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        var element = base.GenerateElement(cell, dataItem);
        if (OpenWizard is null) return element;
        return new Grid { Children = { element, WizardButtons.Create(cell, (c, b) => OpenWizard?.Invoke(c, b)) } };
    }
}
