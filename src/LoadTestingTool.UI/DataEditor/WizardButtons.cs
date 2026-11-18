using Avalonia.Controls;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>The small ✦ button that sits on the border of the current table cell and opens the wizard for it.</summary>
internal static class WizardButtons
{
    public static Control Create(DataGridCell cell, Action<DataGridCell, Control> open)
    {
        var button = new Button
        {
            Content = "✦", Tag = "cell-wizard", Width = 20, Height = 20, Padding = new Avalonia.Thickness(0), FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = cell.Classes.Contains(":current"),
        };
        ToolTip.SetTip(button, "Open the wizard for this cell. With several rows selected, the result is written to all of them");
        cell.Classes.CollectionChanged += (_, _) => button.IsVisible = cell.Classes.Contains(":current");
        button.Click += (_, e) => { e.Handled = true; open(cell, button); };
        button.PointerPressed += (_, e) => e.Handled = true;   // do not let the grid start editing or move the selection
        return button;
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
