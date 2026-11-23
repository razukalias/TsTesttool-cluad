using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace LoadTestingTool.UI.DataEditor;

// The Envs column: how a row fared in each environment of the latest run, and a click for what differs.
public sealed partial class DataEditorWindow
{
    private const string EnvsColumnTag = "envs";

    private DataGridTemplateColumn CreateEnvsColumn() => new()
    {
        Header = CreateHeader(new GridColumn("Envs", ColumnGroup.Id, -1, -1)),
        Width = new DataGridLength(230),
        MinWidth = 120,
        CanUserSort = false,
        CanUserResize = true,
        Tag = EnvsColumnTag,
        CellTemplate = new FuncDataTemplate<DataRowViewModel>((_, _) =>
        {
            var chips = new ItemsControl
            {
                Tag = "row-envs",
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }),
                ItemTemplate = new FuncDataTemplate<EnvChip>((chip, _) => chip is null ? new Border() : ChipView(chip), supportsRecycling: false)
            };
            chips.Bind(ItemsControl.ItemsSourceProperty, new Binding("EnvChips"));
            var button = new Button
            {
                Tag = "row-envs-button", Content = chips, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            button.Bind(ToolTip.TipProperty, new Binding("EnvDetail"));
            button.Click += (_, _) => { if (button.DataContext is DataRowViewModel row) ShowEnvComparison(row, button); };
            return button;
        })
    };

    private static Border ChipView(EnvChip chip)
    {
        var color = Color.Parse(chip.Color);
        return new Border
        {
            Tag = "env-chip", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 1), CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = chip.Text, FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center }
        };
    }

    /// <summary>Click on a row's chips: the status per environment, and which columns differ, fail everywhere or pass everywhere.</summary>
    private void ShowEnvComparison(DataRowViewModel row, Control anchor)
    {
        if (_shell.Current is not { } editor || editor.CompareEnvironments(row) is not { } comparison) return;
        var muted = Brush.Parse("#8EA4BC");
        var content = new StackPanel { Spacing = 8, MinWidth = 340, MaxWidth = 560, Tag = "env-compare" };
        var flyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedLeft };
        content.Children.Add(new TextBlock { Text = $"Data ID {row.CurrentDataId} in each environment", FontWeight = FontWeight.SemiBold });

        var chips = new WrapPanel { Tag = "env-compare-chips" };
        foreach (var chip in comparison.Environments)
        {
            var button = new Button { Tag = "env-compare-chip", Content = ChipView(chip), Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 0 };
            ToolTip.SetTip(button, $"Show {chip.Environment} in the table");
            button.Click += (_, _) => { flyout.Hide(); editor.ShowEnvironment(chip.Environment); };
            chips.Children.Add(button);
        }
        content.Children.Add(chips);

        void Section(string title, string note, IReadOnlyList<ComparedColumn> columns, string tag, bool collapsed = false)
        {
            if (columns.Count == 0) return;
            var list = new StackPanel { Spacing = 1 };
            foreach (var column in columns)
            {
                var line = new Button
                {
                    Tag = "env-compare-column", Padding = new Thickness(4, 2), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), IsEnabled = column.CellIndex >= 1,
                    Content = new TextBlock { Text = $"{column.Title} — {column.Summary}", FontSize = 11.5, TextWrapping = TextWrapping.Wrap }
                };
                ToolTip.SetTip(line, column.CellIndex >= 1 ? "Go to this column" : "This column is not shown in the table");
                var cell = column.CellIndex;
                line.Click += (_, _) => { flyout.Hide(); editor.CurrentCellIndex = cell; OnRevealColumn(this, cell); };
                list.Children.Add(line);
            }
            var header = $"{title} ({columns.Count})";
            if (collapsed) content.Children.Add(new Expander { Tag = tag, Header = header, IsExpanded = false, Content = list });
            else
            {
                var block = new StackPanel { Tag = tag, Spacing = 2 };
                block.Children.Add(new TextBlock { Text = header, FontWeight = FontWeight.SemiBold, FontSize = 12 });
                if (note.Length > 0) block.Children.Add(new TextBlock { Text = note, FontSize = 10.5, Foreground = muted, TextWrapping = TextWrapping.Wrap });
                block.Children.Add(list);
                content.Children.Add(block);
            }
        }

        Section("Differs between environments", "Failed or behaved differently in some environments only.", comparison.Differs, "env-differs");
        Section("Same in every environment", "Went wrong everywhere, which usually points at the expected value, not at an environment.", comparison.SameFailing, "env-same");
        Section("Passed everywhere", string.Empty, comparison.PassedEverywhere, "env-passed", collapsed: true);
        if (comparison.Differs.Count + comparison.SameFailing.Count + comparison.PassedEverywhere.Count == 0)
            content.Children.Add(new TextBlock { Text = "No column details were logged for this row.", FontSize = 11, Foreground = muted });
        if (comparison.PassesUnknown)
            content.Children.Add(new TextBlock { Text = "This run did not log passing checks, so only the failed columns are listed.", FontSize = 10.5, Foreground = muted, TextWrapping = TextWrapping.Wrap, Tag = "env-compare-note" });
        flyout.ShowAt(anchor);
    }
}
